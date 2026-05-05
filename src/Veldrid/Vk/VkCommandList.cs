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
        private readonly List<VkTexture> preDrawSampledImages = new List<VkTexture>(32);

        // Accumulated image-memory barriers for the next batched gd.DeviceApi.vkCmdPipelineBarrier flush.
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
                for (int i = 0; i < submittedCommandBuffers.Count; i++)
                {
                    var submittedCb = submittedCommandBuffers[i];

                    if (submittedCb == completedCb)
                    {
                        availableCommandBuffers.Enqueue(completedCb);
                        submittedCommandBuffers.RemoveAt(i);
                        i -= 1;
                    }
                }
            }

            lock (stagingLock)
            {
                if (submittedStagingInfos.TryGetValue(completedCb, out var info))
                {
                    recycleStagingInfo(info);
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

            if ((destUsage & (BufferUsage.StructuredBufferReadOnly | BufferUsage.StructuredBufferReadWrite)) != 0)
            {
                dstAccess |= VkAccessFlags.ShaderRead;
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

                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    VkImageLayout.TransferSrcOptimal);

                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    VkImageLayout.TransferDstOptimal);

                deviceApi.vkCmdCopyImage(
                    cb,
                    srcVkTexture.OptimalDeviceImage,
                    VkImageLayout.TransferSrcOptimal,
                    dstVkTexture.OptimalDeviceImage,
                    VkImageLayout.TransferDstOptimal,
                    1,
                    &region);

                if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    srcVkTexture.TransitionImageLayout(
                        cb,
                        srcMipLevel,
                        1,
                        srcBaseArrayLayer,
                        layerCount,
                        VkImageLayout.ShaderReadOnlyOptimal);
                }

                if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    dstVkTexture.TransitionImageLayout(
                        cb,
                        dstMipLevel,
                        1,
                        dstBaseArrayLayer,
                        layerCount,
                        VkImageLayout.ShaderReadOnlyOptimal);
                }
            }
            else if (sourceIsStaging && !destIsStaging)
            {
                var srcBuffer = srcVkTexture.StagingBuffer;
                var srcLayout = srcVkTexture.GetSubresourceLayout(
                    srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
                var dstImage = dstVkTexture.OptimalDeviceImage;
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    VkImageLayout.TransferDstOptimal);

                var dstSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = (dstVkTexture.Usage & TextureUsage.DepthStencil) != 0
                        ? FormatHelpers.IsStencilFormat(dstVkTexture.Format)
                            ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                            : VkImageAspectFlags.Depth
                        : VkImageAspectFlags.Color,
                    layerCount = layerCount,
                    mipLevel = dstMipLevel,
                    baseArrayLayer = dstBaseArrayLayer
                };

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

                var regions = new VkBufferImageCopy
                {
                    bufferOffset = srcLayout.offset
                                   + srcZ * depthPitch
                                   + compressedY * rowPitch
                                   + compressedX * blockSizeInBytes,
                    bufferRowLength = bufferRowLength,
                    bufferImageHeight = bufferImageHeight,
                    imageExtent = new VkExtent3D { width = copyWidth, height = copyheight, depth = depth },
                    imageOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                    imageSubresource = dstSubresource
                };

                deviceApi.vkCmdCopyBufferToImage(cb, srcBuffer, dstImage, VkImageLayout.TransferDstOptimal, 1, &regions);

                if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    dstVkTexture.TransitionImageLayout(
                        cb,
                        dstMipLevel,
                        1,
                        dstBaseArrayLayer,
                        layerCount,
                        VkImageLayout.ShaderReadOnlyOptimal);
                }
            }
            else if (!sourceIsStaging)
            {
                var srcImage = srcVkTexture.OptimalDeviceImage;
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    VkImageLayout.TransferSrcOptimal);

                var dstBuffer = dstVkTexture.StagingBuffer;

                var aspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                    ? VkImageAspectFlags.Depth
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

                if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
                {
                    srcVkTexture.TransitionImageLayout(
                        cb,
                        srcMipLevel,
                        1,
                        srcBaseArrayLayer,
                        layerCount,
                        VkImageLayout.ShaderReadOnlyOptimal);
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

            vkSource.TransitionImageLayout(CommandBuffer, 0, 1, 0, 1, VkImageLayout.TransferSrcOptimal);
            vkDestination.TransitionImageLayout(CommandBuffer, 0, 1, 0, 1, VkImageLayout.TransferDstOptimal);

            gd.DeviceApi.vkCmdResolveImage(
                CommandBuffer,
                vkSource.OptimalDeviceImage,
                VkImageLayout.TransferSrcOptimal,
                vkDestination.OptimalDeviceImage,
                VkImageLayout.TransferDstOptimal,
                1,
                &region);

            if ((vkDestination.Usage & TextureUsage.Sampled) != 0) vkDestination.TransitionImageLayout(CommandBuffer, 0, 1, 0, 1, VkImageLayout.ShaderReadOnlyOptimal);
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
            // Flush any textures promoted from compute-storage back to shader-read.
            appendTransitions(preDrawSampledImages, VkImageLayout.ShaderReadOnlyOptimal);
            preDrawSampledImages.Clear();

            // Only re-scan resource sets when something actually changed.
            // An FBO switch (newFramebuffer=true) can leave attachment textures in
            // ColorAttachmentOptimal; a newly-bound resource set may contain textures whose
            // layout we haven't ensured yet. When neither is true every sampled texture is
            // already in ShaderReadOnlyOptimal from the previous draw, so the scan is free.
            if (currentGraphicsPipeline != null)
            {
                bool needScan = newFramebuffer;

                if (!needScan)
                {
                    uint resourceSetCount = currentGraphicsPipeline.ResourceSetCount;

                    for (int s = 0; s < resourceSetCount && s < graphicsResourceSetsChanged.Length; s++)
                    {
                        if (graphicsResourceSetsChanged[s])
                        {
                            needScan = true;
                            break;
                        }
                    }
                }

                if (needScan)
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

                            // Dual-use storage textures (Sampled | Storage) must return to
                            // ShaderReadOnlyOptimal before the next draw so they can be sampled.
                            for (int texIdx = 0; texIdx < vkSet.StorageTextures.Count; texIdx++)
                            {
                                var storageTex = vkSet.StorageTextures[texIdx];
                                if ((storageTex.Usage & TextureUsage.Sampled) != 0)
                                    preDrawSampledImages.Add(storageTex);
                            }
                        }
                    }
                }
            }

            // Emit all accumulated transitions as a single gd.DeviceApi.vkCmdPipelineBarrier.
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
        // emitting any Vulkan commands.  VkTexture.TryGetLayoutTransitionBarrier is a no-op when
        // the texture is already in the requested layout, so clean textures cost only an array read.
        private void appendTransitions(List<VkTexture> textures, VkImageLayout layout)
        {
            if (textures.Count == 0) return;

            for (int i = 0; i < textures.Count; i++)
            {
                var tex = textures[i];

                if (tex.TryGetLayoutTransitionBarrier(0, tex.MipLevels, 0, tex.ActualArrayLayers, layout,
                        out var barrier, out var src, out var dst))
                {
                    imageBarrierBatch.Add(barrier);
                    barrierBatchSrcStage |= src;
                    barrierBatchDstStage |= dst;
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

                    if (vkSet.IsPushDescriptor)
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
                                if (nextSet.IsPushDescriptor)
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

                for (int texIdx = 0; texIdx < vkSet.StorageTextures.Count; texIdx++)
                {
                    var storageTex = vkSet.StorageTextures[texIdx];
                    if ((storageTex.Usage & TextureUsage.Sampled) != 0) preDrawSampledImages.Add(storageTex);
                }
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
                colorAttachments[i].storeOp = VkAttachmentStoreOp.Store;

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
                    depthAttachment.loadOp = newFramebuffer ? VkAttachmentLoadOp.DontCare : VkAttachmentLoadOp.Load;
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
                if (gd.UseKhrDynamicRendering)
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

            // Place a barrier between RenderPasses, so that color / depth outputs
            // can be read in subsequent passes.
            //
            // On Vulkan 1.3+ devices use granular stage masks instead of the catch-all
            // BottomOfPipe → TopOfPipe, which causes full tile flushes on mobile GPUs.
            VkPipelineStageFlags srcStage;
            VkPipelineStageFlags dstStage;

            if (gd.DeviceApiVersion.IsAtLeast(1, 3))
            {
                srcStage = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.LateFragmentTests;
                dstStage = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.EarlyFragmentTests
                           | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.VertexInput;
            }
            else
            {
                srcStage = VkPipelineStageFlags.BottomOfPipe;
                dstStage = VkPipelineStageFlags.TopOfPipe;
            }

            gd.DeviceApi.vkCmdPipelineBarrier(
                CommandBuffer,
                srcStage,
                dstStage,
                VkDependencyFlags.None,
                0,
                null,
                0,
                null,
                0,
                null);
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
                foreach (var buffer in info.BuffersUsed) availableStagingBuffers.Add(buffer);

                foreach (var rrc in info.Resources) rrc.Decrement();

                info.Clear();

                availableStagingInfos.Add(info);
            }
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
                clearSets(currentGraphicsResourceSets);
                Util.EnsureArrayMinimumSize(ref graphicsResourceSetsChanged, vkPipeline.ResourceSetCount);
                gd.DeviceApi.vkCmdBindPipeline(CommandBuffer, VkPipelineBindPoint.Graphics, vkPipeline.DevicePipeline);
                currentGraphicsPipeline = vkPipeline;
            }
            else if (pipeline.IsComputePipeline && currentComputePipeline != pipeline)
            {
                Util.EnsureArrayMinimumSize(ref currentComputeResourceSets, vkPipeline.ResourceSetCount);
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

            for (uint level = 1; level < vkTex.MipLevels; level++)
            {
                vkTex.TransitionImageLayoutNonmatching(CommandBuffer, level - 1, 1, 0, layerCount, VkImageLayout.TransferSrcOptimal);
                vkTex.TransitionImageLayoutNonmatching(CommandBuffer, level, 1, 0, layerCount, VkImageLayout.TransferDstOptimal);

                var deviceImage = vkTex.OptimalDeviceImage;
                uint mipWidth = Math.Max(width >> 1, 1);
                uint mipHeight = Math.Max(height >> 1, 1);
                uint mipDepth = Math.Max(depth >> 1, 1);

                var region = new VkImageBlit
                {
                    srcSubresource = new VkImageSubresourceLayers
                    {
                        aspectMask = VkImageAspectFlags.Color,
                        baseArrayLayer = 0,
                        layerCount = layerCount,
                        mipLevel = level - 1
                    },
                    dstSubresource = new VkImageSubresourceLayers
                    {
                        aspectMask = VkImageAspectFlags.Color,
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

            if ((vkTex.Usage & TextureUsage.Sampled) != 0) vkTex.TransitionImageLayoutNonmatching(CommandBuffer, 0, vkTex.MipLevels, 0, layerCount, VkImageLayout.ShaderReadOnlyOptimal);
        }

        private protected override void PushDebugGroupCore(string name)
        {
            if (!gd.debugMarkerEnabled) return;

            var markerInfo = new VkDebugMarkerMarkerInfoEXT();

            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            markerInfo.pMarkerName = utf8Ptr;

            gd.DeviceApi.vkCmdDebugMarkerBeginEXT(CommandBuffer, &markerInfo);
        }

        private protected override void PopDebugGroupCore()
        {
            if (!gd.debugMarkerEnabled) return;
            gd.DeviceApi.vkCmdDebugMarkerEndEXT(CommandBuffer);
        }

        private protected override void InsertDebugMarkerCore(string name)
        {
            if (!gd.debugMarkerEnabled) return;

            var markerInfo = new VkDebugMarkerMarkerInfoEXT();

            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            markerInfo.pMarkerName = utf8Ptr;

            gd.DeviceApi.vkCmdDebugMarkerInsertEXT(CommandBuffer, &markerInfo);
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
