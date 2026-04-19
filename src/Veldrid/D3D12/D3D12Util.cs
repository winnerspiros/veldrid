using System;
using System.Runtime.Versioning;
using Vortice.Direct3D12;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal static class D3D12Util
    {
        internal static ShaderResourceViewDescription GetSrvDescription(D3D12Texture texture, ref TextureViewDescription desc)
        {
            var srvDesc = new ShaderResourceViewDescription
            {
                Format = D3D12Formats.GetViewFormat(texture.DxgiFormat),
                Shader4ComponentMapping = ShaderComponentMapping.Default
            };

            bool isCubemap = (texture.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap;

            switch (texture.Type)
            {
                case TextureType.Texture1D:
                    if (texture.ArrayLayers > 1)
                    {
                        srvDesc.ViewDimension = ShaderResourceViewDimension.Texture1DArray;
                        srvDesc.Texture1DArray.MostDetailedMip = desc.BaseMipLevel;
                        srvDesc.Texture1DArray.MipLevels = desc.MipLevels;
                        srvDesc.Texture1DArray.FirstArraySlice = desc.BaseArrayLayer;
                        srvDesc.Texture1DArray.ArraySize = desc.ArrayLayers;
                    }
                    else
                    {
                        srvDesc.ViewDimension = ShaderResourceViewDimension.Texture1D;
                        srvDesc.Texture1D.MostDetailedMip = desc.BaseMipLevel;
                        srvDesc.Texture1D.MipLevels = desc.MipLevels;
                    }

                    break;

                case TextureType.Texture2D:
                    if (isCubemap)
                    {
                        if (texture.ArrayLayers > 1)
                        {
                            srvDesc.ViewDimension = ShaderResourceViewDimension.TextureCubeArray;
                            srvDesc.TextureCubeArray.MostDetailedMip = desc.BaseMipLevel;
                            srvDesc.TextureCubeArray.MipLevels = desc.MipLevels;
                            srvDesc.TextureCubeArray.First2DArrayFace = desc.BaseArrayLayer;
                            srvDesc.TextureCubeArray.NumCubes = (desc.ArrayLayers / 6);
                        }
                        else
                        {
                            srvDesc.ViewDimension = ShaderResourceViewDimension.TextureCube;
                            srvDesc.TextureCube.MostDetailedMip = desc.BaseMipLevel;
                            srvDesc.TextureCube.MipLevels = desc.MipLevels;
                        }
                    }
                    else if (texture.ArrayLayers > 1)
                    {
                        if (texture.SampleCount != TextureSampleCount.Count1)
                        {
                            srvDesc.ViewDimension = ShaderResourceViewDimension.Texture2DMultisampledArray;
                            srvDesc.Texture2DMSArray.FirstArraySlice = desc.BaseArrayLayer;
                            srvDesc.Texture2DMSArray.ArraySize = desc.ArrayLayers;
                        }
                        else
                        {
                            srvDesc.ViewDimension = ShaderResourceViewDimension.Texture2DArray;
                            srvDesc.Texture2DArray.MostDetailedMip = desc.BaseMipLevel;
                            srvDesc.Texture2DArray.MipLevels = desc.MipLevels;
                            srvDesc.Texture2DArray.FirstArraySlice = desc.BaseArrayLayer;
                            srvDesc.Texture2DArray.ArraySize = desc.ArrayLayers;
                        }
                    }
                    else
                    {
                        if (texture.SampleCount != TextureSampleCount.Count1)
                        {
                            srvDesc.ViewDimension = ShaderResourceViewDimension.Texture2DMultisampled;
                        }
                        else
                        {
                            srvDesc.ViewDimension = ShaderResourceViewDimension.Texture2D;
                            srvDesc.Texture2D.MostDetailedMip = desc.BaseMipLevel;
                            srvDesc.Texture2D.MipLevels = desc.MipLevels;
                        }
                    }

                    break;

                case TextureType.Texture3D:
                    srvDesc.ViewDimension = ShaderResourceViewDimension.Texture3D;
                    srvDesc.Texture3D.MostDetailedMip = desc.BaseMipLevel;
                    srvDesc.Texture3D.MipLevels = desc.MipLevels;
                    break;
            }

            return srvDesc;
        }

        internal static void AssertSuccess(int hr, string operation)
        {
            if (hr < 0)
                throw new VeldridException($"{operation} failed with HRESULT: 0x{hr:X8}");
        }
    }
}
