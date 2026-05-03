using System.Runtime.InteropServices;
using Vulkan;

namespace Veldrid.Vk
{
    // --- VK_KHR_dynamic_rendering ---

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkRenderingAttachmentInfo
    {
        public const VkStructureType TYPE = (VkStructureType)1000044001;

        public VkStructureType sType;
        public void* pNext;
        public VkImageView imageView;
        public VkImageLayout imageLayout;
        public VkResolveModeFlagBits resolveMode;
        public VkImageView resolveImageView;
        public VkImageLayout resolveImageLayout;
        public VkAttachmentLoadOp loadOp;
        public VkAttachmentStoreOp storeOp;
        public VkClearValue clearValue;

        public static VkRenderingAttachmentInfo New()
        {
            var ret = default(VkRenderingAttachmentInfo);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkRenderingInfo
    {
        public const VkStructureType TYPE = (VkStructureType)1000044000;

        public VkStructureType sType;
        public void* pNext;
        public VkRenderingFlags flags;
        public VkRect2D renderArea;
        public uint layerCount;
        public uint viewMask;
        public uint colorAttachmentCount;
        public VkRenderingAttachmentInfo* pColorAttachments;
        public VkRenderingAttachmentInfo* pDepthAttachment;
        public VkRenderingAttachmentInfo* pStencilAttachment;

        public static VkRenderingInfo New()
        {
            var ret = default(VkRenderingInfo);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPipelineRenderingCreateInfo
    {
        public const VkStructureType TYPE = (VkStructureType)1000044002;

        public VkStructureType sType;
        public void* pNext;
        public uint viewMask;
        public uint colorAttachmentCount;
        public VkFormat* pColorAttachmentFormats;
        public VkFormat depthAttachmentFormat;
        public VkFormat stencilAttachmentFormat;

        public static VkPipelineRenderingCreateInfo New()
        {
            var ret = default(VkPipelineRenderingCreateInfo);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceDynamicRenderingFeatures
    {
        public const VkStructureType TYPE = (VkStructureType)1000044003;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 dynamicRendering;

        public static VkPhysicalDeviceDynamicRenderingFeatures New()
        {
            var ret = default(VkPhysicalDeviceDynamicRenderingFeatures);
            ret.sType = TYPE;
            return ret;
        }
    }

    // Flags for VkRenderingInfo
    internal enum VkRenderingFlags : uint
    {
        None = 0,
        ContentsSecondaryCommandBuffers = 0x00000001,
        Suspending = 0x00000002,
        Resuming = 0x00000004,
    }

    // Resolve mode for VkRenderingAttachmentInfo
    internal enum VkResolveModeFlagBits : uint
    {
        None = 0,
        SampleZero = 0x00000001,
        Average = 0x00000002,
        Min = 0x00000004,
        Max = 0x00000008,
    }

    // --- VK_EXT_memory_budget ---

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceMemoryBudgetPropertiesEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000237000;
        public const int MAX_MEMORY_HEAPS = 16; // VK_MAX_MEMORY_HEAPS

        public VkStructureType sType;
        public void* pNext;
        public fixed ulong heapBudget[MAX_MEMORY_HEAPS];
        public fixed ulong heapUsage[MAX_MEMORY_HEAPS];

        public static VkPhysicalDeviceMemoryBudgetPropertiesEXT New()
        {
            var ret = default(VkPhysicalDeviceMemoryBudgetPropertiesEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    // --- VK_EXT_host_image_copy ---

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceHostImageCopyFeaturesEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000270000;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 hostImageCopy;

        public static VkPhysicalDeviceHostImageCopyFeaturesEXT New()
        {
            var ret = default(VkPhysicalDeviceHostImageCopyFeaturesEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkMemoryToImageCopyEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000270002;

        public VkStructureType sType;
        public void* pNext;
        public void* pHostPointer;
        public uint memoryRowLength;
        public uint memoryImageHeight;
        public VkImageSubresourceLayers imageSubresource;
        public VkOffset3D imageOffset;
        public VkExtent3D imageExtent;

        public static VkMemoryToImageCopyEXT New()
        {
            var ret = default(VkMemoryToImageCopyEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkCopyMemoryToImageInfoEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000270003;

        public VkStructureType sType;
        public void* pNext;
        public uint flags; // VkHostImageCopyFlagsEXT
        public VkImage dstImage;
        public VkImageLayout dstImageLayout;
        public uint regionCount;
        public VkMemoryToImageCopyEXT* pRegions;

        public static VkCopyMemoryToImageInfoEXT New()
        {
            var ret = default(VkCopyMemoryToImageInfoEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkHostImageLayoutTransitionInfoEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000270006;

        public VkStructureType sType;
        public void* pNext;
        public VkImage image;
        public VkImageLayout oldLayout;
        public VkImageLayout newLayout;
        public VkImageSubresourceRange subresourceRange;

        public static VkHostImageLayoutTransitionInfoEXT New()
        {
            var ret = default(VkHostImageLayoutTransitionInfoEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    // --- Function pointer delegates ---

    // VK_KHR_dynamic_rendering
    internal unsafe delegate void VkCmdBeginRenderingT(VkCommandBuffer commandBuffer, VkRenderingInfo* pRenderingInfo);

    internal delegate void VkCmdEndRenderingT(VkCommandBuffer commandBuffer);

    // VK_EXT_host_image_copy
    internal unsafe delegate VkResult VkCopyMemoryToImageExtT(VkDevice device, VkCopyMemoryToImageInfoEXT* pCopyMemoryToImageInfo);

    internal unsafe delegate VkResult VkTransitionImageLayoutExtT(VkDevice device, uint transitionCount, VkHostImageLayoutTransitionInfoEXT* pTransitions);

    // --- VK_KHR_fragment_shading_rate ---

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkFragmentShadingRateCombinerOpKHR
    {
        public const uint KEEP = 0;
        public const uint REPLACE = 1;
        public const uint MIN = 2;
        public const uint MAX = 3;
        public const uint MUL = 4;
    }

    // VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_SHADING_RATE_FEATURES_KHR = 1000226003
    // Must be chained in VkDeviceCreateInfo.pNext to actually activate the feature;
    // without this some Android drivers return a non-null but broken function pointer
    // for vkCmdSetFragmentShadingRateKHR that jumps to 0x0 (SIGSEGV).
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceFragmentShadingRateFeaturesKHR
    {
        public const VkStructureType TYPE = (VkStructureType)1000226003;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 pipelineFragmentShadingRate;
        public VkBool32 primitiveFragmentShadingRate;
        public VkBool32 attachmentFragmentShadingRate;

        public static VkPhysicalDeviceFragmentShadingRateFeaturesKHR New()
        {
            var ret = default(VkPhysicalDeviceFragmentShadingRateFeaturesKHR);
            ret.sType = TYPE;
            return ret;
        }
    }

    internal unsafe delegate void VkCmdSetFragmentShadingRateT(VkCommandBuffer commandBuffer, VkExtent2D* pFragmentSize, uint* combinerOps);

    // --- VK_EXT_mesh_shader ---

    internal delegate void VkCmdDrawMeshTasksExtT(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ);

    // --- VK_KHR_get_surface_capabilities2 ---
    // Required as the underlying query mechanism for VK_EXT_surface_maintenance1.

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceSurfaceInfo2KHR
    {
        public const VkStructureType TYPE = (VkStructureType)1000119000;

        public VkStructureType sType;
        public void* pNext;
        public VkSurfaceKHR surface;

        public static VkPhysicalDeviceSurfaceInfo2KHR New()
        {
            var ret = default(VkPhysicalDeviceSurfaceInfo2KHR);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSurfaceCapabilities2KHR
    {
        public const VkStructureType TYPE = (VkStructureType)1000119001;

        public VkStructureType sType;
        public void* pNext;
        public VkSurfaceCapabilitiesKHR surfaceCapabilities;

        public static VkSurfaceCapabilities2KHR New()
        {
            var ret = default(VkSurfaceCapabilities2KHR);
            ret.sType = TYPE;
            return ret;
        }
    }

    internal unsafe delegate VkResult VkGetPhysicalDeviceSurfaceCapabilities2KhrT(
        VkPhysicalDevice physicalDevice,
        VkPhysicalDeviceSurfaceInfo2KHR* pSurfaceInfo,
        VkSurfaceCapabilities2KHR* pSurfaceCapabilities);

    // --- VK_EXT_surface_maintenance1 ---
    // Lets us discover, per *anchor* present mode, which other present modes the swapchain
    // can be hot-switched to without recreation. Chained as input to
    // vkGetPhysicalDeviceSurfaceCapabilities2KHR; results are returned via the
    // VkSurfacePresentModeCompatibilityEXT struct chained on the output.

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSurfacePresentModeEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000274000;

        public VkStructureType sType;
        public void* pNext;
        public VkPresentModeKHR presentMode;

        public static VkSurfacePresentModeEXT New()
        {
            var ret = default(VkSurfacePresentModeEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSurfacePresentModeCompatibilityEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000274002;

        public VkStructureType sType;
        public void* pNext;
        public uint presentModeCount;
        public VkPresentModeKHR* pPresentModes;

        public static VkSurfacePresentModeCompatibilityEXT New()
        {
            var ret = default(VkSurfacePresentModeCompatibilityEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    // --- VK_EXT_swapchain_maintenance1 (promoted to KHR in Vulkan 1.4) ---
    // Allows changing present mode at vkQueuePresentKHR time without rebuilding the
    // swapchain — useful for runtime "low-latency mode" toggles. Adreno 740, recent
    // Mali / NVIDIA / Intel / Mesa drivers all support it.

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000275000;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 swapchainMaintenance1;

        public static VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT New()
        {
            var ret = default(VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    // Chained to VkSwapchainCreateInfoKHR.pNext on creation. The first entry in
    // pPresentModes is the swapchain's initial present mode; subsequent entries are
    // those we want to keep the option to switch to. Every listed mode must be in the
    // *compatibility set* of the initial mode (queried via VK_EXT_surface_maintenance1).
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSwapchainPresentModesCreateInfoEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000275002;

        public VkStructureType sType;
        public void* pNext;
        public uint presentModeCount;
        public VkPresentModeKHR* pPresentModes;

        public static VkSwapchainPresentModesCreateInfoEXT New()
        {
            var ret = default(VkSwapchainPresentModesCreateInfoEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    // Chained to VkPresentInfoKHR.pNext per-present. pPresentModes is parallel to
    // pSwapchains: one VkPresentModeKHR per swapchain. We only ever present a single
    // swapchain, so the array is length 1.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSwapchainPresentModeInfoEXT
    {
        public const VkStructureType TYPE = (VkStructureType)1000275003;

        public VkStructureType sType;
        public void* pNext;
        public uint swapchainCount;
        public VkPresentModeKHR* pPresentModes;

        public static VkSwapchainPresentModeInfoEXT New()
        {
            var ret = default(VkSwapchainPresentModeInfoEXT);
            ret.sType = TYPE;
            return ret;
        }
    }

    // --- VK_KHR_synchronization2 (core in Vulkan 1.3) ---
    // Enables vkQueueSubmit2 with per-semaphore pipeline stage masks.
    // VkPhysicalDeviceSynchronization2Features is chained at device creation to opt in;
    // VkSubmitInfo2/VkCommandBufferSubmitInfo/VkSemaphoreSubmitInfo replace the legacy
    // VkSubmitInfo on the submission hot-path.

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceSynchronization2Features
    {
        public const VkStructureType TYPE = (VkStructureType)1000314007;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 synchronization2;

        public static VkPhysicalDeviceSynchronization2Features New()
        {
            var ret = default(VkPhysicalDeviceSynchronization2Features);
            ret.sType = TYPE;
            return ret;
        }
    }

    // One entry per command buffer in VkSubmitInfo2.pCommandBufferInfos.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkCommandBufferSubmitInfo
    {
        public const VkStructureType TYPE = (VkStructureType)1000314006;

        public VkStructureType sType;
        public void* pNext;
        public VkCommandBuffer commandBuffer;
        public uint deviceMask; // 0 = use all devices in the group

        public static VkCommandBufferSubmitInfo New()
        {
            var ret = default(VkCommandBufferSubmitInfo);
            ret.sType = TYPE;
            return ret;
        }
    }

    // One entry per semaphore in VkSubmitInfo2.pWaitSemaphoreInfos /
    // VkSubmitInfo2.pSignalSemaphoreInfos.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSemaphoreSubmitInfo
    {
        public const VkStructureType TYPE = (VkStructureType)1000314005;

        public VkStructureType sType;
        public void* pNext;
        public VkSemaphore semaphore;
        public ulong value;     // timeline counter value; 0 for binary semaphores
        public ulong stageMask; // VkPipelineStageFlags2 (64-bit); see VkPipelineStageFlags2KHR constants below
        public uint deviceIndex;

        public static VkSemaphoreSubmitInfo New()
        {
            var ret = default(VkSemaphoreSubmitInfo);
            ret.sType = TYPE;
            return ret;
        }
    }

    // Replaces VkSubmitInfo in vkQueueSubmit2 calls.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkSubmitInfo2
    {
        public const VkStructureType TYPE = (VkStructureType)1000314004;

        public VkStructureType sType;
        public void* pNext;
        public uint flags; // VkSubmitFlags; Protected bit = 0x1, never used here
        public uint waitSemaphoreInfoCount;
        public VkSemaphoreSubmitInfo* pWaitSemaphoreInfos;
        public uint commandBufferInfoCount;
        public VkCommandBufferSubmitInfo* pCommandBufferInfos;
        public uint signalSemaphoreInfoCount;
        public VkSemaphoreSubmitInfo* pSignalSemaphoreInfos;

        public static VkSubmitInfo2 New()
        {
            var ret = default(VkSubmitInfo2);
            ret.sType = TYPE;
            return ret;
        }
    }

    // VkPipelineStageFlags2 bit constants (64-bit) used in VkSemaphoreSubmitInfo.stageMask.
    // Only the values needed in the current submission hot-path are listed.
    internal static class VkPipelineStageFlags2KHR
    {
        // Equivalent to VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT in sync1.
        public const ulong ColorAttachmentOutput = 0x0000_0000_0000_0400UL;
        // Signal all prior commands on the queue have completed.
        public const ulong AllCommands = 0x0000_0000_0001_0000UL;
    }

    // Function pointer type for vkQueueSubmit2 / vkQueueSubmit2KHR.
    internal unsafe delegate VkResult VkQueueSubmit2T(
        VkQueue queue,
        uint submitCount,
        VkSubmitInfo2* pSubmits,
        Vulkan.VkFence fence);

    // --- VK_KHR_timeline_semaphore (core in Vulkan 1.2) ---
    // Foundation for replacing the availableSubmissionFences pool with a single
    // monotonically-incrementing timeline counter. Detection-only at present;
    // hot-path migration is a separate change.

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPhysicalDeviceTimelineSemaphoreFeatures
    {
        public const VkStructureType TYPE = (VkStructureType)1000207000;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 timelineSemaphore;

        public static VkPhysicalDeviceTimelineSemaphoreFeatures New()
        {
            var ret = default(VkPhysicalDeviceTimelineSemaphoreFeatures);
            ret.sType = TYPE;
            return ret;
        }
    }

    // VK_EXT_pipeline_creation_cache_control (core in Vulkan 1.3).
    // Enables VK_PIPELINE_CREATE_FAIL_ON_PIPELINE_COMPILE_REQUIRED_BIT: callers can
    // request a pipeline without blocking if it is not already in the cache, receiving
    // VK_PIPELINE_COMPILE_REQUIRED instead of stalling the render thread for shader
    // compilation. Essential for hitching-free real-time use on mobile.
    internal unsafe struct VkPhysicalDevicePipelineCreationCacheControlFeatures
    {
        public const VkStructureType TYPE = (VkStructureType)1000297001;

        public VkStructureType sType;
        public void* pNext;
        public VkBool32 pipelineCreationCacheControl;

        public static VkPhysicalDevicePipelineCreationCacheControlFeatures New()
        {
            var ret = default(VkPhysicalDevicePipelineCreationCacheControlFeatures);
            ret.sType = TYPE;
            return ret;
        }
    }

    // --- VK_GOOGLE_display_timing ---
    // Allows scheduling presents at exact vblank offsets for minimum
    // input-to-photon latency. Android / Qualcomm-supported since Android 7.
    //
    // Usage:
    //   1. After swapchain creation, call vkGetRefreshCycleDurationGOOGLE to
    //      learn the display's vblank cadence (nanoseconds per frame).
    //   2. On each frame after a successful present, call
    //      vkGetPastPresentationTimingGOOGLE and record the latest
    //      earliestPresentTime from the returned results.
    //   3. Chain VkPresentTimesInfoGOOGLE on VkPresentInfoKHR with
    //      desiredPresentTime = lastEarliestPresentTime + refreshDuration,
    //      targeting the next vblank boundary after the previous frame.
    //      desiredPresentTime = 0 is safe and means "driver decides" — used
    //      until at least one past timing result is available.

    // No sType; plain output struct from vkGetRefreshCycleDurationGOOGLE.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VkRefreshCycleDurationGOOGLE
    {
        public ulong refreshDuration; // nanoseconds per display vblank cycle
    }

    // No sType; one element per returned past present in
    // vkGetPastPresentationTimingGOOGLE.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VkPastPresentationTimingGOOGLE
    {
        public uint presentID;
        public ulong desiredPresentTime;   // value originally requested
        public ulong actualPresentTime;    // when the image was shown to the user
        public ulong earliestPresentTime;  // earliest vblank the image could have hit
        public ulong presentMargin;        // slack before the vblank deadline
    }

    // No sType; one element per swapchain in VkPresentTimesInfoGOOGLE.pTimes.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VkPresentTimeGOOGLE
    {
        public uint presentID;
        // 0 = let driver decide; non-zero = target the vblank at or after this
        // time (nanoseconds on the same clock as actualPresentTime).
        public ulong desiredPresentTime;
    }

    // Chained on VkPresentInfoKHR.pNext to supply per-present timing targets.
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VkPresentTimesInfoGOOGLE
    {
        public const VkStructureType TYPE = (VkStructureType)1000092000;

        public VkStructureType sType;
        public void* pNext;
        public uint swapchainCount;
        public VkPresentTimeGOOGLE* pTimes; // one per entry in VkPresentInfoKHR.pSwapchains

        public static VkPresentTimesInfoGOOGLE New()
        {
            var ret = default(VkPresentTimesInfoGOOGLE);
            ret.sType = TYPE;
            return ret;
        }
    }

    internal unsafe delegate VkResult VkGetRefreshCycleDurationGOOGLET(
        VkDevice device,
        VkSwapchainKHR swapchain,
        VkRefreshCycleDurationGOOGLE* pDisplayTimingProperties);

    internal unsafe delegate VkResult VkGetPastPresentationTimingGOOGLET(
        VkDevice device,
        VkSwapchainKHR swapchain,
        uint* pPresentationTimingCount,
        VkPastPresentationTimingGOOGLE* pPresentationTimings);
}
