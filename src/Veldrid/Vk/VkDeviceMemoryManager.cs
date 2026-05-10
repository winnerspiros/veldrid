using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkDeviceMemoryManager : IDisposable
    {
        private const ulong min_dedicated_allocation_size_dynamic = 1024 * 1024 * 16;
        private const ulong min_dedicated_allocation_size_non_dynamic = 1024 * 1024 * 64;
        private readonly VkDevice device;
        private readonly VkDeviceApi deviceApi;
        private readonly ulong bufferImageGranularity;
        private readonly Lock @lock = new Lock();
        private readonly Dictionary<uint, ChunkAllocatorSet> allocatorsByMemoryTypeUnmapped = new Dictionary<uint, ChunkAllocatorSet>();
        private readonly Dictionary<uint, ChunkAllocatorSet> allocatorsByMemoryType = new Dictionary<uint, ChunkAllocatorSet>();

        public VkDeviceMemoryManager(
            VkDevice device,
            VkDeviceApi deviceApi,
            ulong bufferImageGranularity)
        {
            this.device = device;
            this.deviceApi = deviceApi;
            this.bufferImageGranularity = bufferImageGranularity;
        }

        #region Disposal

        public void Dispose()
        {
            foreach (var kvp in allocatorsByMemoryType) kvp.Value.Dispose();

            foreach (var kvp in allocatorsByMemoryTypeUnmapped) kvp.Value.Dispose();
        }

        #endregion

        public VkMemoryBlock Allocate(
            VkPhysicalDeviceMemoryProperties memProperties,
            uint memoryTypeBits,
            VkMemoryPropertyFlags flags,
            bool persistentMapped,
            ulong size,
            ulong alignment)
        {
            return Allocate(
                memProperties,
                memoryTypeBits,
                flags,
                persistentMapped,
                size,
                alignment,
                false,
                VkImage.Null,
                Vortice.Vulkan.VkBuffer.Null);
        }

        public VkMemoryBlock Allocate(
            VkPhysicalDeviceMemoryProperties memProperties,
            uint memoryTypeBits,
            VkMemoryPropertyFlags flags,
            bool persistentMapped,
            ulong size,
            ulong alignment,
            bool dedicated,
            VkImage dedicatedImage,
            Vortice.Vulkan.VkBuffer dedicatedBuffer)
        {
            if (dedicated)
            {
                if (dedicatedImage != VkImage.Null)
                {
                    var requirementsInfo = new VkImageMemoryRequirementsInfo2();
                    requirementsInfo.image = dedicatedImage;
                    var requirements = new VkMemoryRequirements2();
                    deviceApi.vkGetImageMemoryRequirements2(&requirementsInfo, &requirements);
                    size = requirements.memoryRequirements.size;
                }
                else if (dedicatedBuffer != Vortice.Vulkan.VkBuffer.Null)
                {
                    var requirementsInfo = new VkBufferMemoryRequirementsInfo2();
                    requirementsInfo.buffer = dedicatedBuffer;
                    var requirements = new VkMemoryRequirements2();
                    deviceApi.vkGetBufferMemoryRequirements2(&requirementsInfo, &requirements);
                    size = requirements.memoryRequirements.size;
                }
            }
            else
            {
                // Round up to the nearest multiple of bufferImageGranularity.
                // Use the overflow-safe form: ((size - 1) / g + 1) * g instead of
                // (size + g - 1) / g * g, which overflows when size > ulong.MaxValue - g + 1.
                size = ((size - 1) / bufferImageGranularity + 1) * bufferImageGranularity;
            }

            lock (@lock)
            {
                if (!TryFindMemoryType(memProperties, memoryTypeBits, flags, out uint memoryTypeIndex)) throw new VeldridException("No suitable memory type.");

                ulong minDedicatedAllocationSize = persistentMapped
                    ? min_dedicated_allocation_size_dynamic
                    : min_dedicated_allocation_size_non_dynamic;

                if (dedicated || size >= minDedicatedAllocationSize)
                {
                    var allocateInfo = new VkMemoryAllocateInfo();
                    allocateInfo.allocationSize = size;
                    allocateInfo.memoryTypeIndex = memoryTypeIndex;

                    // ReSharper disable once TooWideLocalVariableScope
                    VkMemoryDedicatedAllocateInfo dedicatedAi;

                    if (dedicated)
                    {
                        dedicatedAi = new VkMemoryDedicatedAllocateInfo();
                        dedicatedAi.buffer = dedicatedBuffer;
                        dedicatedAi.image = dedicatedImage;
                        allocateInfo.pNext = &dedicatedAi;
                    }

                    var allocationResult = deviceApi.vkAllocateMemory(&allocateInfo, null, out var memory);
                    if (allocationResult != VkResult.Success) throw new VeldridException("Unable to allocate sufficient Vulkan memory.");

                    void* mappedPtr = null;

                    if (persistentMapped)
                    {
                        var mapResult = deviceApi.vkMapMemory(memory, 0, size, 0, &mappedPtr);
                        if (mapResult != VkResult.Success) throw new VeldridException("Unable to map newly-allocated Vulkan memory.");
                    }

                    return new VkMemoryBlock(memory, 0, size, memoryTypeBits, mappedPtr, true);
                }

                var allocator = getAllocator(memoryTypeIndex, persistentMapped);
                bool result = allocator.Allocate(size, alignment, out var ret);
                if (!result) throw new VeldridException("Unable to allocate sufficient Vulkan memory.");

                return ret;
            }
        }

        public void Free(VkMemoryBlock block)
        {
            lock (@lock)
            {
                if (block.DedicatedAllocation)
                    deviceApi.vkFreeMemory(block.DeviceMemory, null);
                else
                    getAllocator(block.MemoryTypeIndex, block.IsPersistentMapped).Free(block);
            }
        }

        internal IntPtr Map(VkMemoryBlock memoryBlock)
        {
            void* ret;
            var result = deviceApi.vkMapMemory(memoryBlock.DeviceMemory, memoryBlock.Offset, memoryBlock.Size, 0, &ret);
            CheckResult(result);
            return (IntPtr)ret;
        }

        private ChunkAllocatorSet getAllocator(uint memoryTypeIndex, bool persistentMapped)
        {
            ChunkAllocatorSet ret;

            if (persistentMapped)
            {
                if (!allocatorsByMemoryType.TryGetValue(memoryTypeIndex, out ret))
                {
                    ret = new ChunkAllocatorSet(device, deviceApi, memoryTypeIndex, true);
                    allocatorsByMemoryType.Add(memoryTypeIndex, ret);
                }
            }
            else
            {
                if (!allocatorsByMemoryTypeUnmapped.TryGetValue(memoryTypeIndex, out ret))
                {
                    ret = new ChunkAllocatorSet(device, deviceApi, memoryTypeIndex, false);
                    allocatorsByMemoryTypeUnmapped.Add(memoryTypeIndex, ret);
                }
            }

            return ret;
        }

        private class ChunkAllocatorSet : IDisposable
        {
            private readonly VkDevice device;
            private readonly VkDeviceApi deviceApi;
            private readonly uint memoryTypeIndex;
            private readonly bool persistentMapped;
            private readonly List<ChunkAllocator> allocators = new List<ChunkAllocator>();

            public ChunkAllocatorSet(VkDevice device, VkDeviceApi deviceApi, uint memoryTypeIndex, bool persistentMapped)
            {
                this.device = device;
                this.deviceApi = deviceApi;
                this.memoryTypeIndex = memoryTypeIndex;
                this.persistentMapped = persistentMapped;
            }

            #region Disposal

            public void Dispose()
            {
                foreach (var allocator in allocators) allocator.Dispose();
            }

            #endregion

            public bool Allocate(ulong size, ulong alignment, out VkMemoryBlock block)
            {
                foreach (var allocator in allocators)
                {
                    if (allocator.Allocate(size, alignment, out block))
                        return true;
                }

                var newAllocator = new ChunkAllocator(device, deviceApi, memoryTypeIndex, persistentMapped);
                allocators.Add(newAllocator);
                return newAllocator.Allocate(size, alignment, out block);
            }

            public void Free(VkMemoryBlock block)
            {
                foreach (var chunk in allocators)
                {
                    if (chunk.Memory == block.DeviceMemory)
                        chunk.Free(block);
                }
            }
        }

        private class ChunkAllocator : IDisposable
        {
            public VkDeviceMemory Memory => memory;
            private const ulong persistent_mapped_chunk_size = 1024 * 1024 * 16;
            private const ulong unmapped_chunk_size = 1024 * 1024 * 64;
            private readonly VkDevice device;
            private readonly VkDeviceApi deviceApi;
            private readonly uint memoryTypeIndex;
            private readonly List<VkMemoryBlock> freeBlocks = new List<VkMemoryBlock>();
            private readonly VkDeviceMemory memory;
            private readonly void* mappedPtr;

            public ChunkAllocator(VkDevice device, VkDeviceApi deviceApi, uint memoryTypeIndex, bool persistentMapped)
            {
                this.device = device;
                this.deviceApi = deviceApi;
                this.memoryTypeIndex = memoryTypeIndex;
                ulong totalMemorySize = persistentMapped ? persistent_mapped_chunk_size : unmapped_chunk_size;

                var memoryAi = new VkMemoryAllocateInfo();
                memoryAi.allocationSize = totalMemorySize;
                memoryAi.memoryTypeIndex = this.memoryTypeIndex;
                var result = deviceApi.vkAllocateMemory(&memoryAi, null, out memory);
                CheckResult(result);

                if (persistentMapped)
                {
                    void* ptr = null;

                    result = deviceApi.vkMapMemory(memory, 0, totalMemorySize, 0, &ptr);
                    CheckResult(result);

                    mappedPtr = ptr;
                }

                var initialBlock = new VkMemoryBlock(
                    memory,
                    0,
                    totalMemorySize,
                    this.memoryTypeIndex,
                    mappedPtr,
                    false);
                freeBlocks.Add(initialBlock);
            }

            #region Disposal

            public void Dispose()
            {
                deviceApi.vkFreeMemory(memory, null);
            }

            #endregion

            public bool Allocate(ulong size, ulong alignment, out VkMemoryBlock block)
            {
                checked
                {
                    for (int i = 0; i < freeBlocks.Count; i++)
                    {
                        var freeBlock = freeBlocks[i];
                        ulong alignedBlockSize = freeBlock.Size;

                        if (freeBlock.Offset % alignment != 0)
                        {
                            ulong alignmentCorrection = alignment - freeBlock.Offset % alignment;
                            if (alignedBlockSize <= alignmentCorrection) continue;

                            alignedBlockSize -= alignmentCorrection;
                        }

                        if (alignedBlockSize >= size) // Valid match -- split it and return.
                        {
                            freeBlock.Size = alignedBlockSize;
                            if (freeBlock.Offset % alignment != 0) freeBlock.Offset += alignment - freeBlock.Offset % alignment;

                            block = freeBlock;

                            if (alignedBlockSize != size)
                            {
                                // Update the free block in place to the remainder — avoids RemoveAt+Insert O(n) shifts.
                                freeBlocks[i] = new VkMemoryBlock(
                                    freeBlock.DeviceMemory,
                                    freeBlock.Offset + size,
                                    freeBlock.Size - size,
                                    memoryTypeIndex,
                                    freeBlock.BaseMappedPointer,
                                    false);
                                block.Size = size;
                            }
                            else
                            {
                                freeBlocks.RemoveAt(i);
                            }

#if DEBUG
                            checkAllocatedBlock(block);
#endif
                            return true;
                        }
                    }

                    block = default;
                    return false;
                }
            }

            public void Free(VkMemoryBlock block)
            {
                // Find the insertion point to keep freeBlocks sorted by Offset.
                int i = 0;
                while (i < freeBlocks.Count && freeBlocks[i].Offset < block.Offset)
                    i++;

                bool mergedWithPrev = false;

                // Try to coalesce with the immediately preceding block.
                // When successful, no Insert is needed — we update the preceding entry in-place.
                if (i > 0 && freeBlocks[i - 1].End == block.Offset)
                {
                    var prev = freeBlocks[i - 1];
                    freeBlocks[i - 1] = new VkMemoryBlock(prev.DeviceMemory, prev.Offset, prev.Size + block.Size, memoryTypeIndex, prev.BaseMappedPointer, false);
                    mergedWithPrev = true;

                    // The enlarged prev block may now also be adjacent to freeBlocks[i].
                    if (i < freeBlocks.Count && freeBlocks[i - 1].End == freeBlocks[i].Offset)
                    {
                        var merged = freeBlocks[i - 1];
                        freeBlocks[i - 1] = new VkMemoryBlock(merged.DeviceMemory, merged.Offset, merged.Size + freeBlocks[i].Size, memoryTypeIndex, merged.BaseMappedPointer, false);
                        freeBlocks.RemoveAt(i);
                    }
                }

                if (!mergedWithPrev)
                {
                    // Insert in sorted position, then check whether the new block touches
                    // the following block and coalesce if so.
                    freeBlocks.Insert(i, block);

                    if (i + 1 < freeBlocks.Count && freeBlocks[i].End == freeBlocks[i + 1].Offset)
                    {
                        var cur = freeBlocks[i];
                        freeBlocks[i] = new VkMemoryBlock(cur.DeviceMemory, cur.Offset, cur.Size + freeBlocks[i + 1].Size, memoryTypeIndex, cur.BaseMappedPointer, false);
                        freeBlocks.RemoveAt(i + 1);
                    }
                }

#if DEBUG
                removeAllocatedBlock(block);
#endif
            }

#if DEBUG
            private readonly List<VkMemoryBlock> allocatedBlocks = new List<VkMemoryBlock>();

            private void checkAllocatedBlock(VkMemoryBlock block)
            {
                foreach (var oldBlock in allocatedBlocks) Debug.Assert(!blocksOverlap(block, oldBlock), "Allocated blocks have overlapped.");

                allocatedBlocks.Add(block);
            }

            private bool blocksOverlap(VkMemoryBlock first, VkMemoryBlock second)
            {
                ulong firstStart = first.Offset;
                ulong firstEnd = first.Offset + first.Size;
                ulong secondStart = second.Offset;
                ulong secondEnd = second.Offset + second.Size;

                return (firstStart <= secondStart && firstEnd > secondStart)
                       || (firstStart >= secondStart && firstEnd <= secondEnd)
                       || (firstStart < secondEnd && firstEnd >= secondEnd)
                       || (firstStart <= secondStart && firstEnd >= secondEnd);
            }

            private void removeAllocatedBlock(VkMemoryBlock block)
            {
                Debug.Assert(allocatedBlocks.Remove(block), "Unable to remove a supposedly allocated block.");
            }
#endif
        }
    }

    [DebuggerDisplay("[Mem:{DeviceMemory.Handle}] Off:{Offset}, Size:{Size} End:{Offset+Size}")]
    internal unsafe struct VkMemoryBlock : IEquatable<VkMemoryBlock>
    {
        public readonly uint MemoryTypeIndex;
        public readonly VkDeviceMemory DeviceMemory;
        public readonly void* BaseMappedPointer;
        public readonly bool DedicatedAllocation;

        public ulong Offset;
        public ulong Size;

        public void* BlockMappedPointer => (byte*)BaseMappedPointer + Offset;
        public bool IsPersistentMapped => BaseMappedPointer != null;
        public ulong End => Offset + Size;

        public VkMemoryBlock(
            VkDeviceMemory memory,
            ulong offset,
            ulong size,
            uint memoryTypeIndex,
            void* mappedPtr,
            bool dedicatedAllocation)
        {
            DeviceMemory = memory;
            Offset = offset;
            Size = size;
            MemoryTypeIndex = memoryTypeIndex;
            BaseMappedPointer = mappedPtr;
            DedicatedAllocation = dedicatedAllocation;
        }

        public bool Equals(VkMemoryBlock other)
        {
            return DeviceMemory.Equals(other.DeviceMemory)
                   && Offset.Equals(other.Offset)
                   && Size.Equals(other.Size);
        }
    }
}
