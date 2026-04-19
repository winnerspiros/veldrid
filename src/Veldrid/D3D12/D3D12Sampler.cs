using System.Runtime.Versioning;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Sampler : Sampler
    {
        public CpuDescriptorHandle CpuHandle { get; }

        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set => name = value;
        }

        private readonly D3D12DescriptorAllocator samplerAllocator;
        private readonly D3D12DescriptorRange descriptorRange;
        private string name;
        private bool disposed;

        public D3D12Sampler(ID3D12Device device, D3D12DescriptorAllocator samplerAllocator, ref Veldrid.SamplerDescription description)
        {
            this.samplerAllocator = samplerAllocator;
            descriptorRange = samplerAllocator.Allocate(1);
            CpuHandle = descriptorRange.CpuHandle;

            var comparison = description.ComparisonKind == null
                ? ComparisonFunction.Never
                : D3D12Formats.VdToD3D12ComparisonFunc(description.ComparisonKind.Value);

            var borderColor = toBorderColor(description.BorderColor);

            var samplerDesc = new Vortice.Direct3D12.SamplerDescription(
                D3D12Formats.VdToD3D12Filter(description.Filter, description.ComparisonKind.HasValue),
                D3D12Formats.VdToD3D12AddressMode(description.AddressModeU),
                D3D12Formats.VdToD3D12AddressMode(description.AddressModeV),
                D3D12Formats.VdToD3D12AddressMode(description.AddressModeW),
                description.LodBias,
                description.MaximumAnisotropy,
                comparison,
                in borderColor,
                description.MinimumLod,
                description.MaximumLod);

            device.CreateSampler(ref samplerDesc, CpuHandle);
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                samplerAllocator.Free(descriptorRange);
                disposed = true;
            }
        }

        #endregion

        private static Color4 toBorderColor(SamplerBorderColor borderColor)
        {
            switch (borderColor)
            {
                case SamplerBorderColor.TransparentBlack:
                    return new Color4(0, 0, 0, 0);

                case SamplerBorderColor.OpaqueBlack:
                    return new Color4(0, 0, 0, 1);

                case SamplerBorderColor.OpaqueWhite:
                    return new Color4(1, 1, 1, 1);

                default:
                    throw Illegal.Value<SamplerBorderColor>();
            }
        }
    }
}
