using System;
using System.Collections.Generic;
using Vulkan;
using static Vulkan.VulkanNative;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkSwapchain : Swapchain
    {
        public override Framebuffer Framebuffer => framebuffer;

        public override bool IsDisposed => disposed;

        public VkSwapchainKHR DeviceSwapchain => deviceSwapchain;
        public uint ImageIndex => currentImageIndex;
        public Vulkan.VkFence ImageAvailableFence => imageAvailableFence;
        public VkSurfaceKHR Surface { get; }

        public VkQueue PresentQueue => presentQueue;
        public uint PresentQueueIndex => presentQueueIndex;
        public ResourceRefCount RefCount { get; }

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
        // VkSwapchainPresentModeInfoEXT can carry the active mode.
        public VkPresentModeKHR CurrentPresentMode => currentPresentMode;

        // True only when the swapchain was created with a non-trivial compatibility
        // list (i.e. VK_EXT_swapchain_maintenance1 active AND ≥2 modes available).
        // Lets the per-present chain be skipped in the common single-mode case.
        public bool HasPresentModeHotSwap => compatiblePresentModes != null && compatiblePresentModes.Length > 1;

        private readonly VkGraphicsDevice gd;
        private readonly VkSwapchainFramebuffer framebuffer;
        private readonly uint presentQueueIndex;
        private readonly VkQueue presentQueue;
        private readonly bool colorSrgb;
        private VkSwapchainKHR deviceSwapchain;
        private Vulkan.VkFence imageAvailableFence;
        private bool syncToVBlank;
        private bool? newSyncToVBlank;
        private uint currentImageIndex;
        private string name;
        private bool disposed;

        // VK_EXT_swapchain_maintenance1 hot-swap state.
        // compatiblePresentModes is the union of modes the current swapchain may switch
        // to per-present (always includes currentPresentMode). null if the extension is
        // unavailable for this swapchain — falls back to the recreate path.
        private VkPresentModeKHR currentPresentMode;
        private VkPresentModeKHR[] compatiblePresentModes;

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description)
            : this(gd, ref description, VkSurfaceKHR.Null)
        {
        }

        public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description, VkSurfaceKHR existingSurface)
        {
            this.gd = gd;
            syncToVBlank = description.SyncToVerticalBlank;
            colorSrgb = description.ColorSrgb;

            SwapchainSource swapchainSource = description.Source;

            Surface = existingSurface == VkSurfaceKHR.Null
                ? VkSurfaceUtil.CreateSurface(gd, gd.Instance, swapchainSource)
                : existingSurface;

            if (!getPresentQueueIndex(out presentQueueIndex)) throw new VeldridException("The system does not support presenting the given Vulkan surface.");

            vkGetDeviceQueue(this.gd.Device, presentQueueIndex, 0, out presentQueue);

            framebuffer = new VkSwapchainFramebuffer(gd, this, Surface, description.Width, description.Height, description.DepthFormat);

            createSwapchain(description.Width, description.Height);

            var fenceCi = VkFenceCreateInfo.New();
            fenceCi.flags = VkFenceCreateFlags.None;
            vkCreateFence(this.gd.Device, ref fenceCi, null, out imageAvailableFence);

            AcquireNextImage(this.gd.Device, VkSemaphore.Null, imageAvailableFence);
            vkWaitForFences(this.gd.Device, 1, ref imageAvailableFence, true, ulong.MaxValue);
            vkResetFences(this.gd.Device, 1, ref imageAvailableFence);

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

        public bool AcquireNextImage(VkDevice device, VkSemaphore semaphore, Vulkan.VkFence fence)
        {
            if (newSyncToVBlank != null)
            {
                syncToVBlank = newSyncToVBlank.Value;
                newSyncToVBlank = null;
                recreateAndReacquire(framebuffer.Width, framebuffer.Height);
                return false;
            }
            // Bound the wait so a misbehaving driver (e.g. Adreno after a surface-lost event,
            // where vkAcquireNextImageKHR has been observed to never return) cannot deadlock
            // the render thread. VK_TIMEOUT / VK_NOT_READY are treated like VK_ERROR_OUT_OF_DATE_KHR
            // so the swapchain is force-recreated, converting the hang into a recoverable per-frame stall.
            const ulong acquire_timeout_ns = 100_000_000; // 100 ms
            var result = vkAcquireNextImageKHR(
                device,
                deviceSwapchain,
                acquire_timeout_ns,
                semaphore,
                fence,
                ref currentImageIndex);
            framebuffer.SetImageIndex(currentImageIndex);

            if (result == VkResult.ErrorOutOfDateKHR
                || result == VkResult.SuboptimalKHR
                || result == VkResult.Timeout
                || result == VkResult.NotReady)
            {
                createSwapchain(framebuffer.Width, framebuffer.Height);
                return false;
            }

            if (result != VkResult.Success) throw new VeldridException("Could not acquire next image from the Vulkan swapchain.");

            return true;
        }

        private void recreateAndReacquire(uint width, uint height)
        {
            if (createSwapchain(width, height))
            {
                if (AcquireNextImage(gd.Device, VkSemaphore.Null, imageAvailableFence))
                {
                    vkWaitForFences(gd.Device, 1, ref imageAvailableFence, true, ulong.MaxValue);
                    vkResetFences(gd.Device, 1, ref imageAvailableFence);
                }
            }
        }

        private bool createSwapchain(uint width, uint height)
        {
            // Obtain the surface capabilities first -- this will indicate whether the surface has been lost.
            var result = vkGetPhysicalDeviceSurfaceCapabilitiesKHR(gd.PhysicalDevice, Surface, out var surfaceCapabilities);
            if (result == VkResult.ErrorSurfaceLostKHR) throw new VeldridException("The Swapchain's underlying surface has been lost.");

            if (surfaceCapabilities.minImageExtent.width == 0 && surfaceCapabilities.minImageExtent.height == 0
                                                              && surfaceCapabilities.maxImageExtent.width == 0 && surfaceCapabilities.maxImageExtent.height == 0)
                return false;

            if (deviceSwapchain != VkSwapchainKHR.Null) gd.WaitForIdle();

            currentImageIndex = 0;
            uint surfaceFormatCount = 0;
            result = vkGetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, Surface, ref surfaceFormatCount, null);
            CheckResult(result);
            var formats = new VkSurfaceFormatKHR[surfaceFormatCount];
            result = vkGetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, Surface, ref surfaceFormatCount, out formats[0]);
            CheckResult(result);

            var desiredFormat = colorSrgb
                ? VkFormat.B8g8r8a8Srgb
                : VkFormat.B8g8r8a8Unorm;

            var surfaceFormat = new VkSurfaceFormatKHR();

            if (formats.Length == 1 && formats[0].format == VkFormat.Undefined)
                surfaceFormat = new VkSurfaceFormatKHR { colorSpace = VkColorSpaceKHR.SrgbNonlinearKHR, format = desiredFormat };
            else
            {
                foreach (var format in formats)
                {
                    if (format.colorSpace == VkColorSpaceKHR.SrgbNonlinearKHR && format.format == desiredFormat)
                    {
                        surfaceFormat = format;
                        break;
                    }
                }

                if (surfaceFormat.format == VkFormat.Undefined)
                {
                    if (colorSrgb && surfaceFormat.format != VkFormat.R8g8b8a8Srgb) throw new VeldridException("Unable to create an sRGB Swapchain for this surface.");

                    surfaceFormat = formats[0];
                }
            }

            uint presentModeCount = 0;
            result = vkGetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, Surface, ref presentModeCount, null);
            CheckResult(result);
            var presentModes = new VkPresentModeKHR[presentModeCount];
            result = vkGetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, Surface, ref presentModeCount, out presentModes[0]);
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
            uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.minImageCount + 1);

            var swapchainCi = VkSwapchainCreateInfoKHR.New();
            swapchainCi.surface = Surface;
            swapchainCi.presentMode = presentMode;
            swapchainCi.imageFormat = surfaceFormat.format;
            swapchainCi.imageColorSpace = surfaceFormat.colorSpace;
            uint clampedWidth = Util.Clamp(width, surfaceCapabilities.minImageExtent.width, surfaceCapabilities.maxImageExtent.width);
            uint clampedHeight = Util.Clamp(height, surfaceCapabilities.minImageExtent.height, surfaceCapabilities.maxImageExtent.height);
            swapchainCi.imageExtent = new VkExtent2D { width = clampedWidth, height = clampedHeight };
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

            swapchainCi.preTransform = surfaceCapabilities.currentTransform;
            swapchainCi.compositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueKHR;
            swapchainCi.clipped = true;

            var oldSwapchain = deviceSwapchain;
            swapchainCi.oldSwapchain = oldSwapchain;

            // Pin compatible present modes for the duration of vkCreateSwapchainKHR. The
            // spec is explicit: pPresentModes must be valid only during the create call.
            fixed (VkPresentModeKHR* compatibleModesPtr = compatiblePresentModes)
            {
                var presentModesCi = default(VkSwapchainPresentModesCreateInfoEXT);
                if (compatiblePresentModes != null && compatiblePresentModes.Length > 1)
                {
                    presentModesCi = VkSwapchainPresentModesCreateInfoEXT.New();
                    presentModesCi.presentModeCount = (uint)compatiblePresentModes.Length;
                    presentModesCi.pPresentModes = compatibleModesPtr;
                    swapchainCi.pNext = &presentModesCi;
                }

                result = vkCreateSwapchainKHR(gd.Device, ref swapchainCi, null, out deviceSwapchain);
                CheckResult(result);
            }

            if (oldSwapchain != VkSwapchainKHR.Null) vkDestroySwapchainKHR(gd.Device, oldSwapchain, null);

            framebuffer.SetNewSwapchain(deviceSwapchain, width, height, surfaceFormat, swapchainCi.imageExtent);
            return true;
        }

        // Pure helper: maps (sync, tearing, available modes) → chosen VkPresentModeKHR.
        // Kept in sync with the create-time logic so hot-swap chooses the same mode the
        // recreate path would.
        private static VkPresentModeKHR choosePresentMode(VkPresentModeKHR[] presentModes, bool syncToVBlank, bool allowTearing)
        {
            if (syncToVBlank)
            {
                // Prefer MAILBOX over FIFO_RELAXED when vsync is requested: both avoid tearing
                // under steady state, but MAILBOX replaces the queued frame instead of queueing it,
                // cutting one full frame of input-to-photon latency. This is the canonical
                // "low-latency vsync" choice on Android tilers.
                if (Array.IndexOf(presentModes, VkPresentModeKHR.MailboxKHR) >= 0)
                    return VkPresentModeKHR.MailboxKHR;
                if (Array.IndexOf(presentModes, VkPresentModeKHR.FifoRelaxedKHR) >= 0)
                    return VkPresentModeKHR.FifoRelaxedKHR;
                return VkPresentModeKHR.FifoKHR;
            }

            if (allowTearing && Array.IndexOf(presentModes, VkPresentModeKHR.ImmediateKHR) >= 0)
                return VkPresentModeKHR.ImmediateKHR; // Lowest latency; tearing is acceptable.
            if (Array.IndexOf(presentModes, VkPresentModeKHR.MailboxKHR) >= 0)
                return VkPresentModeKHR.MailboxKHR; // Low latency without tearing.
            if (Array.IndexOf(presentModes, VkPresentModeKHR.ImmediateKHR) >= 0)
                return VkPresentModeKHR.ImmediateKHR; // Fallback: lower latency than FIFO.

            return VkPresentModeKHR.FifoKHR;
        }

        // Returns the set of present modes the swapchain can hot-swap to (always
        // includes anchor). Returns null if VK_EXT_surface_maintenance1 wasn't enabled
        // or the query failed — caller falls back to recreate-on-toggle.
        private VkPresentModeKHR[] queryCompatiblePresentModes(VkPresentModeKHR anchor, VkPresentModeKHR[] surfaceSupported)
        {
            if (gd.GetPhysicalDeviceSurfaceCapabilities2 == null)
                return null;

            var surfaceMode = VkSurfacePresentModeEXT.New();
            surfaceMode.presentMode = anchor;

            var surfaceInfo = VkPhysicalDeviceSurfaceInfo2KHR.New();
            surfaceInfo.surface = Surface;
            surfaceInfo.pNext = &surfaceMode;

            // Two-pass query: first call with pPresentModes = null returns the count.
            var compat = VkSurfacePresentModeCompatibilityEXT.New();
            var caps2 = VkSurfaceCapabilities2KHR.New();
            caps2.pNext = &compat;

            if (gd.GetPhysicalDeviceSurfaceCapabilities2(gd.PhysicalDevice, &surfaceInfo, &caps2) != VkResult.Success)
                return null;

            uint count = compat.presentModeCount;
            if (count == 0)
                return new[] { anchor };

            var modes = new VkPresentModeKHR[count];
            fixed (VkPresentModeKHR* modesPtr = modes)
            {
                compat.pPresentModes = modesPtr;
                if (gd.GetPhysicalDeviceSurfaceCapabilities2(gd.PhysicalDevice, &surfaceInfo, &caps2) != VkResult.Success)
                    return null;
            }

            // Defensive intersection with the surface-supported modes; deduplicate while
            // ensuring `anchor` is the first entry (required by VkSwapchainPresentModesCreateInfoEXT).
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
        // and the next vkQueuePresentKHR will apply it. Returns false if a recreate is needed.
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
            var result = vkGetPhysicalDeviceSurfaceSupportKHR(
                gd.PhysicalDevice,
                queueFamilyIndex,
                surface,
                out var supported);
            CheckResult(result);
            return supported;
        }

        private void disposeCore()
        {
            vkDestroyFence(gd.Device, imageAvailableFence, null);
            framebuffer.Dispose();
            vkDestroySwapchainKHR(gd.Device, deviceSwapchain, null);
            vkDestroySurfaceKHR(gd.Instance, Surface, null);

            disposed = true;
        }
    }
}
