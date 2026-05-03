using System.Diagnostics;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal static class D3D12Formats
    {
        internal static Format ToDxgiFormat(PixelFormat format, bool depthFormat)
        {
            switch (format)
            {
                case PixelFormat.R8UNorm:
                    return Format.R8_UNorm;

                case PixelFormat.R8SNorm:
                    return Format.R8_SNorm;

                case PixelFormat.R8UInt:
                    return Format.R8_UInt;

                case PixelFormat.R8SInt:
                    return Format.R8_SInt;

                case PixelFormat.R16UNorm:
                    return depthFormat ? Format.R16_Typeless : Format.R16_UNorm;

                case PixelFormat.R16SNorm:
                    return Format.R16_SNorm;

                case PixelFormat.R16UInt:
                    return Format.R16_UInt;

                case PixelFormat.R16SInt:
                    return Format.R16_SInt;

                case PixelFormat.R16Float:
                    return Format.R16_Float;

                case PixelFormat.R32UInt:
                    return Format.R32_UInt;

                case PixelFormat.R32SInt:
                    return Format.R32_SInt;

                case PixelFormat.R32Float:
                    return depthFormat ? Format.R32_Typeless : Format.R32_Float;

                case PixelFormat.R8G8UNorm:
                    return Format.R8G8_UNorm;

                case PixelFormat.R8G8SNorm:
                    return Format.R8G8_SNorm;

                case PixelFormat.R8G8UInt:
                    return Format.R8G8_UInt;

                case PixelFormat.R8G8SInt:
                    return Format.R8G8_SInt;

                case PixelFormat.R16G16UNorm:
                    return Format.R16G16_UNorm;

                case PixelFormat.R16G16SNorm:
                    return Format.R16G16_SNorm;

                case PixelFormat.R16G16UInt:
                    return Format.R16G16_UInt;

                case PixelFormat.R16G16SInt:
                    return Format.R16G16_SInt;

                case PixelFormat.R16G16Float:
                    return Format.R16G16_Float;

                case PixelFormat.R32G32UInt:
                    return Format.R32G32_UInt;

                case PixelFormat.R32G32SInt:
                    return Format.R32G32_SInt;

                case PixelFormat.R32G32Float:
                    return Format.R32G32_Float;

                case PixelFormat.R8G8B8A8UNorm:
                    return Format.R8G8B8A8_UNorm;

                case PixelFormat.R8G8B8A8UNormSRgb:
                    return Format.R8G8B8A8_UNorm_SRgb;

                case PixelFormat.B8G8R8A8UNorm:
                    return Format.B8G8R8A8_UNorm;

                case PixelFormat.B8G8R8A8UNormSRgb:
                    return Format.B8G8R8A8_UNorm_SRgb;

                case PixelFormat.R8G8B8A8SNorm:
                    return Format.R8G8B8A8_SNorm;

                case PixelFormat.R8G8B8A8UInt:
                    return Format.R8G8B8A8_UInt;

                case PixelFormat.R8G8B8A8SInt:
                    return Format.R8G8B8A8_SInt;

                case PixelFormat.R16G16B16A16UNorm:
                    return Format.R16G16B16A16_UNorm;

                case PixelFormat.R16G16B16A16SNorm:
                    return Format.R16G16B16A16_SNorm;

                case PixelFormat.R16G16B16A16UInt:
                    return Format.R16G16B16A16_UInt;

                case PixelFormat.R16G16B16A16SInt:
                    return Format.R16G16B16A16_SInt;

                case PixelFormat.R16G16B16A16Float:
                    return Format.R16G16B16A16_Float;

                case PixelFormat.R32G32B32A32UInt:
                    return Format.R32G32B32A32_UInt;

                case PixelFormat.R32G32B32A32SInt:
                    return Format.R32G32B32A32_SInt;

                case PixelFormat.R32G32B32A32Float:
                    return Format.R32G32B32A32_Float;

                case PixelFormat.Bc1RgbUNorm:
                case PixelFormat.Bc1RgbaUNorm:
                    return Format.BC1_UNorm;

                case PixelFormat.Bc1RgbUNormSRgb:
                case PixelFormat.Bc1RgbaUNormSRgb:
                    return Format.BC1_UNorm_SRgb;

                case PixelFormat.Bc2UNorm:
                    return Format.BC2_UNorm;

                case PixelFormat.Bc2UNormSRgb:
                    return Format.BC2_UNorm_SRgb;

                case PixelFormat.Bc3UNorm:
                    return Format.BC3_UNorm;

                case PixelFormat.Bc3UNormSRgb:
                    return Format.BC3_UNorm_SRgb;

                case PixelFormat.Bc4UNorm:
                    return Format.BC4_UNorm;

                case PixelFormat.Bc4SNorm:
                    return Format.BC4_SNorm;

                case PixelFormat.Bc5UNorm:
                    return Format.BC5_UNorm;

                case PixelFormat.Bc5SNorm:
                    return Format.BC5_SNorm;

                case PixelFormat.Bc7UNorm:
                    return Format.BC7_UNorm;

                case PixelFormat.Bc7UNormSRgb:
                    return Format.BC7_UNorm_SRgb;

                case PixelFormat.D24UNormS8UInt:
                    Debug.Assert(depthFormat);
                    return Format.R24G8_Typeless;

                case PixelFormat.D32FloatS8UInt:
                    Debug.Assert(depthFormat);
                    return Format.R32G8X24_Typeless;

                case PixelFormat.R10G10B10A2UNorm:
                    return Format.R10G10B10A2_UNorm;

                case PixelFormat.R10G10B10A2UInt:
                    return Format.R10G10B10A2_UInt;

                case PixelFormat.R11G11B10Float:
                    return Format.R11G11B10_Float;

                case PixelFormat.Etc2R8G8B8UNorm:
                case PixelFormat.Etc2R8G8B8A1UNorm:
                case PixelFormat.Etc2R8G8B8A8UNorm:
                    throw new VeldridException("ETC2 formats are not supported on Direct3D 12.");

                default:
                    throw Illegal.Value<PixelFormat>();
            }
        }

        internal static Format GetTypelessFormat(Format format)
        {
            return format switch
            {
                Format.R32G32B32A32_Typeless or Format.R32G32B32A32_Float or Format.R32G32B32A32_UInt or Format.R32G32B32A32_SInt => Format.R32G32B32A32_Typeless,
                Format.R32G32B32_Typeless or Format.R32G32B32_Float or Format.R32G32B32_UInt or Format.R32G32B32_SInt => Format.R32G32B32_Typeless,
                Format.R16G16B16A16_Typeless or Format.R16G16B16A16_Float or Format.R16G16B16A16_UNorm or Format.R16G16B16A16_UInt or Format.R16G16B16A16_SNorm or Format.R16G16B16A16_SInt => Format.R16G16B16A16_Typeless,
                Format.R32G32_Typeless or Format.R32G32_Float or Format.R32G32_UInt or Format.R32G32_SInt => Format.R32G32_Typeless,
                Format.R10G10B10A2_Typeless or Format.R10G10B10A2_UNorm or Format.R10G10B10A2_UInt => Format.R10G10B10A2_Typeless,
                Format.R8G8B8A8_Typeless or Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb or Format.R8G8B8A8_UInt or Format.R8G8B8A8_SNorm or Format.R8G8B8A8_SInt => Format.R8G8B8A8_Typeless,
                Format.R16G16_Typeless or Format.R16G16_Float or Format.R16G16_UNorm or Format.R16G16_UInt or Format.R16G16_SNorm or Format.R16G16_SInt => Format.R16G16_Typeless,
                Format.R32_Typeless or Format.D32_Float or Format.R32_Float or Format.R32_UInt or Format.R32_SInt => Format.R32_Typeless,
                Format.R24G8_Typeless or Format.D24_UNorm_S8_UInt or Format.R24_UNorm_X8_Typeless or Format.X24_Typeless_G8_UInt => Format.R24G8_Typeless,
                Format.R8G8_Typeless or Format.R8G8_UNorm or Format.R8G8_UInt or Format.R8G8_SNorm or Format.R8G8_SInt => Format.R8G8_Typeless,
                Format.R16_Typeless or Format.R16_Float or Format.D16_UNorm or Format.R16_UNorm or Format.R16_UInt or Format.R16_SNorm or Format.R16_SInt => Format.R16_Typeless,
                Format.R8_Typeless or Format.R8_UNorm or Format.R8_UInt or Format.R8_SNorm or Format.R8_SInt or Format.A8_UNorm => Format.R8_Typeless,
                Format.BC1_Typeless or Format.BC1_UNorm or Format.BC1_UNorm_SRgb => Format.BC1_Typeless,
                Format.BC2_Typeless or Format.BC2_UNorm or Format.BC2_UNorm_SRgb => Format.BC2_Typeless,
                Format.BC3_Typeless or Format.BC3_UNorm or Format.BC3_UNorm_SRgb => Format.BC3_Typeless,
                Format.BC4_Typeless or Format.BC4_UNorm or Format.BC4_SNorm => Format.BC4_Typeless,
                Format.BC5_Typeless or Format.BC5_UNorm or Format.BC5_SNorm => Format.BC5_Typeless,
                Format.B8G8R8A8_Typeless or Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb => Format.B8G8R8A8_Typeless,
                Format.BC7_Typeless or Format.BC7_UNorm or Format.BC7_UNorm_SRgb => Format.BC7_Typeless,
                _ => format,
            };
        }

        internal static PixelFormat ToVdFormat(Format format)
        {
            return format switch
            {
                Format.R8_UNorm => PixelFormat.R8UNorm,
                Format.R8_SNorm => PixelFormat.R8SNorm,
                Format.R8_UInt => PixelFormat.R8UInt,
                Format.R8_SInt => PixelFormat.R8SInt,
                Format.R16_UNorm or Format.D16_UNorm => PixelFormat.R16UNorm,
                Format.R16_SNorm => PixelFormat.R16SNorm,
                Format.R16_UInt => PixelFormat.R16UInt,
                Format.R16_SInt => PixelFormat.R16SInt,
                Format.R16_Float => PixelFormat.R16Float,
                Format.R32_UInt => PixelFormat.R32UInt,
                Format.R32_SInt => PixelFormat.R32SInt,
                Format.R32_Float or Format.D32_Float => PixelFormat.R32Float,
                Format.R8G8_UNorm => PixelFormat.R8G8UNorm,
                Format.R8G8_SNorm => PixelFormat.R8G8SNorm,
                Format.R8G8_UInt => PixelFormat.R8G8UInt,
                Format.R8G8_SInt => PixelFormat.R8G8SInt,
                Format.R16G16_UNorm => PixelFormat.R16G16UNorm,
                Format.R16G16_SNorm => PixelFormat.R16G16SNorm,
                Format.R16G16_UInt => PixelFormat.R16G16UInt,
                Format.R16G16_SInt => PixelFormat.R16G16SInt,
                Format.R16G16_Float => PixelFormat.R16G16Float,
                Format.R32G32_UInt => PixelFormat.R32G32UInt,
                Format.R32G32_SInt => PixelFormat.R32G32SInt,
                Format.R32G32_Float => PixelFormat.R32G32Float,
                Format.R8G8B8A8_UNorm => PixelFormat.R8G8B8A8UNorm,
                Format.R8G8B8A8_UNorm_SRgb => PixelFormat.R8G8B8A8UNormSRgb,
                Format.B8G8R8A8_UNorm => PixelFormat.B8G8R8A8UNorm,
                Format.B8G8R8A8_UNorm_SRgb => PixelFormat.B8G8R8A8UNormSRgb,
                Format.R8G8B8A8_SNorm => PixelFormat.R8G8B8A8SNorm,
                Format.R8G8B8A8_UInt => PixelFormat.R8G8B8A8UInt,
                Format.R8G8B8A8_SInt => PixelFormat.R8G8B8A8SInt,
                Format.R16G16B16A16_UNorm => PixelFormat.R16G16B16A16UNorm,
                Format.R16G16B16A16_SNorm => PixelFormat.R16G16B16A16SNorm,
                Format.R16G16B16A16_UInt => PixelFormat.R16G16B16A16UInt,
                Format.R16G16B16A16_SInt => PixelFormat.R16G16B16A16SInt,
                Format.R16G16B16A16_Float => PixelFormat.R16G16B16A16Float,
                Format.R32G32B32A32_UInt => PixelFormat.R32G32B32A32UInt,
                Format.R32G32B32A32_SInt => PixelFormat.R32G32B32A32SInt,
                Format.R32G32B32A32_Float => PixelFormat.R32G32B32A32Float,
                Format.BC1_UNorm or Format.BC1_Typeless => PixelFormat.Bc1RgbaUNorm,
                Format.BC2_UNorm => PixelFormat.Bc2UNorm,
                Format.BC3_UNorm => PixelFormat.Bc3UNorm,
                Format.BC4_UNorm => PixelFormat.Bc4UNorm,
                Format.BC4_SNorm => PixelFormat.Bc4SNorm,
                Format.BC5_UNorm => PixelFormat.Bc5UNorm,
                Format.BC5_SNorm => PixelFormat.Bc5SNorm,
                Format.BC7_UNorm => PixelFormat.Bc7UNorm,
                Format.D24_UNorm_S8_UInt => PixelFormat.D24UNormS8UInt,
                Format.D32_Float_S8X24_UInt => PixelFormat.D32FloatS8UInt,
                Format.R10G10B10A2_UInt => PixelFormat.R10G10B10A2UInt,
                Format.R10G10B10A2_UNorm => PixelFormat.R10G10B10A2UNorm,
                Format.R11G11B10_Float => PixelFormat.R11G11B10Float,
                _ => throw Illegal.Value<PixelFormat>(),
            };
        }

        internal static Format GetViewFormat(Format format)
        {
            return format switch
            {
                Format.R16_Typeless => Format.R16_UNorm,
                Format.R32_Typeless => Format.R32_Float,
                Format.R32G8X24_Typeless => Format.R32_Float_X8X24_Typeless,
                Format.R24G8_Typeless => Format.R24_UNorm_X8_Typeless,
                _ => format,
            };
        }

        internal static int ToDxgiSampleCount(TextureSampleCount sampleCount)
        {
            return sampleCount switch
            {
                TextureSampleCount.Count1 => 1,
                TextureSampleCount.Count2 => 2,
                TextureSampleCount.Count4 => 4,
                TextureSampleCount.Count8 => 8,
                TextureSampleCount.Count16 => 16,
                TextureSampleCount.Count32 => 32,
                _ => throw Illegal.Value<TextureSampleCount>(),
            };
        }

        internal static Blend VdToD3D12BlendFactor(BlendFactor factor)
        {
            return factor switch
            {
                BlendFactor.Zero => Blend.Zero,
                BlendFactor.One => Blend.One,
                BlendFactor.SourceAlpha => Blend.SourceAlpha,
                BlendFactor.InverseSourceAlpha => Blend.InverseSourceAlpha,
                BlendFactor.DestinationAlpha => Blend.DestinationAlpha,
                BlendFactor.InverseDestinationAlpha => Blend.InverseDestinationAlpha,
                BlendFactor.SourceColor => Blend.SourceColor,
                BlendFactor.InverseSourceColor => Blend.InverseSourceColor,
                BlendFactor.DestinationColor => Blend.DestinationColor,
                BlendFactor.InverseDestinationColor => Blend.InverseDestinationColor,
                BlendFactor.BlendFactor => Blend.BlendFactor,
                BlendFactor.InverseBlendFactor => Blend.InverseBlendFactor,
                _ => throw Illegal.Value<BlendFactor>(),
            };
        }

        internal static BlendOperation VdToD3D12BlendOperation(BlendFunction function)
        {
            return function switch
            {
                BlendFunction.Add => BlendOperation.Add,
                BlendFunction.Subtract => BlendOperation.Subtract,
                BlendFunction.ReverseSubtract => BlendOperation.RevSubtract,
                BlendFunction.Minimum => BlendOperation.Min,
                BlendFunction.Maximum => BlendOperation.Max,
                _ => throw Illegal.Value<BlendFunction>(),
            };
        }

        internal static Vortice.Direct3D12.StencilOperation VdToD3D12StencilOp(StencilOperation op)
        {
            return op switch
            {
                StencilOperation.Keep => Vortice.Direct3D12.StencilOperation.Keep,
                StencilOperation.Zero => Vortice.Direct3D12.StencilOperation.Zero,
                StencilOperation.Replace => Vortice.Direct3D12.StencilOperation.Replace,
                StencilOperation.IncrementAndClamp => Vortice.Direct3D12.StencilOperation.IncrementSaturate,
                StencilOperation.DecrementAndClamp => Vortice.Direct3D12.StencilOperation.DecrementSaturate,
                StencilOperation.Invert => Vortice.Direct3D12.StencilOperation.Invert,
                StencilOperation.IncrementAndWrap => Vortice.Direct3D12.StencilOperation.Increment,
                StencilOperation.DecrementAndWrap => Vortice.Direct3D12.StencilOperation.Decrement,
                _ => throw Illegal.Value<StencilOperation>(),
            };
        }

        internal static ComparisonFunction VdToD3D12ComparisonFunc(ComparisonKind comparisonKind)
        {
            return comparisonKind switch
            {
                ComparisonKind.Never => ComparisonFunction.Never,
                ComparisonKind.Less => ComparisonFunction.Less,
                ComparisonKind.Equal => ComparisonFunction.Equal,
                ComparisonKind.LessEqual => ComparisonFunction.LessEqual,
                ComparisonKind.Greater => ComparisonFunction.Greater,
                ComparisonKind.NotEqual => ComparisonFunction.NotEqual,
                ComparisonKind.GreaterEqual => ComparisonFunction.GreaterEqual,
                ComparisonKind.Always => ComparisonFunction.Always,
                _ => throw Illegal.Value<ComparisonKind>(),
            };
        }

        internal static FillMode VdToD3D12FillMode(PolygonFillMode fillMode)
        {
            return fillMode switch
            {
                PolygonFillMode.Solid => FillMode.Solid,
                PolygonFillMode.Wireframe => FillMode.Wireframe,
                _ => throw Illegal.Value<PolygonFillMode>(),
            };
        }

        internal static CullMode VdToD3D12CullMode(FaceCullMode cullingMode)
        {
            return cullingMode switch
            {
                FaceCullMode.Back => CullMode.Back,
                FaceCullMode.Front => CullMode.Front,
                FaceCullMode.None => CullMode.None,
                _ => throw Illegal.Value<FaceCullMode>(),
            };
        }

        internal static PrimitiveTopologyType VdToD3D12PrimitiveTopologyType(PrimitiveTopology topology)
        {
            return topology switch
            {
                PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip => PrimitiveTopologyType.Triangle,
                PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => PrimitiveTopologyType.Line,
                PrimitiveTopology.PointList => PrimitiveTopologyType.Point,
                _ => throw Illegal.Value<PrimitiveTopology>(),
            };
        }

        internal static Vortice.Direct3D.PrimitiveTopology VdToD3D12PrimitiveTopology(PrimitiveTopology topology)
        {
            return topology switch
            {
                PrimitiveTopology.TriangleList => Vortice.Direct3D.PrimitiveTopology.TriangleList,
                PrimitiveTopology.TriangleStrip => Vortice.Direct3D.PrimitiveTopology.TriangleStrip,
                PrimitiveTopology.LineList => Vortice.Direct3D.PrimitiveTopology.LineList,
                PrimitiveTopology.LineStrip => Vortice.Direct3D.PrimitiveTopology.LineStrip,
                PrimitiveTopology.PointList => Vortice.Direct3D.PrimitiveTopology.PointList,
                _ => throw Illegal.Value<PrimitiveTopology>(),
            };
        }

        internal static Vortice.Direct3D12.TextureAddressMode VdToD3D12AddressMode(SamplerAddressMode mode)
        {
            return mode switch
            {
                SamplerAddressMode.Wrap => Vortice.Direct3D12.TextureAddressMode.Wrap,
                SamplerAddressMode.Mirror => Vortice.Direct3D12.TextureAddressMode.Mirror,
                SamplerAddressMode.Clamp => Vortice.Direct3D12.TextureAddressMode.Clamp,
                SamplerAddressMode.Border => Vortice.Direct3D12.TextureAddressMode.Border,
                _ => throw Illegal.Value<SamplerAddressMode>(),
            };
        }

        internal static Filter VdToD3D12Filter(SamplerFilter filter, bool isComparison)
        {
            return filter switch
            {
                SamplerFilter.MinPointMagPointMipPoint => isComparison ? Filter.ComparisonMinMagMipPoint : Filter.MinMagMipPoint,
                SamplerFilter.MinPointMagPointMipLinear => isComparison ? Filter.ComparisonMinMagPointMipLinear : Filter.MinMagPointMipLinear,
                SamplerFilter.MinPointMagLinearMipPoint => isComparison ? Filter.ComparisonMinPointMagLinearMipPoint : Filter.MinPointMagLinearMipPoint,
                SamplerFilter.MinPointMagLinearMipLinear => isComparison ? Filter.ComparisonMinPointMagMipLinear : Filter.MinPointMagMipLinear,
                SamplerFilter.MinLinearMagPointMipPoint => isComparison ? Filter.ComparisonMinLinearMagMipPoint : Filter.MinLinearMagMipPoint,
                SamplerFilter.MinLinearMagPointMipLinear => isComparison ? Filter.ComparisonMinLinearMagPointMipLinear : Filter.MinLinearMagPointMipLinear,
                SamplerFilter.MinLinearMagLinearMipPoint => isComparison ? Filter.ComparisonMinMagLinearMipPoint : Filter.MinMagLinearMipPoint,
                SamplerFilter.MinLinearMagLinearMipLinear => isComparison ? Filter.ComparisonMinMagMipLinear : Filter.MinMagMipLinear,
                SamplerFilter.Anisotropic => isComparison ? Filter.ComparisonAnisotropic : Filter.Anisotropic,
                _ => throw Illegal.Value<SamplerFilter>(),
            };
        }
    }
}
