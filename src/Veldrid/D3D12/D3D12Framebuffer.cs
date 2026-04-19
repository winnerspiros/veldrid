using System;
using System.Runtime.Versioning;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Framebuffer : Framebuffer
    {
        public CpuDescriptorHandle[] RenderTargetViews { get; }
        public CpuDescriptorHandle? DepthStencilView { get; }

        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set => name = value;
        }

        private readonly D3D12DescriptorAllocator rtvAllocator;
        private readonly D3D12DescriptorAllocator dsvAllocator;
        private readonly D3D12DescriptorRange[] rtvRanges;
        private readonly D3D12DescriptorRange? dsvRange;
        private string name;
        private bool disposed;

        public D3D12Framebuffer(
            ID3D12Device device,
            D3D12DescriptorAllocator rtvAllocator,
            D3D12DescriptorAllocator dsvAllocator,
            ref FramebufferDescription description)
            : base(description.DepthTarget, description.ColorTargets)
        {
            this.rtvAllocator = rtvAllocator;
            this.dsvAllocator = dsvAllocator;

            if (description.DepthTarget != null)
            {
                var d3dDepthTarget = Util.AssertSubtype<Texture, D3D12Texture>(description.DepthTarget.Value.Target);
                var range = dsvAllocator.Allocate(1);
                dsvRange = range;

                var dsvDesc = new DepthStencilViewDescription
                {
                    Format = getDepthFormat(d3dDepthTarget.Format)
                };

                if (d3dDepthTarget.ArrayLayers == 1)
                {
                    if (d3dDepthTarget.SampleCount == TextureSampleCount.Count1)
                    {
                        dsvDesc.ViewDimension = DepthStencilViewDimension.Texture2D;
                        dsvDesc.Texture2D.MipSlice = description.DepthTarget.Value.MipLevel;
                    }
                    else
                    {
                        dsvDesc.ViewDimension = DepthStencilViewDimension.Texture2DMultisampled;
                    }
                }
                else
                {
                    if (d3dDepthTarget.SampleCount == TextureSampleCount.Count1)
                    {
                        dsvDesc.ViewDimension = DepthStencilViewDimension.Texture2DArray;
                        dsvDesc.Texture2DArray.FirstArraySlice = description.DepthTarget.Value.ArrayLayer;
                        dsvDesc.Texture2DArray.ArraySize = 1;
                        dsvDesc.Texture2DArray.MipSlice = description.DepthTarget.Value.MipLevel;
                    }
                    else
                    {
                        dsvDesc.ViewDimension = DepthStencilViewDimension.Texture2DMultisampledArray;
                        dsvDesc.Texture2DMSArray.FirstArraySlice = description.DepthTarget.Value.ArrayLayer;
                        dsvDesc.Texture2DMSArray.ArraySize = 1;
                    }
                }

                device.CreateDepthStencilView(d3dDepthTarget.Resource, dsvDesc, range.CpuHandle);
                DepthStencilView = range.CpuHandle;
            }

            if (description.ColorTargets != null && description.ColorTargets.Length > 0)
            {
                RenderTargetViews = new CpuDescriptorHandle[description.ColorTargets.Length];
                rtvRanges = new D3D12DescriptorRange[description.ColorTargets.Length];

                for (int i = 0; i < RenderTargetViews.Length; i++)
                {
                    var d3dColorTarget = Util.AssertSubtype<Texture, D3D12Texture>(description.ColorTargets[i].Target);
                    var range = rtvAllocator.Allocate(1);
                    rtvRanges[i] = range;

                    var rtvDesc = new RenderTargetViewDescription
                    {
                        Format = D3D12Formats.ToDxgiFormat(d3dColorTarget.Format, false)
                    };

                    if (d3dColorTarget.ArrayLayers > 1 || (d3dColorTarget.Usage & TextureUsage.Cubemap) != 0)
                    {
                        if (d3dColorTarget.SampleCount == TextureSampleCount.Count1)
                        {
                            rtvDesc.ViewDimension = RenderTargetViewDimension.Texture2DArray;
                            rtvDesc.Texture2DArray.ArraySize = 1;
                            rtvDesc.Texture2DArray.FirstArraySlice = description.ColorTargets[i].ArrayLayer;
                            rtvDesc.Texture2DArray.MipSlice = description.ColorTargets[i].MipLevel;
                        }
                        else
                        {
                            rtvDesc.ViewDimension = RenderTargetViewDimension.Texture2DMultisampledArray;
                            rtvDesc.Texture2DMSArray.ArraySize = 1;
                            rtvDesc.Texture2DMSArray.FirstArraySlice = description.ColorTargets[i].ArrayLayer;
                        }
                    }
                    else
                    {
                        if (d3dColorTarget.SampleCount == TextureSampleCount.Count1)
                        {
                            rtvDesc.ViewDimension = RenderTargetViewDimension.Texture2D;
                            rtvDesc.Texture2D.MipSlice = description.ColorTargets[i].MipLevel;
                        }
                        else
                        {
                            rtvDesc.ViewDimension = RenderTargetViewDimension.Texture2DMultisampled;
                        }
                    }

                    device.CreateRenderTargetView(d3dColorTarget.Resource, rtvDesc, range.CpuHandle);
                    RenderTargetViews[i] = range.CpuHandle;
                }
            }
            else
            {
                RenderTargetViews = Array.Empty<CpuDescriptorHandle>();
                rtvRanges = Array.Empty<D3D12DescriptorRange>();
            }
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                if (dsvRange.HasValue)
                    dsvAllocator.Free(dsvRange.Value);

                foreach (var range in rtvRanges)
                    rtvAllocator.Free(range);

                disposed = true;
            }
        }

        #endregion

        private static Vortice.DXGI.Format getDepthFormat(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.R32Float:
                    return Vortice.DXGI.Format.D32_Float;

                case PixelFormat.R16UNorm:
                    return Vortice.DXGI.Format.D16_UNorm;

                case PixelFormat.D24UNormS8UInt:
                    return Vortice.DXGI.Format.D24_UNorm_S8_UInt;

                case PixelFormat.D32FloatS8UInt:
                    return Vortice.DXGI.Format.D32_Float_S8X24_UInt;

                default:
                    throw new VeldridException("Invalid depth texture format: " + format);
            }
        }
    }
}
