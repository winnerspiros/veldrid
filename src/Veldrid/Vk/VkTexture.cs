using System.Diagnostics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkTexture : Texture
    {
        public override uint Width => width;

        public override uint Height => height;

        public override uint Depth => depth;

        public override PixelFormat Format => format;

        public override uint MipLevels { get; }

        public override uint ArrayLayers { get; }
        public uint ActualArrayLayers { get; }

        public override TextureUsage Usage { get; }

        public override TextureType Type { get; }

        public override TextureSampleCount SampleCount { get; }

        public override bool IsDisposed => destroyed;

        public VkImage OptimalDeviceImage => optimalImage;
        public Vortice.Vulkan.VkBuffer StagingBuffer => stagingBuffer;
        public VkMemoryBlock Memory => memoryBlock;

        public VkFormat VkFormat { get; }
        public VkSampleCountFlags VkSampleCount { get; }

        public ResourceRefCount RefCount { get; }
        public bool IsSwapchainTexture { get; }

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                gd.SetResourceName(this, value);
            }
        }

        private readonly VkGraphicsDevice gd;
        private readonly VkImage optimalImage;
        private readonly VkMemoryBlock memoryBlock;
        private readonly Vortice.Vulkan.VkBuffer stagingBuffer;
        private PixelFormat format; // Static for regular images -- may change for shared staging images
        private bool destroyed;

        // Immutable except for shared staging Textures.
        private uint width;
        private uint height;
        private uint depth;

        private readonly VkImageLayout[] imageLayouts;
        private string name;

        internal VkTexture(VkGraphicsDevice gd, ref TextureDescription description)
        {
            this.gd = gd;
            width = description.Width;
            height = description.Height;
            depth = description.Depth;
            MipLevels = description.MipLevels;
            ArrayLayers = description.ArrayLayers;
            bool isCubemap = (description.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap;
            ActualArrayLayers = isCubemap
                ? 6 * ArrayLayers
                : ArrayLayers;
            format = description.Format;
            Usage = description.Usage;
            Type = description.Type;
            SampleCount = description.SampleCount;
            VkSampleCount = VkFormats.VdToVkSampleCount(SampleCount);
            VkFormat = VkFormats.VdToVkPixelFormat(Format, (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);

            bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;
            bool isTransient = (Usage & TextureUsage.Transient) == TextureUsage.Transient;

            if (!isStaging)
            {
                var imageCi = new VkImageCreateInfo();
                imageCi.mipLevels = MipLevels;
                imageCi.arrayLayers = ActualArrayLayers;
                imageCi.imageType = VkFormats.VdToVkTextureType(Type);
                imageCi.extent.width = Width;
                imageCi.extent.height = Height;
                imageCi.extent.depth = Depth;
                // Vulkan spec §12.8: "If the image uses a non-VK_IMAGE_TILING_LINEAR tiling, then
                // initialLayout must be VK_IMAGE_LAYOUT_UNDEFINED." All VkTexture optimal images use
                // VK_IMAGE_TILING_OPTIMAL, so Preinitialized is a spec violation here. Using Undefined
                // for every optimal image is correct; the contents are always written before first read.
                imageCi.initialLayout = VkImageLayout.Undefined;
                imageCi.usage = VkFormats.VdToVkTextureUsage(Usage);
                imageCi.tiling = VkImageTiling.Optimal;
                imageCi.format = VkFormat;
                imageCi.flags = VkImageCreateFlags.MutableFormat;

                imageCi.samples = VkSampleCount;
                if (isCubemap) imageCi.flags |= VkImageCreateFlags.CubeCompatible;

                uint subresourceCount = MipLevels * ActualArrayLayers * Depth;
                var result = gd.DeviceApi.vkCreateImage(&imageCi, null, out optimalImage);
                CheckResult(result);

                VkMemoryRequirements memoryRequirements;
                bool prefersDedicatedAllocation;

                {
                    var memReqsInfo2 = new VkImageMemoryRequirementsInfo2();
                    memReqsInfo2.image = optimalImage;
                    var memReqs2 = new VkMemoryRequirements2();
                    var dedicatedReqs = new VkMemoryDedicatedRequirements();
                    memReqs2.pNext = &dedicatedReqs;
                    gd.DeviceApi.vkGetImageMemoryRequirements2(&memReqsInfo2, &memReqs2);
                    memoryRequirements = memReqs2.memoryRequirements;
                    prefersDedicatedAllocation = dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation;
                }

                // For transient attachments, prefer LAZILY_ALLOCATED memory so the image lives only
                // in tile RAM on tile-based GPUs (Adreno / Mali / PowerVR) — zero physical-memory
                // backing, zero DRAM bandwidth for tile→main writeback. If no such memory type is
                // available (desktop GPUs typically don't expose it), fall back to plain DeviceLocal.
                var memoryProperties = VkMemoryPropertyFlags.DeviceLocal;
                if (isTransient
                    && TryFindMemoryType(
                        gd.PhysicalDeviceMemProperties,
                        memoryRequirements.memoryTypeBits,
                        VkMemoryPropertyFlags.DeviceLocal | VkMemoryPropertyFlags.LazilyAllocated,
                        out _))
                {
                    memoryProperties |= VkMemoryPropertyFlags.LazilyAllocated;
                }

                var memoryToken = gd.MemoryManager.Allocate(
                    gd.PhysicalDeviceMemProperties,
                    memoryRequirements.memoryTypeBits,
                    memoryProperties,
                    false,
                    memoryRequirements.size,
                    memoryRequirements.alignment,
                    prefersDedicatedAllocation,
                    optimalImage,
                    Vortice.Vulkan.VkBuffer.Null);
                memoryBlock = memoryToken;
                result = gd.DeviceApi.vkBindImageMemory(optimalImage, memoryBlock.DeviceMemory, memoryBlock.Offset);
                CheckResult(result);

                imageLayouts = new VkImageLayout[subresourceCount];
                // CPU tracking starts at Undefined to mirror the spec-mandated initialLayout above.
                for (int i = 0; i < imageLayouts.Length; i++) imageLayouts[i] = VkImageLayout.Undefined;
            }
            else // isStaging
            {
                uint depthPitch = FormatHelpers.GetDepthPitch(
                    FormatHelpers.GetRowPitch(Width, Format),
                    Height,
                    Format);
                uint stagingSize = depthPitch * Depth;

                for (uint level = 1; level < MipLevels; level++)
                {
                    Util.GetMipDimensions(this, level, out uint mipWidth, out uint mipHeight, out uint mipDepth);

                    depthPitch = FormatHelpers.GetDepthPitch(
                        FormatHelpers.GetRowPitch(mipWidth, Format),
                        mipHeight,
                        Format);

                    stagingSize += depthPitch * mipDepth;
                }

                stagingSize *= ArrayLayers;

                var bufferCi = new VkBufferCreateInfo();
                bufferCi.usage = VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst;
                bufferCi.size = stagingSize;
                var result = gd.DeviceApi.vkCreateBuffer(&bufferCi, null, out stagingBuffer);
                CheckResult(result);

                VkMemoryRequirements bufferMemReqs;
                bool prefersDedicatedAllocation;

                {
                    var memReqInfo2 = new VkBufferMemoryRequirementsInfo2();
                    memReqInfo2.buffer = stagingBuffer;
                    var memReqs2 = new VkMemoryRequirements2();
                    var dedicatedReqs = new VkMemoryDedicatedRequirements();
                    memReqs2.pNext = &dedicatedReqs;
                    gd.DeviceApi.vkGetBufferMemoryRequirements2(&memReqInfo2, &memReqs2);
                    bufferMemReqs = memReqs2.memoryRequirements;
                    prefersDedicatedAllocation = dedicatedReqs.prefersDedicatedAllocation || dedicatedReqs.requiresDedicatedAllocation;
                }

                // Use "host cached" memory when available, for better performance of GPU -> CPU transfers
                var propertyFlags = VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent | VkMemoryPropertyFlags.HostCached;
                if (!TryFindMemoryType(this.gd.PhysicalDeviceMemProperties, bufferMemReqs.memoryTypeBits, propertyFlags, out _)) propertyFlags ^= VkMemoryPropertyFlags.HostCached;
                memoryBlock = this.gd.MemoryManager.Allocate(
                    this.gd.PhysicalDeviceMemProperties,
                    bufferMemReqs.memoryTypeBits,
                    propertyFlags,
                    true,
                    bufferMemReqs.size,
                    bufferMemReqs.alignment,
                    prefersDedicatedAllocation,
                    VkImage.Null,
                    stagingBuffer);

                result = gd.DeviceApi.vkBindBufferMemory(stagingBuffer, memoryBlock.DeviceMemory, memoryBlock.Offset);
                CheckResult(result);
            }

            clearIfRenderTarget();
            transitionIfSampled();
            RefCount = new ResourceRefCount(refCountedDispose);
        }

        // Used to construct Swapchain textures.
        internal VkTexture(
            VkGraphicsDevice gd,
            uint width,
            uint height,
            uint mipLevels,
            uint arrayLayers,
            VkFormat vkFormat,
            TextureUsage usage,
            TextureSampleCount sampleCount,
            VkImage existingImage)
        {
            Debug.Assert(width > 0 && height > 0);
            this.gd = gd;
            MipLevels = mipLevels;
            this.width = width;
            this.height = height;
            depth = 1;
            VkFormat = vkFormat;
            format = VkFormats.VkToVdPixelFormat(VkFormat);
            ArrayLayers = arrayLayers;
            Usage = usage;
            Type = TextureType.Texture2D;
            SampleCount = sampleCount;
            VkSampleCount = VkFormats.VdToVkSampleCount(sampleCount);
            optimalImage = existingImage;
            imageLayouts = new[] { VkImageLayout.Undefined };
            IsSwapchainTexture = true;

            clearIfRenderTarget();
            RefCount = new ResourceRefCount(DisposeCore);
        }

        internal VkSubresourceLayout GetSubresourceLayout(uint subresource)
        {
            bool staging = stagingBuffer.Handle != 0;
            Util.GetMipLevelAndArrayLayer(this, subresource, out uint mipLevel, out uint arrayLayer);

            if (!staging)
            {
                var aspect = (Usage & TextureUsage.DepthStencil) != 0
                    ? FormatHelpers.IsStencilFormat(Format)
                        ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                        : VkImageAspectFlags.Depth
                    : VkImageAspectFlags.Color;
                var imageSubresource = new VkImageSubresource
                {
                    arrayLayer = arrayLayer,
                    mipLevel = mipLevel,
                    aspectMask = aspect
                };

                gd.DeviceApi.vkGetImageSubresourceLayout(optimalImage, &imageSubresource, out var layout);
                return layout;
            }
            else
            {
                Util.GetMipDimensions(this, mipLevel, out uint mipWidth, out uint mipHeight, out uint _);
                uint rowPitch = FormatHelpers.GetRowPitch(mipWidth, Format);
                uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, mipHeight, Format);

                var layout = new VkSubresourceLayout
                {
                    rowPitch = rowPitch,
                    depthPitch = depthPitch,
                    arrayPitch = depthPitch,
                    size = depthPitch,
                    offset = Util.ComputeSubresourceOffset(this, mipLevel, arrayLayer)
                };

                return layout;
            }
        }

        internal void TransitionImageLayout(
            VkCommandBuffer cb,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            VkImageLayout newLayout)
        {
            if (stagingBuffer != Vortice.Vulkan.VkBuffer.Null) return;

            var oldLayout = imageLayouts[CalculateSubresource(baseMipLevel, baseArrayLayer)];
#if DEBUG
            for (uint level = 0; level < levelCount; level++)
            {
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    if (imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] != oldLayout)
                        throw new VeldridException("Unexpected image layout.");
                }
            }
#endif
            if (oldLayout != newLayout)
            {
                VkImageAspectFlags aspectMask;

                if ((Usage & TextureUsage.DepthStencil) != 0)
                {
                    aspectMask = FormatHelpers.IsStencilFormat(Format)
                        ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                        : VkImageAspectFlags.Depth;
                }
                else
                    aspectMask = VkImageAspectFlags.Color;

                VulkanUtil.TransitionImageLayout(
                    cb,
                    OptimalDeviceImage,
                    baseMipLevel,
                    levelCount,
                    baseArrayLayer,
                    layerCount,
                    aspectMask,
                    imageLayouts[CalculateSubresource(baseMipLevel, baseArrayLayer)],
                    newLayout,
                    gd.DeviceApi);

                for (uint level = 0; level < levelCount; level++)
                {
                    for (uint layer = 0; layer < layerCount; layer++) imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] = newLayout;
                }
            }
        }

        // Fills a VkImageMemoryBarrier for transitioning from the current layout to newLayout and
        // updates imageLayouts so the texture tracks the new state immediately. Does NOT emit any
        // gd.DeviceApi.vkCmdPipelineBarrier — callers are expected to accumulate several barriers and flush them
        // in a single batched gd.DeviceApi.vkCmdPipelineBarrier call (see VkCommandList.flushTransitionBarriers).
        // Returns false (no-op) when the image is a staging buffer or is already in newLayout.
        internal bool TryGetLayoutTransitionBarrier(
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            VkImageLayout newLayout,
            out VkImageMemoryBarrier barrier,
            out VkPipelineStageFlags srcStage,
            out VkPipelineStageFlags dstStage)
        {
            barrier = default;
            srcStage = default;
            dstStage = default;

            if (stagingBuffer != Vortice.Vulkan.VkBuffer.Null) return false;

            // Scan ALL requested subresources to find the first one that needs transitioning.
            // Reading only mip 0 would silently skip the barrier when mip 0 is already in
            // newLayout but other mip levels (e.g. after a partial CopyTexture or
            // GenerateMipmaps) are still in their previous layout.
            VkImageLayout oldLayout = newLayout;
            bool needsTransition = false;
            for (uint level = 0; level < levelCount && !needsTransition; level++)
            {
                for (uint layer = 0; layer < layerCount && !needsTransition; layer++)
                {
                    var subLayout = imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)];
                    if (subLayout != newLayout)
                    {
                        oldLayout = subLayout;
                        needsTransition = true;
                    }
                }
            }

            if (!needsTransition) return false;

            VkImageAspectFlags aspectMask;

            if ((Usage & TextureUsage.DepthStencil) != 0)
            {
                aspectMask = FormatHelpers.IsStencilFormat(Format)
                    ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                    : VkImageAspectFlags.Depth;
            }
            else
                aspectMask = VkImageAspectFlags.Color;

            VulkanUtil.GetTransitionParameters(oldLayout, newLayout,
                out var srcAccess, out var dstAccess, out srcStage, out dstStage);

            barrier = new VkImageMemoryBarrier();
            barrier.oldLayout = oldLayout;
            barrier.newLayout = newLayout;
            barrier.srcAccessMask = srcAccess;
            barrier.dstAccessMask = dstAccess;
            barrier.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            barrier.image = OptimalDeviceImage;
            barrier.subresourceRange.aspectMask = aspectMask;
            barrier.subresourceRange.baseMipLevel = baseMipLevel;
            barrier.subresourceRange.levelCount = levelCount;
            barrier.subresourceRange.baseArrayLayer = baseArrayLayer;
            barrier.subresourceRange.layerCount = layerCount;

            for (uint level = 0; level < levelCount; level++)
            {
                for (uint layer = 0; layer < layerCount; layer++)
                    imageLayouts[CalculateSubresource(baseMipLevel + level, baseArrayLayer + layer)] = newLayout;
            }

            return true;
        }

        internal VkImageLayout GetImageLayout(uint mipLevel, uint arrayLayer)
        {
            return imageLayouts[CalculateSubresource(mipLevel, arrayLayer)];
        }

        internal void SetStagingDimensions(uint width, uint height, uint depth, PixelFormat format)
        {
            Debug.Assert(stagingBuffer != Vortice.Vulkan.VkBuffer.Null);
            Debug.Assert(Usage == TextureUsage.Staging);
            this.width = width;
            this.height = height;
            this.depth = depth;
            this.format = format;
        }

        internal void SetImageLayout(uint mipLevel, uint arrayLayer, VkImageLayout layout)
        {
            imageLayouts[CalculateSubresource(mipLevel, arrayLayer)] = layout;
        }

        private void clearIfRenderTarget()
        {
            // Transient images carry only VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT — no
            // TRANSFER_DST_BIT. gd.DeviceApi.vkCmdClearDepthStencilImage/gd.DeviceApi.vkCmdClearColorImage both
            // require TRANSFER_DST_BIT (Vulkan spec §19.1). Skip the initialisation clear;
            // the first render pass uses loadOp=Clear (initialLayout=Undefined) and handles
            // this correctly on tile-based GPUs.
            if ((Usage & TextureUsage.Transient) != 0)
                return;

            // Swapchain images must NOT be used in any command buffer before the application
            // has acquired them via vkAcquireNextImageKHR (Vulkan spec §34.5: "An application
            // must acquire a presentable image before its contents can be read from or written
            // to.").  Pre-clearing an unacquired image violates the spec and triggers
            // validation errors on strict implementations (MoltenVK, Android validation layers).
            //
            // Per Vulkan spec §34.5, the first acquisition after swapchain creation returns the
            // image in VK_IMAGE_LAYOUT_UNDEFINED. Subsequent acquisitions (after the first
            // present) return it in the layout it was left in — typically PresentSrcKHR (after
            // TransitionToFinalLayout in End()). The first render pass on the first frame handles
            // the Undefined case correctly without any pre-clear:
            //   • Dynamic rendering: beginCurrentDynamicRendering transitions Undefined →
            //     ColorAttachmentOptimal (case handled in GetTransitionParameters).
            //   • Legacy render pass: beginCurrentRenderPass transitions Undefined →
            //     PresentSrcKHR before vkCmdBeginRenderPass (case in GetTransitionParameters)
            //     so renderPassNoClear's initialLayout=PresentSrcKHR is satisfied.
            // Applications are always expected to issue an explicit clear on first use of the
            // swapchain (e.g. via ClearColorTarget), so no initial clear is needed here.
            if (IsSwapchainTexture)
                return;

            // If the image is going to be used as a render target, we need to clear the data before its first use.
            if ((Usage & TextureUsage.RenderTarget) != 0)
                gd.ClearColorTexture(this, new VkClearColorValue(0, 0, 0, 0));
            else if ((Usage & TextureUsage.DepthStencil) != 0)
                gd.ClearDepthTexture(this, new VkClearDepthStencilValue(0, 0));
        }

        private void transitionIfSampled()
        {
            if ((Usage & TextureUsage.Sampled) != 0) gd.TransitionImageLayout(this, VkImageLayout.ShaderReadOnlyOptimal);
        }

        private void refCountedDispose()
        {
            if (!destroyed)
            {
                base.Dispose();

                destroyed = true;

                bool isStaging = (Usage & TextureUsage.Staging) == TextureUsage.Staging;
                if (isStaging)
                    gd.DeviceApi.vkDestroyBuffer(stagingBuffer, null);
                else
                    gd.DeviceApi.vkDestroyImage(optimalImage, null);

                if (memoryBlock.DeviceMemory.Handle != 0) gd.MemoryManager.Free(memoryBlock);
            }
        }

        private protected override void DisposeCore()
        {
            RefCount.Decrement();
        }
    }
}
