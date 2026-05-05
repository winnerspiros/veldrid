using System;
using System.Diagnostics;
using Veldrid.Android;
using Veldrid.MetalBindings;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal static unsafe class VkSurfaceUtil
    {
        internal static VkSurfaceKHR CreateSurface(VkGraphicsDevice gd, VkInstance instance, SwapchainSource swapchainSource)
        {
            // TODO a null GD is passed from VkSurfaceSource.CreateSurface for compatibility
            //      when VkSurfaceInfo is removed we do not have to handle gd == null anymore
            bool doCheck = gd != null;

            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrSurfaceExtensionName))
                throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrSurfaceExtensionName}");

            switch (swapchainSource)
            {
                case XlibSwapchainSource xlibSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrXlibSurfaceExtensionName))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrXlibSurfaceExtensionName}");

                    return createXlib(instance, xlibSource);

                case WaylandSwapchainSource waylandSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrWaylandSurfaceExtensionName))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrWaylandSurfaceExtensionName}");

                    return createWayland(instance, waylandSource);

                case Win32SwapchainSource win32Source:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrWin32SurfaceExtensionName))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrWin32SurfaceExtensionName}");

                    return createWin32(instance, win32Source);

                case AndroidSurfaceSwapchainSource androidSource:
                    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrAndroidSurfaceExtensionName))
                        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrAndroidSurfaceExtensionName}");

                    return createAndroidSurface(instance, androidSource);

                case NSWindowSwapchainSource nsWindowSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VkExtMetalSurfaceExtensionName);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VkMvkMacosSurfaceExtensionName))
                            return createNSWindowSurface(gd, instance, nsWindowSource, hasMetalExtension);

                        throw new VeldridException("Neither macOS surface extension was available: " +
                                                   $"{CommonStrings.VkMvkMacosSurfaceExtensionName}, {CommonStrings.VkExtMetalSurfaceExtensionName}");
                    }

                    return createNSWindowSurface(null, instance, nsWindowSource, false);

                case NSViewSwapchainSource nsViewSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VkExtMetalSurfaceExtensionName);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VkMvkMacosSurfaceExtensionName))
                            return createNSViewSurface(gd, instance, nsViewSource, hasMetalExtension);

                        throw new VeldridException("Neither macOS surface extension was available: " +
                                                   $"{CommonStrings.VkMvkMacosSurfaceExtensionName}, {CommonStrings.VkExtMetalSurfaceExtensionName}");
                    }

                    return createNSViewSurface(null, instance, nsViewSource, false);

                case UIViewSwapchainSource uiViewSource:
                    if (doCheck)
                    {
                        bool hasMetalExtension = gd.HasSurfaceExtension(CommonStrings.VkExtMetalSurfaceExtensionName);
                        if (hasMetalExtension || gd.HasSurfaceExtension(CommonStrings.VkMvkIOSSurfaceExtensionName))
                            return createUIViewSurface(gd, instance, uiViewSource, hasMetalExtension);

                        throw new VeldridException("Neither macOS surface extension was available: " +
                                                   $"{CommonStrings.VkMvkMacosSurfaceExtensionName}, {CommonStrings.VkMvkIOSSurfaceExtensionName}");
                    }

                    return createUIViewSurface(null, instance, uiViewSource, false);

                default:
                    throw new VeldridException("The provided SwapchainSource cannot be used to create a Vulkan surface.");
            }
        }

        private static VkSurfaceKHR createWin32(VkInstance instance, Win32SwapchainSource win32Source)
        {
            var instApi = new VkInstanceApi(instance);
            var surfaceCi = new VkWin32SurfaceCreateInfoKHR();
            surfaceCi.hwnd = win32Source.Hwnd;
            surfaceCi.hinstance = win32Source.Hinstance;
            VkSurfaceKHR surface;
            var result = instApi.vkCreateWin32SurfaceKHR(&surfaceCi, null, &surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR createXlib(VkInstance instance, XlibSwapchainSource xlibSource)
        {
            var instApi = new VkInstanceApi(instance);
            var xsci = new VkXlibSurfaceCreateInfoKHR();
            xsci.dpy = (IntPtr)xlibSource.Display;
            xsci.window = (ulong)xlibSource.Window;
            VkSurfaceKHR surface;
            var result = instApi.vkCreateXlibSurfaceKHR(&xsci, null, &surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR createWayland(VkInstance instance, WaylandSwapchainSource waylandSource)
        {
            var instApi = new VkInstanceApi(instance);
            var wsci = new VkWaylandSurfaceCreateInfoKHR();
            wsci.display = waylandSource.Display;
            wsci.surface = waylandSource.Surface;
            VkSurfaceKHR surface;
            var result = instApi.vkCreateWaylandSurfaceKHR(&wsci, null, &surface);
            CheckResult(result);
            return surface;
        }

        private static VkSurfaceKHR createAndroidSurface(VkInstance instance, AndroidSurfaceSwapchainSource androidSource)
        {
            // Guard against a null JNI Surface jobject. Passing IntPtr.Zero into ANativeWindow_fromSurface
            // can crash inside JNI before we get a chance to inspect the result.
            if (androidSource.Surface == IntPtr.Zero)
                throw new VeldridException(
                    "Android Surface is null. The SurfaceView's Surface is not yet bound to a native window. " +
                    "Defer Vulkan swapchain creation until SurfaceHolder.Callback.surfaceCreated has fired.");

            IntPtr aNativeWindow = AndroidRuntime.ANativeWindow_fromSurface(androidSource.JniEnv, androidSource.Surface);

            if (aNativeWindow == IntPtr.Zero)
                throw new VeldridException(
                    "ANativeWindow_fromSurface returned null. The Java Surface object is not currently associated " +
                    "with a native window (SurfaceHolder may have been released, or surfaceCreated has not fired yet).");

            try
            {
                int setFmtResult = AndroidRuntime.ANativeWindow_setBuffersGeometry(aNativeWindow, 0, 0, 1 /* WINDOW_FORMAT_RGBA_8888 */);
                if (setFmtResult != 0)
                    Debug.WriteLine($"[Veldrid] ANativeWindow_setBuffersGeometry(RGBA_8888) returned {setFmtResult} (non-fatal, continuing).");

                var instApi = new VkInstanceApi(instance);
                var androidSurfaceCi = new VkAndroidSurfaceCreateInfoKHR();
                androidSurfaceCi.window = aNativeWindow;
                VkSurfaceKHR surface;
                var result = instApi.vkCreateAndroidSurfaceKHR(&androidSurfaceCi, null, &surface);
                CheckResult(result);
                return surface;
            }
            finally
            {
                AndroidRuntime.ANativeWindow_release(aNativeWindow);
            }
        }

        private static VkSurfaceKHR createNSWindowSurface(VkGraphicsDevice gd, VkInstance instance, NSWindowSwapchainSource nsWindowSource, bool hasExtMetalSurface)
        {
            var nswindow = new NSWindow(nsWindowSource.NSWindow);
            return createNSViewSurface(gd, instance, new NSViewSwapchainSource(nswindow.contentView), hasExtMetalSurface);
        }

        private static VkSurfaceKHR createNSViewSurface(VkGraphicsDevice gd, VkInstance instance, NSViewSwapchainSource nsViewSource, bool hasExtMetalSurface)
        {
            var contentView = new NSView(nsViewSource.NSView);

            if (!CAMetalLayer.TryCast(contentView.layer, out var metalLayer))
            {
                metalLayer = CAMetalLayer.New();
                contentView.wantsLayer = true;
                contentView.layer = metalLayer.NativePtr;
            }

            var instApi = gd?.InstanceApi ?? new VkInstanceApi(instance);

            if (hasExtMetalSurface)
            {
                var surfaceCi = new VkMetalSurfaceCreateInfoEXT();
                surfaceCi.pLayer = (nint)metalLayer.NativePtr.ToPointer();
                VkSurfaceKHR surface;
                var result = instApi.vkCreateMetalSurfaceEXT(&surfaceCi, null, &surface);
                CheckResult(result);
                return surface;
            }
            else
            {
                // Fallback: VK_MVK_macos_surface — use EXT metal surface wherever possible.
                // On older MoltenVK without EXT_metal_surface this path runs; pLayer == contentView.
                var surfaceCi = new VkMetalSurfaceCreateInfoEXT();
                surfaceCi.pLayer = (nint)contentView.NativePtr.ToPointer();
                VkSurfaceKHR surface;
                var result = instApi.vkCreateMetalSurfaceEXT(&surfaceCi, null, &surface);
                CheckResult(result);
                return surface;
            }
        }

        private static VkSurfaceKHR createUIViewSurface(VkGraphicsDevice gd, VkInstance instance, UIViewSwapchainSource uiViewSource, bool hasExtMetalSurface)
        {
            var uiView = new UIView(uiViewSource.UIView);

            if (!CAMetalLayer.TryCast(uiView.layer, out var metalLayer))
            {
                metalLayer = CAMetalLayer.New();
                metalLayer.frame = uiView.frame;
                metalLayer.opaque = true;
                uiView.layer.addSublayer(metalLayer.NativePtr);
            }

            var instApi = gd?.InstanceApi ?? new VkInstanceApi(instance);
            var surfaceCi = new VkMetalSurfaceCreateInfoEXT();
            surfaceCi.pLayer = (nint)metalLayer.NativePtr.ToPointer();
            VkSurfaceKHR surface;
            var result = instApi.vkCreateMetalSurfaceEXT(&surfaceCi, null, &surface);
            CheckResult(result);
            return surface;
        }
    }
}
