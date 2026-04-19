using Vortice.Direct3D;
using Vortice.Direct3D11;
using System.Runtime.Versioning;

namespace Veldrid.D3D11
{
    [SupportedOSPlatform("windows")]
    internal static class D3D11Util
    {
        public static uint ComputeSubresource(uint mipLevel, uint mipLevelCount, uint arrayLayer)
        {
            return arrayLayer * mipLevelCount + mipLevel;
        }

        internal static ShaderResourceViewDescription GetSrvDesc(
            D3D11Texture tex,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            PixelFormat format)
        {
            var srvDesc = new ShaderResourceViewDescription
            {
                Format = D3D11Formats.GetViewFormat(
                    D3D11Formats.ToDxgiFormat(format, (tex.Usage & TextureUsage.DepthStencil) != 0))
            };

            if ((tex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
            {
                if (tex.ArrayLayers == 1)
                {
                    srvDesc.ViewDimension = ShaderResourceViewDimension.TextureCube;
                    srvDesc.TextureCube.MostDetailedMip = baseMipLevel;
                    srvDesc.TextureCube.MipLevels = levelCount;
                }
                else
                {
                    srvDesc.ViewDimension = ShaderResourceViewDimension.TextureCubeArray;
                    srvDesc.TextureCubeArray.MostDetailedMip = baseMipLevel;
                    srvDesc.TextureCubeArray.MipLevels = levelCount;
                    srvDesc.TextureCubeArray.First2DArrayFace = baseArrayLayer;
                    srvDesc.TextureCubeArray.NumCubes = tex.ArrayLayers;
                }
            }
            else if (tex.Depth == 1)
            {
                if (tex.ArrayLayers == 1)
                {
                    if (tex.Type == TextureType.Texture1D)
                    {
                        srvDesc.ViewDimension = ShaderResourceViewDimension.Texture1D;
                        srvDesc.Texture1D.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture1D.MipLevels = levelCount;
                    }
                    else
                    {
                        srvDesc.ViewDimension = tex.SampleCount == TextureSampleCount.Count1 ? ShaderResourceViewDimension.Texture2D : ShaderResourceViewDimension.Texture2DMultisampled;
                        srvDesc.Texture2D.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture2D.MipLevels = levelCount;
                    }
                }
                else
                {
                    if (tex.Type == TextureType.Texture1D)
                    {
                        srvDesc.ViewDimension = ShaderResourceViewDimension.Texture1DArray;
                        srvDesc.Texture1DArray.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture1DArray.MipLevels = levelCount;
                        srvDesc.Texture1DArray.FirstArraySlice = baseArrayLayer;
                        srvDesc.Texture1DArray.ArraySize = layerCount;
                    }
                    else
                    {
                        srvDesc.ViewDimension = ShaderResourceViewDimension.Texture2DArray;
                        srvDesc.Texture2DArray.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture2DArray.MipLevels = levelCount;
                        srvDesc.Texture2DArray.FirstArraySlice = baseArrayLayer;
                        srvDesc.Texture2DArray.ArraySize = layerCount;
                    }
                }
            }
            else
            {
                srvDesc.ViewDimension = ShaderResourceViewDimension.Texture3D;
                srvDesc.Texture3D.MostDetailedMip = baseMipLevel;
                srvDesc.Texture3D.MipLevels = levelCount;
            }

            return srvDesc;
        }

        internal static uint GetSyncInterval(bool syncToVBlank)
        {
            return syncToVBlank ? 1u : 0u;
        }
    }
}
