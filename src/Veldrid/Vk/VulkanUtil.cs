using System;
using System.Diagnostics;
using Vulkan;
using static Vulkan.VulkanNative;

namespace Veldrid.Vk
{
    internal static unsafe class VulkanUtil
    {
        private static readonly Lazy<bool> s_is_vulkan_loaded = new Lazy<bool>(tryLoadVulkan);
        private static readonly Lazy<string[]> s_instance_extensions = new Lazy<string[]>(enumerateInstanceExtensions);

        public static void CheckResult(VkResult result)
        {
            if (result != VkResult.Success) throw new VeldridException($"Unsuccessful VkResult: {result}");
        }

        public static bool TryFindMemoryType(VkPhysicalDeviceMemoryProperties memProperties, uint typeFilter, VkMemoryPropertyFlags properties, out uint typeIndex)
        {
            typeIndex = 0;

            for (int i = 0; i < memProperties.memoryTypeCount; i++)
            {
                if ((typeFilter & (1 << i)) != 0
                    && (memProperties.GetMemoryType((uint)i).propertyFlags & properties) == properties)
                {
                    typeIndex = (uint)i;
                    return true;
                }
            }

            return false;
        }

        public static string[] EnumerateInstanceLayers()
        {
            uint propCount = 0;
            var result = vkEnumerateInstanceLayerProperties(ref propCount, null);
            CheckResult(result);
            if (propCount == 0) return Array.Empty<string>();

            var props = new VkLayerProperties[propCount];
            vkEnumerateInstanceLayerProperties(ref propCount, ref props[0]);

            string[] ret = new string[propCount];

            for (int i = 0; i < propCount; i++)
            {
                fixed (byte* layerNamePtr = props[i].layerName) ret[i] = Util.GetString(layerNamePtr);
            }

            return ret;
        }

        public static string[] GetInstanceExtensions()
        {
            return s_instance_extensions.Value;
        }

        public static bool IsVulkanLoaded()
        {
            return s_is_vulkan_loaded.Value;
        }

        // Fills srcAccessMask, dstAccessMask, srcStageFlags, dstStageFlags for a layout transition
        // without emitting any barrier. Callers that need to batch multiple transitions use this
        // to accumulate barriers and OR stage masks before a single vkCmdPipelineBarrier call.
        internal static void GetTransitionParameters(
            VkImageLayout oldLayout,
            VkImageLayout newLayout,
            out VkAccessFlags srcAccessMask,
            out VkAccessFlags dstAccessMask,
            out VkPipelineStageFlags srcStageFlags,
            out VkPipelineStageFlags dstStageFlags)
        {
            srcAccessMask = VkAccessFlags.None;
            dstAccessMask = VkAccessFlags.None;
            srcStageFlags = VkPipelineStageFlags.None;
            dstStageFlags = VkPipelineStageFlags.None;

            if ((oldLayout == VkImageLayout.Undefined || oldLayout == VkImageLayout.Preinitialized) && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.FragmentShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.FragmentShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.Preinitialized && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.Preinitialized && newLayout == VkImageLayout.General)
            {
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.ComputeShader;
            }
            else if (oldLayout == VkImageLayout.Preinitialized && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.FragmentShader;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.FragmentShader;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.General)
            {
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.FragmentShader;
                dstStageFlags = VkPipelineStageFlags.ComputeShader;
            }
            else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.FragmentShader;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.FragmentShader;
            }
            else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
                dstStageFlags = VkPipelineStageFlags.FragmentShader;
            }
            else if (oldLayout == VkImageLayout.DepthStencilAttachmentOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.LateFragmentTests;
                dstStageFlags = VkPipelineStageFlags.FragmentShader;
            }
            else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.PresentSrcKHR)
            {
                srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
                dstAccessMask = VkAccessFlags.MemoryRead;
                srcStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
                dstStageFlags = VkPipelineStageFlags.BottomOfPipe;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.PresentSrcKHR)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.MemoryRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.BottomOfPipe;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.LateFragmentTests;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.ComputeShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.ComputeShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.PresentSrcKHR && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.MemoryRead;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.BottomOfPipe;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.PresentSrcKHR && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // Swapchain image released by the presentation engine, transitioning to a render target.
                // Required by the dynamic rendering path: vkCmdBeginRendering does not perform implicit
                // layout transitions the way VkRenderPass does via initialLayout/finalLayout.
                srcAccessMask = VkAccessFlags.MemoryRead;
                dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.BottomOfPipe;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // Ping-pong / post-process buffer that was sampled in the previous pass and is now
                // used as a render target in this pass.
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.FragmentShader;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.Undefined && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // First use of a freshly allocated or newly-acquired image as a render target.
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.Undefined && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
            {
                // First use of a freshly allocated depth/stencil attachment (e.g. transient swapchain depth).
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.EarlyFragmentTests;
            }
            else
                Debug.Fail("Invalid image layout transition.");
        }

        public static void TransitionImageLayout(
            VkCommandBuffer cb,
            VkImage image,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            VkImageAspectFlags aspectMask,
            VkImageLayout oldLayout,
            VkImageLayout newLayout)
        {
            Debug.Assert(oldLayout != newLayout);
            var barrier = VkImageMemoryBarrier.New();
            barrier.oldLayout = oldLayout;
            barrier.newLayout = newLayout;
            barrier.srcQueueFamilyIndex = QueueFamilyIgnored;
            barrier.dstQueueFamilyIndex = QueueFamilyIgnored;
            barrier.image = image;
            barrier.subresourceRange.aspectMask = aspectMask;
            barrier.subresourceRange.baseMipLevel = baseMipLevel;
            barrier.subresourceRange.levelCount = levelCount;
            barrier.subresourceRange.baseArrayLayer = baseArrayLayer;
            barrier.subresourceRange.layerCount = layerCount;

            GetTransitionParameters(oldLayout, newLayout,
                out barrier.srcAccessMask, out barrier.dstAccessMask,
                out var srcStageFlags, out var dstStageFlags);

            vkCmdPipelineBarrier(
                cb,
                srcStageFlags,
                dstStageFlags,
                VkDependencyFlags.None,
                0, null,
                0, null,
                1, &barrier);
        }

        private static string[] enumerateInstanceExtensions()
        {
            if (!IsVulkanLoaded()) return Array.Empty<string>();

            uint propCount = 0;
            var result = vkEnumerateInstanceExtensionProperties((byte*)null, ref propCount, null);
            if (result != VkResult.Success) return Array.Empty<string>();

            if (propCount == 0) return Array.Empty<string>();

            var props = new VkExtensionProperties[propCount];
            vkEnumerateInstanceExtensionProperties((byte*)null, ref propCount, ref props[0]);

            string[] ret = new string[propCount];

            for (int i = 0; i < propCount; i++)
            {
                fixed (byte* extensionNamePtr = props[i].extensionName) ret[i] = Util.GetString(extensionNamePtr);
            }

            return ret;
        }

        private static bool tryLoadVulkan()
        {
            try
            {
                uint propCount;
                vkEnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
                return true;
            }
            catch { return false; }
        }
    }

    internal static unsafe class VkPhysicalDeviceMemoryPropertiesEx
    {
        public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
        {
            return (&memoryProperties.memoryTypes_0)[index];
        }
    }
}
