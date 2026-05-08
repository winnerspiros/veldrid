using System;
using System.Diagnostics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
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
            var result = vkEnumerateInstanceLayerProperties(&propCount, null);
            CheckResult(result);
            if (propCount == 0) return Array.Empty<string>();

            var props = new VkLayerProperties[propCount];
            fixed (VkLayerProperties* propsPtr = props)
                vkEnumerateInstanceLayerProperties(&propCount, propsPtr);

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
        // to accumulate barriers and OR stage masks before a single gd.DeviceApi.vkCmdPipelineBarrier call.
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
                srcStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if ((oldLayout == VkImageLayout.Undefined || oldLayout == VkImageLayout.Preinitialized) && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if ((oldLayout == VkImageLayout.Undefined || oldLayout == VkImageLayout.Preinitialized) && newLayout == VkImageLayout.General)
            {
                srcAccessMask = VkAccessFlags.None;
                // Storage images can be both read AND written from any shader stage.
                dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
            }
            else if ((oldLayout == VkImageLayout.Undefined || oldLayout == VkImageLayout.Preinitialized) && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.ShaderRead;
                // Storage image writes can originate in any shader stage (vertex/fragment/compute).
                srcStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
                dstStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.General)
            {
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                srcStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
                // Storage image reads/writes can also happen in graphics shaders, not only compute.
                dstStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
            }
            else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
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
                dstStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
            }
            else if (oldLayout == VkImageLayout.DepthStencilAttachmentOptimal && newLayout == VkImageLayout.ShaderReadOnlyOptimal)
            {
                srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                dstAccessMask = VkAccessFlags.ShaderRead;
                srcStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
                dstStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
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
                // ColorAttachmentRead covers blend reads of the just-uploaded content.
                dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
            {
                srcAccessMask = VkAccessFlags.TransferWrite;
                // DepthStencilAttachmentRead covers depth-test reads of the uploaded content.
                dstAccessMask = VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.TransferRead;
                // Storage image writes can originate in any shader stage (vertex/fragment/compute).
                srcStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.TransferDstOptimal)
            {
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.TransferWrite;
                // Storage image writes can originate in any shader stage (vertex/fragment/compute).
                srcStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.PresentSrcKHR && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                srcAccessMask = VkAccessFlags.MemoryRead;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.BottomOfPipe;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.PresentSrcKHR && newLayout == VkImageLayout.TransferDstOptimal)
            {
                // Presentation engine has finished reading; transition to a copy destination.
                // The presentation engine's read synchronization is via semaphore (queue submit level),
                // so srcAccessMask=MemoryRead / srcStage=BottomOfPipe is sufficient to order the barrier.
                srcAccessMask = VkAccessFlags.MemoryRead;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.BottomOfPipe;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.PresentSrcKHR && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // Swapchain image released by the presentation engine, transitioning to a render target.
                // Required by the dynamic rendering path: vkCmdBeginRendering does not perform implicit
                // layout transitions the way VkRenderPass does via initialLayout/finalLayout.
                // ColorAttachmentRead covers blend reads of the swapchain image content.
                srcAccessMask = VkAccessFlags.MemoryRead;
                dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.BottomOfPipe;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // Ping-pong / post-process buffer that was sampled in the previous pass and is now
                // used as a render target in this pass.
                // ColorAttachmentRead covers blend reads that mix new output with the prior content.
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.ShaderReadOnlyOptimal && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
            {
                // A depth texture sampled in the previous frame is reused as a depth attachment
                // (e.g. shadow-map double-buffering or depth pre-pass re-binding).
                // DepthStencilAttachmentRead covers the depth-test reads of existing depth values.
                srcAccessMask = VkAccessFlags.ShaderRead;
                dstAccessMask = VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.ComputeShader;
                dstStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
            }
            else if (oldLayout == VkImageLayout.DepthStencilAttachmentOptimal && newLayout == VkImageLayout.TransferSrcOptimal)
            {
                // Depth/stencil texture being read back via CopyTexture.
                srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                dstAccessMask = VkAccessFlags.TransferRead;
                srcStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
                dstStageFlags = VkPipelineStageFlags.Transfer;
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
                dstStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
            }
            else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // A non-sampled render target was used as a CopyTexture source and stays in
                // TransferSrcOptimal (the sampled back-transition only fires when Sampled is set).
                // Return it to ColorAttachmentOptimal before the next render pass.
                // ColorAttachmentRead covers blend reads of the render target content.
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
            {
                // Same as above but for a depth/stencil attachment that was read-back via CopyTexture.
                // DepthStencilAttachmentRead covers depth-test reads of the existing depth values.
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
            }
            else if (oldLayout == VkImageLayout.DepthStencilAttachmentOptimal && newLayout == VkImageLayout.TransferDstOptimal)
            {
                // Depth/stencil attachment being overwritten via CopyTexture (e.g. restoring a saved depth buffer).
                srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                dstAccessMask = VkAccessFlags.TransferWrite;
                srcStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
                dstStageFlags = VkPipelineStageFlags.Transfer;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.ColorAttachmentOptimal)
            {
                // Storage image (General) repurposed as a color render target.
                // ColorAttachmentRead covers blend reads of the prior storage-image content.
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
                dstStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
            }
            else if (oldLayout == VkImageLayout.General && newLayout == VkImageLayout.DepthStencilAttachmentOptimal)
            {
                // Storage depth image repurposed as a depth/stencil attachment.
                // DepthStencilAttachmentRead covers depth-test reads of the existing depth values.
                srcAccessMask = VkAccessFlags.ShaderWrite;
                dstAccessMask = VkAccessFlags.DepthStencilAttachmentRead | VkAccessFlags.DepthStencilAttachmentWrite;
                srcStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
                dstStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
            }
            else if (oldLayout == VkImageLayout.ColorAttachmentOptimal && newLayout == VkImageLayout.General)
            {
                // Color render target being bound as a storage image (e.g. for compute post-process).
                srcAccessMask = VkAccessFlags.ColorAttachmentWrite;
                dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                srcStageFlags = VkPipelineStageFlags.ColorAttachmentOutput;
                dstStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
            }
            else if (oldLayout == VkImageLayout.DepthStencilAttachmentOptimal && newLayout == VkImageLayout.General)
            {
                // Depth/stencil attachment being bound as a storage image.
                srcAccessMask = VkAccessFlags.DepthStencilAttachmentWrite;
                dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                srcStageFlags = VkPipelineStageFlags.EarlyFragmentTests | VkPipelineStageFlags.LateFragmentTests;
                dstStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
            }
            else if (oldLayout == VkImageLayout.TransferSrcOptimal && newLayout == VkImageLayout.General)
            {
                // A storage image used as a CopyTexture source (no Sampled back-transition) is left in
                // TransferSrcOptimal.  When next bound as a storage image, bring it back to General.
                srcAccessMask = VkAccessFlags.TransferRead;
                dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
            }
            else if (oldLayout == VkImageLayout.TransferDstOptimal && newLayout == VkImageLayout.General)
            {
                // A storage image used as a CopyTexture destination (no Sampled back-transition) is left in
                // TransferDstOptimal.  When next bound as a storage image, bring it back to General.
                srcAccessMask = VkAccessFlags.TransferWrite;
                dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                srcStageFlags = VkPipelineStageFlags.Transfer;
                dstStageFlags = VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader;
            }
            else if ((oldLayout == VkImageLayout.Undefined || oldLayout == VkImageLayout.Preinitialized) && newLayout == VkImageLayout.PresentSrcKHR)
            {
                // First use of a newly-created swapchain image in the legacy render-pass path.
                // renderPassNoClear declares initialLayout=PresentSrcKHR, so the image must be
                // in PresentSrcKHR when vkCmdBeginRenderPass is called (Vulkan spec §12.8.2).
                // Per spec §34.5 the very first acquisition after swapchain creation returns the
                // image in VK_IMAGE_LAYOUT_UNDEFINED; after the first present, subsequent
                // acquisitions return it in PresentSrcKHR, so this barrier is only reached once
                // per swapchain image (on the very first frame after creation or recreation).
                //
                // Preinitialized is included to match the pattern of all other Undefined/
                // Preinitialized → X cases in this function. Swapchain images are always
                // created with initialLayout=Undefined (spec-mandated for optimal-tiling images),
                // so Preinitialized is never reached in practice; it is a defensive belt-and-
                // suspenders inclusion.
                //
                // srcAccessMask=None / srcStage=TopOfPipe: no prior GPU writes to make visible
                // (Undefined means contents don't matter).
                // dstAccessMask=MemoryRead / dstStage=BottomOfPipe: the render pass subpass
                // dependency (srcStageMask=BottomOfPipe) picks up ordering from here and drives
                // the implicit PresentSrcKHR→ColorAttachmentOptimal transition in the subpass.
                srcAccessMask = VkAccessFlags.None;
                dstAccessMask = VkAccessFlags.MemoryRead;
                srcStageFlags = VkPipelineStageFlags.TopOfPipe;
                dstStageFlags = VkPipelineStageFlags.BottomOfPipe;
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
            VkImageLayout newLayout,
            VkDeviceApi deviceApi)
        {
            Debug.Assert(oldLayout != newLayout);
            var barrier = new VkImageMemoryBarrier();
            barrier.oldLayout = oldLayout;
            barrier.newLayout = newLayout;
            barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.image = image;
            barrier.subresourceRange.aspectMask = aspectMask;
            barrier.subresourceRange.baseMipLevel = baseMipLevel;
            barrier.subresourceRange.levelCount = levelCount;
            barrier.subresourceRange.baseArrayLayer = baseArrayLayer;
            barrier.subresourceRange.layerCount = layerCount;

            GetTransitionParameters(oldLayout, newLayout,
                out barrier.srcAccessMask, out barrier.dstAccessMask,
                out var srcStageFlags, out var dstStageFlags);

            deviceApi.vkCmdPipelineBarrier(
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
            var result = vkEnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
            if (result != VkResult.Success) return Array.Empty<string>();

            if (propCount == 0) return Array.Empty<string>();

            var props = new VkExtensionProperties[propCount];
            fixed (VkExtensionProperties* propsPtr = props)
                vkEnumerateInstanceExtensionProperties((byte*)null, &propCount, propsPtr);

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
            return (&memoryProperties.memoryTypes[0])[index];
        }
    }
}
