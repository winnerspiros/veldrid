using System.Runtime.Versioning;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Veldrid.D3D12
{
    // Minimal forward-declaration stub. Will be fully implemented later.
    [SupportedOSPlatform("windows")]
    internal partial class D3D12GraphicsDevice
    {
        public ID3D12Device Device { get; }
        public ID3D12CommandQueue CommandQueue { get; }
        public IDXGIFactory4 DxgiFactory { get; }
        public D3D12DescriptorAllocator RtvAllocator { get; }
        public D3D12DescriptorAllocator DsvAllocator { get; }
    }
}
