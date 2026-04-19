using System;
using System.Runtime.Versioning;
using System.Threading;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Fence : Fence
    {
        public ID3D12Fence DeviceFence { get; }
        public ulong CurrentValue => currentValue;

        public override bool Signaled => DeviceFence.CompletedValue >= (ulong)currentValue;
        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                DeviceFence.Name = value;
            }
        }

        private readonly ManualResetEventSlim fenceEvent;
        private ulong currentValue;
        private string name;
        private bool disposed;

        public D3D12Fence(ID3D12Device device, bool signaled)
        {
            currentValue = signaled ? 0UL : 1UL;
            DeviceFence = device.CreateFence(signaled ? 1UL : 0UL);
            fenceEvent = new ManualResetEventSlim(signaled);
        }

        public override void Reset()
        {
            Interlocked.Increment(ref currentValue);
            fenceEvent.Reset();
        }

        public ulong IncrementAndGetValue()
        {
            fenceEvent.Reset();
            return Interlocked.Increment(ref currentValue);
        }

        internal bool Wait(ulong nanosecondTimeout)
        {
            if (Signaled)
                return true;

            ulong timeout = Math.Min(int.MaxValue, nanosecondTimeout / 1_000_000);
            DeviceFence.SetEventOnCompletion(currentValue, fenceEvent.WaitHandle);
            return fenceEvent.Wait((int)timeout);
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                DeviceFence.Dispose();
                fenceEvent.Dispose();
                disposed = true;
            }
        }

        #endregion
    }
}
