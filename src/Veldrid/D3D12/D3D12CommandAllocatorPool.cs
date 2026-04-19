using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12CommandAllocatorPool : IDisposable
    {
        private readonly ID3D12Device device;
        private readonly CommandListType commandListType;
        private readonly Lock @lock = new Lock();
        private readonly Queue<(ulong FenceValue, ID3D12CommandAllocator Allocator)> availableAllocators = new();

        public D3D12CommandAllocatorPool(ID3D12Device device, CommandListType commandListType)
        {
            this.device = device;
            this.commandListType = commandListType;
        }

        public ID3D12CommandAllocator GetAllocator(ulong completedFenceValue)
        {
            lock (@lock)
            {
                if (availableAllocators.Count > 0 && availableAllocators.Peek().FenceValue <= completedFenceValue)
                {
                    var allocator = availableAllocators.Dequeue().Allocator;
                    allocator.Reset();
                    return allocator;
                }

                return device.CreateCommandAllocator(commandListType);
            }
        }

        public void ReturnAllocator(ulong fenceValue, ID3D12CommandAllocator allocator)
        {
            lock (@lock)
            {
                availableAllocators.Enqueue((fenceValue, allocator));
            }
        }

        public void Dispose()
        {
            while (availableAllocators.Count > 0)
                availableAllocators.Dequeue().Allocator.Dispose();
        }
    }
}
