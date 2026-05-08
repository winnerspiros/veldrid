using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkSwapchain : Swapchain
    {
        public override Framebuffer Framebuffer => framebuffer;

        public override bool IsDisposed => disposed;

        public VkSwapchainKHR DeviceSwapchain => deviceSwapchain;
        public uint ImageIndex => currentImageIndex;
        public Vortice.Vulkan.VkFence ImageAvailableFence => imageAvailableFence;
        public VkSurfaceKHR Surface => surface;

        public VkQueue PresentQueue => presentQueue;
        public uint PresentQueueIndex => presentQueueIndex;
        public ResourceRefCount RefCount { get; }

        // True if the swapchain is in a known-bad state and must be re-created before
        // the next present (e.g. transient zero-extent surface, surface-lost recovery
        // partially completed). SwapBuffersCore reads this and skips gd.DeviceApi.vkQueuePresentKHR
        // on this frame, retrying createSwapchain instead.
        public bool NeedsRecreation => needsRecreation;

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                gd.SetResourceName(this, value);
            }
        }

        public override bool SyncToVerticalBlank
        {
            get => newSyncToVBlank ?? syncToVBlank;
            set
            {
                if (syncToVBlank == value && newSyncToVBlank == null)
                    return;

                // Try a hot-swap (no swapchain rebuild) when VK_EXT_swapchain_maintenance1
                // is available and the new mode is in the create-time compatibility set.
                if (tryHotSwapPresentMode(value, allowTearing))
                {
                    syncToVBlank = value;
                    newSyncToVBlank = null;
                    return;
                }

                // Hot-swap unavailable: defer to the AcquireNextImage recreate path.
                // Re-toggling back to the live value clears any pending change instead of
                // queuing a redundant recreate.
                newSyncToVBlank = syncToVBlank != value ? value : (bool?)null;
            }
        }

        private bool allowTearing;

        public bool AllowTearing
        {
            get => allowTearing;
            set
            {
                if (allowTearing == value)
                    return;

                if (tryHotSwapPresentMode(syncToVBlank, value))
                {
                    allowTearing = value;
                    return;
                }

                allowTearing = value;
                recreateAndReacquire(framebuffer.Width, framebuffer.Height);
            }
        }

        // Exposed to VkGraphicsDevice.SwapBuffersCore so the per-present
        // VkSwapchainPresentModeInfoKHR can carry the active mode.
        public VkPresentModeKHR CurrentPresentMode => currentPresentMode;

        // True only when the swapchain was created with a non-trivial compatibility
        // list (i.e. VK_EXT_swapchain_maintenance1 active AND ≥2 modes available).
        // Lets the per-present chain be skipped in the common single-mode case.
        public bool HasPresentModeHotSwap => compatiblePresentModes != null && compatiblePresentModes.Length > 1;

        private readonly VkGraphicsDevice gd;
        private readonly VkSwapchainFramebuffer framebuffer;
        // The original SwapchainSource is retained so we can re-create the underlying
        // VkSurfaceKHR on VK_ERROR_SURFACE_LOST_KHR (Android stop/start, surfaceDestroyed
        // → surfaceCreated, lock screen, fold/unfold, PiP exit). Without this, a single
        // surface-lost event leaves the swapchain permanently dead and the framework
        // sees a permanent black screen for the lifetime of the process.
        private readonly SwapchainSource swapchainSource;
        private VkSurfaceKHR surface;
        private uint presentQueueIndex;
        private VkQueue presentQueue;
        private readonly bool colorSrgb;
        private VkSwapchainKHR deviceSwapchain;
        private Vortice.Vulkan.VkFence imageAvailableFence;
        private bool syncToVBlank;
        private bool? newSyncToVBlank;
        private uint currentImageIndex;
        private string name;
        private bool disposed;
        private bool needsRecreation;
        // Set by createSwapchain when any WSI call returns VK_ERROR_SURFACE_LOST_KHR;
        // attemptRecreate reads this to decide whether to rebuild the surface and retry.
        private bool lastCreateSurfaceLost;

        // VK_EXT_swapchain_maintenance1 hot-swap state.
        // compatiblePresentModes is the union of modes the current swapchain may switch
        // to per-present (always includes currentPresentMode). null if the extension is
        // unavailable for this swapchain — falls back to the recreate path.
        private VkPresentModeKHR currentPresentMode;
        private VkPresentModeKHR[] compatiblePresentModes;

        // VK_GOOGLE_display_timing state.
        // refreshDuration: nanoseconds per display vblank cycle; 0 when unavailable.
        // nextPresentID:   monotonically-increasing counter correlated with past-timing
        //                  query results to advance desiredPresentTime each frame.
        // lastEarliestPresentTime: most recent earliestPresentTime observed in past
        //                  timing results; used to compute the next vblank target.
        // All three are reset in initDisplayTiming on every (re)create so a new
        // swapchain (after rotation, fold, HDMI attach, etc.) starts fresh.
        private ulong displayTimingRefreshDuration;
        private uint displayTimingNextPresentID;
        private ulong displayTimingLastEarliestPresentTime;

        // True when VK_GOOGLE_display_timing is active and the refresh duration was
        // successfully queried for the current deviceSwapchain.
        public bool HasDisplayTiming => displayTimingRefreshDuration != 0;

        // The presentID to stamp on the next VkPresentTimeGOOGLE entry.
        public uint NextPresentID => displayTimingNextPresentID;

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description)
            : this(gd, ref description, VkSurfaceKHR.Null)
        {
        }

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description, VkSurfaceKHR existingSurface)
        {
            this.gd = gd;
            syncToVBlank = description.SyncToVerticalBlank;
            colorSrgb = description.ColorSrgb;

            swapchainSource = description.Source;

            surface = existingSurface == VkSurfaceKHR.Null
                ? VkSurfaceUtil.CreateSurface(gd, gd.Instance, swapchainSource)
                : existingSurface;

            if (!getPresentQueueIndex(out presentQueueIndex)) throw new VeldridException("The system does not support presenting the given Vulkan surface.");

            gd.DeviceApi.vkGetDeviceQueue(presentQueueIndex, 0, out presentQueue);

            framebuffer = new VkSwapchainFramebuffer(gd, this, surface, description.Width, description.Height, description.DepthFormat);

            // On Android the SurfaceView can be in a transient state where
            // vkGetPhysicalDeviceSurfaceCapabilitiesKHR reports a 0×0 extent
            // (between surfaceCreated and surfaceChanged). attemptRecreate
            // returns false in that window. Poll briefly so startup doesn't
            // proceed against a VK_NULL_HANDLE swapchain (which would silently
            // black-screen on the very first AcquireNextImage), then fall
            // through to a managed exception so the host can retry the create.
            // attemptRecreate also rebuilds the surface on VK_ERROR_SURFACE_LOST_KHR.
            const int max_initial_create_attempts = 25; // ~250 ms total
            const int initial_create_retry_delay_ms = 10;
            bool created = false;
            for (int attempt = 0; attempt < max_initial_create_attempts; attempt++)
            {
                if (attemptRecreate(description.Width, description.Height))
                {
                    created = true;
                    break;
                }

                System.Threading.Thread.Sleep(initial_create_retry_delay_ms);
            }
            if (!created)
            {
                // The swapchain never came up; clean up everything we constructed
                // before throwing so we don't leak the surface/framebuffer/old chain.
                framebuffer.Dispose();
                if (deviceSwapchain != VkSwapchainKHR.Null)
                    gd.DeviceApi.vkDestroySwapchainKHR(deviceSwapchain, null);
                if (surface != VkSurfaceKHR.Null)
                    gd.InstanceApi.vkDestroySurfaceKHR(surface, null);
                throw new VeldridException("The Vulkan surface was not ready in time; cannot create a swapchain.");
            }

            var fenceCi = new VkFenceCreateInfo();
            fenceCi.flags = VkFenceCreateFlags.None;
            gd.DeviceApi.vkCreateFence(&fenceCi, null, out imageAvailableFence);

            if (AcquireNextImage(this.gd.Device, VkSemaphore.Null, imageAvailableFence))
                WaitAndResetImageAvailableFence();

            RefCount = new ResourceRefCount(disposeCore);
        }

        #region Disposal

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        #endregion

        public override void Resize(uint width, uint height)
        {
            recreateAndReacquire(width, height);
        }

        /// <summary>
        ///     Triggered by <see cref="VkGraphicsDevice.SwapBuffersCore" /> when
        ///     <c>gd.DeviceApi.vkQueuePresentKHR</c> reports <c>VK_ERROR_OUT_OF_DATE_KHR</c> or
        ///     <c>VK_SUBOPTIMAL_KHR</c> (typical on Android after a rotation /
        ///     fold / DeX-attach). Recreates the swapchain in-place and
        ///     re-acquires so the next frame doesn't have to bounce a second
        ///     OUT_OF_DATE through the acquire path. Never throws on these
        ///     two results — callers expect a needs-rebuild signal, not an
        ///     exception (the per-rotate exception cost is what the osu!
        ///     framework retry loop was trying to avoid).
        /// </summary>
        public void RecreateAfterPresent()
        {
            recreateAndReacquire(framebuffer.Width, framebuffer.Height);
        }

        public bool AcquireNextImage(VkDevice device, VkSemaphore semaphore, Vortice.Vulkan.VkFence fence)
        {
            if (newSyncToVBlank != null)
            {
                syncToVBlank = newSyncToVBlank.Value;
                newSyncToVBlank = null;
                recreateAndReacquire(framebuffer.Width, framebuffer.Height);
                return false;
            }

            // If a previous frame couldn't (re)create the swapchain (e.g. transient
            // zero-extent surface) we still hold the prior deviceSwapchain. Try the
            // create again here before attempting an acquire.
            if (needsRecreation && !attemptRecreate(framebuffer.Width, framebuffer.Height))
                return false;

            // Bound the wait so a misbehaving driver (e.g. Adreno after a surface-lost event,
            // where vkAcquireNextImageKHR has been observed to never return) cannot deadlock
            // the render thread. VK_TIMEOUT / VK_NOT_READY are treated like VK_ERROR_OUT_OF_DATE_KHR
            // so the swapchain is force-recreated, converting the hang into a recoverable per-frame stall.
            const ulong acquire_timeout_ns = 100_000_000; // 100 ms
            var result = gd.DeviceApi.vkAcquireNextImageKHR(deviceSwapchain,
                acquire_timeout_ns,
                semaphore,
                fence,
                out currentImageIndex);

            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                // Drop the dead surface and rebuild from the original SwapchainSource.
                // On Android this re-resolves the JNI Surface → fresh ANativeWindow →
                // fresh VkSurfaceKHR, recovering from stop/start, surfaceDestroyed →
                // surfaceCreated, lock screen, fold/unfold, PiP exit, etc.
                rebuildFenceAfterFailedAcquire(ref fence);
                if (!recreateSurfaceAndSwapchain(framebuffer.Width, framebuffer.Height))
                    needsRecreation = true;
                return false;
            }

            if (result == VkResult.ErrorOutOfDateKHR
                || result == VkResult.SuboptimalKHR
                || result == VkResult.Timeout
                || result == VkResult.NotReady)
            {
                // SUBOPTIMAL_KHR signals the fence/semaphore per spec; the others do not.
                // Either way, destroy + recreate the fence so the next acquire can reuse
                // it without hitting "fence must be unsignaled" or worse, racing a still-
                // pending driver signal.
                rebuildFenceAfterFailedAcquire(ref fence);
                if (!attemptRecreate(framebuffer.Width, framebuffer.Height))
                    needsRecreation = true;
                return false;
            }

            if (result != VkResult.Success) throw new VeldridException("Could not acquire next image from the Vulkan swapchain.");

            framebuffer.SetImageIndex(currentImageIndex);
            return true;
        }

        // Bounded fence wait + reset so a misbehaving driver cannot wedge startup or
        // the recreate path. On timeout, destroy + recreate the fence — its "in-use"
        // state is unknown and reusing it would be UB on the next acquire.
        internal void WaitAndResetImageAvailableFence()
        {
            const ulong fence_wait_timeout_ns = 250_000_000; // 250 ms
            var fence = imageAvailableFence;
            var result = gd.DeviceApi.vkWaitForFences(1, &fence, true, fence_wait_timeout_ns);
            if (result == VkResult.Success)
            {
                gd.DeviceApi.vkResetFences(1, &fence);
                return;
            }

            // Driver never signaled within the budget — replace the fence wholesale.
            recreateImageAvailableFence();
            needsRecreation = true;
        }

        // Replaces imageAvailableFence with a fresh, unsignaled fence. Safe to call
        // even when the original may still have an in-flight signal pending: the old
        // fence handle is destroyed, and the spec only forbids destruction while in
        // use by a *queue submission*; vkAcquireNextImageKHR does not enqueue a queue
        // op for the fence in the strict sense — it's signaled by the WSI layer.
        // Even so, we drain pending GPU work first to be conservative.
        // NOTE: uses WaitForIdleLockFree (vkDeviceWaitIdle, no graphicsQueueLock)
        // because this method is called from within lock(graphicsQueueLock) in the
        // SwapBuffersCore → acquireAndWaitNextImage path. WaitForIdle would attempt
        // to re-enter the non-reentrant Lock and deadlock the render thread.
        private void recreateImageAvailableFence()
        {
            gd.WaitForIdleLockFree();
            gd.DeviceApi.vkDestroyFence(imageAvailableFence, null);
            var fenceCi = new VkFenceCreateInfo();
            fenceCi.flags = VkFenceCreateFlags.None;
            gd.DeviceApi.vkCreateFence(&fenceCi, null, out imageAvailableFence);
        }

        // After a non-Success acquire the fence may or may not be signaled
        // (SUBOPTIMAL_KHR signals; the others don't). Always rebuild it so the next
        // acquire starts from a known-clean state. The `fence` parameter is kept
        // up-to-date for callers who hold the same handle.
        private void rebuildFenceAfterFailedAcquire(ref Vortice.Vulkan.VkFence fence)
        {
            if (fence != imageAvailableFence) return;
            recreateImageAvailableFence();
            fence = imageAvailableFence;
        }

        private void recreateAndReacquire(uint width, uint height)
        {
            if (!attemptRecreate(width, height))
            {
                needsRecreation = true;
                return;
            }

            if (AcquireNextImage(gd.Device, VkSemaphore.Null, imageAvailableFence))
                WaitAndResetImageAvailableFence();
        }

        // Wraps createSwapchain with surface-lost recovery. If the surface has died,
        // rebuild it from the original SwapchainSource and retry the create once.
        private bool attemptRecreate(uint width, uint height)
        {
            if (createSwapchain(width, height))
            {
                needsRecreation = false;
                return true;
            }

            // Retry once after rebuilding the surface, but only when the WSI explicitly
            // told us the surface is dead. A zero-extent return (transient Android state)
            // is *not* fixed by surface recreation — the surface is fine, just not ready.
            if (lastCreateSurfaceLost && recreateSurface() && createSwapchain(width, height))
            {
                needsRecreation = false;
                return true;
            }

            needsRecreation = true;
            return false;
        }

        private bool recreateSurfaceAndSwapchain(uint width, uint height)
        {
            if (!recreateSurface()) return false;
            return attemptRecreate(width, height);
        }

        // Destroys the dead VkSurfaceKHR and creates a fresh one from the original
        // SwapchainSource. On Android this re-resolves the JNI Surface so the new
        // VkSurfaceKHR wraps the current ANativeWindow. Returns false if the source
        // didn't yield a new surface (host hasn't re-created it yet) so callers can
        // mark needsRecreation and retry next frame.
        private bool recreateSurface()
        {
            try
            {
                // NOTE: uses WaitForIdleLockFree (vkDeviceWaitIdle, no graphicsQueueLock)
                // because recreateSurface is reachable from within lock(graphicsQueueLock)
                // (SwapBuffersCore → acquireAndWaitNextImage → AcquireNextImage →
                // recreateSurfaceAndSwapchain). WaitForIdle would deadlock.
                gd.WaitForIdleLockFree();

                var oldSurface = surface;
                var newSurface = VkSurfaceUtil.CreateSurface(gd, gd.Instance, swapchainSource);
                if (newSurface == VkSurfaceKHR.Null)
                    return false;

                // The new surface may live on a different queue family; verify it before
                // committing to the swap. If it doesn't, throw away the new surface and
                // keep the original (still-dead) one — caller will retry next frame.
                surface = newSurface;
                if (!getPresentQueueIndex(out var newPresentQueueIndex))
                {
                    gd.InstanceApi.vkDestroySurfaceKHR(newSurface, null);
                    surface = oldSurface;
                    return false;
                }

                // The existing deviceSwapchain is bound to the old (now-dead) surface.
                // Per Vulkan spec, oldSwapchain passed to vkCreateSwapchainKHR must be
                // associated with the same surface as the new chain — so we must
                // destroy it here rather than letting createSwapchain reuse it as
                // oldSwapchain. compatiblePresentModes is also surface-relative and
                // must be re-queried by the next createSwapchain call.
                if (deviceSwapchain != VkSwapchainKHR.Null)
                {
                    gd.DeviceApi.vkDestroySwapchainKHR(deviceSwapchain, null);
                    deviceSwapchain = VkSwapchainKHR.Null;
                }
                compatiblePresentModes = null;

                if (newPresentQueueIndex != presentQueueIndex)
                {
                    presentQueueIndex = newPresentQueueIndex;
                    gd.DeviceApi.vkGetDeviceQueue(presentQueueIndex, 0, out presentQueue);
                }

                if (oldSurface != VkSurfaceKHR.Null)
                    gd.InstanceApi.vkDestroySurfaceKHR(oldSurface, null);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool createSwapchain(uint width, uint height)
        {
            lastCreateSurfaceLost = false;

            // Obtain the surface capabilities first -- this will indicate whether the surface has been lost.
            var result = gd.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(gd.PhysicalDevice, surface, out var surfaceCapabilities);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }

            if (surfaceCapabilities.minImageExtent.width == 0 && surfaceCapabilities.minImageExtent.height == 0
                                                              && surfaceCapabilities.maxImageExtent.width == 0 && surfaceCapabilities.maxImageExtent.height == 0)
                return false;

            // Android dp-scale early-out: during SurfaceHolder.SetFormat transitions the
            // ANativeWindow briefly reports dp-scaled dimensions. Bail out here — BEFORE
            // vkDeviceWaitIdle — so the ~50ms GPU drain isn't paid on every retry cycle.
            // (The late guard right before swapchainCi.imageExtent is belt-and-braces.)
            if (OperatingSystem.IsAndroid()
                && surfaceCapabilities.currentExtent.width != uint.MaxValue // fixed-extent surface
                && width > 0 && height > 0)
            {
                ulong reqArea = (ulong)width * (ulong)height;
                ulong extArea = (ulong)surfaceCapabilities.currentExtent.width * (ulong)surfaceCapabilities.currentExtent.height;
                if (extArea > 0 && extArea <= reqArea / 4)
                {
                    Debug.WriteLine($"[Veldrid/VkSwapchain] dp-scale early-out: extent {surfaceCapabilities.currentExtent.width}x{surfaceCapabilities.currentExtent.height} <= 1/4 of requested {width}x{height}; skipping WaitForIdle.");
                    return false;
                }
            }

            // NOTE: uses WaitForIdleLockFree (vkDeviceWaitIdle, no graphicsQueueLock) because
            // createSwapchain is reachable from within lock(graphicsQueueLock) via
            // SwapBuffersCore → handlePresentResult → RecreateAfterPresent → recreateAndReacquire
            // → attemptRecreate → createSwapchain. WaitForIdle would deadlock.
            if (deviceSwapchain != VkSwapchainKHR.Null) gd.WaitForIdleLockFree();

            currentImageIndex = 0;
            uint surfaceFormatCount = 0;
            result = gd.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, surface, &surfaceFormatCount, null);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
            CheckResult(result);
            var formats = new VkSurfaceFormatKHR[surfaceFormatCount];
            fixed (VkSurfaceFormatKHR* fmtPtr = formats)
                result = gd.InstanceApi.vkGetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, surface, &surfaceFormatCount, fmtPtr);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
            CheckResult(result);

            var desiredFormat = colorSrgb
                ? VkFormat.B8G8R8A8Srgb
                : VkFormat.B8G8R8A8Unorm;

            var surfaceFormat = new VkSurfaceFormatKHR();

            if (formats.Length == 1 && formats[0].format == VkFormat.Undefined)
                surfaceFormat = new VkSurfaceFormatKHR { colorSpace = VkColorSpaceKHR.SrgbNonLinear, format = desiredFormat };
            else
            {
                foreach (var format in formats)
                {
                    if (format.colorSpace == VkColorSpaceKHR.SrgbNonLinear && format.format == desiredFormat)
                    {
                        surfaceFormat = format;
                        break;
                    }
                }

                if (surfaceFormat.format == VkFormat.Undefined)
                {
                    if (colorSrgb) throw new VeldridException("Unable to create an sRGB Swapchain for this surface.");

                    surfaceFormat = formats[0];
                }
            }

            uint presentModeCount = 0;
            result = gd.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, surface, &presentModeCount, null);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
            CheckResult(result);
            var presentModes = new VkPresentModeKHR[presentModeCount];
            fixed (VkPresentModeKHR* pmPtr = presentModes)
                result = gd.InstanceApi.vkGetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, surface, &presentModeCount, pmPtr);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
            CheckResult(result);

            var presentMode = choosePresentMode(presentModes, syncToVBlank, allowTearing);

            // VK_EXT_swapchain_maintenance1: query the compatibility set for the chosen
            // initial present mode. Modes in this set can be hot-swapped per-present
            // without rebuilding the swapchain (e.g. low-latency mode toggle at runtime).
            // We intersect with the surface-supported modes to be safe; drivers are
            // *supposed* to only return supported modes but defense-in-depth is cheap.
            compatiblePresentModes = gd.HasSwapchainMaintenance1
                ? queryCompatiblePresentModes(presentMode, presentModes)
                : null;
            currentPresentMode = presentMode;

            uint maxImageCount = surfaceCapabilities.maxImageCount == 0 ? uint.MaxValue : surfaceCapabilities.maxImageCount;

            // Image count tuning for latency:
            // IMMEDIATE: use minImageCount (typically 2) — with no vblank queuing there
            //   is at most one pending-display image at any time, so a third image only
            //   adds memory without reducing latency. Acquire never stalls in IMMEDIATE
            //   mode because the driver returns the just-replaced image immediately.
            // FIFO / FIFO_RELAXED / MAILBOX: use minImageCount + 1 (triple-buffering) to
            //   prevent the GPU from stalling on vkAcquireNextImageKHR while the display
            //   controller holds two images across the vblank boundary.
            uint imageCount = presentMode == VkPresentModeKHR.Immediate
                ? Math.Min(maxImageCount, surfaceCapabilities.minImageCount)
                : Math.Min(maxImageCount, surfaceCapabilities.minImageCount + 1);

            var swapchainCi = new VkSwapchainCreateInfoKHR();
            swapchainCi.surface = surface;
            swapchainCi.presentMode = presentMode;
            swapchainCi.imageFormat = surfaceFormat.format;
            swapchainCi.imageColorSpace = surfaceFormat.colorSpace;

            // The Vulkan spec defines currentExtent == (uint.MaxValue, uint.MaxValue) as the
            // sentinel meaning "the surface has no fixed size; use your preferred extent".
            // On Android (and most fixed-output surfaces) currentExtent holds the true pixel
            // dimensions; we MUST use them exactly. Clamping a caller-supplied width/height
            // against min/max when currentExtent is fixed produces the wrong extent and gives
            // a permanently black swapchain without surfacing a Vulkan error.
            VkExtent2D chosenExtent;
            if (surfaceCapabilities.currentExtent.width != uint.MaxValue)
            {
                // Fixed (Android / display-output) surface: use currentExtent directly.
                chosenExtent = surfaceCapabilities.currentExtent;
            }
            else
            {
                // Variable-size surface (most desktop platforms): clamp requested extent.
                chosenExtent = new VkExtent2D
                {
                    width = Util.Clamp(width, surfaceCapabilities.minImageExtent.width, surfaceCapabilities.maxImageExtent.width),
                    height = Util.Clamp(height, surfaceCapabilities.minImageExtent.height, surfaceCapabilities.maxImageExtent.height)
                };
            }

            // Android dp-scale guard: during SurfaceHolder.SetFormat transitions the ANativeWindow
            // briefly reports dp-scaled dimensions (e.g. 1029×480 on a 3088×1440 3×-density device).
            // If currentExtent.area ≤ ¼ × requestedArea the surface is mid-transition; return false
            // so attemptRecreate retries in 10ms rather than baking a 1/9-scale swapchain.
            if (OperatingSystem.IsAndroid()
                && surfaceCapabilities.currentExtent.width != uint.MaxValue // fixed-extent surface
                && width > 0 && height > 0)
            {
                ulong reqArea = (ulong)width * (ulong)height;
                ulong extArea = (ulong)chosenExtent.width * (ulong)chosenExtent.height;
                if (extArea > 0 && extArea <= reqArea / 4)
                {
                    Debug.WriteLine($"[Veldrid/VkSwapchain] dp-scale guard: extent {chosenExtent.width}x{chosenExtent.height} <= 1/4 of requested {width}x{height}; retrying.");
                    return false;
                }
            }

            swapchainCi.imageExtent = chosenExtent;
            swapchainCi.minImageCount = imageCount;
            swapchainCi.imageArrayLayers = 1;
            swapchainCi.imageUsage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferDst;

            var queueFamilyIndices = new FixedArray2<uint>(gd.GraphicsQueueIndex, gd.PresentQueueIndex);

            if (gd.GraphicsQueueIndex != gd.PresentQueueIndex)
            {
                swapchainCi.imageSharingMode = VkSharingMode.Concurrent;
                swapchainCi.queueFamilyIndexCount = 2;
                swapchainCi.pQueueFamilyIndices = &queueFamilyIndices.First;
            }
            else
            {
                swapchainCi.imageSharingMode = VkSharingMode.Exclusive;
                swapchainCi.queueFamilyIndexCount = 0;
            }

            // Always use IDENTITY preTransform and let the compositor handle surface rotation.
            //
            // When preTransform == IDENTITY and currentTransform == ROTATE_90, the compositor applies
            // the 90° rotation before presenting — transparent to the application, ~0.1 ms cost on
            // Adreno 740, and the only correct approach without a full scene pre-rotation pipeline.
            //
            // DO NOT set preTransform = currentTransform without also applying the inverse rotation
            // matrix to every render pass projection. Without that matrix, setting them equal means
            // the compositor passes the swapchain image through unmodified, but the hardware display
            // controller still rotates by currentTransform — producing the 90°-rotated, tiled, or
            // duplicated display seen on Adreno 740 portrait devices in landscape mode.
            //
            // History: PR #18 set preTransform = currentTransform to fix an Adreno black screen.
            // That black screen was actually caused by MAILBOX stalls (#19), oversized texture-update
            // batches (#20), the fragment-shading-rate SIGSEGV (#21), and the push-descriptor null
            // pointer (#22). All four are now fixed; IDENTITY is correct here.
            var preTransform = VkSurfaceTransformFlagsKHR.Identity;
            swapchainCi.preTransform = preTransform;
            swapchainCi.compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque;
            swapchainCi.clipped = true;

            var oldSwapchain = deviceSwapchain;
            swapchainCi.oldSwapchain = oldSwapchain;

            // Diagnostic log: emit once per (re)create so runtime logs show exactly what
            // the Vulkan WSI negotiated. Uses Debug.WriteLine so output appears in Android
            // logcat (via the mono/.NET Android runtime) and in attached debuggers on all
            // platforms. Gated to DEBUG builds on non-Android to keep release builds quiet.
#if DEBUG
            logSwapchainDiagnostics(
                width, height,
                surfaceCapabilities,
                formats, surfaceFormat,
                presentModes, presentMode,
                preTransform,
                imageCount,
                compatiblePresentModes != null && compatiblePresentModes.Length > 1);
#else
            if (OperatingSystem.IsAndroid())
                logSwapchainDiagnostics(
                    width, height,
                    surfaceCapabilities,
                    formats, surfaceFormat,
                    presentModes, presentMode,
                    preTransform,
                    imageCount,
                    compatiblePresentModes != null && compatiblePresentModes.Length > 1);
#endif

            // Pin compatible present modes for the duration of vkCreateSwapchainKHR. The
            // spec is explicit: pPresentModes must be valid only during the create call.
            fixed (VkPresentModeKHR* compatibleModesPtr = compatiblePresentModes)
            {
                var presentModesCi = default(VkSwapchainPresentModesCreateInfoKHR);
                if (compatiblePresentModes != null && compatiblePresentModes.Length > 1)
                {
                    presentModesCi = new VkSwapchainPresentModesCreateInfoKHR();
                    presentModesCi.presentModeCount = (uint)compatiblePresentModes.Length;
                    presentModesCi.pPresentModes = compatibleModesPtr;
                    swapchainCi.pNext = &presentModesCi;
                }

                result = gd.DeviceApi.vkCreateSwapchainKHR(&swapchainCi, null, out deviceSwapchain);
                if (result == VkResult.ErrorSurfaceLostKHR)
                {
                    lastCreateSurfaceLost = true;
                    deviceSwapchain = oldSwapchain; // create failed, leave the old chain in place
                    return false;
                }
                CheckResult(result);
            }

            if (oldSwapchain != VkSwapchainKHR.Null) gd.DeviceApi.vkDestroySwapchainKHR(oldSwapchain, null);

            // Pass chosenExtent (== swapchainCi.imageExtent) for BOTH the desired
            // dimensions and the swapchain extent so VkSwapchainFramebuffer's
            // desiredWidth/Height can never disagree with scExtent. With preTransform
            // always IDENTITY, the compositor handles any surface rotation; the extent
            // reflects the caller's requested dimensions directly. This makes the
            // contract impossible to violate even if a future caller passes stale
            // width/height.
            framebuffer.SetNewSwapchain(deviceSwapchain, swapchainCi.imageExtent.width, swapchainCi.imageExtent.height, surfaceFormat, swapchainCi.imageExtent);

            // VK_GOOGLE_display_timing: (re)query the refresh cycle for this swapchain
            // and reset per-present counters. Called on every (re)create so fold/
            // rotation/HDMI display changes are always picked up.
            initDisplayTiming();
            return true;
        }

        // Logs the key WSI negotiation decisions to Debug output (logcat on Android).
        // Called once per swapchain (re)create so runtime logs show the exact state
        // rather than requiring a repro to attach a Vulkan validation layer.
        private static void logSwapchainDiagnostics(
            uint requestedWidth,
            uint requestedHeight,
            VkSurfaceCapabilitiesKHR caps,
            VkSurfaceFormatKHR[] availableFormats,
            VkSurfaceFormatKHR chosenFormat,
            VkPresentModeKHR[] availablePresentModes,
            VkPresentModeKHR chosenPresentMode,
            VkSurfaceTransformFlagsKHR chosenPreTransform,
            uint chosenImageCount,
            bool maintenancePNextChained)
        {
            bool fixedExtent = caps.currentExtent.width != uint.MaxValue;
            var chosen = fixedExtent ? caps.currentExtent
                : new VkExtent2D
                {
                    width = Util.Clamp(requestedWidth, caps.minImageExtent.width, caps.maxImageExtent.width),
                    height = Util.Clamp(requestedHeight, caps.minImageExtent.height, caps.maxImageExtent.height)
                };

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Veldrid/VkSwapchain] createSwapchain diagnostics:");
            sb.AppendLine($"  requested      : {requestedWidth}x{requestedHeight}");
            sb.AppendLine($"  currentExtent  : {(fixedExtent ? $"{caps.currentExtent.width}x{caps.currentExtent.height} (fixed)" : "variable (sentinel)")}");
            sb.AppendLine($"  minExtent      : {caps.minImageExtent.width}x{caps.minImageExtent.height}");
            sb.AppendLine($"  maxExtent      : {caps.maxImageExtent.width}x{caps.maxImageExtent.height}");
            sb.AppendLine($"  chosenExtent   : {chosen.width}x{chosen.height}");
            sb.AppendLine($"  surfaceFormat  : {chosenFormat.format} / {chosenFormat.colorSpace}");
            sb.Append(    $"  availFormats   :");
            foreach (var f in availableFormats)
                sb.Append($" {f.format}/{f.colorSpace}");
            sb.AppendLine();
            sb.AppendLine($"  presentMode    : {chosenPresentMode}");
            sb.Append(    $"  availModes     :");
            foreach (var m in availablePresentModes)
                sb.Append($" {m}");
            sb.AppendLine();
            sb.AppendLine($"  currentXform   : {caps.currentTransform}");
            sb.AppendLine($"  supportedXform : {caps.supportedTransforms}");
            sb.AppendLine($"  chosenPreXform : {chosenPreTransform}");
            sb.AppendLine($"  compositeAlpha : OpaqueKHR");
            sb.AppendLine($"  minImageCount  : {caps.minImageCount}  maxImageCount: {(caps.maxImageCount == 0 ? "unlimited" : caps.maxImageCount.ToString())}");
            sb.AppendLine($"  chosenImgCount : {chosenImageCount}");
            sb.AppendLine($"  maintenance1   : {(maintenancePNextChained ? "pNext chained" : "not chained")}");
            sb.AppendLine($"  preTransform   : {chosenPreTransform}  (currentTransform={caps.currentTransform}{(chosenPreTransform == VkSurfaceTransformFlagsKHR.Identity ? ", compositor will rotate — correct for IDENTITY" : ", compositor passthrough — app must pre-rotate scene")})");

            Debug.WriteLine(sb.ToString());
        }

        // Pure helper: maps (sync, tearing, available modes) → chosen VkPresentModeKHR.
        // Kept in sync with the create-time logic so hot-swap chooses the same mode the
        // recreate path would.
        private static VkPresentModeKHR choosePresentMode(VkPresentModeKHR[] presentModes, bool syncToVBlank, bool allowTearing)
        {
            if (syncToVBlank)
            {
                // Under vsync, prefer FIFO_RELAXED → FIFO. We deliberately do *not* prefer
                // MAILBOX even though it would replace a queued frame with a newer one:
                //
                //   • Qualcomm Adreno (all 7xx-series drivers tested, including 512.676.73)
                //     stall vkAcquireNextImageKHR / gd.DeviceApi.vkQueuePresentKHR under submission
                //     pressure (texture-upload bursts, first-frame pipeline compilation),
                //     producing ANR-style black screens. This is architectural, not a
                //     driver-version-specific bug: MAILBOX requires the driver to retire
                //     the oldest queued image when a newer one arrives, and Adreno's
                //     internal image manager blocks the caller while it does so.
                //
                //   • On 120 Hz displays rendering at ~120 fps, MAILBOX latency ≈ FIFO
                //     latency. The only scenario where it wins is render-fps >> refresh-hz
                //     (e.g. 240 fps → 120 Hz), which is uncommon on mobile.
                //
                //   • VK_GOOGLE_display_timing (active when HasDisplayTiming is true)
                //     closes the remaining vsync latency gap by targeting the optimal
                //     vblank slot per-present, without MAILBOX's stall risk.
                //
                //   • Khronos guidance, Google's Android Vulkan samples, and ANGLE all
                //     default to FIFO on Android for the same reasons.
                //
                // FIFO_RELAXED gives the lowest-latency tear-free option that's broadly
                // safe; FIFO is mandatory per spec and is the universal fallback.
                if (Array.IndexOf(presentModes, VkPresentModeKHR.FifoRelaxed) >= 0)
                    return VkPresentModeKHR.FifoRelaxed;
                return VkPresentModeKHR.Fifo;
            }

            if (allowTearing && Array.IndexOf(presentModes, VkPresentModeKHR.Immediate) >= 0)
                return VkPresentModeKHR.Immediate; // Lowest latency; tearing is acceptable.

            // On Android, avoid MAILBOX even in non-vsync mode: Adreno (7xx-series) drivers
            // stall vkAcquireNextImageKHR / gd.DeviceApi.vkQueuePresentKHR indefinitely under heavy
            // submission pressure (texture-upload bursts, first-frame pipeline compilation)
            // regardless of the syncToVBlank setting, producing ANR-style black screens.
            // When vsync is off, prefer IMMEDIATE for true uncapped rendering: it presents
            // without waiting for a vblank boundary, so a 25 ms frame on a 120 Hz display
            // isn't forced to the next vblank slot (avoiding the every-3rd-vblank ~40 fps
            // drop that FIFO_RELAXED causes in that scenario).
            // IMMEDIATE does not exhibit the same submission-pressure stall as MAILBOX.
            // FIFO_RELAXED is the best-effort fallback; FIFO is the mandatory-per-spec last resort.
            if (OperatingSystem.IsAndroid())
            {
                if (!syncToVBlank && Array.IndexOf(presentModes, VkPresentModeKHR.Immediate) >= 0)
                    return VkPresentModeKHR.Immediate;
                if (Array.IndexOf(presentModes, VkPresentModeKHR.FifoRelaxed) >= 0)
                    return VkPresentModeKHR.FifoRelaxed;
                return VkPresentModeKHR.Fifo;
            }

            if (Array.IndexOf(presentModes, VkPresentModeKHR.Mailbox) >= 0)
                return VkPresentModeKHR.Mailbox; // Low latency without tearing.
            if (Array.IndexOf(presentModes, VkPresentModeKHR.Immediate) >= 0)
                return VkPresentModeKHR.Immediate; // Fallback: lower latency than FIFO.

            return VkPresentModeKHR.Fifo;
        }

        // Returns the set of present modes the swapchain can hot-swap to (always
        // includes anchor). Returns null if VK_EXT_surface_maintenance1 wasn't enabled
        // or the query failed — caller falls back to recreate-on-toggle.
        private VkPresentModeKHR[] queryCompatiblePresentModes(VkPresentModeKHR anchor, VkPresentModeKHR[] surfaceSupported)
        {
            var surfaceMode = new VkSurfacePresentModeKHR();
            surfaceMode.presentMode = anchor;

            var surfaceInfo = new VkPhysicalDeviceSurfaceInfo2KHR();
            surfaceInfo.surface = Surface;
            surfaceInfo.pNext = &surfaceMode;

            // Two-pass query: first call with pPresentModes = null returns the count.
            var compat = new VkSurfacePresentModeCompatibilityKHR();
            var caps2 = new VkSurfaceCapabilities2KHR();
            caps2.pNext = &compat;

            if (gd.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilities2KHR(gd.PhysicalDevice, &surfaceInfo, &caps2) != VkResult.Success)
                return null;

            uint count = compat.presentModeCount;
            if (count == 0)
                return new[] { anchor };

            var modes = new VkPresentModeKHR[count];
            fixed (VkPresentModeKHR* modesPtr = modes)
            {
                compat.pPresentModes = modesPtr;
                if (gd.InstanceApi.vkGetPhysicalDeviceSurfaceCapabilities2KHR(gd.PhysicalDevice, &surfaceInfo, &caps2) != VkResult.Success)
                    return null;
            }

            // Defensive intersection with the surface-supported modes; deduplicate while
            // ensuring `anchor` is the first entry (required by VkSwapchainPresentModesCreateInfoKHR).
            var result = new List<VkPresentModeKHR>((int)count) { anchor };
            for (int i = 0; i < count; i++)
            {
                var m = modes[i];
                if (m == anchor) continue;
                if (Array.IndexOf(surfaceSupported, m) < 0) continue;
                if (result.Contains(m)) continue;
                result.Add(m);
            }

            return result.ToArray();
        }

        // Returns true if the present mode that (sync, tearing) imply is in the current
        // swapchain's hot-swap compatibility set, in which case we update currentPresentMode
        // and the next gd.DeviceApi.vkQueuePresentKHR will apply it. Returns false if a recreate is needed.
        private bool tryHotSwapPresentMode(bool syncToVBlankCandidate, bool allowTearingCandidate)
        {
            if (!gd.HasSwapchainMaintenance1 || compatiblePresentModes == null || compatiblePresentModes.Length <= 1)
                return false;

            // Re-query surface-supported modes is expensive; the compat set is itself
            // already a subset of supported, so it's also the candidate universe.
            var desired = choosePresentMode(compatiblePresentModes, syncToVBlankCandidate, allowTearingCandidate);
            if (Array.IndexOf(compatiblePresentModes, desired) < 0)
                return false;

            currentPresentMode = desired;
            return true;
        }

        // --- VK_GOOGLE_display_timing helpers ---

        // Returns the desiredPresentTime to chain in VkPresentTimesInfoGOOGLE for the
        // next frame. Returns 0 ("let driver decide") until at least one past timing
        // result has been recorded, after which we target lastEarliestPresentTime +
        // refreshDuration — the next vblank boundary after the previous frame's
        // earliest-possible display time.
        public ulong GetDesiredPresentTime()
        {
            // IMMEDIATE / uncapped: return 0 (present ASAP).
            // A future desiredPresentTime causes SurfaceFlinger to hold the frame until
            // the next vblank even in IMMEDIATE mode, capping FPS to the refresh rate.
            if (!syncToVBlank)
                return 0;

            if (displayTimingLastEarliestPresentTime == 0)
                return 0;

            return displayTimingLastEarliestPresentTime + displayTimingRefreshDuration;
        }

        // Increment the monotonic present counter after a successful gd.DeviceApi.vkQueuePresentKHR.
        public void AdvancePresentID()
        {
            displayTimingNextPresentID++;
        }

        // Query all pending past-timing results from the driver and update
        // displayTimingLastEarliestPresentTime. Drains the driver's internal ring
        // buffer to prevent it growing unbounded; capped at 8 entries per call to
        // keep stack usage bounded (in practice 1–3 entries per frame).
        public void DrainPastPresentationTimings()
        {
            if (!gd.HasDisplayTiming)
                return;

            uint count = 0;
            if (gd.DeviceApi.vkGetPastPresentationTimingGOOGLE(deviceSwapchain, &count, null) != VkResult.Success
                || count == 0)
                return;

            count = Math.Min(count, 8u);
            var timings = stackalloc VkPastPresentationTimingGOOGLE[(int)count];
            if (gd.DeviceApi.vkGetPastPresentationTimingGOOGLE(deviceSwapchain, &count, timings) != VkResult.Success)
                return;

            for (uint i = 0; i < count; i++)
            {
                if (timings[i].earliestPresentTime > displayTimingLastEarliestPresentTime)
                    displayTimingLastEarliestPresentTime = timings[i].earliestPresentTime;
            }
        }

        // Initialise (or re-initialise after swapchain recreate) the per-swapchain
        // display timing state. Resets all counters so a newly-created swapchain
        // starts from a clean baseline regardless of what the previous swapchain did.
        // Called unconditionally at the end of createSwapchain; no-ops when the
        // device extension is absent (gd.GetRefreshCycleDurationGOOGLE == null).
        private void initDisplayTiming()
        {
            // Always reset per-present state: the new swapchain has no timing history.
            displayTimingNextPresentID = 0;
            displayTimingLastEarliestPresentTime = 0;

            if (!gd.HasDisplayTiming)
            {
                displayTimingRefreshDuration = 0;
                return;
            }

            var timing = default(VkRefreshCycleDurationGOOGLE);
            if (gd.DeviceApi.vkGetRefreshCycleDurationGOOGLE(deviceSwapchain, &timing) != VkResult.Success
                || timing.refreshDuration == 0)
            {
                displayTimingRefreshDuration = 0;
                return;
            }

            displayTimingRefreshDuration = timing.refreshDuration;
        }

        private bool getPresentQueueIndex(out uint queueFamilyIndex)
        {
            uint deviceGraphicsQueueIndex = gd.GraphicsQueueIndex;
            uint devicePresentQueueIndex = gd.PresentQueueIndex;

            if (queueSupportsPresent(deviceGraphicsQueueIndex, Surface))
            {
                queueFamilyIndex = deviceGraphicsQueueIndex;
                return true;
            }

            if (deviceGraphicsQueueIndex != devicePresentQueueIndex && queueSupportsPresent(devicePresentQueueIndex, Surface))
            {
                queueFamilyIndex = devicePresentQueueIndex;
                return true;
            }

            queueFamilyIndex = 0;
            return false;
        }

        private bool queueSupportsPresent(uint queueFamilyIndex, VkSurfaceKHR surface)
        {
            VkBool32 supported;
            var result = gd.InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(gd.PhysicalDevice, queueFamilyIndex, surface, &supported);
            CheckResult(result);
            return supported;
        }

        private void disposeCore()
        {
            gd.DeviceApi.vkDestroyFence(imageAvailableFence, null);
            framebuffer.Dispose();
            gd.DeviceApi.vkDestroySwapchainKHR(deviceSwapchain, null);
            gd.InstanceApi.vkDestroySurfaceKHR(Surface, null);

            disposed = true;
        }
    }
}
