using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vulkan;
using static Vulkan.VulkanNative;

namespace Veldrid.Vk
{
    /// <summary>
    ///     Vulkan implementation of <see cref="BufferUpdateBatch" />: stages every pending region into a
    ///     single growable host-visible buffer, then on <see cref="Submit" /> records one command buffer that
    ///     performs all the buffer-to-buffer copies and ends with a single <c>vkQueueSubmit</c>.
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

            // vkCmdCopyBuffer requires srcOffset and dstOffset to be 4-byte aligned and size to be a
            // multiple of 4 only when used with vkCmdUpdateBuffer; for vkCmdCopyBuffer the only constraint
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

            // One vkCmdCopyBuffer per destination buffer, batching all regions for that destination.
            // Sort by destination identity so identical destinations group naturally; then issue one call
            // per group with a contiguous span of regions backed by a single pooled scratch array.
            pendingCopies.Sort(static (a, b) =>
                RuntimeHelpers.GetHashCode(a.Destination).CompareTo(RuntimeHelpers.GetHashCode(b.Destination)));

            var regionScratch = ArrayPool<VkBufferCopy>.Shared.Rent(pendingCopies.Count);
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
                        vkCmdCopyBuffer(cb, stagingBuffer.DeviceBuffer, dst.DeviceBuffer, (uint)regionCount, regionsPtr);
                }
            }
            finally
            {
                ArrayPool<VkBufferCopy>.Shared.Return(regionScratch);
            }

            pool.EndAndSubmit(cb);
            gd.RegisterSubmittedStagingBuffer(cb, stagingBuffer);

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
