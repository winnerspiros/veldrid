using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal struct D3D12DescriptorRange
    {
        public CpuDescriptorHandle CpuHandle;
        public GpuDescriptorHandle GpuHandle;
        public uint Count;
        public uint HeapIndex;
    }

    [SupportedOSPlatform("windows")]
    internal class D3D12DescriptorAllocator : IDisposable
    {
        private readonly ID3D12Device device;
        private readonly DescriptorHeapType heapType;
        private readonly uint descriptorCount;
        private readonly bool shaderVisible;
        private readonly uint incrementSize;
        private readonly Lock @lock = new Lock();
        private readonly List<HeapBlock> heapBlocks = new List<HeapBlock>();

        public D3D12DescriptorAllocator(
            ID3D12Device device,
            DescriptorHeapType heapType,
            uint descriptorCount,
            bool shaderVisible)
        {
            this.device = device;
            this.heapType = heapType;
            this.descriptorCount = descriptorCount;
            this.shaderVisible = shaderVisible;
            incrementSize = (uint)device.GetDescriptorHandleIncrementSize(heapType);

            heapBlocks.Add(createHeapBlock());
        }

        public D3D12DescriptorRange Allocate(uint count)
        {
            Debug.Assert(count > 0);
            Debug.Assert(count <= descriptorCount);

            lock (@lock)
            {
                for (int i = 0; i < heapBlocks.Count; i++)
                {
                    if (heapBlocks[i].TryAllocate(count, out uint startIndex))
                        return createRange(heapBlocks[i], (uint)i, startIndex, count);
                }

                var newBlock = createHeapBlock();
                heapBlocks.Add(newBlock);
                uint heapIndex = (uint)(heapBlocks.Count - 1);

                bool success = newBlock.TryAllocate(count, out uint newStartIndex);
                Debug.Assert(success);

                return createRange(newBlock, heapIndex, newStartIndex, count);
            }
        }

        public void Free(D3D12DescriptorRange range)
        {
            lock (@lock)
            {
                Debug.Assert(range.HeapIndex < heapBlocks.Count);
                uint startIndex = getIndexFromCpuHandle(heapBlocks[(int)range.HeapIndex], range.CpuHandle);
                heapBlocks[(int)range.HeapIndex].Free(startIndex, range.Count);
            }
        }

        public void Dispose()
        {
            foreach (var block in heapBlocks)
                block.Heap.Dispose();

            heapBlocks.Clear();
        }

        private HeapBlock createHeapBlock()
        {
            var desc = new DescriptorHeapDescription
            {
                Type = heapType,
                DescriptorCount = descriptorCount,
                Flags = shaderVisible ? DescriptorHeapFlags.ShaderVisible : DescriptorHeapFlags.None,
                NodeMask = 0
            };

            var heap = device.CreateDescriptorHeap(desc);

            return new HeapBlock(heap, descriptorCount);
        }

        private D3D12DescriptorRange createRange(HeapBlock block, uint heapIndex, uint startIndex, uint count)
        {
            var cpuStart = block.Heap.GetCPUDescriptorHandleForHeapStart();
            cpuStart.Ptr += (nuint)(startIndex * incrementSize);

            var gpuHandle = default(GpuDescriptorHandle);

            if (shaderVisible)
            {
                gpuHandle = block.Heap.GetGPUDescriptorHandleForHeapStart();
                gpuHandle.Ptr += (ulong)(startIndex * incrementSize);
            }

            return new D3D12DescriptorRange
            {
                CpuHandle = cpuStart,
                GpuHandle = gpuHandle,
                Count = count,
                HeapIndex = heapIndex
            };
        }

        private uint getIndexFromCpuHandle(HeapBlock block, CpuDescriptorHandle handle)
        {
            nuint basePtr = block.Heap.GetCPUDescriptorHandleForHeapStart().Ptr;
            return (uint)((handle.Ptr - basePtr) / incrementSize);
        }

        /// <summary>
        /// Manages a single descriptor heap and its free ranges.
        /// Free ranges are kept sorted by start index and merged on free.
        /// </summary>
        private class HeapBlock
        {
            public readonly ID3D12DescriptorHeap Heap;
            private readonly List<FreeRange> freeRanges;

            public HeapBlock(ID3D12DescriptorHeap heap, uint totalDescriptors)
            {
                Heap = heap;
                freeRanges = new List<FreeRange> { new FreeRange(0, totalDescriptors) };
            }

            public bool TryAllocate(uint count, out uint startIndex)
            {
                for (int i = 0; i < freeRanges.Count; i++)
                {
                    var range = freeRanges[i];

                    if (range.Count >= count)
                    {
                        startIndex = range.Start;

                        if (range.Count == count)
                        {
                            freeRanges.RemoveAt(i);
                        }
                        else
                        {
                            freeRanges[i] = new FreeRange(range.Start + count, range.Count - count);
                        }

                        return true;
                    }
                }

                startIndex = 0;
                return false;
            }

            public void Free(uint start, uint count)
            {
                // Find the insertion point to keep the list sorted by Start.
                int insertIndex = 0;

                for (int i = 0; i < freeRanges.Count; i++)
                {
                    if (freeRanges[i].Start > start)
                        break;

                    insertIndex = i + 1;
                }

                freeRanges.Insert(insertIndex, new FreeRange(start, count));

                // Merge with the next range if adjacent.
                if (insertIndex + 1 < freeRanges.Count)
                {
                    var current = freeRanges[insertIndex];
                    var next = freeRanges[insertIndex + 1];

                    if (current.Start + current.Count == next.Start)
                    {
                        freeRanges[insertIndex] = new FreeRange(current.Start, current.Count + next.Count);
                        freeRanges.RemoveAt(insertIndex + 1);
                    }
                }

                // Merge with the previous range if adjacent.
                if (insertIndex > 0)
                {
                    var prev = freeRanges[insertIndex - 1];
                    var current = freeRanges[insertIndex];

                    if (prev.Start + prev.Count == current.Start)
                    {
                        freeRanges[insertIndex - 1] = new FreeRange(prev.Start, prev.Count + current.Count);
                        freeRanges.RemoveAt(insertIndex);
                    }
                }
            }
        }

        private readonly struct FreeRange
        {
            public readonly uint Start;
            public readonly uint Count;

            public FreeRange(uint start, uint count)
            {
                Start = start;
                Count = count;
            }
        }
    }
}
