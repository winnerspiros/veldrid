using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;
namespace Veldrid.Vk
{
    internal unsafe class VkCommandList : CommandList
    {
        public VkCommandPool CommandPool => pool;
        public VkCommandBuffer CommandBuffer { get; private set; }

        public ResourceRefCount RefCount { get; }

        public override bool IsDisposed => destroyed;

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
        // Accumulated image-memory barriers for the next batchedgd.DeviceApi.vkCmdPipelineBarrier flush.
        // Reused every preDrawCommand / preDispatchCommand call — no per-frame allocation.
        private readonly List<VkImageMemoryBarrier> imageBarrierBatch = new List<VkImageMemoryBarrier>(32);
        private VkPipelineStageFlags barrierBatchSrcStage;
        private VkPipelineStageFlags barrierBatchDstStage;

        private readonly Lock commandBufferListLock = new Lock();
        private readonly Queue<VkCommandBuffer> availableCommandBuffers = new Queue<VkCommandBuffer>();
        private readonly List<VkCommandBuffer> submittedCommandBuffers = new List<VkCommandBuffer>();
        private readonly Lock stagingLock = new Lock();
        private readonly Dictionary<VkCommandBuffer, StagingResourceInfo> submittedStagingInfos = new Dictionary<VkCommandBuffer, StagingResourceInfo>();
        private readonly List<StagingResourceInfo> availableStagingInfos = new List<StagingResourceInfo>();
        private readonly List<VkBuffer> availableStagingBuffers = new List<VkBuffer>();
        private readonly VkCommandPool pool;
        private bool destroyed;

        private bool commandBufferBegun;
        private bool commandBufferEnded;
        private VkRect2D[] scissorRects = Array.Empty<VkRect2D>();
        private VkViewport[] cachedViewports = Array.Empty<VkViewport>();

        private VkClearValue[] clearValues = Array.Empty<VkClearValue>();
        private bool[] validColorClearValues = Array.Empty<bool>();
        private VkClearValue? depthClearValue;

        // Graphics State
        private VkFramebufferBase currentFramebuffer;
        private bool currentFramebufferEverActive;
        private VkRenderPass activeRenderPass;
        private VkPipeline currentGraphicsPipeline;
        private BoundResourceSetInfo[] currentGraphicsResourceSets = Array.Empty<BoundResourceSetInfo>();
        private bool[] graphicsResourceSetsChanged;

        // Cached vertex / index buffer state — skip redundant vkCmdBind* calls when the same
        // buffer+offset is re-submitted on consecutive draw calls or across resource-set changes.
        private VkBuffer[] cachedVertexBuffers = Array.Empty<VkBuffer>();
        private ulong[] cachedVertexOffsets = Array.Empty<ulong>();
        private VkBuffer cachedIndexBuffer;
        private ulong cachedIndexBufferOffset;
        private VkIndexType cachedIndexType;

        private bool newFramebuffer; // Render pass cycle state

        // Sentinel value used in activeRenderPass to indicate that dynamic rendering
        // (vkCmdBeginRendering) is active rather than a traditional VkRenderPass.
        private static readonly VkRenderPass dynamicRenderingSentinel = new VkRenderPass(ulong.MaxValue);

        // Compute State
        private VkPipeline currentComputePipeline;
        private BoundResourceSetInfo[] currentComputeResourceSets = Array.Empty<BoundResourceSetInfo>();
        private bool[] computeResourceSetsChanged;
        private string name;

        private StagingResourceInfo currentStagingInfo;

        public VkCommandList(VkGraphicsDevice gd, ref CommandListDescription description)
            : base(ref description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
        {
            this.gd = gd;
            var poolCi = new VkCommandPoolCreateInfo();
            poolCi.flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
            poolCi.queueFamilyIndex = gd.GraphicsQueueIndex;
            var result = gd.DeviceApi.vkCreateCommandPool(&poolCi, null, out pool);
            CheckResult(result);

            CommandBuffer = getNextCommandBuffer();
            RefCount = new ResourceRefCount(disposeCore);
        }

        #region Disposal

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        #endregion

        public void CommandBufferSubmitted(VkCommandBuffer cb)
        {
            RefCount.Increment();
            foreach (var rrc in currentStagingInfo.Resources) rrc.Increment();

            submittedStagingInfos.Add(cb, currentStagingInfo);
            currentStagingInfo = null;
        }

        public void CommandBufferCompleted(VkCommandBuffer completedCb)
        {
            lock (commandBufferListLock)
            {
                // Each CB appears at most once in the list; use Remove to avoid the
                // unnecessary O(n) continuation and the stale i-- bookkeeping.
                if (submittedCommandBuffers.Remove(completedCb))
                {
                    // Reset the command buffer immediately after the GPU fence signals so the
                    // Vulkan validation layer no longer considers it as referencing any resources
                    // (images, buffers, etc.). Without an immediate reset, destroying resources
                    // that the CB recorded against triggers VUID-vkDestroyImage-image-01000 /
                    // VUID-vkDestroyBuffer-buffer-00922 even though the GPU work is complete.
                    // vkResetCommandBuffer is safe to call here: the associated fence has already
                    // signaled, meaning all GPU access to those resources has finished.
                    var resetResult = gd.DeviceApi.vkResetCommandBuffer(completedCb, VkCommandBufferResetFlags.None);
                    CheckResult(resetResult);
                    availableCommandBuffers.Enqueue(completedCb);
                }
            }

            lock (stagingLock)
            {
                if (submittedStagingInfos.TryGetValue(completedCb, out var info))
                {
                    // Call the lock-free core directly — we already hold stagingLock and
                    // System.Threading.Lock is non-reentrant, so calling recycleStagingInfo()
                    // (which also acquires stagingLock) would deadlock.
                    recycleStagingInfoCore(info);
                    submittedStagingInfos.Remove(completedCb);
                }
            }

            RefCount.Decrement();
        }

        public override void Begin()
        {
            if (commandBufferBegun)
            {
                throw new VeldridException(
                    "CommandList must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
            }

            if (commandBufferEnded)
            {
                commandBufferEnded = false;
                CommandBuffer = getNextCommandBuffer();
                if (currentStagingInfo != null) recycleStagingInfo(currentStagingInfo);
            }

            currentStagingInfo = getStagingResourceInfo();

            var beginInfo = new VkCommandBufferBeginInfo();
            beginInfo.flags = VkCommandBufferUsageFlags.OneTimeSubmit;
            gd.DeviceApi.vkBeginCommandBuffer(CommandBuffer, &beginInfo);
            commandBufferBegun = true;

            ClearCachedState();
            currentFramebuffer = null;
            currentGraphicsPipeline = null;
            clearSets(currentGraphicsResourceSets);
            Util.ClearArray(scissorRects);
            Util.ClearArray(cachedViewports);
            Util.ClearArray(cachedVertexBuffers);
            Util.ClearArray(cachedVertexOffsets);
            cachedIndexBuffer = null;
            cachedIndexBufferOffset = 0;
            cachedIndexType = default;

            currentComputePipeline = null;
            clearSets(currentComputeResourceSets);
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            preDispatchCommand();

            gd.DeviceApi.vkCmdDispatch(CommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public override void End()
        {
            if (!commandBufferBegun) throw new VeldridException("CommandBuffer must have been started before End() may be called.");

            commandBufferBegun = false;
            commandBufferEnded = true;

            if (!currentFramebufferEverActive && currentFramebuffer != null)
                beginCurrentRenderPass();

            if (activeRenderPass != VkRenderPass.Null)
            {
                endCurrentRenderPass();
                currentFramebuffer!.TransitionToFinalLayout(CommandBuffer);
            }
            else if (currentFramebufferEverActive && currentFramebuffer != null)
            {
                // The render pass was already ended mid-frame (e.g. by ensureNoRenderPass()
                // called from CopyTexture or Dispatch) and no SetFramebufferCore was called
                // after (which would have reset currentFramebufferEverActive=false). In that
                // case endCurrentRenderPass() set activeRenderPass=Null without calling
                // TransitionToFinalLayout, leaving the framebuffer attachments in their
                // intermediate layout (ColorAttachmentOptimal for colour, or whatever layout
                // a copy left them in). We must emit the final-layout transition now so that
                // the swapchain image is in PresentSrcKHR before vkQueuePresentKHR, and
                // sampled attachments are in ShaderReadOnlyOptimal before the next sample.
                currentFramebuffer.TransitionToFinalLayout(CommandBuffer);
            }

            gd.DeviceApi.vkEndCommandBuffer(CommandBuffer);
            submittedCommandBuffers.Add(CommandBuffer);
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            if (index == 0 || gd.Features.MultipleViewports)
            {
                var scissor = new VkRect2D(new VkOffset2D((int)x, (int)y), new VkExtent2D(width, height));

                if (scissorRects[index] != scissor)
                {
                    scissorRects[index] = scissor;
                    gd.DeviceApi.vkCmdSetScissor(CommandBuffer, index, 1, &scissor);
                }
            }
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            if (index == 0 || gd.Features.MultipleViewports)
            {
                float vpY = gd.IsClipSpaceYInverted
                    ? viewport.Y
                    : viewport.Height + viewport.Y;
                float vpHeight = gd.IsClipSpaceYInverted
                    ? viewport.Height
                    : -viewport.Height;

                var vkViewport = new VkViewport
                {
                    x = viewport.X,
                    y = vpY,
                    width = viewport.Width,
                    height = vpHeight,
                    minDepth = viewport.MinDepth,
                    maxDepth = viewport.MaxDepth
                };

                ref var cached = ref cachedViewports[index];
                if (cached.x != vkViewport.x
                    || cached.y != vkViewport.y
                    || cached.width != vkViewport.width
                    || cached.height != vkViewport.height
                    || cached.minDepth != vkViewport.minDepth
                    || cached.maxDepth != vkViewport.maxDepth)
                {
                    cached = vkViewport;
                    gd.DeviceApi.vkCmdSetViewport(CommandBuffer, index, 1, &vkViewport);
                }
            }
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            VkBuffer stagingBuffer = getStagingBuffer(sizeInBytes);
            gd.UpdateBuffer(stagingBuffer, 0, source, sizeInBytes);
            CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);
        }

        protected override void CopyBufferCore(
            DeviceBuffer source,
            uint sourceOffset,
            DeviceBuffer destination,
            uint destinationOffset,
            uint sizeInBytes)
        {
            ensureNoRenderPass();

            VkBuffer srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
            currentStagingInfo.Resources.Add(srcVkBuffer.RefCount);
            VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);
            currentStagingInfo.Resources.Add(dstVkBuffer.RefCount);

            VkBufferCopy region = new VkBufferCopy
            {
                srcOffset = sourceOffset,
                dstOffset = destinationOffset,
                size = sizeInBytes
            };

            gd.DeviceApi.vkCmdCopyBuffer(CommandBuffer, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, &region);

            // Build access/stage masks covering all ways the destination buffer may be consumed.
            var dstAccess = VkAccessFlags.None;
            var dstStage = VkPipelineStageFlags.None;
            var destUsage = destination.Usage;

            if ((destUsage & BufferUsage.UniformBuffer) != 0)
            {
                dstAccess |= VkAccessFlags.UniformRead;
                dstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
            }

            if ((destUsage & BufferUsage.VertexBuffer) != 0)
            {
                dstAccess |= VkAccessFlags.VertexAttributeRead;
                dstStage |= VkPipelineStageFlags.VertexInput;
            }

            if ((destUsage & BufferUsage.IndexBuffer) != 0)
            {
                dstAccess |= VkAccessFlags.IndexRead;
                dstStage |= VkPipelineStageFlags.VertexInput;
            }

            if ((destUsage & BufferUsage.StructuredBufferReadOnly) != 0)
            {
                dstAccess |= VkAccessFlags.ShaderRead;
                dstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
            }

            if ((destUsage & BufferUsage.StructuredBufferReadWrite) != 0)
            {
                // Storage RW buffers can be both read and written by shaders; include ShaderWrite
                // so the barrier covers subsequent shader writes that follow the transfer write.
                dstAccess |= VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                dstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
            }

            if ((destUsage & BufferUsage.IndirectBuffer) != 0)
            {
                dstAccess |= VkAccessFlags.IndirectCommandRead;
                dstStage |= VkPipelineStageFlags.DrawIndirect;
            }

            // Fallback for buffers with no explicit GPU-read usage (e.g. Staging-only).
            if (dstAccess == VkAccessFlags.None)
            {
                dstAccess = VkAccessFlags.MemoryRead;
                dstStage = VkPipelineStageFlags.AllCommands;
            }

            VkMemoryBarrier barrier;
            barrier.sType = VkStructureType.MemoryBarrier;
            barrier.srcAccessMask = VkAccessFlags.TransferWrite;
            barrier.dstAccessMask = dstAccess;
            barrier.pNext = null;
            gd.DeviceApi.vkCmdPipelineBarrier(
                CommandBuffer,
                VkPipelineStageFlags.Transfer,
                dstStage,
                VkDependencyFlags.None,
                1, &barrier,
                0, null,
                0, null);
        }

        protected override void CopyTextureCore(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            ensureNoRenderPass();
            CopyTextureCore_VkCommandBuffer(
                CommandBuffer,
                gd.DeviceApi,
                source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
                destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
                width, height, depth, layerCount);

            VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
            currentStagingInfo.Resources.Add(srcVkTexture.RefCount);
            VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);
            currentStagingInfo.Resources.Add(dstVkTexture.RefCount);
        }

        internal static void CopyTextureCore_VkCommandBuffer(
            VkCommandBuffer cb,
            VkDeviceApi deviceApi,
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel,
            uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel,
            uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            var srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
            var dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);

            bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
            bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

            if (!sourceIsStaging && !destIsStaging)
            {
                var srcAspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                    ? FormatHelpers.IsStencilFormat(srcVkTexture.Format)
                        ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                        : VkImageAspectFlags.Depth
                    : VkImageAspectFlags.Color;

                var srcSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = srcAspect,
                    layerCount = layerCount,
                    mipLevel = srcMipLevel,
                    baseArrayLayer = srcBaseArrayLayer
                };

                var dstAspect = (dstVkTexture.Usage & TextureUsage.DepthStencil) != 0
                    ? FormatHelpers.IsStencilFormat(dstVkTexture.Format)
                        ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                        : VkImageAspectFlags.Depth
                    : VkImageAspectFlags.Color;

                var dstSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = dstAspect,
                    layerCount = layerCount,
                    mipLevel = dstMipLevel,
                    baseArrayLayer = dstBaseArrayLayer
                };

                var region = new VkImageCopy
                {
                    srcOffset = new VkOffset3D { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                    dstOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                    srcSubresource = srcSubresource,
                    dstSubresource = dstSubresource,
                    extent = new VkExtent3D { width = width, height = height, depth = depth }
                };

                // Batch pre-copy transitions (src→TransferSrcOptimal, dst→TransferDstOptimal) into
                // a single vkCmdPipelineBarrier call.  Use per-layer calls (layerCount=1 each) rather
                // than a single range call covering all layers:  TryGetLayoutTransitionBarrier uses
                // the first mismatched layer's oldLayout for the entire range, which is a Vulkan spec
                // violation (§12.8) when array layers have mixed layouts after partial prior copies.
                // This mirrors the fix applied to appendTransitions.
                {
                    var preCopyBarriers = stackalloc VkImageMemoryBarrier[(int)(2 * layerCount)];
                    int n = 0;
                    VkPipelineStageFlags preSrc = VkPipelineStageFlags.None, preDst = VkPipelineStageFlags.None;

                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        if (srcVkTexture.TryGetLayoutTransitionBarrier(srcMipLevel, 1, srcBaseArrayLayer + layer, 1,
                                VkImageLayout.TransferSrcOptimal, out var srcPre, out var ss, out var sd))
                        { preCopyBarriers[n++] = srcPre; preSrc |= ss; preDst |= sd; }

                        if (dstVkTexture.TryGetLayoutTransitionBarrier(dstMipLevel, 1, dstBaseArrayLayer + layer, 1,
                                VkImageLayout.TransferDstOptimal, out var dstPre, out var ds, out var dd))
                        { preCopyBarriers[n++] = dstPre; preSrc |= ds; preDst |= dd; }
                    }

                    if (n > 0)
                        deviceApi.vkCmdPipelineBarrier(cb, preSrc, preDst, VkDependencyFlags.None,
                            0, null, 0, null, (uint)n, preCopyBarriers);
                }

                deviceApi.vkCmdCopyImage(
                    cb,
                    srcVkTexture.OptimalDeviceImage,
                    VkImageLayout.TransferSrcOptimal,
                    dstVkTexture.OptimalDeviceImage,
                    VkImageLayout.TransferDstOptimal,
                    1,
                    &region);

                // Batch post-copy back-transitions (sampled textures only) into a single call.
                // Also uses per-layer calls for the same spec-correctness reason as the pre-copy barriers.
                {
                    var postCopyBarriers = stackalloc VkImageMemoryBarrier[(int)(2 * layerCount)];
                    int n = 0;
                    VkPipelineStageFlags postSrc = VkPipelineStageFlags.None, postDst = VkPipelineStageFlags.None;

                    if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
                    {
                        for (uint layer = 0; layer < layerCount; layer++)
                        {
                            if (srcVkTexture.TryGetLayoutTransitionBarrier(srcMipLevel, 1, srcBaseArrayLayer + layer, 1,
                                    VkImageLayout.ShaderReadOnlyOptimal, out var sb, out var ss, out var sd))
                            { postCopyBarriers[n++] = sb; postSrc |= ss; postDst |= sd; }
                        }
                    }

                    if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
                    {
                        for (uint layer = 0; layer < layerCount; layer++)
                        {
                            if (dstVkTexture.TryGetLayoutTransitionBarrier(dstMipLevel, 1, dstBaseArrayLayer + layer, 1,
                                    VkImageLayout.ShaderReadOnlyOptimal, out var db, out var ds, out var dd))
                            { postCopyBarriers[n++] = db; postSrc |= ds; postDst |= dd; }
                        }
                    }

                    if (n > 0)
                        deviceApi.vkCmdPipelineBarrier(cb, postSrc, postDst, VkDependencyFlags.None,
                            0, null, 0, null, (uint)n, postCopyBarriers);
                }
            }
            else if (sourceIsStaging && !destIsStaging)
            {
                var srcBuffer = srcVkTexture.StagingBuffer;
                var dstImage = dstVkTexture.OptimalDeviceImage;

                // Pre-copy: transition destination layers to TransferDstOptimal.
                // Use per-layer TryGetLayoutTransitionBarrier (matching the optimal→optimal path)
                // instead of a single range TransitionImageLayout call: the destination layers may
                // be in mixed layouts after partial prior copies (e.g. one layer was a render target,
                // another was never used). A range barrier with the first layer's oldLayout applied
                // to all layers is a Vulkan spec violation (§12.8) and corrupts tile-cache state on
                // TBDR GPUs. Batch all resulting barriers into a single vkCmdPipelineBarrier call.
                {
                    var preCopyBarriers = stackalloc VkImageMemoryBarrier[(int)layerCount];
                    int n = 0;
                    VkPipelineStageFlags preSrc = VkPipelineStageFlags.None, preDst = VkPipelineStageFlags.None;

                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        if (dstVkTexture.TryGetLayoutTransitionBarrier(dstMipLevel, 1, dstBaseArrayLayer + layer, 1,
                                VkImageLayout.TransferDstOptimal, out var b, out var s, out var d))
                        { preCopyBarriers[n++] = b; preSrc |= s; preDst |= d; }
                    }

                    if (n > 0)
                        deviceApi.vkCmdPipelineBarrier(cb, preSrc, preDst, VkDependencyFlags.None,
                            0, null, 0, null, (uint)n, preCopyBarriers);
                }

                var dstAspect = (dstVkTexture.Usage & TextureUsage.DepthStencil) != 0
                    ? FormatHelpers.IsStencilFormat(dstVkTexture.Format)
                        ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                        : VkImageAspectFlags.Depth
                    : VkImageAspectFlags.Color;

                Util.GetMipDimensions(srcVkTexture, srcMipLevel, out uint mipWidth, out uint mipHeight, out uint _);
                uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
                uint bufferRowLength = Math.Max(mipWidth, blockSize);
                uint bufferImageHeight = Math.Max(mipHeight, blockSize);
                uint compressedX = srcX / blockSize;
                uint compressedY = srcY / blockSize;
                uint blockSizeInBytes = blockSize == 1
                    ? FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format)
                    : FormatHelpers.GetBlockSizeInBytes(srcVkTexture.Format);
                uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcVkTexture.Format);
                uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcVkTexture.Format);

                uint copyWidth = Math.Min(width, mipWidth);
                uint copyheight = Math.Min(height, mipHeight);

                // Emit one VkBufferImageCopy region per array layer. A single region with
                // layerCount > 1 would only be correct when all layers are contiguous at stride
                // (bufferRowLength × bufferImageHeight × blockSizeInBytes) — matching the layout
                // that vkCmdCopyBufferToImage implicitly assumes. But the staging buffer
                // interleaves ALL mip levels of each layer together
                // (layout = ComputeArrayLayerOffset + ComputeMipOffset), so when MipLevels > 1
                // the per-layer stride in the buffer is larger than the single-mip depthPitch.
                // Using one region per layer with the correct per-layer bufferOffset (from
                // GetSubresourceLayout) mirrors the GPU→staging path at the copy-image-to-buffer
                // site and is always correct regardless of MipLevels.
                var regions = stackalloc VkBufferImageCopy[(int)layerCount];

                for (uint layer = 0; layer < layerCount; layer++)
                {
                    var srcLayout = srcVkTexture.GetSubresourceLayout(
                        srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer + layer));

                    regions[layer] = new VkBufferImageCopy
                    {
                        bufferOffset = srcLayout.offset
                                       + srcZ * depthPitch
                                       + compressedY * rowPitch
                                       + compressedX * blockSizeInBytes,
                        bufferRowLength = bufferRowLength,
                        bufferImageHeight = bufferImageHeight,
                        imageExtent = new VkExtent3D { width = copyWidth, height = copyheight, depth = depth },
                        imageOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                        imageSubresource = new VkImageSubresourceLayers
                        {
                            aspectMask = dstAspect,
                            layerCount = 1,
                            mipLevel = dstMipLevel,
                            baseArrayLayer = dstBaseArrayLayer + layer
                        }
                    };
                }

                deviceApi.vkCmdCopyBufferToImage(cb, srcBuffer, dstImage, VkImageLayout.TransferDstOptimal, layerCount, regions);

                // Post-copy: transition sampled destination layers back to ShaderReadOnlyOptimal.
                // Also per-layer for the same spec-correctness reason as the pre-copy barriers.
                if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    var postCopyBarriers = stackalloc VkImageMemoryBarrier[(int)layerCount];
                    int n = 0;
                    VkPipelineStageFlags postSrc = VkPipelineStageFlags.None, postDst = VkPipelineStageFlags.None;

                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        if (dstVkTexture.TryGetLayoutTransitionBarrier(dstMipLevel, 1, dstBaseArrayLayer + layer, 1,
                                VkImageLayout.ShaderReadOnlyOptimal, out var b, out var s, out var d))
                        { postCopyBarriers[n++] = b; postSrc |= s; postDst |= d; }
                    }

                    if (n > 0)
                        deviceApi.vkCmdPipelineBarrier(cb, postSrc, postDst, VkDependencyFlags.None,
                            0, null, 0, null, (uint)n, postCopyBarriers);
                }
            }
            else if (!sourceIsStaging)
            {
                var srcImage = srcVkTexture.OptimalDeviceImage;

                // Pre-copy: transition source layers to TransferSrcOptimal.
                // Use per-layer TryGetLayoutTransitionBarrier (same spec-correctness reason as the
                // optimal→optimal path): source layers may be in mixed layouts (e.g. one rendered
                // to, another still in ShaderReadOnlyOptimal). A range barrier with the wrong
                // oldLayout for any layer is a Vulkan spec violation (§12.8).
                {
                    var preCopyBarriers = stackalloc VkImageMemoryBarrier[(int)layerCount];
                    int n = 0;
                    VkPipelineStageFlags preSrc = VkPipelineStageFlags.None, preDst = VkPipelineStageFlags.None;

                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        if (srcVkTexture.TryGetLayoutTransitionBarrier(srcMipLevel, 1, srcBaseArrayLayer + layer, 1,
                                VkImageLayout.TransferSrcOptimal, out var b, out var s, out var d))
                        { preCopyBarriers[n++] = b; preSrc |= s; preDst |= d; }
                    }

                    if (n > 0)
                        deviceApi.vkCmdPipelineBarrier(cb, preSrc, preDst, VkDependencyFlags.None,
                            0, null, 0, null, (uint)n, preCopyBarriers);
                }

                var dstBuffer = dstVkTexture.StagingBuffer;

                var aspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                    ? FormatHelpers.IsStencilFormat(srcVkTexture.Format)
                        ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                        : VkImageAspectFlags.Depth
                    : VkImageAspectFlags.Color;

                Util.GetMipDimensions(dstVkTexture, dstMipLevel, out uint mipWidth, out uint mipHeight, out uint _);
                uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
                uint bufferRowLength = Math.Max(mipWidth, blockSize);
                uint bufferImageHeight = Math.Max(mipHeight, blockSize);
                uint compressedDstX = dstX / blockSize;
                uint compressedDstY = dstY / blockSize;
                uint blockSizeInBytes = blockSize == 1
                    ? FormatSizeHelpers.GetSizeInBytes(dstVkTexture.Format)
                    : FormatHelpers.GetBlockSizeInBytes(dstVkTexture.Format);
                uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, dstVkTexture.Format);
                uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, dstVkTexture.Format);

                var layers = stackalloc VkBufferImageCopy[(int)layerCount];

                for (uint layer = 0; layer < layerCount; layer++)
                {
                    var dstLayout = dstVkTexture.GetSubresourceLayout(
                        dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer));

                    var srcSubresource = new VkImageSubresourceLayers
                    {
                        aspectMask = aspect,
                        layerCount = 1,
                        mipLevel = srcMipLevel,
                        baseArrayLayer = srcBaseArrayLayer + layer
                    };

                    var region = new VkBufferImageCopy
                    {
                        bufferRowLength = bufferRowLength,
                        bufferImageHeight = bufferImageHeight,
                        bufferOffset = dstLayout.offset
                                       + dstZ * depthPitch
                                       + compressedDstY * rowPitch
                                       + compressedDstX * blockSizeInBytes,
                        imageExtent = new VkExtent3D { width = width, height = height, depth = depth },
                        imageOffset = new VkOffset3D { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                        imageSubresource = srcSubresource
                    };

                    layers[layer] = region;
                }

                deviceApi.vkCmdCopyImageToBuffer(cb, srcImage, VkImageLayout.TransferSrcOptimal, dstBuffer, layerCount, layers);

                // Post-copy: transition sampled source layers back to ShaderReadOnlyOptimal.
                // Per-layer for the same spec-correctness reason as the pre-copy barriers.
                if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    var postCopyBarriers = stackalloc VkImageMemoryBarrier[(int)layerCount];
                    int n = 0;
                    VkPipelineStageFlags postSrc = VkPipelineStageFlags.None, postDst = VkPipelineStageFlags.None;

                    for (uint layer = 0; layer < layerCount; layer++)
                    {
                        if (srcVkTexture.TryGetLayoutTransitionBarrier(srcMipLevel, 1, srcBaseArrayLayer + layer, 1,
                                VkImageLayout.ShaderReadOnlyOptimal, out var b, out var s, out var d))
                        { postCopyBarriers[n++] = b; postSrc |= s; postDst |= d; }
                    }

                    if (n > 0)
                        deviceApi.vkCmdPipelineBarrier(cb, postSrc, postDst, VkDependencyFlags.None,
                            0, null, 0, null, (uint)n, postCopyBarriers);
                }
            }
            else
            {
                Debug.Assert(sourceIsStaging && destIsStaging);
                var srcBuffer = srcVkTexture.StagingBuffer;
                var srcLayout = srcVkTexture.GetSubresourceLayout(
                    srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
                var dstBuffer = dstVkTexture.StagingBuffer;
                var dstLayout = dstVkTexture.GetSubresourceLayout(
                    dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer));

                uint zLimit = Math.Max(depth, layerCount);

                if (!FormatHelpers.IsCompressedFormat(source.Format))
                {
                    uint pixelSize = FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format);
                    uint regionCount = zLimit * height;
                    var copyRegions = new VkBufferCopy[regionCount];
                    int idx = 0;

                    for (uint zz = 0; zz < zLimit; zz++)
                    {
                        for (uint yy = 0; yy < height; yy++)
                        {
                            copyRegions[idx++] = new VkBufferCopy
                            {
                                srcOffset = srcLayout.offset
                                            + srcLayout.depthPitch * (zz + srcZ)
                                            + srcLayout.rowPitch * (yy + srcY)
                                            + pixelSize * srcX,
                                dstOffset = dstLayout.offset
                                            + dstLayout.depthPitch * (zz + dstZ)
                                            + dstLayout.rowPitch * (yy + dstY)
                                            + pixelSize * dstX,
                                size = width * pixelSize
                            };
                        }
                    }

                    fixed (VkBufferCopy* regionsPtr = copyRegions)
                        deviceApi.vkCmdCopyBuffer(cb, srcBuffer, dstBuffer, regionCount, regionsPtr);
                }
                else // IsCompressedFormat
                {
                    uint denseRowSize = FormatHelpers.GetRowPitch(width, source.Format);
                    uint numRows = FormatHelpers.GetNumRows(height, source.Format);
                    uint compressedSrcX = srcX / 4;
                    uint compressedSrcY = srcY / 4;
                    uint compressedDstX = dstX / 4;
                    uint compressedDstY = dstY / 4;
                    uint blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(source.Format);
                    uint regionCount = zLimit * numRows;
                    var copyRegions = new VkBufferCopy[regionCount];
                    int idx = 0;

                    for (uint zz = 0; zz < zLimit; zz++)
                    {
                        for (uint row = 0; row < numRows; row++)
                        {
                            copyRegions[idx++] = new VkBufferCopy
                            {
                                srcOffset = srcLayout.offset
                                            + srcLayout.depthPitch * (zz + srcZ)
                                            + srcLayout.rowPitch * (row + compressedSrcY)
                                            + blockSizeInBytes * compressedSrcX,
                                dstOffset = dstLayout.offset
                                            + dstLayout.depthPitch * (zz + dstZ)
                                            + dstLayout.rowPitch * (row + compressedDstY)
                                            + blockSizeInBytes * compressedDstX,
                                size = denseRowSize
                            };
                        }
                    }

                    fixed (VkBufferCopy* regionsPtr = copyRegions)
                        deviceApi.vkCmdCopyBuffer(cb, srcBuffer, dstBuffer, regionCount, regionsPtr);
                }
            }
        }

        protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            preDrawCommand();
            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
            currentStagingInfo.Resources.Add(vkBuffer.RefCount);
            gd.DeviceApi.vkCmdDrawIndirect(CommandBuffer, vkBuffer.DeviceBuffer, offset, drawCount, stride);
        }

        protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            preDrawCommand();
            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
            currentStagingInfo.Resources.Add(vkBuffer.RefCount);
            gd.DeviceApi.vkCmdDrawIndexedIndirect(CommandBuffer, vkBuffer.DeviceBuffer, offset, drawCount, stride);
        }

        protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
        {
            preDispatchCommand();

            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
            currentStagingInfo.Resources.Add(vkBuffer.RefCount);
            gd.DeviceApi.vkCmdDispatchIndirect(CommandBuffer, vkBuffer.DeviceBuffer, offset);
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            if (activeRenderPass != VkRenderPass.Null) endCurrentRenderPass();

            var vkSource = Util.AssertSubtype<Texture, VkTexture>(source);
            currentStagingInfo.Resources.Add(vkSource.RefCount);
            var vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);
            currentStagingInfo.Resources.Add(vkDestination.RefCount);
            var aspectFlags = (source.Usage & TextureUsage.DepthStencil) != 0
                ? FormatHelpers.IsStencilFormat(source.Format)
                    ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                    : VkImageAspectFlags.Depth
                : VkImageAspectFlags.Color;
            var region = new VkImageResolve
            {
                extent = new VkExtent3D { width = source.Width, height = source.Height, depth = source.Depth },
                srcSubresource = new VkImageSubresourceLayers { layerCount = 1, aspectMask = aspectFlags },
                dstSubresource = new VkImageSubresourceLayers { layerCount = 1, aspectMask = aspectFlags }
            };

            // Batch both pre-resolve transitions (src→TransferSrcOptimal, dst→TransferDstOptimal)
            // into a single vkCmdPipelineBarrier call to reduce pipeline-barrier overhead.
            {
                var preResolveBarriers = stackalloc VkImageMemoryBarrier[2];
                int n = 0;
                VkPipelineStageFlags preSrc = VkPipelineStageFlags.None, preDst = VkPipelineStageFlags.None;

                if (vkSource.TryGetLayoutTransitionBarrier(0, 1, 0, 1,
                        VkImageLayout.TransferSrcOptimal, out var srcPre, out var ss, out var sd))
                { preResolveBarriers[n++] = srcPre; preSrc |= ss; preDst |= sd; }

                if (vkDestination.TryGetLayoutTransitionBarrier(0, 1, 0, 1,
                        VkImageLayout.TransferDstOptimal, out var dstPre, out var ds, out var dd))
                { preResolveBarriers[n++] = dstPre; preSrc |= ds; preDst |= dd; }

                if (n > 0)
                    gd.DeviceApi.vkCmdPipelineBarrier(CommandBuffer, preSrc, preDst, VkDependencyFlags.None,
                        0, null, 0, null, (uint)n, preResolveBarriers);
            }

            gd.DeviceApi.vkCmdResolveImage(
                CommandBuffer,
                vkSource.OptimalDeviceImage,
                VkImageLayout.TransferSrcOptimal,
                vkDestination.OptimalDeviceImage,
                VkImageLayout.TransferDstOptimal,
                1,
                &region);

            if ((vkDestination.Usage & TextureUsage.Sampled) != 0)
            {
                if (vkDestination.TryGetLayoutTransitionBarrier(0, 1, 0, 1,
                        VkImageLayout.ShaderReadOnlyOptimal, out var barrier, out var src, out var dst))
                {
                    imageBarrierBatch.Add(barrier);
                    barrierBatchSrcStage |= src;
                    barrierBatchDstStage |= dst;
                }
            }

            // Transition sampled source back to ShaderReadOnlyOptimal, consistent with
            // CopyTextureCore which also performs an immediate post-copy back-transition.
            // Without this the source stays in TransferSrcOptimal until appendTransitions
            // lazily picks it up on the next draw, adding an avoidable deferred transition.
            if ((vkSource.Usage & TextureUsage.Sampled) != 0)
            {
                if (vkSource.TryGetLayoutTransitionBarrier(0, 1, 0, 1,
                        VkImageLayout.ShaderReadOnlyOptimal, out var barrier, out var src, out var dst))
                {
                    imageBarrierBatch.Add(barrier);
                    barrierBatchSrcStage |= src;
                    barrierBatchDstStage |= dst;
                }
            }

            // Flush both post-resolve transitions (dst and src) as a single vkCmdPipelineBarrier
            // rather than two separate TransitionImageLayout calls. On Mali/Adreno reducing the
            // number of pipeline barriers matters for tile-pass scheduling.
            flushTransitionBarriers();
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            if (activeRenderPass.Handle != VkRenderPass.Null)
                endCurrentRenderPass();
            else if (!currentFramebufferEverActive && currentFramebuffer != null)
            {
                // This forces any queued up texture clears to be emitted.
                beginCurrentRenderPass();
                endCurrentRenderPass();
            }

            currentFramebuffer?.TransitionToFBOSwitchLayout(CommandBuffer);

            var vkFb = Util.AssertSubtype<Framebuffer, VkFramebufferBase>(fb);
            currentFramebuffer = vkFb;
            currentFramebufferEverActive = false;
            newFramebuffer = true;
            Util.EnsureArrayMinimumSize(ref scissorRects, Math.Max(1, (uint)vkFb.ColorTargets.Count));
            Util.EnsureArrayMinimumSize(ref cachedViewports, Math.Max(1, (uint)vkFb.ColorTargets.Count));
            uint clearValueCount = (uint)vkFb.ColorTargets.Count;
            Util.EnsureArrayMinimumSize(ref clearValues, clearValueCount + 1); // Leave an extra space for the depth value (tracked separately).
            Util.ClearArray(validColorClearValues);
            Util.EnsureArrayMinimumSize(ref validColorClearValues, clearValueCount);
            currentStagingInfo.Resources.Add(vkFb.RefCount);

            if (fb is VkSwapchainFramebuffer scFb) currentStagingInfo.Resources.Add(scFb.Swapchain.RefCount);
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            if (!currentGraphicsResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
            {
                currentGraphicsResourceSets[slot].Offsets.Dispose();
                currentGraphicsResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
                graphicsResourceSetsChanged[slot] = true;
                Util.AssertSubtype<ResourceSet, VkResourceSet>(rs);
            }
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            if (!currentComputeResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
            {
                currentComputeResourceSets[slot].Offsets.Dispose();
                currentComputeResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
                computeResourceSetsChanged[slot] = true;
                Util.AssertSubtype<ResourceSet, VkResourceSet>(rs);
            }
        }

        private VkCommandBuffer getNextCommandBuffer()
        {
            lock (commandBufferListLock)
            {
                if (availableCommandBuffers.Count > 0)
                {
                    var cachedCb = availableCommandBuffers.Dequeue();
                    var resetResult = gd.DeviceApi.vkResetCommandBuffer(cachedCb, VkCommandBufferResetFlags.None);
                    CheckResult(resetResult);
                    return cachedCb;
                }
            }

            var cbAi = new VkCommandBufferAllocateInfo();
            cbAi.commandPool = pool;
            cbAi.commandBufferCount = 1;
            cbAi.level = VkCommandBufferLevel.Primary;
            VkCommandBuffer cb;
            var result = gd.DeviceApi.vkAllocateCommandBuffers(&cbAi, &cb);
            CheckResult(result);
            return cb;
        }

        private void preDrawCommand()
        {
            // Always scan ALL graphics resource sets for texture layout transitions.
            //
            // The former "needScan" optimisation (skip when resource sets are unchanged) was
            // incorrect: it depended on a preDrawSampledImages list to transition dual-use
            // Sampled|Storage textures back to ShaderReadOnlyOptimal before the next draw.
            // When the same storage texture was bound across consecutive draws with unchanged
            // resource sets (needScan=false), the list would fire unconditionally and leave the
            // texture in ShaderReadOnlyOptimal — the wrong layout for a storage image binding.
            //
            // TryGetLayoutTransitionBarrier is a cheap O(1) no-op when the texture is already in
            // the target layout, so scanning all resource sets every draw costs only a handful of
            // array reads (< 200 for a typical 8-set/10-texture-per-set setup) — negligible
            // compared to the GPU work recorded by the surrounding draw call.
            if (currentGraphicsPipeline != null)
            {
                uint setCount = currentGraphicsPipeline.ResourceSetCount;

                for (int slot = 0; slot < setCount && slot < currentGraphicsResourceSets.Length; slot++)
                {
                    if (currentGraphicsResourceSets[slot].Set != null)
                    {
                        var vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(currentGraphicsResourceSets[slot].Set);
                        appendTransitions(vkSet.SampledTextures, VkImageLayout.ShaderReadOnlyOptimal);

                        // Graphics shaders can also bind storage images (TextureReadWrite).
                        // Transition them to General, just as the compute dispatch path does.
                        appendTransitions(vkSet.StorageTextures, VkImageLayout.General);
                    }
                }
            }

            // Emit all accumulated transitions as a single vkCmdPipelineBarrier.
            flushTransitionBarriers();

            if (currentGraphicsPipeline == null)
                throw new VeldridException("A graphics pipeline must be bound before drawing.");

            ensureRenderPassActive();

            flushNewResourceSets(
                currentGraphicsResourceSets,
                graphicsResourceSetsChanged,
                currentGraphicsPipeline.ResourceSetCount,
                VkPipelineBindPoint.Graphics,
                currentGraphicsPipeline.PipelineLayout);
        }

        // Appends layout transitions for each texture in the list to imageBarrierBatch without
        // emitting any Vulkan commands.
        //
        // Iterates per-mip-level AND per-array-layer rather than using a single full-range barrier
        // because a texture can have subresources in different layouts:
        //
        //   • After a partial CopyTexture/ResolveTexture, one mip may be in TransferSrcOptimal
        //     while others remain in ShaderReadOnlyOptimal.
        //   • After rendering to a specific array layer via a framebuffer attachment, that layer
        //     is in ColorAttachmentOptimal while other layers remain in ShaderReadOnlyOptimal.
        //
        // Emitting a range barrier (all layers) with the first mismatched layer's oldLayout is a
        // Vulkan spec violation: §12.8 requires every subresource in the barrier's range to be in
        // the declared oldLayout.  On tile-based GPU this can silently corrupt tile-cache state.
        //
        // Per-subresource calls are cheap: TryGetLayoutTransitionBarrier returns false immediately
        // (1 array read) when the subresource is already in the target layout, so the common case
        // (all subresources already correct) costs only MipLevels × ArrayLayers reads per texture.
        private void appendTransitions(List<VkTexture> textures, VkImageLayout layout)
        {
            if (textures.Count == 0) return;

            for (int i = 0; i < textures.Count; i++)
            {
                var tex = textures[i];

                for (uint mip = 0; mip < tex.MipLevels; mip++)
                {
                    for (uint layer = 0; layer < tex.ActualArrayLayers; layer++)
                    {
                        if (tex.TryGetLayoutTransitionBarrier(mip, 1, layer, 1, layout,
                                out var barrier, out var src, out var dst))
                        {
                            imageBarrierBatch.Add(barrier);
                            barrierBatchSrcStage |= src;
                            barrierBatchDstStage |= dst;
                        }
                    }
                }
            }
        }

        // Emits all barriers accumulated since the last flush as a single gd.DeviceApi.vkCmdPipelineBarrier call.
        private unsafe void flushTransitionBarriers()
        {
            int count = imageBarrierBatch.Count;
            if (count == 0) return;

            // CollectionsMarshal.AsSpan returns the List's internal backing array as a Span.
            // The list is not modified while the fixed block is executing, so the GC pin is safe.
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(imageBarrierBatch);

            fixed (VkImageMemoryBarrier* barriers = span)
            {
                gd.DeviceApi.vkCmdPipelineBarrier(
                    CommandBuffer,
                    barrierBatchSrcStage,
                    barrierBatchDstStage,
                    VkDependencyFlags.None,
                    0, null,
                    0, null,
                    (uint)count, barriers);
            }

            imageBarrierBatch.Clear();
            barrierBatchSrcStage = VkPipelineStageFlags.None;
            barrierBatchDstStage = VkPipelineStageFlags.None;
        }

        private void flushNewResourceSets(
            BoundResourceSetInfo[] resourceSets,
            bool[] resourceSetsChanged,
            uint resourceSetCount,
            VkPipelineBindPoint bindPoint,
            VkPipelineLayout pipelineLayout)
        {
            var pipeline = bindPoint == VkPipelineBindPoint.Graphics ? currentGraphicsPipeline : currentComputePipeline;

            var descriptorSets = stackalloc VkDescriptorSet[(int)resourceSetCount];
            uint* dynamicOffsets = stackalloc uint[(int)pipeline.DynamicOffsetsCount];
            uint currentBatchCount = 0;
            uint currentBatchFirstSet = 0;
            uint currentBatchDynamicOffsetCount = 0;

            for (uint currentSlot = 0; currentSlot < resourceSetCount; currentSlot++)
            {
                if (resourceSetsChanged[currentSlot])
                {
                    resourceSetsChanged[currentSlot] = false;

                    // After a pipeline switch clearSets() nullifies all Sets but leaves
                    // changed bits true. Skip null slots (treat like an unchanged slot:
                    // flush any in-progress batch and advance the first-set cursor).
                    if (resourceSets[currentSlot].Set == null)
                    {
                        if (currentBatchCount != 0)
                        {
                            gd.DeviceApi.vkCmdBindDescriptorSets(
                                CommandBuffer,
                                bindPoint,
                                pipelineLayout,
                                currentBatchFirstSet,
                                currentBatchCount,
                                descriptorSets,
                                currentBatchDynamicOffsetCount,
                                dynamicOffsets);
                            currentBatchCount = 0;
                            currentBatchDynamicOffsetCount = 0;
                        }

                        currentBatchFirstSet = currentSlot + 1;
                        continue;
                    }

                    var vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(resourceSets[currentSlot].Set);

                    // Increment ref count on first use of a set.
                    currentStagingInfo.Resources.Add(vkSet.RefCount);
                    for (int i = 0; i < vkSet.RefCounts.Count; i++) currentStagingInfo.Resources.Add(vkSet.RefCounts[i]);

                    if (vkSet.IsPushDescriptor && gd.HasPushDescriptors)
                    {
                        // Flush any pending traditional batch before the push.
                        if (currentBatchCount != 0)
                        {
                            gd.DeviceApi.vkCmdBindDescriptorSets(
                                CommandBuffer,
                                bindPoint,
                                pipelineLayout,
                                currentBatchFirstSet,
                                currentBatchCount,
                                descriptorSets,
                                currentBatchDynamicOffsetCount,
                                dynamicOffsets);
                            currentBatchCount = 0;
                            currentBatchDynamicOffsetCount = 0;
                        }

                        // Push descriptors directly into the command buffer.
                        pushDescriptorSet(vkSet, bindPoint, pipelineLayout, currentSlot);
                        currentBatchFirstSet = currentSlot + 1;
                    }
                    else
                    {
                        // Traditional bind path.
                        descriptorSets[currentBatchCount] = vkSet.DescriptorSet;
                        currentBatchCount += 1;

                        ref var curSetOffsets = ref resourceSets[currentSlot].Offsets;

                        for (uint i = 0; i < curSetOffsets.Count; i++)
                        {
                            dynamicOffsets[currentBatchDynamicOffsetCount] = curSetOffsets.Get(i);
                            currentBatchDynamicOffsetCount += 1;
                        }

                        bool batchEnded = currentSlot == resourceSetCount - 1;

                        // Check if next slot breaks the batch (unchanged, null, or push descriptor).
                        if (!batchEnded && currentSlot + 1 < resourceSetCount)
                        {
                            if (!resourceSetsChanged[currentSlot + 1])
                                batchEnded = true;
                            else if (resourceSets[currentSlot + 1].Set == null)
                                batchEnded = true;
                            else
                            {
                                var nextSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(resourceSets[currentSlot + 1].Set);
                                if (nextSet.IsPushDescriptor && gd.HasPushDescriptors)
                                    batchEnded = true;
                            }
                        }

                        if (batchEnded && currentBatchCount != 0)
                        {
                            gd.DeviceApi.vkCmdBindDescriptorSets(
                                CommandBuffer,
                                bindPoint,
                                pipelineLayout,
                                currentBatchFirstSet,
                                currentBatchCount,
                                descriptorSets,
                                currentBatchDynamicOffsetCount,
                                dynamicOffsets);
                            currentBatchCount = 0;
                            currentBatchDynamicOffsetCount = 0;
                            currentBatchFirstSet = currentSlot + 1;
                        }
                    }
                }
                else
                {
                    // Unchanged slot breaks the batch.
                    if (currentBatchCount != 0)
                    {
                        gd.DeviceApi.vkCmdBindDescriptorSets(
                            CommandBuffer,
                            bindPoint,
                            pipelineLayout,
                            currentBatchFirstSet,
                            currentBatchCount,
                            descriptorSets,
                            currentBatchDynamicOffsetCount,
                            dynamicOffsets);
                        currentBatchCount = 0;
                        currentBatchDynamicOffsetCount = 0;
                    }

                    currentBatchFirstSet = currentSlot + 1;
                }
            }

            // Flush any remaining batch.
            if (currentBatchCount != 0)
            {
                gd.DeviceApi.vkCmdBindDescriptorSets(
                    CommandBuffer,
                    bindPoint,
                    pipelineLayout,
                    currentBatchFirstSet,
                    currentBatchCount,
                    descriptorSets,
                    currentBatchDynamicOffsetCount,
                    dynamicOffsets);
            }
        }

        private void pushDescriptorSet(
            VkResourceSet vkSet,
            VkPipelineBindPoint bindPoint,
            VkPipelineLayout pipelineLayout,
            uint setIndex)
        {
            // Defensive guard: this path must be unreachable when push descriptors are disabled.
            Debug.Assert(gd.HasPushDescriptors, "pushDescriptorSet called on a device without push descriptor support");
            if (!gd.HasPushDescriptors)
                return;

            var writes = vkSet.PushWrites;
            var bufferInfos = vkSet.PushBufferInfos;
            var imageInfos = vkSet.PushImageInfos;
            uint writeCount = (uint)writes.Length;

            fixed (VkWriteDescriptorSet* writesPtr = writes)
            fixed (VkDescriptorBufferInfo* bufInfosPtr = bufferInfos)
            fixed (VkDescriptorImageInfo* imgInfosPtr = imageInfos)
            {
                // Fix up the info pointers — they were not set during VkResourceSet
                // construction because the arrays were not yet pinned.
                for (int w = 0; w < writeCount; w++)
                {
                    var type = writesPtr[w].descriptorType;

                    if (type == VkDescriptorType.UniformBuffer || type == VkDescriptorType.UniformBufferDynamic
                        || type == VkDescriptorType.StorageBuffer || type == VkDescriptorType.StorageBufferDynamic)
                    {
                        writesPtr[w].pBufferInfo = &bufInfosPtr[w];
                        writesPtr[w].pImageInfo = null;
                    }
                    else
                    {
                        writesPtr[w].pImageInfo = &imgInfosPtr[w];
                        writesPtr[w].pBufferInfo = null;
                    }
                }

                gd.DeviceApi.vkCmdPushDescriptorSetKHR(
                    CommandBuffer,
                    bindPoint,
                    pipelineLayout,
                    setIndex,
                    writeCount,
                    writesPtr);
            }
        }

        private void preDispatchCommand()
        {
            ensureNoRenderPass();

            if (currentComputePipeline == null)
                throw new VeldridException("A compute pipeline must be bound before dispatching.");

            for (uint currentSlot = 0; currentSlot < currentComputePipeline.ResourceSetCount; currentSlot++)
            {
                if (currentComputeResourceSets[currentSlot].Set == null)
                    continue;

                var vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(
                    currentComputeResourceSets[currentSlot].Set);

                appendTransitions(vkSet.SampledTextures, VkImageLayout.ShaderReadOnlyOptimal);
                appendTransitions(vkSet.StorageTextures, VkImageLayout.General);
            }

            flushTransitionBarriers();

            flushNewResourceSets(
                currentComputeResourceSets,
                computeResourceSetsChanged,
                currentComputePipeline.ResourceSetCount,
                VkPipelineBindPoint.Compute,
                currentComputePipeline.PipelineLayout);
        }

        private void ensureRenderPassActive()
        {
            if (activeRenderPass == VkRenderPass.Null) beginCurrentRenderPass();
        }

        private void ensureNoRenderPass()
        {
            if (activeRenderPass != VkRenderPass.Null) endCurrentRenderPass();
        }

        private void beginCurrentRenderPass()
        {
            Debug.Assert(activeRenderPass == VkRenderPass.Null);
            Debug.Assert(currentFramebuffer != null);
            currentFramebufferEverActive = true;

            // Use dynamic rendering when available — eliminates VkRenderPass/VkFramebuffer overhead.
            if (gd.HasDynamicRendering)
            {
                beginCurrentDynamicRendering();
                return;
            }

            uint attachmentCount = currentFramebuffer.AttachmentCount;
            bool haveAnyAttachments = currentFramebuffer.ColorTargets.Count > 0 || currentFramebuffer.DepthTarget != null;
            bool haveAllClearValues = depthClearValue.HasValue || currentFramebuffer.DepthTarget == null;
            bool haveAnyClearValues = depthClearValue.HasValue;

            for (int i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
            {
                if (!validColorClearValues[i])
                    haveAllClearValues = false;
                else
                    haveAnyClearValues = true;
            }

            var renderPassBi = new VkRenderPassBeginInfo();
            renderPassBi.renderArea = new VkRect2D(new VkOffset2D(0, 0), new VkExtent2D(currentFramebuffer.RenderableWidth, currentFramebuffer.RenderableHeight));
            renderPassBi.framebuffer = currentFramebuffer.CurrentFramebuffer;

            if (!haveAnyAttachments || !haveAllClearValues)
            {
                // On the first bind of a sampled offscreen FBO (newFramebuffer=true) in the legacy
                // render-pass path, use renderPassClearSampledInit which has loadOp=Clear /
                // initialLayout=Undefined for sampled color attachments.  This prevents the driver
                // from loading stale tile-RAM data via loadOp=Load from ShaderReadOnlyOptimal on
                // TBDR GPUs (same invariant as the dynamic-rendering path).
                //
                // Exception: if the first color target is already in ColorAttachmentOptimal, this is
                // a mid-frame return to a previously-rendered FBO (inner FBO was bound in between and
                // TransitionToFBOSwitchLayout was a no-op). In that case renderPassClearSampledInit
                // would expect initialLayout=Undefined but the actual layout is ColorAttachmentOptimal,
                // and its loadOp=Clear would wipe partial content. Use RenderPassNoClearLoad instead.
                bool midFrameReturn = false;

                if (newFramebuffer && currentFramebuffer.ColorTargets.Count > 0)
                {
                    var firstTarget = currentFramebuffer.ColorTargets[0];
                    var firstTargetLayout = Util.AssertSubtype<Texture, VkTexture>(firstTarget.Target)
                        .GetImageLayout(firstTarget.MipLevel, firstTarget.ArrayLayer);
                    midFrameReturn = firstTargetLayout == VkImageLayout.ColorAttachmentOptimal;
                }

                // Emit explicit pre-render-pass barriers to bring each attachment to its declared
                // initialLayout. VkRenderPass requires the image to be in initialLayout when
                // vkCmdBeginRenderPass is called (Vulkan spec §12.8.2), but attachments may be in
                // unexpected layouts if they were last used in the Transfer stage (e.g. after
                // VkTextureUpdateBatch or CopyTextureCore). For renderPassClear the initialLayout
                // is VK_IMAGE_LAYOUT_UNDEFINED (which accepts any actual layout per §12.8.2), so
                // TryGetLayoutTransitionBarrier will no-op for those cases — we emit barriers
                // unconditionally here for simplicity and correctness.
                //
                // This mirrors beginCurrentDynamicRendering which also emits explicit barriers.
                // Note: imageBarrierBatch is always empty here because flushTransitionBarriers()
                // was called by preDrawCommand before ensureRenderPassActive, and this method is
                // called from SetFramebufferCore/End() where no barriers have been queued.
                //
                // The correct target layout depends on which render pass will be used:
                //   renderPassNoClearLoad (mid-frame re-use): ALL attachments → Color/DepthStencilAttachmentOptimal
                //     regardless of Sampled flag (initialLayout=ColorAttachmentOptimal for all color,
                //     DepthStencilAttachmentOptimal for depth in that render pass).
                //   renderPassClearSampledInit (first bind of sampled FBO): depth → DepthStencilAttachmentOptimal
                //     regardless of Sampled (only sampled *color* gets initialLayout=Undefined there).
                //   renderPassNoClearInit (first bind, normal): Sampled → ShaderReadOnlyOptimal,
                //     non-sampled → Color/DepthStencilAttachmentOptimal.
                //
                // Transitioning Sampled attachments to ShaderReadOnlyOptimal when the render pass
                // declares initialLayout=ColorAttachmentOptimal / DepthStencilAttachmentOptimal
                // would violate Vulkan spec §12.8.2 and produce undefined behaviour on tile GPUs.
                bool willUseNoClearLoad = !newFramebuffer || midFrameReturn;
                bool willUseClearSampledInit = !willUseNoClearLoad
                    && currentFramebuffer.RenderPassClearSampledInit != VkRenderPass.Null;

                for (int i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
                {
                    var ca = currentFramebuffer.ColorTargets[i];
                    var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                    // Choose the target layout that matches the initialLayout declared by the
                    // render pass that will be used, so the image is in the expected layout when
                    // vkCmdBeginRenderPass is called (Vulkan spec §12.8.2).
                    //
                    // renderPassNoClearLoad  → initialLayout=ColorAttachmentOptimal for ALL color attachments.
                    // renderPassNoClearInit  → initialLayout per attachment type:
                    //   • Sampled offscreen   : ShaderReadOnlyOptimal
                    //   • Swapchain (presented): PresentSrcKHR  — render pass does the implicit PresentSrcKHR→ColorAttachmentOptimal
                    //   • Other (non-sampled) : ColorAttachmentOptimal
                    //
                    // For swapchain textures we MUST NOT emit a PresentSrcKHR→ColorAttachmentOptimal
                    // barrier here: doing so would leave the image in ColorAttachmentOptimal while
                    // renderPassNoClearInit declares initialLayout=PresentSrcKHR, which is a Vulkan
                    // spec violation and produces undefined behaviour (§12.8.2 requireds the image
                    // to be in initialLayout at render pass start when initialLayout≠Undefined).
                    VkImageLayout targetLayout;
                    if (willUseNoClearLoad)
                    {
                        targetLayout = VkImageLayout.ColorAttachmentOptimal;
                    }
                    else if ((vkTex.Usage & TextureUsage.Sampled) != 0)
                    {
                        targetLayout = VkImageLayout.ShaderReadOnlyOptimal;
                    }
                    else if (vkTex.IsSwapchainTexture)
                    {
                        // renderPassNoClearInit has initialLayout=PresentSrcKHR for swapchain
                        // images.  Keep the image in PresentSrcKHR so the render pass performs
                        // the PresentSrcKHR→ColorAttachmentOptimal transition implicitly.
                        targetLayout = VkImageLayout.PresentSrcKHR;
                    }
                    else
                    {
                        targetLayout = VkImageLayout.ColorAttachmentOptimal;
                    }
                    if (vkTex.TryGetLayoutTransitionBarrier(ca.MipLevel, 1, ca.ArrayLayer, 1,
                            targetLayout, out var barrier, out var src, out var dst))
                    {
                        imageBarrierBatch.Add(barrier);
                        barrierBatchSrcStage |= src;
                        barrierBatchDstStage |= dst;
                    }
                }

                if (currentFramebuffer.DepthTarget.HasValue)
                {
                    var ca = currentFramebuffer.DepthTarget.Value;
                    var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                    // renderPassNoClearLoad and renderPassClearSampledInit both declare
                    // initialLayout=DepthStencilAttachmentOptimal for depth regardless of Sampled.
                    // Only renderPassNoClearInit uses ShaderReadOnlyOptimal for sampled depth.
                    var targetLayout = (!willUseNoClearLoad && !willUseClearSampledInit
                                        && (vkTex.Usage & TextureUsage.Sampled) != 0)
                        ? VkImageLayout.ShaderReadOnlyOptimal
                        : VkImageLayout.DepthStencilAttachmentOptimal;
                    if (vkTex.TryGetLayoutTransitionBarrier(ca.MipLevel, 1, ca.ArrayLayer, 1,
                            targetLayout, out var barrier, out var src, out var dst))
                    {
                        imageBarrierBatch.Add(barrier);
                        barrierBatchSrcStage |= src;
                        barrierBatchDstStage |= dst;
                    }
                }

                flushTransitionBarriers();

                if (newFramebuffer && !midFrameReturn && currentFramebuffer.RenderPassClearSampledInit != VkRenderPass.Null)
                {
                    // Inject transparent-black into the clear-value slots for any sampled color
                    // attachment that has no explicit caller-supplied clear value.
                    for (int i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
                    {
                        if (!validColorClearValues[i])
                        {
                            var vkColorTex = Util.AssertSubtype<Texture, VkTexture>(currentFramebuffer.ColorTargets[i].Target);
                            if ((vkColorTex.Usage & TextureUsage.Sampled) != 0)
                                clearValues[i] = default; // transparent black (0,0,0,0)
                        }
                    }

                    renderPassBi.renderPass = currentFramebuffer.RenderPassClearSampledInit;
                    fixed (VkClearValue* clearValuesPtr = &clearValues[0])
                    {
                        renderPassBi.clearValueCount = attachmentCount;
                        renderPassBi.pClearValues = clearValuesPtr;
                        gd.DeviceApi.vkCmdBeginRenderPass(CommandBuffer, &renderPassBi, VkSubpassContents.Inline);
                    }

                    activeRenderPass = renderPassBi.renderPass;

                    // The render pass cleared all sampled attachments (loadOp=Clear).  Any
                    // non-sampled attachment with a pending explicit clear must be issued as
                    // gd.DeviceApi.vkCmdClearAttachments inside the now-active pass.
                    if (haveAnyClearValues)
                    {
                        if (depthClearValue.HasValue)
                        {
                            ClearDepthStencilCore(depthClearValue.Value.depthStencil.depth, (byte)depthClearValue.Value.depthStencil.stencil);
                            depthClearValue = null;
                        }

                        for (uint i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
                        {
                            if (validColorClearValues[i])
                            {
                                validColorClearValues[i] = false;
                                var vkColorTex = Util.AssertSubtype<Texture, VkTexture>(currentFramebuffer.ColorTargets[(int)i].Target);
                                if ((vkColorTex.Usage & TextureUsage.Sampled) == 0)
                                {
                                    // Non-sampled attachment uses loadOp=Load in this pass, so
                                    // the caller's explicit clear must be emitted manually.
                                    var vkClearValue = clearValues[i];
                                    ClearColorTarget(i, new RgbaFloat(
                                        vkClearValue.color.float32[0],
                                        vkClearValue.color.float32[1],
                                        vkClearValue.color.float32[2],
                                        vkClearValue.color.float32[3]));
                                }
                                // Sampled attachments: already cleared by renderPassClearSampledInit loadOp.
                            }
                        }
                    }
                    else
                    {
                        Util.ClearArray(validColorClearValues);
                    }
                }
                else
                {
                    // midFrameReturn: newFramebuffer=true but the image is in ColorAttachmentOptimal
                    // from earlier this frame → use Load to preserve partial content.
                    renderPassBi.renderPass = (newFramebuffer && !midFrameReturn)
                        ? currentFramebuffer.RenderPassNoClearInit
                        : currentFramebuffer.RenderPassNoClearLoad;
                    gd.DeviceApi.vkCmdBeginRenderPass(CommandBuffer, &renderPassBi, VkSubpassContents.Inline);
                    activeRenderPass = renderPassBi.renderPass;

                    if (haveAnyClearValues)
                    {
                        if (depthClearValue.HasValue)
                        {
                            ClearDepthStencilCore(depthClearValue.Value.depthStencil.depth, (byte)depthClearValue.Value.depthStencil.stencil);
                            depthClearValue = null;
                        }

                        for (uint i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
                        {
                            if (validColorClearValues[i])
                            {
                                validColorClearValues[i] = false;
                                var vkClearValue = clearValues[i];
                                var clearColor = new RgbaFloat(
                                    vkClearValue.color.float32[0],
                                    vkClearValue.color.float32[1],
                                    vkClearValue.color.float32[2],
                                    vkClearValue.color.float32[3]);
                                ClearColorTarget(i, clearColor);
                            }
                        }
                    }
                }
            }
            else
            {
                // We have clear values for every attachment.
                renderPassBi.renderPass = currentFramebuffer.RenderPassClear;

                fixed (VkClearValue* clearValuesPtr = &clearValues[0])
                {
                    renderPassBi.clearValueCount = attachmentCount;
                    renderPassBi.pClearValues = clearValuesPtr;

                    if (depthClearValue.HasValue)
                    {
                        clearValues[currentFramebuffer.ColorTargets.Count] = depthClearValue.Value;
                        depthClearValue = null;
                    }

                    gd.DeviceApi.vkCmdBeginRenderPass(CommandBuffer, &renderPassBi, VkSubpassContents.Inline);
                    activeRenderPass = currentFramebuffer.RenderPassClear;
                    Util.ClearArray(validColorClearValues);
                }
            }

            newFramebuffer = false;
        }

        private void beginCurrentDynamicRendering()
        {
            int colorCount = currentFramebuffer.ColorTargets.Count;
            var colorViews = currentFramebuffer.ColorAttachmentViews;
            var colorAttachments = stackalloc VkRenderingAttachmentInfo[colorCount > 0 ? colorCount : 1];

            // Capture layout BEFORE transitions. If an image is already in ColorAttachmentOptimal,
            // it was rendered to earlier in this frame (before a mid-frame FBO switch). We must use
            // loadOp=Load on return to preserve that content. PresentSrcKHR = fresh acquisition;
            // DontCare is safe there (game always calls Clear on first use via validColorClearValues).
            var priorColorLayouts = stackalloc VkImageLayout[colorCount > 0 ? colorCount : 1];
            for (int i = 0; i < colorCount; i++)
            {
                var ca = currentFramebuffer.ColorTargets[i];
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                priorColorLayouts[i] = vkTex.GetImageLayout(ca.MipLevel, ca.ArrayLayer);
            }

            // Dynamic rendering has no implicit layout transitions (unlike VkRenderPass, which
            // handles them via VkAttachmentDescription.initialLayout/finalLayout). Emit explicit
            // barriers here so every attachment is in the correct layout when vkCmdBeginRendering
            // is called — as required by the Vulkan spec.
            //
            // Accumulate all attachment barriers into imageBarrierBatch (which is empty at this point —
            // flushTransitionBarriers() was called just before ensureRenderPassActive in preDrawCommand)
            // and flush them all in a single vkCmdPipelineBarrier rather than one call per attachment.
            // This matters most on mobile where reducing pipeline stalls is critical.
            for (int i = 0; i < colorCount; i++)
            {
                var ca = currentFramebuffer.ColorTargets[i];
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);

                if (vkTex.TryGetLayoutTransitionBarrier(ca.MipLevel, 1, ca.ArrayLayer, 1,
                        VkImageLayout.ColorAttachmentOptimal,
                        out var barrier, out var src, out var dst))
                {
                    imageBarrierBatch.Add(barrier);
                    barrierBatchSrcStage |= src;
                    barrierBatchDstStage |= dst;
                }
            }

            if (currentFramebuffer.DepthTarget.HasValue)
            {
                var ca = currentFramebuffer.DepthTarget.Value;
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);

                if (vkTex.TryGetLayoutTransitionBarrier(ca.MipLevel, 1, ca.ArrayLayer, 1,
                        VkImageLayout.DepthStencilAttachmentOptimal,
                        out var barrier, out var src, out var dst))
                {
                    imageBarrierBatch.Add(barrier);
                    barrierBatchSrcStage |= src;
                    barrierBatchDstStage |= dst;
                }
            }

            // Flush all attachment transitions as a single vkCmdPipelineBarrier (no-op if every
            // attachment was already in the correct layout, e.g. a framebuffer reused this frame).
            flushTransitionBarriers();

            for (int i = 0; i < colorCount; i++)
            {
                colorAttachments[i] = new VkRenderingAttachmentInfo();
                colorAttachments[i].imageView = colorViews[i];
                colorAttachments[i].imageLayout = VkImageLayout.ColorAttachmentOptimal;
                colorAttachments[i].resolveMode = VkResolveModeFlags.None;

                // Transient color (LAZILY_ALLOCATED) must use DontCare storeOp so the driver
                // knows it does not need to flush tile-RAM color contents to main memory.
                // Using Store defeats lazy allocation and wastes bandwidth on tiler GPUs.
                var vkColorTexForStore = Util.AssertSubtype<Texture, VkTexture>(currentFramebuffer.ColorTargets[i].Target);
                bool isTransientColor = (vkColorTexForStore.Usage & TextureUsage.Transient) != 0;
                colorAttachments[i].storeOp = isTransientColor ? VkAttachmentStoreOp.DontCare : VkAttachmentStoreOp.Store;

                if (validColorClearValues[i])
                {
                    colorAttachments[i].loadOp = VkAttachmentLoadOp.Clear;
                    colorAttachments[i].clearValue = clearValues[i];
                    validColorClearValues[i] = false;
                }
                else
                {
                    // If the image was already in ColorAttachmentOptimal before our transition loop,
                    // it was rendered to earlier this frame (swapchain returned from a mid-frame FBO
                    // switch). Use Load to preserve that content. Otherwise fall back to the normal
                    // newFramebuffer heuristic.
                    bool wasAlreadyColorAttachment = priorColorLayouts[i] == VkImageLayout.ColorAttachmentOptimal;
                    if (wasAlreadyColorAttachment)
                    {
                        colorAttachments[i].loadOp = VkAttachmentLoadOp.Load;
                    }
                    else if (newFramebuffer)
                    {
                        // On TBR GPUs (Adreno/Mali), DontCare exposes stale tile-RAM content from a previous
                        // render pass sharing the same tile region → flickering black boxes / gray rectangles.
                        // For sampled (offscreen) FBOs, use Clear(0,0,0,0) as a safe fallback when the app
                        // hasn't queued an explicit clear. The swapchain surface (not sampled) keeps DontCare
                        // as the expected usage pattern is for apps to clear it explicitly each frame.
                        var vkColorTex = Util.AssertSubtype<Texture, VkTexture>(currentFramebuffer.ColorTargets[i].Target);
                        bool isSampledOffscreen = (vkColorTex.Usage & TextureUsage.Sampled) != 0;
                        if (isSampledOffscreen)
                        {
                            colorAttachments[i].loadOp = VkAttachmentLoadOp.Clear;
                            colorAttachments[i].clearValue = new VkClearValue { color = new VkClearColorValue(0f, 0f, 0f, 0f) };
                        }
                        else
                        {
                            colorAttachments[i].loadOp = VkAttachmentLoadOp.DontCare;
                        }
                    }
                    else
                    {
                        colorAttachments[i].loadOp = VkAttachmentLoadOp.Load;
                    }
                }
            }

            var renderingInfo = new VkRenderingInfo();
            renderingInfo.renderArea = new VkRect2D(new VkOffset2D(0, 0), new VkExtent2D(currentFramebuffer.RenderableWidth, currentFramebuffer.RenderableHeight));
            renderingInfo.layerCount = 1;
            renderingInfo.colorAttachmentCount = (uint)colorCount;
            renderingInfo.pColorAttachments = colorCount > 0 ? colorAttachments : null;

            VkRenderingAttachmentInfo depthAttachment;

            if (currentFramebuffer.DepthTarget != null)
            {
                var vkDepthTex = Util.AssertSubtype<Texture, VkTexture>(currentFramebuffer.DepthTarget.Value.Target);
                // Transient depth (LAZILY_ALLOCATED) must use DontCare storeOp so the driver knows
                // it does not need to flush tile-RAM depth/stencil contents to main memory.
                // Using Store would defeat the purpose of lazy allocation and cost ~35 MB/frame of
                // unnecessary DRAM writeback on tiler GPUs (Adreno / Mali).
                bool isTransientDepth = (vkDepthTex.Usage & TextureUsage.Transient) != 0;

                depthAttachment = new VkRenderingAttachmentInfo();
                depthAttachment.imageView = currentFramebuffer.DepthAttachmentView;
                depthAttachment.imageLayout = VkImageLayout.DepthStencilAttachmentOptimal;
                depthAttachment.resolveMode = VkResolveModeFlags.None;
                depthAttachment.storeOp = isTransientDepth ? VkAttachmentStoreOp.DontCare : VkAttachmentStoreOp.Store;

                if (depthClearValue.HasValue)
                {
                    depthAttachment.loadOp = VkAttachmentLoadOp.Clear;
                    depthAttachment.clearValue = depthClearValue.Value;
                    depthClearValue = null;
                }
                else
                {
                    // For transient depth buffers (LAZILY_ALLOCATED, never sampled/read back),
                    // always use DontCare — the storeOp is always DontCare, so content is
                    // discarded at the end of every render pass.  Any subsequent render pass
                    // (whether newFramebuffer=true or a mid-frame re-entry after a CopyTexture /
                    // Dispatch that called endCurrentRenderPass) would load undefined garbage from
                    // tile RAM with loadOp=Load, causing spurious depth-test failures on TBDR GPUs
                    // (Adreno / Mali).  DontCare also lets the driver skip the DRAM→tile fill,
                    // preserving the LAZILY_ALLOCATED memory bandwidth win.
                    // For non-transient depth, always Load to preserve whatever content the app
                    // last wrote (e.g. depth-prepass results carried across frames).
                    depthAttachment.loadOp = isTransientDepth
                        ? VkAttachmentLoadOp.DontCare
                        : VkAttachmentLoadOp.Load;
                }

                renderingInfo.pDepthAttachment = &depthAttachment;

                // If the format has stencil, share the depth attachment for stencil too.
                if (currentFramebuffer.DepthTarget != null
                    && FormatHelpers.IsStencilFormat(currentFramebuffer.DepthTarget.Value.Target.Format))
                {
                    renderingInfo.pStencilAttachment = &depthAttachment;
                }
            }

            if (gd.UseKhrDynamicRendering)
                gd.DeviceApi.vkCmdBeginRenderingKHR(CommandBuffer, &renderingInfo);
            else
                gd.DeviceApi.vkCmdBeginRendering(CommandBuffer, &renderingInfo);

            // Use a sentinel render pass value to indicate dynamic rendering is active.
            // Any non-null value signals "inside a render pass" to ensureRenderPassActive / ensureNoRenderPass.
            activeRenderPass = dynamicRenderingSentinel;
            newFramebuffer = false;
        }

        private void endCurrentRenderPass()
        {
            Debug.Assert(activeRenderPass != VkRenderPass.Null);

            if (activeRenderPass == dynamicRenderingSentinel)
            {
                // Dynamic rendering path.
                // UseKhrDynamicRendering: the begin call used vkCmdBeginRenderingKHR, so the
                // matching end must also use the KHR alias.
                // UseKhrEndRendering: core vkCmdBeginRendering was used for begin (pointer was
                // non-null) but core vkCmdEndRendering is null; use the KHR alias for end only.
                // Both cases require vkCmdEndRenderingKHR; neither calls vkCmdEndRendering.
                if (gd.UseKhrDynamicRendering || gd.UseKhrEndRendering)
                    gd.DeviceApi.vkCmdEndRenderingKHR(CommandBuffer);
                else
                    gd.DeviceApi.vkCmdEndRendering(CommandBuffer);
            }
            else
            {
                // Traditional render pass path.
                gd.DeviceApi.vkCmdEndRenderPass(CommandBuffer);
            }

            currentFramebuffer.TransitionToIntermediateLayout(CommandBuffer);
            activeRenderPass = VkRenderPass.Null;

            // Place a memory + execution barrier between RenderPasses so that color/depth
            // outputs written in this pass are AVAILABLE and VISIBLE to subsequent passes.
            //
            // An execution-only barrier (no access masks) is insufficient: the Vulkan spec
            // requires explicit srcAccessMask/dstAccessMask to guarantee cache flush and
            // invalidation.  Without them, vkCmdBeginRendering's loadOp=Load may read stale
            // data on implementations that don't implicitly flush render-pass tile caches —
            // notably some mobile GPUs (Adreno / Mali) in dynamic rendering mode, which lacks
            // the implicit subpass end-dependency that legacy VkRenderPass provides.
            //
            // Use granular stage masks (available since Vulkan 1.0) on all versions:
            //   VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT / VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT
            //   are execution-only stages that must have empty access masks (Vulkan spec §7.6.2).
            //   Pairing them with non-zero srcAccessMask/dstAccessMask is a spec violation that
            //   can cause incorrect cache visibility on strict Vulkan implementations.
            //   EarlyFragmentTests is included in srcStage because early-z writes (depth-test
            //   passes with depth-write enabled) originate there — omitting it would leave depth
            //   writes from early-z passes unsynchronized with subsequent readers.
            var srcStage = VkPipelineStageFlags.ColorAttachmentOutput
                           | VkPipelineStageFlags.EarlyFragmentTests
                           | VkPipelineStageFlags.LateFragmentTests;
            // Cover all pipeline stages that can consume render-pass outputs:
            //   ColorAttachmentOutput / EarlyFragmentTests  → next render pass reads attachments
            //   FragmentShader / VertexShader               → sampling the output as a texture
            //   ComputeShader                               → compute post-process reads the output
            //   Transfer                                    → CopyTexture / GenerateMipmaps after render pass
            // NOTE: VertexInput (vertex/index buffer fetch) was here previously but is wrong —
            //   render-pass outputs are never consumed in the VertexInput stage.
            var dstStage = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests
                           | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexShader
                           | VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.Transfer;

            var memBarrier = new VkMemoryBarrier();
            memBarrier.sType = VkStructureType.MemoryBarrier;
            // Make all color and depth/stencil writes from this render pass available.
            memBarrier.srcAccessMask = VkAccessFlags.ColorAttachmentWrite
                                       | VkAccessFlags.DepthStencilAttachmentWrite;
            // Invalidate all destination cache types that the next pass may read through.
            memBarrier.dstAccessMask = VkAccessFlags.ColorAttachmentRead
                                       | VkAccessFlags.ColorAttachmentWrite
                                       | VkAccessFlags.DepthStencilAttachmentRead
                                       | VkAccessFlags.DepthStencilAttachmentWrite
                                       | VkAccessFlags.ShaderRead
                                       | VkAccessFlags.ShaderWrite
                                       | VkAccessFlags.InputAttachmentRead
                                       | VkAccessFlags.TransferRead
                                       | VkAccessFlags.TransferWrite;

            gd.DeviceApi.vkCmdPipelineBarrier(
                CommandBuffer,
                srcStage,
                dstStage,
                VkDependencyFlags.None,
                1, &memBarrier,
                0, null,
                0, null);
        }

        private void clearSets(BoundResourceSetInfo[] boundSets)
        {
            foreach (var boundSetInfo in boundSets) boundSetInfo.Offsets.Dispose();
            Util.ClearArray(boundSets);
        }

        [Conditional("DEBUG")]
        private void debugFullPipelineBarrier()
        {
            var memoryBarrier = new VkMemoryBarrier();
            memoryBarrier.srcAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT |
                                          VK_ACCESS_INDEX_READ_BIT |
                                          VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT |
                                          VK_ACCESS_UNIFORM_READ_BIT |
                                          VK_ACCESS_INPUT_ATTACHMENT_READ_BIT |
                                          VK_ACCESS_SHADER_READ_BIT |
                                          VK_ACCESS_SHADER_WRITE_BIT |
                                          VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
                                          VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                                          VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
                                          VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT |
                                          VK_ACCESS_TRANSFER_READ_BIT |
                                          VK_ACCESS_TRANSFER_WRITE_BIT |
                                          VK_ACCESS_HOST_READ_BIT |
                                          VK_ACCESS_HOST_WRITE_BIT;
            memoryBarrier.dstAccessMask = VK_ACCESS_INDIRECT_COMMAND_READ_BIT |
                                          VK_ACCESS_INDEX_READ_BIT |
                                          VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT |
                                          VK_ACCESS_UNIFORM_READ_BIT |
                                          VK_ACCESS_INPUT_ATTACHMENT_READ_BIT |
                                          VK_ACCESS_SHADER_READ_BIT |
                                          VK_ACCESS_SHADER_WRITE_BIT |
                                          VK_ACCESS_COLOR_ATTACHMENT_READ_BIT |
                                          VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT |
                                          VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT |
                                          VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT |
                                          VK_ACCESS_TRANSFER_READ_BIT |
                                          VK_ACCESS_TRANSFER_WRITE_BIT |
                                          VK_ACCESS_HOST_READ_BIT |
                                          VK_ACCESS_HOST_WRITE_BIT;

            gd.DeviceApi.vkCmdPipelineBarrier(
                CommandBuffer,
                VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, // srcStageMask
                VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, // dstStageMask
                VkDependencyFlags.None,
                1, // memoryBarrierCount
                &memoryBarrier, // pMemoryBarriers
                0, null,
                0, null);
        }

        private VkBuffer getStagingBuffer(uint size)
        {
            lock (stagingLock)
            {
                VkBuffer ret = null;

                for (int i = 0; i < availableStagingBuffers.Count; i++)
                {
                    var buffer = availableStagingBuffers[i];

                    if (buffer.SizeInBytes >= size)
                    {
                        ret = buffer;
                        // Swap-remove: move the last element into this slot and shrink the list in
                        // O(1) rather than paying the O(n) element shift that List.Remove causes.
                        int last = availableStagingBuffers.Count - 1;
                        availableStagingBuffers[i] = availableStagingBuffers[last];
                        availableStagingBuffers.RemoveAt(last);
                        break;
                    }
                }

                if (ret == null)
                {
                    ret = (VkBuffer)gd.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
                    ret.Name = $"Staging Buffer (CommandList {name})";
                }

                currentStagingInfo.BuffersUsed.Add(ret);
                return ret;
            }
        }

        private void disposeCore()
        {
            if (!destroyed)
            {
                destroyed = true;
                gd.DeviceApi.vkDestroyCommandPool(pool, null);

                Debug.Assert(submittedStagingInfos.Count == 0);

                foreach (var buffer in availableStagingBuffers) buffer.Dispose();
            }
        }

        private StagingResourceInfo getStagingResourceInfo()
        {
            lock (stagingLock)
            {
                StagingResourceInfo ret;
                int availableCount = availableStagingInfos.Count;

                if (availableCount > 0)
                {
                    ret = availableStagingInfos[availableCount - 1];
                    availableStagingInfos.RemoveAt(availableCount - 1);
                }
                else
                    ret = new StagingResourceInfo();

                return ret;
            }
        }

        private void recycleStagingInfo(StagingResourceInfo info)
        {
            lock (stagingLock)
            {
                recycleStagingInfoCore(info);
            }
        }

        // Lock-free inner body of recycleStagingInfo. Must be called while stagingLock is held.
        // Exists as a separate method because CommandBufferCompleted already holds stagingLock
        // and System.Threading.Lock is non-reentrant — calling recycleStagingInfo from within
        // lock(stagingLock) would deadlock.
        private void recycleStagingInfoCore(StagingResourceInfo info)
        {
            foreach (var buffer in info.BuffersUsed) availableStagingBuffers.Add(buffer);

            foreach (var rrc in info.Resources) rrc.Decrement();

            info.Clear();

            availableStagingInfos.Add(info);
        }

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
        {
            var clearValue = new VkClearValue
            {
                color = new VkClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A)
            };

            if (activeRenderPass != VkRenderPass.Null)
            {
                var clearAttachment = new VkClearAttachment
                {
                    colorAttachment = index,
                    aspectMask = VkImageAspectFlags.Color,
                    clearValue = clearValue
                };

                var colorTex = currentFramebuffer.ColorTargets[(int)index].Target;
                var clearRect = new VkClearRect
                {
                    baseArrayLayer = 0,
                    layerCount = 1,
                    rect = new VkRect2D(0, 0, colorTex.Width, colorTex.Height)
                };

                gd.DeviceApi.vkCmdClearAttachments(CommandBuffer, 1, &clearAttachment, 1, &clearRect);
            }
            else
            {
                // Queue up the clear value for the next RenderPass.
                clearValues[index] = clearValue;
                validColorClearValues[index] = true;
            }
        }

        private protected override void ClearDepthStencilCore(float depth, byte stencil)
        {
            var clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(depth, stencil) };

            if (activeRenderPass != VkRenderPass.Null)
            {
                var aspect = currentFramebuffer.DepthTarget is FramebufferAttachment depthAttachment && FormatHelpers.IsStencilFormat(depthAttachment.Target.Format)
                    ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                    : VkImageAspectFlags.Depth;

                var clearAttachment = new VkClearAttachment
                {
                    aspectMask = aspect,
                    clearValue = clearValue
                };

                uint renderableWidth = currentFramebuffer.RenderableWidth;
                uint renderableHeight = currentFramebuffer.RenderableHeight;

                if (renderableWidth > 0 && renderableHeight > 0)
                {
                    var clearRect = new VkClearRect
                    {
                        baseArrayLayer = 0,
                        layerCount = 1,
                        rect = new VkRect2D(0, 0, renderableWidth, renderableHeight)
                    };

                    gd.DeviceApi.vkCmdClearAttachments(CommandBuffer, 1, &clearAttachment, 1, &clearRect);
                }
            }
            else
            {
                // Queue up the clear value for the next RenderPass.
                depthClearValue = clearValue;
            }
        }

        private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            preDrawCommand();
            gd.DeviceApi.vkCmdDraw(CommandBuffer, vertexCount, instanceCount, vertexStart, instanceStart);
        }

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            preDrawCommand();
            gd.DeviceApi.vkCmdDrawIndexed(CommandBuffer, indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            var deviceBuffer = vkBuffer.DeviceBuffer;
            ulong offset64 = offset;

            Util.EnsureArrayMinimumSize(ref cachedVertexBuffers, index + 1);
            Util.EnsureArrayMinimumSize(ref cachedVertexOffsets, index + 1);

            // Skip the GPU call when the same buffer+offset is already bound in this slot.
            if (cachedVertexBuffers[index] == vkBuffer && cachedVertexOffsets[index] == offset64)
                return;

            cachedVertexBuffers[index] = vkBuffer;
            cachedVertexOffsets[index] = offset64;

            gd.DeviceApi.vkCmdBindVertexBuffers(CommandBuffer, index, 1, &deviceBuffer, &offset64);
            currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        }

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            var vkIndexType = VkFormats.VdToVkIndexFormat(format);
            ulong offset64 = offset;

            // Skip the GPU call when the same buffer+offset+type is already bound.
            if (cachedIndexBuffer == vkBuffer && cachedIndexBufferOffset == offset64 && cachedIndexType == vkIndexType)
                return;

            cachedIndexBuffer = vkBuffer;
            cachedIndexBufferOffset = offset64;
            cachedIndexType = vkIndexType;

            gd.DeviceApi.vkCmdBindIndexBuffer(CommandBuffer, vkBuffer.DeviceBuffer, offset64, vkIndexType);
            currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            var vkPipeline = Util.AssertSubtype<Pipeline, VkPipeline>(pipeline);

            if (!pipeline.IsComputePipeline && currentGraphicsPipeline != pipeline)
            {
                Util.EnsureArrayMinimumSize(ref currentGraphicsResourceSets, vkPipeline.ResourceSetCount);
                // Per Vulkan spec §14.2.2, descriptor sets survive a pipeline switch when both
                // pipelines share the same VkPipelineLayout object (guaranteed by the layout
                // cache in VkGraphicsDevice).  Only clear the bound sets when the layout actually
                // changes; skipping clearSets avoids unnecessary vkCmdBindDescriptorSets calls
                // for the common case of pipeline variants that share a resource layout.
                if (currentGraphicsPipeline == null
                    || currentGraphicsPipeline.PipelineLayout != vkPipeline.PipelineLayout)
                    clearSets(currentGraphicsResourceSets);
                Util.EnsureArrayMinimumSize(ref graphicsResourceSetsChanged, vkPipeline.ResourceSetCount);
                gd.DeviceApi.vkCmdBindPipeline(CommandBuffer, VkPipelineBindPoint.Graphics, vkPipeline.DevicePipeline);
                currentGraphicsPipeline = vkPipeline;
            }
            else if (pipeline.IsComputePipeline && currentComputePipeline != pipeline)
            {
                Util.EnsureArrayMinimumSize(ref currentComputeResourceSets, vkPipeline.ResourceSetCount);
                if (currentComputePipeline == null
                    || currentComputePipeline.PipelineLayout != vkPipeline.PipelineLayout)
                    clearSets(currentComputeResourceSets);
                Util.EnsureArrayMinimumSize(ref computeResourceSetsChanged, vkPipeline.ResourceSetCount);
                gd.DeviceApi.vkCmdBindPipeline(CommandBuffer, VkPipelineBindPoint.Compute, vkPipeline.DevicePipeline);
                currentComputePipeline = vkPipeline;
            }

            currentStagingInfo.Resources.Add(vkPipeline.RefCount);
        }

        private protected override void GenerateMipmapsCore(Texture texture)
        {
            ensureNoRenderPass();
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            currentStagingInfo.Resources.Add(vkTex.RefCount);

            uint layerCount = vkTex.ArrayLayers;
            if ((vkTex.Usage & TextureUsage.Cubemap) != 0) layerCount *= 6;

            uint width = vkTex.Width;
            uint height = vkTex.Height;
            uint depth = vkTex.Depth;

            // Compute the correct aspect mask for the blit subresource ranges.
            // Using VkImageAspectFlags.Color on a depth-format image is a Vulkan spec
            // violation and triggers validation errors.
            var blitAspect = (vkTex.Usage & TextureUsage.DepthStencil) != 0
                ? FormatHelpers.IsStencilFormat(vkTex.Format)
                    ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                    : VkImageAspectFlags.Depth
                : VkImageAspectFlags.Color;

            for (uint level = 1; level < vkTex.MipLevels; level++)
            {
                // Collect per-layer barriers for the src mip (level-1 → TransferSrcOptimal) and
                // dst mip (level → TransferDstOptimal) into imageBarrierBatch, then flush once.
                //
                // Iterating per-layer (instead of using a single all-layer range barrier) is
                // required for correctness when array layers are in mixed layouts — e.g. after
                // rendering to only some layers of a cubemap.  A range barrier with the wrong
                // oldLayout for any subresource in the range is a Vulkan spec violation (§12.8)
                // that can silently corrupt tile-cache state on TBDR GPUs (Adreno/Mali).
                //
                // The common case (all layers in the same layout) produces per-layer entries that
                // are structurally identical except for baseArrayLayer, all batched into a single
                // vkCmdPipelineBarrier call — identical GPU cost to the previous range approach.
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    if (vkTex.TryGetLayoutTransitionBarrier(level - 1, 1, layer, 1,
                            VkImageLayout.TransferSrcOptimal,
                            out var srcBarrier, out var srcStageA, out var dstStageA))
                    {
                        imageBarrierBatch.Add(srcBarrier);
                        barrierBatchSrcStage |= srcStageA;
                        barrierBatchDstStage |= dstStageA;
                    }

                    if (vkTex.TryGetLayoutTransitionBarrier(level, 1, layer, 1,
                            VkImageLayout.TransferDstOptimal,
                            out var dstBarrier, out var srcStageB, out var dstStageB))
                    {
                        imageBarrierBatch.Add(dstBarrier);
                        barrierBatchSrcStage |= srcStageB;
                        barrierBatchDstStage |= dstStageB;
                    }
                }

                flushTransitionBarriers();

                var deviceImage = vkTex.OptimalDeviceImage;
                uint mipWidth = Math.Max(width >> 1, 1);
                uint mipHeight = Math.Max(height >> 1, 1);
                uint mipDepth = Math.Max(depth >> 1, 1);

                var region = new VkImageBlit
                {
                    srcSubresource = new VkImageSubresourceLayers
                    {
                        aspectMask = blitAspect,
                        baseArrayLayer = 0,
                        layerCount = layerCount,
                        mipLevel = level - 1
                    },
                    dstSubresource = new VkImageSubresourceLayers
                    {
                        aspectMask = blitAspect,
                        baseArrayLayer = 0,
                        layerCount = layerCount,
                        mipLevel = level
                    },
                };
                region.srcOffsets[0] = new VkOffset3D();
                region.srcOffsets[1] = new VkOffset3D { x = (int)width, y = (int)height, z = (int)depth };
                region.dstOffsets[0] = new VkOffset3D();
                region.dstOffsets[1] = new VkOffset3D { x = (int)mipWidth, y = (int)mipHeight, z = (int)mipDepth };

                gd.DeviceApi.vkCmdBlitImage(
                    CommandBuffer,
                    deviceImage, VkImageLayout.TransferSrcOptimal,
                    deviceImage, VkImageLayout.TransferDstOptimal,
                    1, &region,
                    gd.GetFormatFilter(vkTex.VkFormat));

                width = mipWidth;
                height = mipHeight;
                depth = mipDepth;
            }

            // After the mip-generation loop all levels are in either TransferSrcOptimal (0..N-2)
            // or TransferDstOptimal (N-1). Transition back to the stable layout that matches the
            // texture's usage so subsequent render-pass initialLayout references and layout-
            // tracking in beginCurrentDynamicRendering/preDrawCommand stay correct.
            //
            // Without this, non-sampled RenderTarget textures left in TransferSrcOptimal would
            // mismatch the baked initialLayout=ColorAttachmentOptimal in the legacy render pass,
            // causing a Vulkan validation error and potential GPU hang on tile-based hardware.
            //
            // Accumulate all post-loop transitions into imageBarrierBatch and emit them in a
            // single vkCmdPipelineBarrier call. For a 9-mip cubemap (9 × 6 = 54 subresources)
            // this costs a single pipeline barrier call regardless of mip/layer count.
            var finalLayout = (vkTex.Usage & TextureUsage.Sampled) != 0
                ? VkImageLayout.ShaderReadOnlyOptimal
                : (vkTex.Usage & TextureUsage.RenderTarget) != 0
                    ? VkImageLayout.ColorAttachmentOptimal
                    : (vkTex.Usage & TextureUsage.DepthStencil) != 0
                        ? VkImageLayout.DepthStencilAttachmentOptimal
                        // Pure storage textures (no Sampled/RenderTarget/DepthStencil flags) must
                        // end in General so subsequent storage image binds see the correct layout.
                        : VkImageLayout.General;

            for (uint mip = 0; mip < vkTex.MipLevels; mip++)
            {
                for (uint layer = 0; layer < layerCount; layer++)
                {
                    if (vkTex.TryGetLayoutTransitionBarrier(mip, 1, layer, 1, finalLayout,
                            out var barrier, out var src, out var dst))
                    {
                        imageBarrierBatch.Add(barrier);
                        barrierBatchSrcStage |= src;
                        barrierBatchDstStage |= dst;
                    }
                }
            }

            flushTransitionBarriers();
        }

        private protected override void PushDebugGroupCore(string name)
        {
            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            if (gd.debugUtilsEnabled)
            {
                // VK_EXT_debug_utils preferred path (RenderDoc-compatible).
                // All debug_utils functions are on VkInstanceApi in Vortice.
                var labelInfo = new VkDebugUtilsLabelEXT { pLabelName = utf8Ptr };
                gd.InstanceApi.vkCmdBeginDebugUtilsLabelEXT(CommandBuffer, &labelInfo);
            }
            else if (gd.debugMarkerEnabled)
            {
                var markerInfo = new VkDebugMarkerMarkerInfoEXT { pMarkerName = utf8Ptr };
                gd.DeviceApi.vkCmdDebugMarkerBeginEXT(CommandBuffer, &markerInfo);
            }
        }

        private protected override void PopDebugGroupCore()
        {
            if (gd.debugUtilsEnabled)
                gd.InstanceApi.vkCmdEndDebugUtilsLabelEXT(CommandBuffer);
            else if (gd.debugMarkerEnabled)
                gd.DeviceApi.vkCmdDebugMarkerEndEXT(CommandBuffer);
        }

        private protected override void InsertDebugMarkerCore(string name)
        {
            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            if (gd.debugUtilsEnabled)
            {
                var labelInfo = new VkDebugUtilsLabelEXT { pLabelName = utf8Ptr };
                gd.InstanceApi.vkCmdInsertDebugUtilsLabelEXT(CommandBuffer, &labelInfo);
            }
            else if (gd.debugMarkerEnabled)
            {
                var markerInfo = new VkDebugMarkerMarkerInfoEXT { pMarkerName = utf8Ptr };
                gd.DeviceApi.vkCmdDebugMarkerInsertEXT(CommandBuffer, &markerInfo);
            }
        }

        private protected override void SetShadingRateCore(ShadingRate rate)
        {
            if (!gd.HasFragmentShadingRate)
                return;

            // Map Veldrid ShadingRate to VkExtent2D fragment size.
            var fragmentSize = rate switch
            {
                ShadingRate.Rate1x2 => new VkExtent2D(1, 2),
                ShadingRate.Rate2x1 => new VkExtent2D(2, 1),
                ShadingRate.Rate2x2 => new VkExtent2D(2, 2),
                ShadingRate.Rate2x4 => new VkExtent2D(2, 4),
                ShadingRate.Rate4x2 => new VkExtent2D(4, 2),
                ShadingRate.Rate4x4 => new VkExtent2D(4, 4),
                _ => new VkExtent2D(1, 1), // Rate1x1 or default
            };

            // Use KEEP for both combiners (pipeline + image) — the per-draw rate is authoritative.
            var combiners = stackalloc VkFragmentShadingRateCombinerOpKHR[2];
            combiners[0] = VkFragmentShadingRateCombinerOpKHR.Keep;
            combiners[1] = VkFragmentShadingRateCombinerOpKHR.Keep;

            gd.DeviceApi.vkCmdSetFragmentShadingRateKHR(CommandBuffer, &fragmentSize, combiners);
        }

        private protected override void DispatchMeshCore(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            if (!gd.HasMeshShader)
                throw new NotSupportedException("Mesh shaders are not supported by this Vulkan device.");

            preDrawCommand();
            gd.DeviceApi.vkCmdDrawMeshTasksEXT(CommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        private class StagingResourceInfo
        {
            public List<VkBuffer> BuffersUsed { get; } = new List<VkBuffer>();
            public HashSet<ResourceRefCount> Resources { get; } = new HashSet<ResourceRefCount>();

            public void Clear()
            {
                BuffersUsed.Clear();
                Resources.Clear();
            }
        }
    }
}
