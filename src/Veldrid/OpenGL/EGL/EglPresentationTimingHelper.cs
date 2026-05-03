using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Veldrid.OpenGL.EGL
{
    /// <summary>
    ///     Implements the EGL equivalent of VK_GOOGLE_display_timing: schedules each
    ///     <c>eglSwapBuffers</c> at the next optimal vblank slot to minimise
    ///     input-to-photon latency on Android tile-based GPUs (Adreno, Mali, PowerVR).
    ///
    ///     Requires <c>EGL_ANDROID_presentation_time</c> (for scheduling) and optionally
    ///     <c>EGL_ANDROID_get_frame_timestamps</c> (for adaptive vblank targeting based on
    ///     real compositor feedback). Falls back gracefully when either extension is absent.
    ///
    ///     Threading: all methods must be called on the EGL rendering thread.
    /// </summary>
    internal sealed unsafe class EglPresentationTimingHelper
    {
        // ── EGL extension constants ──────────────────────────────────────────────

        // EGL_ANDROID_presentation_time
        private const int EGL_TIMESTAMPS_ANDROID = 0x3430; // eglSurfaceAttrib attribute

        // EGL_ANDROID_get_frame_timestamps — compositor-timing query names
        private const int EGL_COMPOSITE_INTERVAL_ANDROID = 0x3431; // nanoseconds per vblank

        // EGL_ANDROID_get_frame_timestamps — per-frame event timestamps
        private const int EGL_DISPLAY_PRESENT_TIME_ANDROID = 0x343A; // when the image reached the display

        // Sentinel values returned when a timestamp is not yet available or invalid.
        private const long EGL_TIMESTAMP_PENDING_ANDROID = -2L;
        private const long EGL_TIMESTAMP_INVALID_ANDROID = -1L;

        // ── Dynamically loaded delegate types ────────────────────────────────────

        // eglPresentationTimeANDROID(dpy, surface, desiredPresentTime)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EglPresentationTimeANDROIDT(IntPtr dpy, IntPtr sur, long time);

        // eglGetCompositorTimingANDROID(dpy, surface, numTimestamps, names[], values[]) → composite interval
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EglGetCompositorTimingANDROIDT(IntPtr dpy, IntPtr sur, int n, int* names, long* values);

        // eglGetNextFrameIdANDROID(dpy, surface, frameId*) → next frame counter
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EglGetNextFrameIdANDROIDT(IntPtr dpy, IntPtr sur, ulong* frameId);

        // eglGetFrameTimestampsANDROID(dpy, surface, frameId, numTimestamps, names[], values[])
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint EglGetFrameTimestampsANDROIDT(IntPtr dpy, IntPtr sur, ulong frameId, int n, int* timestamps, long* values);

        // ── State ────────────────────────────────────────────────────────────────

        private readonly IntPtr display;
        private readonly IntPtr surface;

        private readonly EglPresentationTimeANDROIDT presentationTimeANDROID;
        private readonly EglGetCompositorTimingANDROIDT getCompositorTimingANDROID;  // may be null
        private readonly EglGetNextFrameIdANDROIDT getNextFrameIdANDROID;            // may be null
        private readonly EglGetFrameTimestampsANDROIDT getFrameTimestampsANDROID;   // may be null

        // Timing model — mirrors the VK_GOOGLE_display_timing / VkSwapchain implementation.
        // compositeInterval: nanoseconds per vblank cycle; 0 until first successful query.
        // lastDisplayPresentTime: wall-clock ns of the most recently confirmed display event.
        // pendingFrameId / hasPendingFrameId: the frame whose timestamps we're still waiting on.
        private long compositeInterval;
        private long lastDisplayPresentTime;
        private ulong pendingFrameId;
        private bool hasPendingFrameId;

        // ── Construction ─────────────────────────────────────────────────────────

        private EglPresentationTimingHelper(
            IntPtr display,
            IntPtr surface,
            EglPresentationTimeANDROIDT presentationTime,
            EglGetCompositorTimingANDROIDT compositorTiming,
            EglGetNextFrameIdANDROIDT nextFrameId,
            EglGetFrameTimestampsANDROIDT frameTimestamps)
        {
            this.display = display;
            this.surface = surface;
            presentationTimeANDROID = presentationTime;
            getCompositorTimingANDROID = compositorTiming;
            getNextFrameIdANDROID = nextFrameId;
            getFrameTimestampsANDROID = frameTimestamps;

            // Enable per-frame timestamp collection. Required for getFrameTimestampsANDROID
            // to return non-INVALID values; harmless if the extension is absent (eglSurfaceAttrib
            // ignores unknown attribute values on most drivers).
            if (frameTimestamps != null)
                EglNative.eglSurfaceAttrib(display, surface, EGL_TIMESTAMPS_ANDROID, 1 /* EGL_TRUE */);
        }

        /// <summary>
        ///     Attempts to create a helper. Returns <c>null</c> when
        ///     <c>EGL_ANDROID_presentation_time</c> is not supported by the driver.
        /// </summary>
        internal static EglPresentationTimingHelper TryCreate(IntPtr display, IntPtr surface)
        {
            bool hasPresentationTime = hasEglExtension(display, "EGL_ANDROID_presentation_time");
            if (!hasPresentationTime)
                return null;

            var presentationTimeFn = loadEglProc<EglPresentationTimeANDROIDT>("eglPresentationTimeANDROID");
            if (presentationTimeFn == null)
                return null;

            // EGL_ANDROID_get_frame_timestamps is optional; lets us adapt the vblank target based
            // on real compositor feedback (same as vkGetPastPresentationTimingGOOGLE on the Vulkan side).
            bool hasFrameTimestamps = hasEglExtension(display, "EGL_ANDROID_get_frame_timestamps");
            EglGetCompositorTimingANDROIDT compositorTimingFn = null;
            EglGetNextFrameIdANDROIDT nextFrameIdFn = null;
            EglGetFrameTimestampsANDROIDT frameTimestampsFn = null;

            if (hasFrameTimestamps)
            {
                compositorTimingFn = loadEglProc<EglGetCompositorTimingANDROIDT>("eglGetCompositorTimingANDROID");
                nextFrameIdFn = loadEglProc<EglGetNextFrameIdANDROIDT>("eglGetNextFrameIdANDROID");
                frameTimestampsFn = loadEglProc<EglGetFrameTimestampsANDROIDT>("eglGetFrameTimestampsANDROID");

                // If any of the frame-timestamp functions failed to load, disable the whole group
                // to avoid partial state.
                if (compositorTimingFn == null || nextFrameIdFn == null || frameTimestampsFn == null)
                {
                    compositorTimingFn = null;
                    nextFrameIdFn = null;
                    frameTimestampsFn = null;
                }
            }

            return new EglPresentationTimingHelper(
                display, surface,
                presentationTimeFn,
                compositorTimingFn,
                nextFrameIdFn,
                frameTimestampsFn);
        }

        // ── Per-frame hooks ───────────────────────────────────────────────────────

        /// <summary>
        ///     Must be called immediately <em>before</em> <c>eglSwapBuffers</c>.
        ///     Schedules the frame for the next optimal vblank slot.
        /// </summary>
        internal void BeforeSwap()
        {
            long desiredTime = 0L;

            if (compositeInterval > 0 && lastDisplayPresentTime > 0)
            {
                // Target the vblank immediately after the most recently confirmed display time —
                // the same heuristic as VK_GOOGLE_display_timing / VkSwapchain.GetDesiredPresentTime().
                desiredTime = lastDisplayPresentTime + compositeInterval;

                // Advance to a future vblank if we're already past the computed target (heavy frame,
                // pause, etc.) so we never request a time in the past.
                long now = getCurrentTimeNs();
                while (desiredTime < now)
                    desiredTime += compositeInterval;
            }

            presentationTimeANDROID(display, surface, desiredTime);

            // Capture the next frame ID so AfterSwap can retrieve its display timestamp.
            if (getNextFrameIdANDROID != null && !hasPendingFrameId)
            {
                ulong frameId;
                if (getNextFrameIdANDROID(display, surface, &frameId) != 0)
                {
                    pendingFrameId = frameId;
                    hasPendingFrameId = true;
                }
            }
        }

        /// <summary>
        ///     Must be called immediately <em>after</em> <c>eglSwapBuffers</c>.
        ///     Drains pending frame-timestamp data to keep the timing model current.
        /// </summary>
        internal void AfterSwap()
        {
            if (getFrameTimestampsANDROID == null || !hasPendingFrameId)
                return;

            // Query the display-present time for the pending frame.
            // The driver typically makes this available 1–3 frames later.
            int name = EGL_DISPLAY_PRESENT_TIME_ANDROID;
            long value;
            if (getFrameTimestampsANDROID(display, surface, pendingFrameId, 1, &name, &value) != 0
                && value != EGL_TIMESTAMP_PENDING_ANDROID
                && value != EGL_TIMESTAMP_INVALID_ANDROID
                && value > 0L)
            {
                if (value > lastDisplayPresentTime)
                    lastDisplayPresentTime = value;

                hasPendingFrameId = false;

                // (Re)query the composite interval so fold / HDMI / variable-refresh-rate
                // display changes are picked up automatically — mirrors the per-recreate call in
                // VkSwapchain.initDisplayTiming().
                if (getCompositorTimingANDROID != null)
                {
                    int intervalName = EGL_COMPOSITE_INTERVAL_ANDROID;
                    long interval;
                    if (getCompositorTimingANDROID(display, surface, 1, &intervalName, &interval) != 0
                        && interval > 0L)
                    {
                        compositeInterval = interval;
                    }
                }
            }
            // If still PENDING, leave hasPendingFrameId true and retry next frame.
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool hasEglExtension(IntPtr display, string name)
        {
            IntPtr strPtr = EglNative.eglQueryString(display, EglNative.EGL_EXTENSIONS);
            if (strPtr == IntPtr.Zero)
                return false;

            // The extension string is space-separated. We parse it into tokens to avoid false
            // positives from prefix matches (e.g. "EGL_ANDROID_presentation_time2" ≠
            // "EGL_ANDROID_presentation_time").
            string extensions = Marshal.PtrToStringAnsi(strPtr);
            if (extensions == null)
                return false;

            foreach (var token in extensions.Split(' '))
            {
                if (token == name)
                    return true;
            }

            return false;
        }

        private static TDelegate loadEglProc<TDelegate>(string name) where TDelegate : class
        {
            IntPtr ptr = EglNative.eglGetProcAddress(name);
            if (ptr == IntPtr.Zero)
                return null;
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(ptr);
        }

        /// <summary>
        ///     Returns the current monotonic time in nanoseconds.
        ///     On Linux/Android <see cref="Stopwatch" /> is backed by <c>CLOCK_MONOTONIC</c>,
        ///     which is the same clock used by Android's EGL presentation-time API
        ///     (<c>EGLnsecsANDROID</c> = nanoseconds since device boot).
        /// </summary>
        private static long getCurrentTimeNs()
        {
            return (long)((ulong)Stopwatch.GetTimestamp()
                          * 1_000_000_000UL
                          / (ulong)Stopwatch.Frequency);
        }
    }
}
