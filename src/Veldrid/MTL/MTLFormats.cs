using Veldrid.MetalBindings;

namespace Veldrid.MTL
{
    internal static class MtlFormats
    {
        internal static MTLPixelFormat VdToMtlPixelFormat(PixelFormat format, bool depthFormat)
        {
            return format switch
            {
                PixelFormat.R8UNorm => MTLPixelFormat.R8Unorm,
                PixelFormat.R8SNorm => MTLPixelFormat.R8Snorm,
                PixelFormat.R8UInt => MTLPixelFormat.R8Uint,
                PixelFormat.R8SInt => MTLPixelFormat.R8Sint,
                PixelFormat.R16UNorm => depthFormat ? MTLPixelFormat.Depth16Unorm : MTLPixelFormat.R16Unorm,
                PixelFormat.R16SNorm => MTLPixelFormat.R16Snorm,
                PixelFormat.R16UInt => MTLPixelFormat.R16Uint,
                PixelFormat.R16SInt => MTLPixelFormat.R16Sint,
                PixelFormat.R16Float => MTLPixelFormat.R16Float,
                PixelFormat.R32UInt => MTLPixelFormat.R32Uint,
                PixelFormat.R32SInt => MTLPixelFormat.R32Sint,
                PixelFormat.R32Float => depthFormat ? MTLPixelFormat.Depth32Float : MTLPixelFormat.R32Float,
                PixelFormat.R8G8UNorm => MTLPixelFormat.RG8Unorm,
                PixelFormat.R8G8SNorm => MTLPixelFormat.RG8Snorm,
                PixelFormat.R8G8UInt => MTLPixelFormat.RG8Uint,
                PixelFormat.R8G8SInt => MTLPixelFormat.RG8Sint,
                PixelFormat.R16G16UNorm => MTLPixelFormat.RG16Unorm,
                PixelFormat.R16G16SNorm => MTLPixelFormat.RG16Snorm,
                PixelFormat.R16G16UInt => MTLPixelFormat.RG16Uint,
                PixelFormat.R16G16SInt => MTLPixelFormat.RG16Sint,
                PixelFormat.R16G16Float => MTLPixelFormat.RG16Float,
                PixelFormat.R32G32UInt => MTLPixelFormat.RG32Uint,
                PixelFormat.R32G32SInt => MTLPixelFormat.RG32Sint,
                PixelFormat.R32G32Float => MTLPixelFormat.RG32Float,
                PixelFormat.R8G8B8A8UNorm => MTLPixelFormat.RGBA8Unorm,
                PixelFormat.R8G8B8A8UNormSRgb => MTLPixelFormat.RGBA8Unorm_sRGB,
                PixelFormat.B8G8R8A8UNorm => MTLPixelFormat.BGRA8Unorm,
                PixelFormat.B8G8R8A8UNormSRgb => MTLPixelFormat.BGRA8Unorm_sRGB,
                PixelFormat.R8G8B8A8SNorm => MTLPixelFormat.RGBA8Snorm,
                PixelFormat.R8G8B8A8UInt => MTLPixelFormat.RGBA8Uint,
                PixelFormat.R8G8B8A8SInt => MTLPixelFormat.RGBA8Sint,
                PixelFormat.R16G16B16A16UNorm => MTLPixelFormat.RGBA16Unorm,
                PixelFormat.R16G16B16A16SNorm => MTLPixelFormat.RGBA16Snorm,
                PixelFormat.R16G16B16A16UInt => MTLPixelFormat.RGBA16Uint,
                PixelFormat.R16G16B16A16SInt => MTLPixelFormat.RGBA16Sint,
                PixelFormat.R16G16B16A16Float => MTLPixelFormat.RGBA16Float,
                PixelFormat.R32G32B32A32UInt => MTLPixelFormat.RGBA32Uint,
                PixelFormat.R32G32B32A32SInt => MTLPixelFormat.RGBA32Sint,
                PixelFormat.R32G32B32A32Float => MTLPixelFormat.RGBA32Float,
                PixelFormat.Bc1RgbUNorm or PixelFormat.Bc1RgbaUNorm => MTLPixelFormat.BC1_RGBA,
                PixelFormat.Bc1RgbUNormSRgb or PixelFormat.Bc1RgbaUNormSRgb => MTLPixelFormat.BC1_RGBA_sRGB,
                PixelFormat.Bc2UNorm => MTLPixelFormat.BC2_RGBA,
                PixelFormat.Bc2UNormSRgb => MTLPixelFormat.BC2_RGBA_sRGB,
                PixelFormat.Bc3UNorm => MTLPixelFormat.BC3_RGBA,
                PixelFormat.Bc3UNormSRgb => MTLPixelFormat.BC3_RGBA_sRGB,
                PixelFormat.Bc4UNorm => MTLPixelFormat.BC4_RUnorm,
                PixelFormat.Bc4SNorm => MTLPixelFormat.BC4_RSnorm,
                PixelFormat.Bc5UNorm => MTLPixelFormat.BC5_RGUnorm,
                PixelFormat.Bc5SNorm => MTLPixelFormat.BC5_RGSnorm,
                PixelFormat.Bc7UNorm => MTLPixelFormat.BC7_RGBAUnorm,
                PixelFormat.Bc7UNormSRgb => MTLPixelFormat.BC7_RGBAUnorm_sRGB,
                PixelFormat.Etc2R8G8B8UNorm => MTLPixelFormat.ETC2_RGB8,
                PixelFormat.Etc2R8G8B8A1UNorm => MTLPixelFormat.ETC2_RGB8A1,
                PixelFormat.Etc2R8G8B8A8UNorm => MTLPixelFormat.EAC_RGBA8,
                PixelFormat.D24UNormS8UInt => MTLPixelFormat.Depth24Unorm_Stencil8,
                PixelFormat.D32FloatS8UInt => MTLPixelFormat.Depth32Float_Stencil8,
                PixelFormat.R10G10B10A2UNorm => MTLPixelFormat.RGB10A2Unorm,
                PixelFormat.R10G10B10A2UInt => MTLPixelFormat.RGB10A2Uint,
                PixelFormat.R11G11B10Float => MTLPixelFormat.RG11B10Float,
                _ => throw Illegal.Value<PixelFormat>(),
            };
        }

        internal static bool IsFormatSupported(PixelFormat format, TextureUsage usage, MtlFeatureSupport metalFeatures)
        {
            return format switch
            {
                PixelFormat.Bc1RgbUNorm or PixelFormat.Bc1RgbUNormSRgb or PixelFormat.Bc1RgbaUNorm or PixelFormat.Bc1RgbaUNormSRgb or PixelFormat.Bc2UNorm or PixelFormat.Bc2UNormSRgb or PixelFormat.Bc3UNorm or PixelFormat.Bc3UNormSRgb or PixelFormat.Bc4UNorm or PixelFormat.Bc4SNorm or PixelFormat.Bc5UNorm or PixelFormat.Bc5SNorm or PixelFormat.Bc7UNorm or PixelFormat.Bc7UNormSRgb => metalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v1)
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         || metalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v2)
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         || metalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v3),
                PixelFormat.Etc2R8G8B8UNorm or PixelFormat.Etc2R8G8B8A1UNorm or PixelFormat.Etc2R8G8B8A8UNorm => metalFeatures.IsSupported(MTLFeatureSet.iOS_GPUFamily1_v1)
                                                                                                                   || metalFeatures.IsSupported(MTLFeatureSet.iOS_GPUFamily2_v1)
                                                                                                                   || metalFeatures.IsSupported(MTLFeatureSet.iOS_GPUFamily3_v1)
                                                                                                                   || metalFeatures.IsSupported(MTLFeatureSet.iOS_GPUFamily4_v1),
                PixelFormat.R16UNorm => (usage & TextureUsage.DepthStencil) == 0
                                        || metalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v2)
                                        || metalFeatures.IsSupported(MTLFeatureSet.macOS_GPUFamily1_v3),
                _ => true,
            };
        }

        internal static MTLTriangleFillMode VdToMtlFillMode(PolygonFillMode fillMode)
        {
            return fillMode switch
            {
                PolygonFillMode.Solid => MTLTriangleFillMode.Fill,
                PolygonFillMode.Wireframe => MTLTriangleFillMode.Lines,
                _ => throw Illegal.Value<PolygonFillMode>(),
            };
        }

        internal static MTLWinding VdVoMtlFrontFace(FrontFace frontFace)
        {
            return frontFace == FrontFace.CounterClockwise ? MTLWinding.CounterClockwise : MTLWinding.Clockwise;
        }

        internal static void GetMinMagMipFilter(
            SamplerFilter filter,
            out MTLSamplerMinMagFilter min,
            out MTLSamplerMinMagFilter mag,
            out MTLSamplerMipFilter mip)
        {
            switch (filter)
            {
                case SamplerFilter.Anisotropic:
                    min = mag = MTLSamplerMinMagFilter.Linear;
                    mip = MTLSamplerMipFilter.Linear;
                    break;

                case SamplerFilter.MinLinearMagLinearMipLinear:
                    min = MTLSamplerMinMagFilter.Linear;
                    mag = MTLSamplerMinMagFilter.Linear;
                    mip = MTLSamplerMipFilter.Linear;
                    break;

                case SamplerFilter.MinLinearMagLinearMipPoint:
                    min = MTLSamplerMinMagFilter.Linear;
                    mag = MTLSamplerMinMagFilter.Linear;
                    mip = MTLSamplerMipFilter.Nearest;
                    break;

                case SamplerFilter.MinLinearMagPointMipLinear:
                    min = MTLSamplerMinMagFilter.Linear;
                    mag = MTLSamplerMinMagFilter.Nearest;
                    mip = MTLSamplerMipFilter.Linear;
                    break;

                case SamplerFilter.MinLinearMagPointMipPoint:
                    min = MTLSamplerMinMagFilter.Linear;
                    mag = MTLSamplerMinMagFilter.Nearest;
                    mip = MTLSamplerMipFilter.Nearest;
                    break;

                case SamplerFilter.MinPointMagLinearMipLinear:
                    min = MTLSamplerMinMagFilter.Nearest;
                    mag = MTLSamplerMinMagFilter.Linear;
                    mip = MTLSamplerMipFilter.Linear;
                    break;

                case SamplerFilter.MinPointMagLinearMipPoint:
                    min = MTLSamplerMinMagFilter.Nearest;
                    mag = MTLSamplerMinMagFilter.Linear;
                    mip = MTLSamplerMipFilter.Nearest;
                    break;

                case SamplerFilter.MinPointMagPointMipLinear:
                    min = MTLSamplerMinMagFilter.Nearest;
                    mag = MTLSamplerMinMagFilter.Nearest;
                    mip = MTLSamplerMipFilter.Nearest;
                    break;

                case SamplerFilter.MinPointMagPointMipPoint:
                    min = MTLSamplerMinMagFilter.Nearest;
                    mag = MTLSamplerMinMagFilter.Nearest;
                    mip = MTLSamplerMipFilter.Nearest;
                    break;

                default:
                    throw Illegal.Value<SamplerFilter>();
            }
        }

        internal static MTLTextureType VdToMtlTextureType(
            TextureType type,
            uint arrayLayers,
            bool multiSampled,
            bool cube)
        {
            switch (type)
            {
                case TextureType.Texture1D:
                    return arrayLayers > 1 ? MTLTextureType.Type1DArray : MTLTextureType.Type1D;

                case TextureType.Texture2D:
                    if (cube)
                        return arrayLayers > 1 ? MTLTextureType.TypeCubeArray : MTLTextureType.TypeCube;
                    if (multiSampled)
                        return MTLTextureType.Type2DMultisample;

                    return arrayLayers > 1 ? MTLTextureType.Type2DArray : MTLTextureType.Type2D;

                case TextureType.Texture3D:
                    return MTLTextureType.Type3D;

                default:
                    throw Illegal.Value<TextureType>();
            }
        }

        internal static MTLBlendFactor VdToMtlBlendFactor(BlendFactor vdFactor)
        {
            return vdFactor switch
            {
                BlendFactor.Zero => MTLBlendFactor.Zero,
                BlendFactor.One => MTLBlendFactor.One,
                BlendFactor.SourceAlpha => MTLBlendFactor.SourceAlpha,
                BlendFactor.InverseSourceAlpha => MTLBlendFactor.OneMinusSourceAlpha,
                BlendFactor.DestinationAlpha => MTLBlendFactor.DestinationAlpha,
                BlendFactor.InverseDestinationAlpha => MTLBlendFactor.OneMinusDestinationAlpha,
                BlendFactor.SourceColor => MTLBlendFactor.SourceColor,
                BlendFactor.InverseSourceColor => MTLBlendFactor.OneMinusSourceColor,
                BlendFactor.DestinationColor => MTLBlendFactor.DestinationColor,
                BlendFactor.InverseDestinationColor => MTLBlendFactor.OneMinusDestinationColor,
                BlendFactor.BlendFactor => MTLBlendFactor.BlendColor,
                BlendFactor.InverseBlendFactor => MTLBlendFactor.OneMinusBlendColor,
                _ => throw Illegal.Value<BlendFactor>(),
            };
        }

        internal static MTLBlendOperation VdToMtlBlendOp(BlendFunction vdFunction)
        {
            return vdFunction switch
            {
                BlendFunction.Add => MTLBlendOperation.Add,
                BlendFunction.Maximum => MTLBlendOperation.Max,
                BlendFunction.Minimum => MTLBlendOperation.Min,
                BlendFunction.ReverseSubtract => MTLBlendOperation.ReverseSubtract,
                BlendFunction.Subtract => MTLBlendOperation.Subtract,
                _ => throw Illegal.Value<BlendFunction>(),
            };
        }

        internal static MTLColorWriteMask VdToMtlColorWriteMask(ColorWriteMask vdMask)
        {
            var mask = MTLColorWriteMask.None;

            if ((vdMask & ColorWriteMask.Red) == ColorWriteMask.Red)
                mask |= MTLColorWriteMask.Red;
            if ((vdMask & ColorWriteMask.Green) == ColorWriteMask.Green)
                mask |= MTLColorWriteMask.Green;
            if ((vdMask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
                mask |= MTLColorWriteMask.Blue;
            if ((vdMask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
                mask |= MTLColorWriteMask.Alpha;

            return mask;
        }

        internal static MTLDataType VdVoMtlShaderConstantType(ShaderConstantType type)
        {
            return type switch
            {
                ShaderConstantType.Bool => MTLDataType.Bool,
                ShaderConstantType.UInt16 => MTLDataType.UShort,
                ShaderConstantType.Int16 => MTLDataType.Short,
                ShaderConstantType.UInt32 => MTLDataType.UInt,
                ShaderConstantType.Int32 => MTLDataType.Int,
                ShaderConstantType.Float => MTLDataType.Float,
                ShaderConstantType.UInt64 or ShaderConstantType.Int64 or ShaderConstantType.Double => throw new VeldridException("Metal does not support 64-bit shader constants."),
                _ => throw Illegal.Value<ShaderConstantType>(),
            };
        }

        internal static MTLCompareFunction VdToMtlCompareFunction(ComparisonKind comparisonKind)
        {
            return comparisonKind switch
            {
                ComparisonKind.Always => MTLCompareFunction.Always,
                ComparisonKind.Equal => MTLCompareFunction.Equal,
                ComparisonKind.Greater => MTLCompareFunction.Greater,
                ComparisonKind.GreaterEqual => MTLCompareFunction.GreaterEqual,
                ComparisonKind.Less => MTLCompareFunction.Less,
                ComparisonKind.LessEqual => MTLCompareFunction.LessEqual,
                ComparisonKind.Never => MTLCompareFunction.Never,
                ComparisonKind.NotEqual => MTLCompareFunction.NotEqual,
                _ => throw Illegal.Value<ComparisonKind>(),
            };
        }

        internal static MTLCullMode VdToMtlCullMode(FaceCullMode cullMode)
        {
            return cullMode switch
            {
                FaceCullMode.Front => MTLCullMode.Front,
                FaceCullMode.Back => MTLCullMode.Back,
                FaceCullMode.None => MTLCullMode.None,
                _ => throw Illegal.Value<FaceCullMode>(),
            };
        }

        internal static MTLSamplerBorderColor VdToMtlBorderColor(SamplerBorderColor borderColor)
        {
            return borderColor switch
            {
                SamplerBorderColor.TransparentBlack => MTLSamplerBorderColor.TransparentBlack,
                SamplerBorderColor.OpaqueBlack => MTLSamplerBorderColor.OpaqueBlack,
                SamplerBorderColor.OpaqueWhite => MTLSamplerBorderColor.OpaqueWhite,
                _ => throw Illegal.Value<SamplerBorderColor>(),
            };
        }

        internal static MTLSamplerAddressMode VdToMtlAddressMode(SamplerAddressMode mode)
        {
            return mode switch
            {
                SamplerAddressMode.Border => MTLSamplerAddressMode.ClampToBorderColor,
                SamplerAddressMode.Clamp => MTLSamplerAddressMode.ClampToEdge,
                SamplerAddressMode.Mirror => MTLSamplerAddressMode.MirrorRepeat,
                SamplerAddressMode.Wrap => MTLSamplerAddressMode.Repeat,
                _ => throw Illegal.Value<SamplerAddressMode>(),
            };
        }

        internal static MTLPrimitiveType VdToMtlPrimitiveTopology(PrimitiveTopology primitiveTopology)
        {
            return primitiveTopology switch
            {
                PrimitiveTopology.LineList => MTLPrimitiveType.Line,
                PrimitiveTopology.LineStrip => MTLPrimitiveType.LineStrip,
                PrimitiveTopology.TriangleList => MTLPrimitiveType.Triangle,
                PrimitiveTopology.TriangleStrip => MTLPrimitiveType.TriangleStrip,
                PrimitiveTopology.PointList => MTLPrimitiveType.Point,
                _ => throw Illegal.Value<PrimitiveTopology>(),
            };
        }

        internal static MTLTextureUsage VdToMtlTextureUsage(TextureUsage usage)
        {
            var ret = MTLTextureUsage.Unknown;

            if ((usage & TextureUsage.Sampled) == TextureUsage.Sampled) ret |= MTLTextureUsage.ShaderRead;

            if ((usage & TextureUsage.Storage) == TextureUsage.Storage) ret |= MTLTextureUsage.ShaderWrite;

            if ((usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
                || (usage & TextureUsage.RenderTarget) == TextureUsage.RenderTarget)
                ret |= MTLTextureUsage.RenderTarget;

            return ret;
        }

        internal static MTLVertexFormat VdToMtlVertexFormat(VertexElementFormat format)
        {
            return format switch
            {
                VertexElementFormat.Byte2Norm => MTLVertexFormat.uchar2Normalized,
                VertexElementFormat.Byte2 => MTLVertexFormat.uchar2,
                VertexElementFormat.Byte4Norm => MTLVertexFormat.uchar4Normalized,
                VertexElementFormat.Byte4 => MTLVertexFormat.uchar4,
                VertexElementFormat.SByte2Norm => MTLVertexFormat.char2Normalized,
                VertexElementFormat.SByte2 => MTLVertexFormat.char2,
                VertexElementFormat.SByte4Norm => MTLVertexFormat.char4Normalized,
                VertexElementFormat.SByte4 => MTLVertexFormat.char4,
                VertexElementFormat.UShort2Norm => MTLVertexFormat.ushort2Normalized,
                VertexElementFormat.UShort2 => MTLVertexFormat.ushort2,
                VertexElementFormat.Short2Norm => MTLVertexFormat.short2Normalized,
                VertexElementFormat.Short2 => MTLVertexFormat.short2,
                VertexElementFormat.UShort4Norm => MTLVertexFormat.ushort4Normalized,
                VertexElementFormat.UShort4 => MTLVertexFormat.ushort4,
                VertexElementFormat.Short4Norm => MTLVertexFormat.short4Normalized,
                VertexElementFormat.Short4 => MTLVertexFormat.short4,
                VertexElementFormat.UInt1 => MTLVertexFormat.@uint,
                VertexElementFormat.UInt2 => MTLVertexFormat.uint2,
                VertexElementFormat.UInt3 => MTLVertexFormat.uint3,
                VertexElementFormat.UInt4 => MTLVertexFormat.uint4,
                VertexElementFormat.Int1 => MTLVertexFormat.@int,
                VertexElementFormat.Int2 => MTLVertexFormat.int2,
                VertexElementFormat.Int3 => MTLVertexFormat.int3,
                VertexElementFormat.Int4 => MTLVertexFormat.int4,
                VertexElementFormat.Float1 => MTLVertexFormat.@float,
                VertexElementFormat.Float2 => MTLVertexFormat.float2,
                VertexElementFormat.Float3 => MTLVertexFormat.float3,
                VertexElementFormat.Float4 => MTLVertexFormat.float4,
                VertexElementFormat.Half1 => MTLVertexFormat.half,
                VertexElementFormat.Half2 => MTLVertexFormat.half2,
                VertexElementFormat.Half4 => MTLVertexFormat.half4,
                _ => throw Illegal.Value<VertexElementFormat>(),
            };
        }

        internal static MTLIndexType VdToMtlIndexFormat(IndexFormat format)
        {
            return format == IndexFormat.UInt16 ? MTLIndexType.UInt16 : MTLIndexType.UInt32;
        }

        internal static MTLStencilOperation VdToMtlStencilOperation(StencilOperation op)
        {
            return op switch
            {
                StencilOperation.Keep => MTLStencilOperation.Keep,
                StencilOperation.Zero => MTLStencilOperation.Zero,
                StencilOperation.Replace => MTLStencilOperation.Replace,
                StencilOperation.IncrementAndClamp => MTLStencilOperation.IncrementClamp,
                StencilOperation.DecrementAndClamp => MTLStencilOperation.DecrementClamp,
                StencilOperation.Invert => MTLStencilOperation.Invert,
                StencilOperation.IncrementAndWrap => MTLStencilOperation.IncrementWrap,
                StencilOperation.DecrementAndWrap => MTLStencilOperation.DecrementWrap,
                _ => throw Illegal.Value<StencilOperation>(),
            };
        }

        internal static uint GetMaxTexture1DWidth(MTLFeatureSet fs)
        {
            return fs switch
            {
                MTLFeatureSet.iOS_GPUFamily1_v1 or MTLFeatureSet.iOS_GPUFamily2_v1 => 4096,
                MTLFeatureSet.iOS_GPUFamily1_v2 or MTLFeatureSet.iOS_GPUFamily2_v2 or MTLFeatureSet.iOS_GPUFamily1_v3 or MTLFeatureSet.iOS_GPUFamily2_v3 or MTLFeatureSet.iOS_GPUFamily1_v4 or MTLFeatureSet.iOS_GPUFamily2_v4 or MTLFeatureSet.tvOS_GPUFamily1_v1 or MTLFeatureSet.tvOS_GPUFamily1_v2 or MTLFeatureSet.tvOS_GPUFamily1_v3 => 8192,
                MTLFeatureSet.iOS_GPUFamily3_v1 or MTLFeatureSet.iOS_GPUFamily3_v2 or MTLFeatureSet.iOS_GPUFamily3_v3 or MTLFeatureSet.iOS_GPUFamily4_v1 or MTLFeatureSet.tvOS_GPUFamily2_v1 or MTLFeatureSet.macOS_GPUFamily1_v1 or MTLFeatureSet.macOS_GPUFamily1_v2 or MTLFeatureSet.macOS_GPUFamily1_v3 => 16384,
                _ => 4096,
            };
        }

        internal static uint GetMaxTexture2DDimensions(MTLFeatureSet fs)
        {
            return fs switch
            {
                MTLFeatureSet.iOS_GPUFamily1_v1 or MTLFeatureSet.iOS_GPUFamily2_v1 => 4096,
                MTLFeatureSet.iOS_GPUFamily1_v2 or MTLFeatureSet.iOS_GPUFamily2_v2 or MTLFeatureSet.iOS_GPUFamily1_v3 or MTLFeatureSet.iOS_GPUFamily2_v3 or MTLFeatureSet.iOS_GPUFamily1_v4 or MTLFeatureSet.iOS_GPUFamily2_v4 or MTLFeatureSet.tvOS_GPUFamily1_v1 or MTLFeatureSet.tvOS_GPUFamily1_v2 or MTLFeatureSet.tvOS_GPUFamily1_v3 => 8192,
                MTLFeatureSet.iOS_GPUFamily3_v1 or MTLFeatureSet.iOS_GPUFamily3_v2 or MTLFeatureSet.iOS_GPUFamily3_v3 or MTLFeatureSet.iOS_GPUFamily4_v1 or MTLFeatureSet.tvOS_GPUFamily2_v1 or MTLFeatureSet.macOS_GPUFamily1_v1 or MTLFeatureSet.macOS_GPUFamily1_v2 or MTLFeatureSet.macOS_GPUFamily1_v3 => 16384,
                _ => 4096,
            };
        }

        internal static uint GetMaxTextureCubeDimensions(MTLFeatureSet fs)
        {
            return fs switch
            {
                MTLFeatureSet.iOS_GPUFamily1_v1 or MTLFeatureSet.iOS_GPUFamily2_v1 => 4096,
                MTLFeatureSet.iOS_GPUFamily1_v2 or MTLFeatureSet.iOS_GPUFamily2_v2 or MTLFeatureSet.iOS_GPUFamily1_v3 or MTLFeatureSet.iOS_GPUFamily2_v3 or MTLFeatureSet.iOS_GPUFamily1_v4 or MTLFeatureSet.iOS_GPUFamily2_v4 or MTLFeatureSet.tvOS_GPUFamily1_v1 or MTLFeatureSet.tvOS_GPUFamily1_v2 or MTLFeatureSet.tvOS_GPUFamily1_v3 => 8192,
                MTLFeatureSet.iOS_GPUFamily3_v1 or MTLFeatureSet.iOS_GPUFamily3_v2 or MTLFeatureSet.iOS_GPUFamily3_v3 or MTLFeatureSet.iOS_GPUFamily4_v1 or MTLFeatureSet.tvOS_GPUFamily2_v1 or MTLFeatureSet.macOS_GPUFamily1_v1 or MTLFeatureSet.macOS_GPUFamily1_v2 or MTLFeatureSet.macOS_GPUFamily1_v3 => 16384,
                _ => 4096,
            };
        }

        internal static uint GetMaxTextureVolume(MTLFeatureSet fs)
        {
            return 2048;
        }
    }
}
