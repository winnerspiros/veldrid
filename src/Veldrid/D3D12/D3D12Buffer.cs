using System;
using System.Runtime.Versioning;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Buffer : DeviceBuffer
    {
        public override uint SizeInBytes { get; }
        public override BufferUsage Usage { get; }
        public override bool IsDisposed => disposed;

        public ID3D12Resource Resource { get; }
        public HeapType HeapType { get; }

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                Resource.Name = value;
            }
        }

        private readonly uint structureByteStride;
        private readonly bool rawBuffer;
        private string name;
        private bool disposed;

        public D3D12Buffer(ID3D12Device device, uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool rawBuffer)
        {
            SizeInBytes = sizeInBytes;
            Usage = usage;
            this.structureByteStride = structureByteStride;
            this.rawBuffer = rawBuffer;

            HeapType heapType;

            if ((usage & BufferUsage.Staging) == BufferUsage.Staging)
                heapType = Vortice.Direct3D12.HeapType.Readback;
            else if ((usage & BufferUsage.Dynamic) == BufferUsage.Dynamic)
                heapType = Vortice.Direct3D12.HeapType.Upload;
            else
                heapType = Vortice.Direct3D12.HeapType.Default;

            HeapType = heapType;

            var resourceFlags = ResourceFlags.None;

            if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite)
                resourceFlags |= ResourceFlags.AllowUnorderedAccess;

            var resourceDesc = ResourceDescription.Buffer(sizeInBytes, resourceFlags);

            var heapProperties = new HeapProperties(heapType);

            // Upload and readback heaps require GenericRead and CopyDest initial states respectively.
            var initialState = heapType switch
            {
                Vortice.Direct3D12.HeapType.Upload => ResourceStates.GenericRead,
                Vortice.Direct3D12.HeapType.Readback => ResourceStates.CopyDest,
                _ => ResourceStates.Common
            };

            Resource = device.CreateCommittedResource(heapProperties, HeapFlags.None, resourceDesc, initialState);
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                Resource.Dispose();
                disposed = true;
            }
        }

        #endregion
    }
}
