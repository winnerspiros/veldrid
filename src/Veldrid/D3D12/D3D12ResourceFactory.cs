using System;
using System.Runtime.Versioning;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12ResourceFactory : ResourceFactory, IDisposable
    {
        public override GraphicsBackend BackendType => GraphicsBackend.Direct3D12;

        private readonly D3D12GraphicsDevice gd;
        private readonly ID3D12Device device;

        public D3D12ResourceFactory(D3D12GraphicsDevice gd)
            : base(gd.Features)
        {
            this.gd = gd;
            device = gd.Device;
        }

        public override CommandList CreateCommandList(ref CommandListDescription description)
        {
            return new D3D12CommandList(gd, ref description);
        }

        public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
        {
            return new D3D12Framebuffer(device, gd.RtvAllocator, gd.DsvAllocator, ref description);
        }

        public override Pipeline CreateComputePipeline(ref ComputePipelineDescription description)
        {
            return new D3D12Pipeline(device, ref description);
        }

        public override ResourceLayout CreateResourceLayout(ref ResourceLayoutDescription description)
        {
            return new D3D12ResourceLayout(ref description);
        }

        public override ResourceSet CreateResourceSet(ref ResourceSetDescription description)
        {
            ValidationHelpers.ValidateResourceSet(gd, ref description);
            return new D3D12ResourceSet(ref description);
        }

        public override Fence CreateFence(bool signaled)
        {
            return new D3D12Fence(device, signaled);
        }

        public override Swapchain CreateSwapchain(ref SwapchainDescription description)
        {
            return new D3D12Swapchain(gd, ref description);
        }

        protected override Pipeline CreateGraphicsPipelineCore(ref GraphicsPipelineDescription description)
        {
            return new D3D12Pipeline(device, ref description);
        }

        protected override Sampler CreateSamplerCore(ref SamplerDescription description)
        {
            return new D3D12Sampler(device, gd.SamplerAllocator, ref description);
        }

        protected override Shader CreateShaderCore(ref ShaderDescription description)
        {
            return new D3D12Shader(description);
        }

        protected override Texture CreateTextureCore(ref TextureDescription description)
        {
            return new D3D12Texture(device, ref description);
        }

        protected override Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description)
        {
            var existingResource = new ID3D12Resource((IntPtr)nativeTexture);
            return new D3D12Texture(existingResource, description.Type, description.Format);
        }

        protected override TextureView CreateTextureViewCore(ref TextureViewDescription description)
        {
            return new D3D12TextureView(device, gd.CbvSrvUavAllocator, ref description);
        }

        protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
        {
            return new D3D12Buffer(
                device,
                description.SizeInBytes,
                description.Usage,
                description.StructureByteStride,
                description.RawBuffer);
        }

        public void Dispose()
        {
        }
    }
}
