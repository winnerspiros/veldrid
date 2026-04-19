using System.Runtime.InteropServices;
using Vulkan;

namespace Veldrid.Vk
{
    // ─── VK_KHR_dynamic_rendering ───────────────────────────────────────────

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

    // ─── VK_EXT_memory_budget ───────────────────────────────────────────────

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

    // ─── VK_EXT_host_image_copy ─────────────────────────────────────────────

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

    // ─── Function pointer delegates ─────────────────────────────────────────

    // VK_KHR_dynamic_rendering
    internal unsafe delegate void VkCmdBeginRenderingT(VkCommandBuffer commandBuffer, VkRenderingInfo* pRenderingInfo);

    internal delegate void VkCmdEndRenderingT(VkCommandBuffer commandBuffer);

    // VK_EXT_host_image_copy
    internal unsafe delegate VkResult VkCopyMemoryToImageExtT(VkDevice device, VkCopyMemoryToImageInfoEXT* pCopyMemoryToImageInfo);

    internal unsafe delegate VkResult VkTransitionImageLayoutExtT(VkDevice device, uint transitionCount, VkHostImageLayoutTransitionInfoEXT* pTransitions);
}
