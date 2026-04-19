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
            switch (format)
            {
                case Format.R32G32B32A32_Typeless:
                case Format.R32G32B32A32_Float:
                case Format.R32G32B32A32_UInt:
                case Format.R32G32B32A32_SInt:
                    return Format.R32G32B32A32_Typeless;

                case Format.R32G32B32_Typeless:
                case Format.R32G32B32_Float:
                case Format.R32G32B32_UInt:
                case Format.R32G32B32_SInt:
                    return Format.R32G32B32_Typeless;

                case Format.R16G16B16A16_Typeless:
                case Format.R16G16B16A16_Float:
                case Format.R16G16B16A16_UNorm:
                case Format.R16G16B16A16_UInt:
                case Format.R16G16B16A16_SNorm:
                case Format.R16G16B16A16_SInt:
                    return Format.R16G16B16A16_Typeless;

                case Format.R32G32_Typeless:
                case Format.R32G32_Float:
                case Format.R32G32_UInt:
                case Format.R32G32_SInt:
                    return Format.R32G32_Typeless;

                case Format.R10G10B10A2_Typeless:
                case Format.R10G10B10A2_UNorm:
                case Format.R10G10B10A2_UInt:
                    return Format.R10G10B10A2_Typeless;

                case Format.R8G8B8A8_Typeless:
                case Format.R8G8B8A8_UNorm:
                case Format.R8G8B8A8_UNorm_SRgb:
                case Format.R8G8B8A8_UInt:
                case Format.R8G8B8A8_SNorm:
                case Format.R8G8B8A8_SInt:
                    return Format.R8G8B8A8_Typeless;

                case Format.R16G16_Typeless:
                case Format.R16G16_Float:
                case Format.R16G16_UNorm:
                case Format.R16G16_UInt:
                case Format.R16G16_SNorm:
                case Format.R16G16_SInt:
                    return Format.R16G16_Typeless;

                case Format.R32_Typeless:
                case Format.D32_Float:
                case Format.R32_Float:
                case Format.R32_UInt:
                case Format.R32_SInt:
                    return Format.R32_Typeless;

                case Format.R24G8_Typeless:
                case Format.D24_UNorm_S8_UInt:
                case Format.R24_UNorm_X8_Typeless:
                case Format.X24_Typeless_G8_UInt:
                    return Format.R24G8_Typeless;

                case Format.R8G8_Typeless:
                case Format.R8G8_UNorm:
                case Format.R8G8_UInt:
                case Format.R8G8_SNorm:
                case Format.R8G8_SInt:
                    return Format.R8G8_Typeless;

                case Format.R16_Typeless:
                case Format.R16_Float:
                case Format.D16_UNorm:
                case Format.R16_UNorm:
                case Format.R16_UInt:
                case Format.R16_SNorm:
                case Format.R16_SInt:
                    return Format.R16_Typeless;

                case Format.R8_Typeless:
                case Format.R8_UNorm:
                case Format.R8_UInt:
                case Format.R8_SNorm:
                case Format.R8_SInt:
                case Format.A8_UNorm:
                    return Format.R8_Typeless;

                case Format.BC1_Typeless:
                case Format.BC1_UNorm:
                case Format.BC1_UNorm_SRgb:
                    return Format.BC1_Typeless;

                case Format.BC2_Typeless:
                case Format.BC2_UNorm:
                case Format.BC2_UNorm_SRgb:
                    return Format.BC2_Typeless;

                case Format.BC3_Typeless:
                case Format.BC3_UNorm:
                case Format.BC3_UNorm_SRgb:
                    return Format.BC3_Typeless;

                case Format.BC4_Typeless:
                case Format.BC4_UNorm:
                case Format.BC4_SNorm:
                    return Format.BC4_Typeless;

                case Format.BC5_Typeless:
                case Format.BC5_UNorm:
                case Format.BC5_SNorm:
                    return Format.BC5_Typeless;

                case Format.B8G8R8A8_Typeless:
                case Format.B8G8R8A8_UNorm:
                case Format.B8G8R8A8_UNorm_SRgb:
                    return Format.B8G8R8A8_Typeless;

                case Format.BC7_Typeless:
                case Format.BC7_UNorm:
                case Format.BC7_UNorm_SRgb:
                    return Format.BC7_Typeless;

                default:
                    return format;
            }
        }

        internal static PixelFormat ToVdFormat(Format format)
        {
            switch (format)
            {
                case Format.R8_UNorm:
                    return PixelFormat.R8UNorm;

                case Format.R8_SNorm:
                    return PixelFormat.R8SNorm;

                case Format.R8_UInt:
                    return PixelFormat.R8UInt;

                case Format.R8_SInt:
                    return PixelFormat.R8SInt;

                case Format.R16_UNorm:
                case Format.D16_UNorm:
                    return PixelFormat.R16UNorm;

                case Format.R16_SNorm:
                    return PixelFormat.R16SNorm;

                case Format.R16_UInt:
                    return PixelFormat.R16UInt;

                case Format.R16_SInt:
                    return PixelFormat.R16SInt;

                case Format.R16_Float:
                    return PixelFormat.R16Float;

                case Format.R32_UInt:
                    return PixelFormat.R32UInt;

                case Format.R32_SInt:
                    return PixelFormat.R32SInt;

                case Format.R32_Float:
                case Format.D32_Float:
                    return PixelFormat.R32Float;

                case Format.R8G8_UNorm:
                    return PixelFormat.R8G8UNorm;

                case Format.R8G8_SNorm:
                    return PixelFormat.R8G8SNorm;

                case Format.R8G8_UInt:
                    return PixelFormat.R8G8UInt;

                case Format.R8G8_SInt:
                    return PixelFormat.R8G8SInt;

                case Format.R16G16_UNorm:
                    return PixelFormat.R16G16UNorm;

                case Format.R16G16_SNorm:
                    return PixelFormat.R16G16SNorm;

                case Format.R16G16_UInt:
                    return PixelFormat.R16G16UInt;

                case Format.R16G16_SInt:
                    return PixelFormat.R16G16SInt;

                case Format.R16G16_Float:
                    return PixelFormat.R16G16Float;

                case Format.R32G32_UInt:
                    return PixelFormat.R32G32UInt;

                case Format.R32G32_SInt:
                    return PixelFormat.R32G32SInt;

                case Format.R32G32_Float:
                    return PixelFormat.R32G32Float;

                case Format.R8G8B8A8_UNorm:
                    return PixelFormat.R8G8B8A8UNorm;

                case Format.R8G8B8A8_UNorm_SRgb:
                    return PixelFormat.R8G8B8A8UNormSRgb;

                case Format.B8G8R8A8_UNorm:
                    return PixelFormat.B8G8R8A8UNorm;

                case Format.B8G8R8A8_UNorm_SRgb:
                    return PixelFormat.B8G8R8A8UNormSRgb;

                case Format.R8G8B8A8_SNorm:
                    return PixelFormat.R8G8B8A8SNorm;

                case Format.R8G8B8A8_UInt:
                    return PixelFormat.R8G8B8A8UInt;

                case Format.R8G8B8A8_SInt:
                    return PixelFormat.R8G8B8A8SInt;

                case Format.R16G16B16A16_UNorm:
                    return PixelFormat.R16G16B16A16UNorm;

                case Format.R16G16B16A16_SNorm:
                    return PixelFormat.R16G16B16A16SNorm;

                case Format.R16G16B16A16_UInt:
                    return PixelFormat.R16G16B16A16UInt;

                case Format.R16G16B16A16_SInt:
                    return PixelFormat.R16G16B16A16SInt;

                case Format.R16G16B16A16_Float:
                    return PixelFormat.R16G16B16A16Float;

                case Format.R32G32B32A32_UInt:
                    return PixelFormat.R32G32B32A32UInt;

                case Format.R32G32B32A32_SInt:
                    return PixelFormat.R32G32B32A32SInt;

                case Format.R32G32B32A32_Float:
                    return PixelFormat.R32G32B32A32Float;

                case Format.BC1_UNorm:
                case Format.BC1_Typeless:
                    return PixelFormat.Bc1RgbaUNorm;

                case Format.BC2_UNorm:
                    return PixelFormat.Bc2UNorm;

                case Format.BC3_UNorm:
                    return PixelFormat.Bc3UNorm;

                case Format.BC4_UNorm:
                    return PixelFormat.Bc4UNorm;

                case Format.BC4_SNorm:
                    return PixelFormat.Bc4SNorm;

                case Format.BC5_UNorm:
                    return PixelFormat.Bc5UNorm;

                case Format.BC5_SNorm:
                    return PixelFormat.Bc5SNorm;

                case Format.BC7_UNorm:
                    return PixelFormat.Bc7UNorm;

                case Format.D24_UNorm_S8_UInt:
                    return PixelFormat.D24UNormS8UInt;

                case Format.D32_Float_S8X24_UInt:
                    return PixelFormat.D32FloatS8UInt;

                case Format.R10G10B10A2_UInt:
                    return PixelFormat.R10G10B10A2UInt;

                case Format.R10G10B10A2_UNorm:
                    return PixelFormat.R10G10B10A2UNorm;

                case Format.R11G11B10_Float:
                    return PixelFormat.R11G11B10Float;

                default:
                    throw Illegal.Value<PixelFormat>();
            }
        }

        internal static Format GetViewFormat(Format format)
        {
            switch (format)
            {
                case Format.R16_Typeless:
                    return Format.R16_UNorm;

                case Format.R32_Typeless:
                    return Format.R32_Float;

                case Format.R32G8X24_Typeless:
                    return Format.R32_Float_X8X24_Typeless;

                case Format.R24G8_Typeless:
                    return Format.R24_UNorm_X8_Typeless;

                default:
                    return format;
            }
        }

        internal static int ToDxgiSampleCount(TextureSampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case TextureSampleCount.Count1:
                    return 1;

                case TextureSampleCount.Count2:
                    return 2;

                case TextureSampleCount.Count4:
                    return 4;

                case TextureSampleCount.Count8:
                    return 8;

                case TextureSampleCount.Count16:
                    return 16;

                case TextureSampleCount.Count32:
                    return 32;

                default:
                    throw Illegal.Value<TextureSampleCount>();
            }
        }

        internal static Blend VdToD3D12BlendFactor(BlendFactor factor)
        {
            switch (factor)
            {
                case BlendFactor.Zero:
                    return Blend.Zero;

                case BlendFactor.One:
                    return Blend.One;

                case BlendFactor.SourceAlpha:
                    return Blend.SourceAlpha;

                case BlendFactor.InverseSourceAlpha:
                    return Blend.InverseSourceAlpha;

                case BlendFactor.DestinationAlpha:
                    return Blend.DestinationAlpha;

                case BlendFactor.InverseDestinationAlpha:
                    return Blend.InverseDestinationAlpha;

                case BlendFactor.SourceColor:
                    return Blend.SourceColor;

                case BlendFactor.InverseSourceColor:
                    return Blend.InverseSourceColor;

                case BlendFactor.DestinationColor:
                    return Blend.DestinationColor;

                case BlendFactor.InverseDestinationColor:
                    return Blend.InverseDestinationColor;

                case BlendFactor.BlendFactor:
                    return Blend.BlendFactor;

                case BlendFactor.InverseBlendFactor:
                    return Blend.InverseBlendFactor;

                default:
                    throw Illegal.Value<BlendFactor>();
            }
        }

        internal static BlendOperation VdToD3D12BlendOperation(BlendFunction function)
        {
            switch (function)
            {
                case BlendFunction.Add:
                    return BlendOperation.Add;

                case BlendFunction.Subtract:
                    return BlendOperation.Subtract;

                case BlendFunction.ReverseSubtract:
                    return BlendOperation.RevSubtract;

                case BlendFunction.Minimum:
                    return BlendOperation.Min;

                case BlendFunction.Maximum:
                    return BlendOperation.Max;

                default:
                    throw Illegal.Value<BlendFunction>();
            }
        }

        internal static Vortice.Direct3D12.StencilOperation VdToD3D12StencilOp(StencilOperation op)
        {
            switch (op)
            {
                case StencilOperation.Keep:
                    return Vortice.Direct3D12.StencilOperation.Keep;

                case StencilOperation.Zero:
                    return Vortice.Direct3D12.StencilOperation.Zero;

                case StencilOperation.Replace:
                    return Vortice.Direct3D12.StencilOperation.Replace;

                case StencilOperation.IncrementAndClamp:
                    return Vortice.Direct3D12.StencilOperation.IncrementSaturate;

                case StencilOperation.DecrementAndClamp:
                    return Vortice.Direct3D12.StencilOperation.DecrementSaturate;

                case StencilOperation.Invert:
                    return Vortice.Direct3D12.StencilOperation.Invert;

                case StencilOperation.IncrementAndWrap:
                    return Vortice.Direct3D12.StencilOperation.Increment;

                case StencilOperation.DecrementAndWrap:
                    return Vortice.Direct3D12.StencilOperation.Decrement;

                default:
                    throw Illegal.Value<StencilOperation>();
            }
        }

        internal static ComparisonFunction VdToD3D12ComparisonFunc(ComparisonKind comparisonKind)
        {
            switch (comparisonKind)
            {
                case ComparisonKind.Never:
                    return ComparisonFunction.Never;

                case ComparisonKind.Less:
                    return ComparisonFunction.Less;

                case ComparisonKind.Equal:
                    return ComparisonFunction.Equal;

                case ComparisonKind.LessEqual:
                    return ComparisonFunction.LessEqual;

                case ComparisonKind.Greater:
                    return ComparisonFunction.Greater;

                case ComparisonKind.NotEqual:
                    return ComparisonFunction.NotEqual;

                case ComparisonKind.GreaterEqual:
                    return ComparisonFunction.GreaterEqual;

                case ComparisonKind.Always:
                    return ComparisonFunction.Always;

                default:
                    throw Illegal.Value<ComparisonKind>();
            }
        }

        internal static FillMode VdToD3D12FillMode(PolygonFillMode fillMode)
        {
            switch (fillMode)
            {
                case PolygonFillMode.Solid:
                    return FillMode.Solid;

                case PolygonFillMode.Wireframe:
                    return FillMode.Wireframe;

                default:
                    throw Illegal.Value<PolygonFillMode>();
            }
        }

        internal static CullMode VdToD3D12CullMode(FaceCullMode cullingMode)
        {
            switch (cullingMode)
            {
                case FaceCullMode.Back:
                    return CullMode.Back;

                case FaceCullMode.Front:
                    return CullMode.Front;

                case FaceCullMode.None:
                    return CullMode.None;

                default:
                    throw Illegal.Value<FaceCullMode>();
            }
        }

        internal static PrimitiveTopologyType VdToD3D12PrimitiveTopologyType(PrimitiveTopology topology)
        {
            switch (topology)
            {
                case PrimitiveTopology.TriangleList:
                case PrimitiveTopology.TriangleStrip:
                    return PrimitiveTopologyType.Triangle;

                case PrimitiveTopology.LineList:
                case PrimitiveTopology.LineStrip:
                    return PrimitiveTopologyType.Line;

                case PrimitiveTopology.PointList:
                    return PrimitiveTopologyType.Point;

                default:
                    throw Illegal.Value<PrimitiveTopology>();
            }
        }

        internal static Vortice.Direct3D.PrimitiveTopology VdToD3D12PrimitiveTopology(PrimitiveTopology topology)
        {
            switch (topology)
            {
                case PrimitiveTopology.TriangleList:
                    return Vortice.Direct3D.PrimitiveTopology.TriangleList;

                case PrimitiveTopology.TriangleStrip:
                    return Vortice.Direct3D.PrimitiveTopology.TriangleStrip;

                case PrimitiveTopology.LineList:
                    return Vortice.Direct3D.PrimitiveTopology.LineList;

                case PrimitiveTopology.LineStrip:
                    return Vortice.Direct3D.PrimitiveTopology.LineStrip;

                case PrimitiveTopology.PointList:
                    return Vortice.Direct3D.PrimitiveTopology.PointList;

                default:
                    throw Illegal.Value<PrimitiveTopology>();
            }
        }

        internal static Vortice.Direct3D12.TextureAddressMode VdToD3D12AddressMode(SamplerAddressMode mode)
        {
            switch (mode)
            {
                case SamplerAddressMode.Wrap:
                    return Vortice.Direct3D12.TextureAddressMode.Wrap;

                case SamplerAddressMode.Mirror:
                    return Vortice.Direct3D12.TextureAddressMode.Mirror;

                case SamplerAddressMode.Clamp:
                    return Vortice.Direct3D12.TextureAddressMode.Clamp;

                case SamplerAddressMode.Border:
                    return Vortice.Direct3D12.TextureAddressMode.Border;

                default:
                    throw Illegal.Value<SamplerAddressMode>();
            }
        }

        internal static Filter VdToD3D12Filter(SamplerFilter filter, bool isComparison)
        {
            switch (filter)
            {
                case SamplerFilter.MinPointMagPointMipPoint:
                    return isComparison ? Filter.ComparisonMinMagMipPoint : Filter.MinMagMipPoint;

                case SamplerFilter.MinPointMagPointMipLinear:
                    return isComparison ? Filter.ComparisonMinMagPointMipLinear : Filter.MinMagPointMipLinear;

                case SamplerFilter.MinPointMagLinearMipPoint:
                    return isComparison ? Filter.ComparisonMinPointMagLinearMipPoint : Filter.MinPointMagLinearMipPoint;

                case SamplerFilter.MinPointMagLinearMipLinear:
                    return isComparison ? Filter.ComparisonMinPointMagMipLinear : Filter.MinPointMagMipLinear;

                case SamplerFilter.MinLinearMagPointMipPoint:
                    return isComparison ? Filter.ComparisonMinLinearMagMipPoint : Filter.MinLinearMagMipPoint;

                case SamplerFilter.MinLinearMagPointMipLinear:
                    return isComparison ? Filter.ComparisonMinLinearMagPointMipLinear : Filter.MinLinearMagPointMipLinear;

                case SamplerFilter.MinLinearMagLinearMipPoint:
                    return isComparison ? Filter.ComparisonMinMagLinearMipPoint : Filter.MinMagLinearMipPoint;

                case SamplerFilter.MinLinearMagLinearMipLinear:
                    return isComparison ? Filter.ComparisonMinMagMipLinear : Filter.MinMagMipLinear;

                case SamplerFilter.Anisotropic:
                    return isComparison ? Filter.ComparisonAnisotropic : Filter.Anisotropic;

                default:
                    throw Illegal.Value<SamplerFilter>();
            }
        }
    }
}
