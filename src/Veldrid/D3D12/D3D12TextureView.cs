using System.Runtime.Versioning;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12TextureView : TextureView
    {
        public CpuDescriptorHandle SrvCpuHandle { get; }
        public CpuDescriptorHandle? UavCpuHandle { get; }

        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set => name = value;
        }

        private readonly D3D12DescriptorAllocator cbvSrvUavAllocator;
        private readonly D3D12DescriptorRange srvRange;
        private readonly D3D12DescriptorRange? uavRange;
        private string name;
        private bool disposed;

        public D3D12TextureView(ID3D12Device device, D3D12DescriptorAllocator cbvSrvUavAllocator, ref TextureViewDescription description)
            : base(ref description)
        {
            this.cbvSrvUavAllocator = cbvSrvUavAllocator;

            var d3dTex = Util.AssertSubtype<Texture, D3D12Texture>(description.Target);

            // Allocate and create SRV.
            srvRange = cbvSrvUavAllocator.Allocate(1);
            SrvCpuHandle = srvRange.CpuHandle;

            var srvDesc = new ShaderResourceViewDescription
            {
                Format = D3D12Formats.GetViewFormat(d3dTex.DxgiFormat),
                Shader4ComponentMapping = ShaderComponentMapping.Default
            };

            bool isCubemap = (d3dTex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap;
            bool isArray = d3dTex.ArrayLayers > 1 || isCubemap;

            switch (d3dTex.Type)
            {
                case TextureType.Texture1D:
                    if (isArray)
                    {
                        srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture1DArray;
                        srvDesc.Texture1DArray.MostDetailedMip = description.BaseMipLevel;
                        srvDesc.Texture1DArray.MipLevels = description.MipLevels;
                        srvDesc.Texture1DArray.FirstArraySlice = description.BaseArrayLayer;
                        srvDesc.Texture1DArray.ArraySize = description.ArrayLayers;
                    }
                    else
                    {
                        srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture1D;
                        srvDesc.Texture1D.MostDetailedMip = description.BaseMipLevel;
                        srvDesc.Texture1D.MipLevels = description.MipLevels;
                    }

                    break;

                case TextureType.Texture2D:
                    if (isCubemap)
                    {
                        if (d3dTex.ArrayLayers > 1)
                        {
                            srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.TextureCubeArray;
                            srvDesc.TextureCubeArray.MostDetailedMip = description.BaseMipLevel;
                            srvDesc.TextureCubeArray.MipLevels = description.MipLevels;
                            srvDesc.TextureCubeArray.First2DArrayFace = description.BaseArrayLayer;
                            srvDesc.TextureCubeArray.NumCubes = (description.ArrayLayers / 6);
                        }
                        else
                        {
                            srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.TextureCube;
                            srvDesc.TextureCube.MostDetailedMip = description.BaseMipLevel;
                            srvDesc.TextureCube.MipLevels = description.MipLevels;
                        }
                    }
                    else if (isArray)
                    {
                        if (d3dTex.SampleCount != TextureSampleCount.Count1)
                        {
                            srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DMultisampledArray;
                            srvDesc.Texture2DMSArray.FirstArraySlice = description.BaseArrayLayer;
                            srvDesc.Texture2DMSArray.ArraySize = description.ArrayLayers;
                        }
                        else
                        {
                            srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DArray;
                            srvDesc.Texture2DArray.MostDetailedMip = description.BaseMipLevel;
                            srvDesc.Texture2DArray.MipLevels = description.MipLevels;
                            srvDesc.Texture2DArray.FirstArraySlice = description.BaseArrayLayer;
                            srvDesc.Texture2DArray.ArraySize = description.ArrayLayers;
                        }
                    }
                    else
                    {
                        if (d3dTex.SampleCount != TextureSampleCount.Count1)
                        {
                            srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2DMultisampled;
                        }
                        else
                        {
                            srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D;
                            srvDesc.Texture2D.MostDetailedMip = description.BaseMipLevel;
                            srvDesc.Texture2D.MipLevels = description.MipLevels;
                        }
                    }

                    break;

                case TextureType.Texture3D:
                    srvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture3D;
                    srvDesc.Texture3D.MostDetailedMip = description.BaseMipLevel;
                    srvDesc.Texture3D.MipLevels = description.MipLevels;
                    break;
            }

            device.CreateShaderResourceView(d3dTex.Resource, srvDesc, SrvCpuHandle);

            // Create UAV if the texture supports storage.
            if ((d3dTex.Usage & TextureUsage.Storage) == TextureUsage.Storage)
            {
                uavRange = cbvSrvUavAllocator.Allocate(1);
                UavCpuHandle = uavRange.Value.CpuHandle;

                var uavDesc = new UnorderedAccessViewDescription
                {
                    Format = D3D12Formats.GetViewFormat(d3dTex.DxgiFormat)
                };

                if (d3dTex.Depth == 1)
                {
                    if (d3dTex.ArrayLayers == 1)
                    {
                        if (d3dTex.Type == TextureType.Texture1D)
                        {
                            uavDesc.ViewDimension = UnorderedAccessViewDimension.Texture1D;
                            uavDesc.Texture1D.MipSlice = description.BaseMipLevel;
                        }
                        else
                        {
                            uavDesc.ViewDimension = UnorderedAccessViewDimension.Texture2D;
                            uavDesc.Texture2D.MipSlice = description.BaseMipLevel;
                        }
                    }
                    else
                    {
                        if (d3dTex.Type == TextureType.Texture1D)
                        {
                            uavDesc.ViewDimension = UnorderedAccessViewDimension.Texture1DArray;
                            uavDesc.Texture1DArray.MipSlice = description.BaseMipLevel;
                            uavDesc.Texture1DArray.FirstArraySlice = description.BaseArrayLayer;
                            uavDesc.Texture1DArray.ArraySize = description.ArrayLayers;
                        }
                        else
                        {
                            uavDesc.ViewDimension = UnorderedAccessViewDimension.Texture2DArray;
                            uavDesc.Texture2DArray.MipSlice = description.BaseMipLevel;
                            uavDesc.Texture2DArray.FirstArraySlice = description.BaseArrayLayer;
                            uavDesc.Texture2DArray.ArraySize = description.ArrayLayers;
                        }
                    }
                }
                else
                {
                    uavDesc.ViewDimension = UnorderedAccessViewDimension.Texture3D;
                    uavDesc.Texture3D.MipSlice = description.BaseMipLevel;
                    uavDesc.Texture3D.FirstWSlice = 0;
                    uavDesc.Texture3D.WSize = d3dTex.Depth;
                }

                device.CreateUnorderedAccessView(d3dTex.Resource, null, uavDesc, UavCpuHandle.Value);
            }
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                cbvSrvUavAllocator.Free(srvRange);

                if (uavRange.HasValue)
                    cbvSrvUavAllocator.Free(uavRange.Value);

                disposed = true;
            }
        }

        #endregion
    }
}
