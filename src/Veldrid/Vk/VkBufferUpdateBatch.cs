using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
namespace Veldrid.Vk
{
    /// <summary>
    ///     Vulkan implementation of <see cref="BufferUpdateBatch" />: stages every pending region into a
    ///     single growable host-visible buffer, then on <see cref="Submit" /> records one command buffer that
    ///     performs all the buffer-to-buffer copies and ends with a single <c>gd.DeviceApi.vkQueueSubmit</c>.
    ///     Mirrors <see cref="VkTextureUpdateBatch" /> for the buffer path.
    ///
    ///     <para>
    ///         Persistently-mapped destinations bypass the batch entirely: the existing
    ///         <c>UpdateBufferCore</c> path memcpys directly into the mapped pointer with no submission, so
    ///         routing them through a batch would only add overhead.
    ///     </para>
    /// </summary>
    internal sealed unsafe class VkBufferUpdateBatch : BufferUpdateBatch
    {
        private readonly VkGraphicsDevice gd;
        private readonly List<PendingCopy> pendingCopies = new List<PendingCopy>();

        private VkBuffer stagingBuffer;
        private uint stagingOffset;

        public VkBufferUpdateBatch(VkGraphicsDevice gd)
        {
            this.gd = gd;
            MarkOpen();
        }

        internal void Reopen()
        {
            Debug.Assert(stagingBuffer == null);
            Debug.Assert(pendingCopies.Count == 0);
            stagingOffset = 0;
            MarkOpen();
        }

        public override void Add(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            CheckOpen();

            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);

            // Persistent-mapped fast path matches UpdateBufferCore: write straight into the mapped pointer,
            // no submission needed, so batching gives nothing.
            if (vkBuffer.Memory.IsPersistentMapped)
            {
                byte* destPtr = (byte*)vkBuffer.Memory.BlockMappedPointer + bufferOffsetInBytes;
                Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);
                return;
            }

            // gd.DeviceApi.vkCmdCopyBuffer requires srcOffset and dstOffset to be 4-byte aligned and size to be a
            // multiple of 4 only when used with vkCmdUpdateBuffer; for gd.DeviceApi.vkCmdCopyBuffer the only constraint
            // is that the regions don't exceed buffer bounds. We still 4-align the staging offset to keep
            // adjacent memcpys naturally aligned and to satisfy any driver fast-paths that prefer it.
            stagingOffset = align(stagingOffset, 4u);
            ensureStagingCapacity(stagingOffset + sizeInBytes);

            byte* dst = (byte*)stagingBuffer.Memory.BlockMappedPointer + stagingOffset;
            Unsafe.CopyBlock(dst, source.ToPointer(), sizeInBytes);

            pendingCopies.Add(new PendingCopy
            {
                Destination = vkBuffer,
                Region = new VkBufferCopy
                {
                    srcOffset = stagingOffset,
                    dstOffset = bufferOffsetInBytes,
                    size = sizeInBytes
                }
            });

            stagingOffset += sizeInBytes;
        }

        public override void Submit()
        {
            CheckOpen();

            if (pendingCopies.Count == 0) return;

            var pool = gd.GetFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();

            // Sort by the underlying VkBuffer handle (a unique uint64 device address) so that all
            // pending copies to the same destination land in contiguous slots. Using
            // RuntimeHelpers.GetHashCode (object identity hash) would also group same-object entries,
            // but hash collisions can interleave two different destinations and defeat the batching.
            pendingCopies.Sort(static (a, b) =>
                a.Destination.DeviceBuffer.Handle.CompareTo(b.Destination.DeviceBuffer.Handle));

            var regionScratch = ArrayPool<VkBufferCopy>.Shared.Rent(pendingCopies.Count);

            // Accumulate dstAccessMask / dstStageFlags across all destination buffers so we can
            // emit one VkMemoryBarrier that covers every pending copy (TRANSFER_WRITE → all consumers).
            var combinedDstAccess = VkAccessFlags.None;
            var combinedDstStage = VkPipelineStageFlags.None;

            try
            {
                int i = 0;
                while (i < pendingCopies.Count)
                {
                    var dst = pendingCopies[i].Destination;
                    int groupStart = i;
                    while (i < pendingCopies.Count && ReferenceEquals(pendingCopies[i].Destination, dst)) i++;

                    int regionCount = i - groupStart;
                    for (int j = 0; j < regionCount; j++) regionScratch[j] = pendingCopies[groupStart + j].Region;

                    fixed (VkBufferCopy* regionsPtr = regionScratch)
                        gd.DeviceApi.vkCmdCopyBuffer(cb, stagingBuffer.DeviceBuffer, dst.DeviceBuffer, (uint)regionCount, regionsPtr);

                    // Accumulate the consumer masks for this destination.
                    var destUsage = dst.Usage;
                    if ((destUsage & BufferUsage.UniformBuffer) != 0)
                    {
                        combinedDstAccess |= VkAccessFlags.UniformRead;
                        combinedDstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
                    }
                    if ((destUsage & BufferUsage.VertexBuffer) != 0)
                    {
                        combinedDstAccess |= VkAccessFlags.VertexAttributeRead;
                        combinedDstStage |= VkPipelineStageFlags.VertexInput;
                    }
                    if ((destUsage & BufferUsage.IndexBuffer) != 0)
                    {
                        combinedDstAccess |= VkAccessFlags.IndexRead;
                        combinedDstStage |= VkPipelineStageFlags.VertexInput;
                    }
                    if ((destUsage & BufferUsage.StructuredBufferReadOnly) != 0)
                    {
                        combinedDstAccess |= VkAccessFlags.ShaderRead;
                        combinedDstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
                    }
                    if ((destUsage & BufferUsage.StructuredBufferReadWrite) != 0)
                    {
                        // Storage RW buffers can be both read and written by shaders; include ShaderWrite
                        // so the barrier covers subsequent shader writes that follow the transfer write.
                        combinedDstAccess |= VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
                        combinedDstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
                    }
                    if ((destUsage & BufferUsage.IndirectBuffer) != 0)
                    {
                        combinedDstAccess |= VkAccessFlags.IndirectCommandRead;
                        combinedDstStage |= VkPipelineStageFlags.DrawIndirect;
                    }
                }
            }
            finally
            {
                ArrayPool<VkBufferCopy>.Shared.Return(regionScratch);
            }

            // Emit a single memory barrier making all TRANSFER_WRITE operations visible to every
            // GPU consumer type present in the batch.  Omitting this barrier is a Vulkan spec
            // violation that can cause stale data to be read by subsequent draw/dispatch commands.
            if (combinedDstAccess == VkAccessFlags.None)
            {
                combinedDstAccess = VkAccessFlags.MemoryRead;
                combinedDstStage = VkPipelineStageFlags.AllCommands;
            }

            var memBarrier = new VkMemoryBarrier();
            memBarrier.sType = VkStructureType.MemoryBarrier;
            memBarrier.srcAccessMask = VkAccessFlags.TransferWrite;
            memBarrier.dstAccessMask = combinedDstAccess;
            gd.DeviceApi.vkCmdPipelineBarrier(
                cb,
                VkPipelineStageFlags.Transfer,
                combinedDstStage,
                VkDependencyFlags.None,
                1, &memBarrier,
                0, null,
                0, null);

            // Register BEFORE EndAndSubmit: if the GPU completes the fence between submit and
            // registration, completeFenceSubmission would miss the entry and permanently leak the buffer.
            gd.RegisterSubmittedStagingBuffer(cb, stagingBuffer);
            pool.EndAndSubmit(cb);

            stagingBuffer = null;
            stagingOffset = 0;
            pendingCopies.Clear();
        }

        protected override void ReleaseToPool()
        {
            if (stagingBuffer != null)
            {
                gd.ReturnUnusedStagingBuffer(stagingBuffer);
                stagingBuffer = null;
            }

            stagingOffset = 0;
            pendingCopies.Clear();
            gd.ReturnBufferUpdateBatch(this);
        }

        private void ensureStagingCapacity(uint requiredSize)
        {
            if (stagingBuffer != null && stagingBuffer.SizeInBytes >= requiredSize) return;

            uint newSize = stagingBuffer == null
                ? requiredSize
                : Math.Max(stagingBuffer.SizeInBytes * 2, requiredSize);

            var newBuffer = gd.RentStagingBuffer(newSize);

            if (stagingBuffer != null)
            {
                // Preserve already-staged bytes so previously-recorded srcOffsets remain valid.
                Buffer.MemoryCopy(
                    stagingBuffer.Memory.BlockMappedPointer,
                    newBuffer.Memory.BlockMappedPointer,
                    newBuffer.SizeInBytes,
                    stagingOffset);
                gd.ReturnUnusedStagingBuffer(stagingBuffer);
            }

            stagingBuffer = newBuffer;
        }

        private static uint align(uint value, uint alignment)
        {
            uint remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }

        private struct PendingCopy
        {
            public VkBuffer Destination;
            public VkBufferCopy Region;
        }
    }
}
