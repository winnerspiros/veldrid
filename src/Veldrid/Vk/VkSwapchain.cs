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
        public VkSurfaceKHR Surface => surface;

        public VkQueue PresentQueue => presentQueue;
        public uint PresentQueueIndex => presentQueueIndex;
        public ResourceRefCount RefCount { get; }

        // True if the swapchain is in a known-bad state and must be re-created before
        // the next present (e.g. transient zero-extent surface, surface-lost recovery
        // partially completed). SwapBuffersCore reads this and skips vkQueuePresentKHR
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
        // VkSwapchainPresentModeInfoEXT can carry the active mode.
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
        private Vulkan.VkFence imageAvailableFence;
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

            vkGetDeviceQueue(this.gd.Device, presentQueueIndex, 0, out presentQueue);

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
                throw new VeldridException("The Vulkan surface was not ready in time; cannot create a swapchain.");

            var fenceCi = VkFenceCreateInfo.New();
            fenceCi.flags = VkFenceCreateFlags.None;
            vkCreateFence(this.gd.Device, ref fenceCi, null, out imageAvailableFence);

            if (AcquireNextImage(this.gd.Device, VkSemaphore.Null, imageAvailableFence))
                waitAndResetImageAvailableFence();

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
        ///     <c>vkQueuePresentKHR</c> reports <c>VK_ERROR_OUT_OF_DATE_KHR</c> or
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

        public bool AcquireNextImage(VkDevice device, VkSemaphore semaphore, Vulkan.VkFence fence)
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
            var result = vkAcquireNextImageKHR(
                device,
                deviceSwapchain,
                acquire_timeout_ns,
                semaphore,
                fence,
                ref currentImageIndex);
            framebuffer.SetImageIndex(currentImageIndex);

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

            return true;
        }

        // Bounded fence wait + reset so a misbehaving driver cannot wedge startup or
        // the recreate path. On timeout, destroy + recreate the fence — its "in-use"
        // state is unknown and reusing it would be UB on the next acquire.
        internal void WaitAndResetImageAvailableFence()
        {
            const ulong fence_wait_timeout_ns = 250_000_000; // 250 ms
            var result = vkWaitForFences(gd.Device, 1, ref imageAvailableFence, true, fence_wait_timeout_ns);
            if (result == VkResult.Success)
            {
                vkResetFences(gd.Device, 1, ref imageAvailableFence);
                return;
            }

            // Driver never signaled within the budget — replace the fence wholesale.
            recreateImageAvailableFence();
            needsRecreation = true;
        }

        private void waitAndResetImageAvailableFence() => WaitAndResetImageAvailableFence();

        // Replaces imageAvailableFence with a fresh, unsignaled fence. Safe to call
        // even when the original may still have an in-flight signal pending: the old
        // fence handle is destroyed, and the spec only forbids destruction while in
        // use by a *queue submission*; vkAcquireNextImageKHR does not enqueue a queue
        // op for the fence in the strict sense — it's signaled by the WSI layer.
        // Even so, we vkDeviceWaitIdle first to drain any pending GPU work that
        // could be holding a reference.
        private void recreateImageAvailableFence()
        {
            gd.WaitForIdle();
            vkDestroyFence(gd.Device, imageAvailableFence, null);
            var fenceCi = VkFenceCreateInfo.New();
            fenceCi.flags = VkFenceCreateFlags.None;
            vkCreateFence(gd.Device, ref fenceCi, null, out imageAvailableFence);
        }

        // After a non-Success acquire the fence may or may not be signaled
        // (SUBOPTIMAL_KHR signals; the others don't). Always rebuild it so the next
        // acquire starts from a known-clean state. The `fence` parameter is kept
        // up-to-date for callers who hold the same handle.
        private void rebuildFenceAfterFailedAcquire(ref Vulkan.VkFence fence)
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
                waitAndResetImageAvailableFence();
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
                gd.WaitForIdle();

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
                    vkDestroySurfaceKHR(gd.Instance, newSurface, null);
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
                    vkDestroySwapchainKHR(gd.Device, deviceSwapchain, null);
                    deviceSwapchain = VkSwapchainKHR.Null;
                }
                compatiblePresentModes = null;

                if (newPresentQueueIndex != presentQueueIndex)
                {
                    presentQueueIndex = newPresentQueueIndex;
                    vkGetDeviceQueue(gd.Device, presentQueueIndex, 0, out presentQueue);
                }

                if (oldSurface != VkSurfaceKHR.Null)
                    vkDestroySurfaceKHR(gd.Instance, oldSurface, null);

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
            var result = vkGetPhysicalDeviceSurfaceCapabilitiesKHR(gd.PhysicalDevice, surface, out var surfaceCapabilities);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }

            if (surfaceCapabilities.minImageExtent.width == 0 && surfaceCapabilities.minImageExtent.height == 0
                                                              && surfaceCapabilities.maxImageExtent.width == 0 && surfaceCapabilities.maxImageExtent.height == 0)
                return false;

            if (deviceSwapchain != VkSwapchainKHR.Null) gd.WaitForIdle();

            currentImageIndex = 0;
            uint surfaceFormatCount = 0;
            result = vkGetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, surface, ref surfaceFormatCount, null);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
            CheckResult(result);
            var formats = new VkSurfaceFormatKHR[surfaceFormatCount];
            result = vkGetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, surface, ref surfaceFormatCount, out formats[0]);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
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
            result = vkGetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, surface, ref presentModeCount, null);
            if (result == VkResult.ErrorSurfaceLostKHR)
            {
                lastCreateSurfaceLost = true;
                return false;
            }
            CheckResult(result);
            var presentModes = new VkPresentModeKHR[presentModeCount];
            result = vkGetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, surface, ref presentModeCount, out presentModes[0]);
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
            uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.minImageCount + 1);

            var swapchainCi = VkSwapchainCreateInfoKHR.New();
            swapchainCi.surface = surface;
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

            // Adreno (and to a lesser extent some Mali drivers) report a rotated
            // currentTransform (e.g. Rotate90KHR) for activities that are already
            // landscape-locked at the OS level. Honouring it produces a black or
            // 90°-rotated swapchain because the compositor double-rotates.
            // Forcing IDENTITY when the surface advertises it as a supported
            // transform avoids that whole class of bugs and is what every Android
            // sample/engine ships in practice. Driver still applies the final
            // display rotation via the system compositor.
            var preTransform = surfaceCapabilities.currentTransform;
            if (OperatingSystem.IsAndroid()
                && (surfaceCapabilities.supportedTransforms & VkSurfaceTransformFlagsKHR.IdentityKHR) != 0)
                preTransform = VkSurfaceTransformFlagsKHR.IdentityKHR;
            swapchainCi.preTransform = preTransform;
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
                if (result == VkResult.ErrorSurfaceLostKHR)
                {
                    lastCreateSurfaceLost = true;
                    deviceSwapchain = oldSwapchain; // create failed, leave the old chain in place
                    return false;
                }
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
                // Under vsync, prefer FIFO_RELAXED → FIFO. We deliberately do *not* prefer
                // MAILBOX here, even though it would shave one frame of input-to-photon latency:
                //   - On Qualcomm Adreno (notably 7xx-series) drivers MAILBOX has been observed
                //     to stall vkAcquireNextImageKHR / vkQueuePresentKHR indefinitely under
                //     heavy submission pressure (e.g. texture-upload bursts), producing ANR-style
                //     black screens.
                //   - MAILBOX requires an extra in-flight image (~33% more swapchain memory) and
                //     an extra compositor round-trip; on tile-based mobile GPUs that translates
                //     to back-pressure rather than reduced latency.
                //   - Khronos guidance, Google's Android Vulkan samples, and ANGLE all default
                //     to FIFO on Android.
                // FIFO_RELAXED gives the lowest-latency tear-free option that's broadly safe;
                // FIFO is mandatory per spec and is the universal fallback.
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
