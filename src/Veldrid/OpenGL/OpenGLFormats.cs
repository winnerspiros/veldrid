using System.Diagnostics;
using Veldrid.OpenGLBindings;

namespace Veldrid.OpenGL
{
    internal static class OpenGLFormats
    {
        internal static DrawElementsType VdToGLDrawElementsType(IndexFormat format)
        {
            return format switch
            {
                IndexFormat.UInt16 => DrawElementsType.UnsignedShort,
                IndexFormat.UInt32 => DrawElementsType.UnsignedInt,
                _ => throw Illegal.Value<IndexFormat>(),
            };
        }

        internal static ShaderType VdToGLShaderType(ShaderStages stage)
        {
            return stage switch
            {
                ShaderStages.Vertex => ShaderType.VertexShader,
                ShaderStages.Geometry => ShaderType.GeometryShader,
                ShaderStages.TessellationControl => ShaderType.TessControlShader,
                ShaderStages.TessellationEvaluation => ShaderType.TessEvaluationShader,
                ShaderStages.Fragment => ShaderType.FragmentShader,
                ShaderStages.Compute => ShaderType.ComputeShader,
                _ => throw Illegal.Value<ShaderStages>(),
            };
        }

        internal static PixelInternalFormat VdToGLPixelInternalFormat(PixelFormat format)
        {
            return format switch
            {
                PixelFormat.R8UNorm => PixelInternalFormat.R8,
                PixelFormat.R8SNorm => PixelInternalFormat.R8Snorm,
                PixelFormat.R8UInt => PixelInternalFormat.R8ui,
                PixelFormat.R8SInt => PixelInternalFormat.R8i,
                PixelFormat.R16UNorm => PixelInternalFormat.R16,
                PixelFormat.R16SNorm => PixelInternalFormat.R16Snorm,
                PixelFormat.R16UInt => PixelInternalFormat.R16ui,
                PixelFormat.R16SInt => PixelInternalFormat.R16i,
                PixelFormat.R16Float => PixelInternalFormat.R16f,
                PixelFormat.R32UInt => PixelInternalFormat.R32ui,
                PixelFormat.R32SInt => PixelInternalFormat.R32i,
                PixelFormat.R32Float => PixelInternalFormat.R32f,
                PixelFormat.R8G8UNorm => PixelInternalFormat.Rg8,
                PixelFormat.R8G8SNorm => PixelInternalFormat.Rg8Snorm,
                PixelFormat.R8G8UInt => PixelInternalFormat.Rg8ui,
                PixelFormat.R8G8SInt => PixelInternalFormat.Rg8i,
                PixelFormat.R16G16UNorm => PixelInternalFormat.Rg16,
                PixelFormat.R16G16SNorm => PixelInternalFormat.Rg16Snorm,
                PixelFormat.R16G16UInt => PixelInternalFormat.Rg16ui,
                PixelFormat.R16G16SInt => PixelInternalFormat.Rg16i,
                PixelFormat.R16G16Float => PixelInternalFormat.Rg16f,
                PixelFormat.R32G32UInt => PixelInternalFormat.Rg32ui,
                PixelFormat.R32G32SInt => PixelInternalFormat.Rg32i,
                PixelFormat.R32G32Float => PixelInternalFormat.Rg32f,
                PixelFormat.R8G8B8A8UNorm => PixelInternalFormat.Rgba8,
                PixelFormat.R8G8B8A8UNormSRgb => PixelInternalFormat.Srgb8Alpha8,
                PixelFormat.R8G8B8A8SNorm => PixelInternalFormat.Rgba8Snorm,
                PixelFormat.R8G8B8A8UInt => PixelInternalFormat.Rgba8ui,
                PixelFormat.R8G8B8A8SInt => PixelInternalFormat.Rgba8i,
                PixelFormat.R16G16B16A16UNorm => PixelInternalFormat.Rgba16,
                PixelFormat.R16G16B16A16SNorm => PixelInternalFormat.Rgba16Snorm,
                PixelFormat.R16G16B16A16UInt => PixelInternalFormat.Rgba16ui,
                PixelFormat.R16G16B16A16SInt => PixelInternalFormat.Rgba16i,
                PixelFormat.R16G16B16A16Float => PixelInternalFormat.Rgba16f,
                PixelFormat.R32G32B32A32Float => PixelInternalFormat.Rgba32f,
                PixelFormat.R32G32B32A32UInt => PixelInternalFormat.Rgba32ui,
                PixelFormat.R32G32B32A32SInt => PixelInternalFormat.Rgba32i,
                PixelFormat.B8G8R8A8UNorm => PixelInternalFormat.Rgba,
                PixelFormat.B8G8R8A8UNormSRgb => PixelInternalFormat.Srgb8Alpha8,
                PixelFormat.Bc1RgbUNorm => PixelInternalFormat.CompressedRgbS3tcDxt1Ext,
                PixelFormat.Bc1RgbUNormSRgb => PixelInternalFormat.CompressedSrgbS3tcDxt1Ext,
                PixelFormat.Bc1RgbaUNorm => PixelInternalFormat.CompressedRgbaS3tcDxt1Ext,
                PixelFormat.Bc1RgbaUNormSRgb => PixelInternalFormat.CompressedSrgbAlphaS3tcDxt1Ext,
                PixelFormat.Bc2UNorm => PixelInternalFormat.CompressedRgbaS3tcDxt3Ext,
                PixelFormat.Bc2UNormSRgb => PixelInternalFormat.CompressedSrgbAlphaS3tcDxt3Ext,
                PixelFormat.Bc3UNorm => PixelInternalFormat.CompressedRgbaS3tcDxt5Ext,
                PixelFormat.Bc3UNormSRgb => PixelInternalFormat.CompressedSrgbAlphaS3tcDxt5Ext,
                PixelFormat.Bc4UNorm => PixelInternalFormat.CompressedRedRgtc1,
                PixelFormat.Bc4SNorm => PixelInternalFormat.CompressedSignedRedRgtc1,
                PixelFormat.Bc5UNorm => PixelInternalFormat.CompressedRgRgtc2,
                PixelFormat.Bc5SNorm => PixelInternalFormat.CompressedSignedRgRgtc2,
                PixelFormat.Bc7UNorm => PixelInternalFormat.CompressedRgbaBptcUnorm,
                PixelFormat.Bc7UNormSRgb => PixelInternalFormat.CompressedSrgbAlphaBptcUnorm,
                PixelFormat.Etc2R8G8B8UNorm => PixelInternalFormat.CompressedRgb8Etc2,
                PixelFormat.Etc2R8G8B8A1UNorm => PixelInternalFormat.CompressedRgb8PunchthroughAlpha1Etc2,
                PixelFormat.Etc2R8G8B8A8UNorm => PixelInternalFormat.CompressedRgba8Etc2Eac,
                PixelFormat.D32FloatS8UInt => PixelInternalFormat.Depth32fStencil8,
                PixelFormat.D24UNormS8UInt => PixelInternalFormat.Depth24Stencil8,
                PixelFormat.R10G10B10A2UNorm => PixelInternalFormat.Rgb10A2,
                PixelFormat.R10G10B10A2UInt => PixelInternalFormat.Rgb10A2ui,
                PixelFormat.R11G11B10Float => PixelInternalFormat.R11fG11fB10f,
                _ => throw Illegal.Value<PixelFormat>(),
            };
        }

        internal static TextureWrapMode VdToGLTextureWrapMode(SamplerAddressMode mode)
        {
            return mode switch
            {
                SamplerAddressMode.Wrap => TextureWrapMode.Repeat,
                SamplerAddressMode.Mirror => TextureWrapMode.MirroredRepeat,
                SamplerAddressMode.Clamp => TextureWrapMode.ClampToEdge,
                SamplerAddressMode.Border => TextureWrapMode.ClampToBorder,
                _ => throw Illegal.Value<SamplerAddressMode>(),
            };
        }

        internal static GLPixelFormat VdToGLPixelFormat(PixelFormat format)
        {
            return format switch
            {
                PixelFormat.R8UNorm or PixelFormat.R16UNorm or PixelFormat.R16Float or PixelFormat.R32Float or PixelFormat.Bc4UNorm => GLPixelFormat.Red,
                PixelFormat.R8SNorm or PixelFormat.R8UInt or PixelFormat.R8SInt or PixelFormat.R16SNorm or PixelFormat.R16UInt or PixelFormat.R16SInt or PixelFormat.R32UInt or PixelFormat.R32SInt or PixelFormat.Bc4SNorm => GLPixelFormat.RedInteger,
                PixelFormat.R8G8UNorm or PixelFormat.R16G16UNorm or PixelFormat.R16G16Float or PixelFormat.R32G32Float or PixelFormat.Bc5UNorm => GLPixelFormat.Rg,
                PixelFormat.R8G8SNorm or PixelFormat.R8G8UInt or PixelFormat.R8G8SInt or PixelFormat.R16G16SNorm or PixelFormat.R16G16UInt or PixelFormat.R16G16SInt or PixelFormat.R32G32UInt or PixelFormat.R32G32SInt or PixelFormat.Bc5SNorm => GLPixelFormat.RgInteger,
                PixelFormat.R8G8B8A8UNorm or PixelFormat.R8G8B8A8UNormSRgb or PixelFormat.R16G16B16A16UNorm or PixelFormat.R16G16B16A16Float or PixelFormat.R32G32B32A32Float => GLPixelFormat.Rgba,
                PixelFormat.B8G8R8A8UNorm or PixelFormat.B8G8R8A8UNormSRgb => GLPixelFormat.Bgra,
                PixelFormat.R8G8B8A8SNorm or PixelFormat.R8G8B8A8UInt or PixelFormat.R8G8B8A8SInt or PixelFormat.R16G16B16A16SNorm or PixelFormat.R16G16B16A16UInt or PixelFormat.R16G16B16A16SInt or PixelFormat.R32G32B32A32UInt or PixelFormat.R32G32B32A32SInt => GLPixelFormat.RgbaInteger,
                PixelFormat.Bc1RgbUNorm or PixelFormat.Bc1RgbUNormSRgb or PixelFormat.Etc2R8G8B8UNorm => GLPixelFormat.Rgb,
                PixelFormat.Bc1RgbaUNorm or PixelFormat.Bc1RgbaUNormSRgb or PixelFormat.Bc2UNorm or PixelFormat.Bc2UNormSRgb or PixelFormat.Bc3UNorm or PixelFormat.Bc3UNormSRgb or PixelFormat.Bc7UNorm or PixelFormat.Bc7UNormSRgb or PixelFormat.Etc2R8G8B8A1UNorm or PixelFormat.Etc2R8G8B8A8UNorm => GLPixelFormat.Rgba,
                PixelFormat.D24UNormS8UInt => GLPixelFormat.DepthStencil,
                PixelFormat.D32FloatS8UInt => GLPixelFormat.DepthStencil,
                PixelFormat.R10G10B10A2UNorm => GLPixelFormat.Rgba,
                PixelFormat.R10G10B10A2UInt => GLPixelFormat.RgbaInteger,
                PixelFormat.R11G11B10Float => GLPixelFormat.Rgb,
                _ => throw Illegal.Value<PixelFormat>(),
            };
        }

        internal static GLPixelType VdToGLPixelType(PixelFormat format)
        {
            return format switch
            {
                PixelFormat.R8UNorm or PixelFormat.R8UInt or PixelFormat.R8G8UNorm or PixelFormat.R8G8UInt or PixelFormat.R8G8B8A8UNorm or PixelFormat.R8G8B8A8UNormSRgb or PixelFormat.R8G8B8A8UInt or PixelFormat.B8G8R8A8UNorm or PixelFormat.B8G8R8A8UNormSRgb => GLPixelType.UnsignedByte,
                PixelFormat.R8SNorm or PixelFormat.R8SInt or PixelFormat.R8G8SNorm or PixelFormat.R8G8SInt or PixelFormat.R8G8B8A8SNorm or PixelFormat.R8G8B8A8SInt or PixelFormat.Bc4SNorm or PixelFormat.Bc5SNorm => GLPixelType.Byte,
                PixelFormat.R16UNorm or PixelFormat.R16UInt or PixelFormat.R16G16UNorm or PixelFormat.R16G16UInt or PixelFormat.R16G16B16A16UNorm or PixelFormat.R16G16B16A16UInt => GLPixelType.UnsignedShort,
                PixelFormat.R16SNorm or PixelFormat.R16SInt or PixelFormat.R16G16SNorm or PixelFormat.R16G16SInt or PixelFormat.R16G16B16A16SNorm or PixelFormat.R16G16B16A16SInt => GLPixelType.Short,
                PixelFormat.R32UInt or PixelFormat.R32G32UInt or PixelFormat.R32G32B32A32UInt => GLPixelType.UnsignedInt,
                PixelFormat.R32SInt or PixelFormat.R32G32SInt or PixelFormat.R32G32B32A32SInt => GLPixelType.Int,
                PixelFormat.R16Float or PixelFormat.R16G16Float or PixelFormat.R16G16B16A16Float => GLPixelType.HalfFloat,
                PixelFormat.R32Float or PixelFormat.R32G32Float or PixelFormat.R32G32B32A32Float => GLPixelType.Float,
                PixelFormat.Bc1RgbUNorm or PixelFormat.Bc1RgbUNormSRgb or PixelFormat.Bc1RgbaUNorm or PixelFormat.Bc1RgbaUNormSRgb or PixelFormat.Bc2UNorm or PixelFormat.Bc2UNormSRgb or PixelFormat.Bc3UNorm or PixelFormat.Bc3UNormSRgb or PixelFormat.Bc4UNorm or PixelFormat.Bc5UNorm or PixelFormat.Bc7UNorm or PixelFormat.Bc7UNormSRgb or PixelFormat.Etc2R8G8B8UNorm or PixelFormat.Etc2R8G8B8A1UNorm or PixelFormat.Etc2R8G8B8A8UNorm => GLPixelType.UnsignedByte, // ?
                PixelFormat.D32FloatS8UInt => GLPixelType.Float32UnsignedInt248Rev,
                PixelFormat.D24UNormS8UInt => GLPixelType.UnsignedInt248,
                PixelFormat.R10G10B10A2UNorm or PixelFormat.R10G10B10A2UInt => GLPixelType.UnsignedInt1010102,
                PixelFormat.R11G11B10Float => GLPixelType.UnsignedInt10F11F11FRev,
                _ => throw Illegal.Value<PixelFormat>(),
            };
        }

        internal static SizedInternalFormat VdToGLSizedInternalFormat(PixelFormat format, bool depthFormat)
        {
            switch (format)
            {
                case PixelFormat.R8UNorm:
                    return SizedInternalFormat.R8;

                case PixelFormat.R8SNorm:
                    return SizedInternalFormat.R8i;

                case PixelFormat.R8UInt:
                    return SizedInternalFormat.R8ui;

                case PixelFormat.R8SInt:
                    return SizedInternalFormat.R8i;

                case PixelFormat.R16UNorm:
                    return depthFormat ? (SizedInternalFormat)PixelInternalFormat.DepthComponent16 : SizedInternalFormat.R16;

                case PixelFormat.R16SNorm:
                    return SizedInternalFormat.R16i;

                case PixelFormat.R16UInt:
                    return SizedInternalFormat.R16ui;

                case PixelFormat.R16SInt:
                    return SizedInternalFormat.R16i;

                case PixelFormat.R16Float:
                    return SizedInternalFormat.R16f;

                case PixelFormat.R32UInt:
                    return SizedInternalFormat.R32ui;

                case PixelFormat.R32SInt:
                    return SizedInternalFormat.R32i;

                case PixelFormat.R32Float:
                    return depthFormat ? (SizedInternalFormat)PixelInternalFormat.DepthComponent32f : SizedInternalFormat.R32f;

                case PixelFormat.R8G8UNorm:
                    return SizedInternalFormat.Rg8;

                case PixelFormat.R8G8SNorm:
                    return SizedInternalFormat.Rg8i;

                case PixelFormat.R8G8UInt:
                    return SizedInternalFormat.Rg8ui;

                case PixelFormat.R8G8SInt:
                    return SizedInternalFormat.Rg8i;

                case PixelFormat.R16G16UNorm:
                    return SizedInternalFormat.Rg16;

                case PixelFormat.R16G16SNorm:
                    return SizedInternalFormat.Rg16i;

                case PixelFormat.R16G16UInt:
                    return SizedInternalFormat.Rg16ui;

                case PixelFormat.R16G16SInt:
                    return SizedInternalFormat.Rg16i;

                case PixelFormat.R16G16Float:
                    return SizedInternalFormat.Rg16f;

                case PixelFormat.R32G32UInt:
                    return SizedInternalFormat.Rg32ui;

                case PixelFormat.R32G32SInt:
                    return SizedInternalFormat.Rg32i;

                case PixelFormat.R32G32Float:
                    return SizedInternalFormat.Rg32f;

                case PixelFormat.R8G8B8A8UNorm:
                    return SizedInternalFormat.Rgba8;

                case PixelFormat.R8G8B8A8UNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.Srgb8Alpha8;

                case PixelFormat.R8G8B8A8SNorm:
                    return SizedInternalFormat.Rgba8i;

                case PixelFormat.R8G8B8A8UInt:
                    return SizedInternalFormat.Rgba8ui;

                case PixelFormat.R8G8B8A8SInt:
                    return SizedInternalFormat.Rgba8i;

                case PixelFormat.B8G8R8A8UNorm:
                    return SizedInternalFormat.Rgba8;

                case PixelFormat.B8G8R8A8UNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.Srgb8Alpha8;

                case PixelFormat.R16G16B16A16UNorm:
                    return SizedInternalFormat.Rgba16;

                case PixelFormat.R16G16B16A16SNorm:
                    return SizedInternalFormat.Rgba16i;

                case PixelFormat.R16G16B16A16UInt:
                    return SizedInternalFormat.Rgba16ui;

                case PixelFormat.R16G16B16A16SInt:
                    return SizedInternalFormat.Rgba16i;

                case PixelFormat.R16G16B16A16Float:
                    return SizedInternalFormat.Rgba16f;

                case PixelFormat.R32G32B32A32UInt:
                    return SizedInternalFormat.Rgba32ui;

                case PixelFormat.R32G32B32A32SInt:
                    return SizedInternalFormat.Rgba32i;

                case PixelFormat.R32G32B32A32Float:
                    return SizedInternalFormat.Rgba32f;

                case PixelFormat.Bc1RgbUNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgbS3tcDxt1Ext;

                case PixelFormat.Bc1RgbUNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSrgbS3tcDxt1Ext;

                case PixelFormat.Bc1RgbaUNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgbaS3tcDxt1Ext;

                case PixelFormat.Bc1RgbaUNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSrgbAlphaS3tcDxt1Ext;

                case PixelFormat.Bc2UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgbaS3tcDxt3Ext;

                case PixelFormat.Bc2UNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSrgbAlphaS3tcDxt3Ext;

                case PixelFormat.Bc3UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;

                case PixelFormat.Bc3UNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSrgbAlphaS3tcDxt5Ext;

                case PixelFormat.Bc4UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRedRgtc1;

                case PixelFormat.Bc4SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSignedRedRgtc1;

                case PixelFormat.Bc5UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgRgtc2;

                case PixelFormat.Bc5SNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSignedRgRgtc2;

                case PixelFormat.Bc7UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgbaBptcUnorm;

                case PixelFormat.Bc7UNormSRgb:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedSrgbAlphaBptcUnorm;

                case PixelFormat.Etc2R8G8B8UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgb8Etc2;

                case PixelFormat.Etc2R8G8B8A1UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgb8PunchthroughAlpha1Etc2;

                case PixelFormat.Etc2R8G8B8A8UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.CompressedRgba8Etc2Eac;

                case PixelFormat.D32FloatS8UInt:
                    Debug.Assert(depthFormat);
                    return (SizedInternalFormat)PixelInternalFormat.Depth32fStencil8;

                case PixelFormat.D24UNormS8UInt:
                    Debug.Assert(depthFormat);
                    return (SizedInternalFormat)PixelInternalFormat.Depth24Stencil8;

                case PixelFormat.R10G10B10A2UNorm:
                    return (SizedInternalFormat)PixelInternalFormat.Rgb10A2;

                case PixelFormat.R10G10B10A2UInt:
                    return (SizedInternalFormat)PixelInternalFormat.Rgb10A2ui;

                case PixelFormat.R11G11B10Float:
                    return (SizedInternalFormat)PixelInternalFormat.R11fG11fB10f;

                default:
                    throw Illegal.Value<PixelFormat>();
            }
        }

        internal static void VdToGLTextureMinMagFilter(SamplerFilter filter, bool mip, out TextureMinFilter min, out TextureMagFilter mag)
        {
            switch (filter)
            {
                case SamplerFilter.Anisotropic:
                case SamplerFilter.MinPointMagPointMipPoint:
                    min = mip ? TextureMinFilter.NearestMipmapNearest : TextureMinFilter.Nearest;
                    mag = TextureMagFilter.Nearest;
                    break;

                case SamplerFilter.MinPointMagPointMipLinear:
                    min = mip ? TextureMinFilter.NearestMipmapLinear : TextureMinFilter.Nearest;
                    mag = TextureMagFilter.Nearest;
                    break;

                case SamplerFilter.MinPointMagLinearMipPoint:
                    min = mip ? TextureMinFilter.NearestMipmapNearest : TextureMinFilter.Nearest;
                    mag = TextureMagFilter.Linear;
                    break;

                case SamplerFilter.MinPointMagLinearMipLinear:
                    min = mip ? TextureMinFilter.NearestMipmapLinear : TextureMinFilter.Nearest;
                    mag = TextureMagFilter.Linear;
                    break;

                case SamplerFilter.MinLinearMagPointMipPoint:
                    min = mip ? TextureMinFilter.LinearMipmapNearest : TextureMinFilter.Linear;
                    mag = TextureMagFilter.Nearest;
                    break;

                case SamplerFilter.MinLinearMagPointMipLinear:
                    min = mip ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear;
                    mag = TextureMagFilter.Nearest;
                    break;

                case SamplerFilter.MinLinearMagLinearMipPoint:
                    min = mip ? TextureMinFilter.LinearMipmapNearest : TextureMinFilter.Linear;
                    mag = TextureMagFilter.Linear;
                    break;

                case SamplerFilter.MinLinearMagLinearMipLinear:
                    min = mip ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear;
                    mag = TextureMagFilter.Linear;
                    break;

                default:
                    throw Illegal.Value<SamplerFilter>();
            }
        }

        internal static BufferAccessMask VdToGLMapMode(MapMode mode)
        {
            return mode switch
            {
                MapMode.Read => BufferAccessMask.Read,
                MapMode.Write => BufferAccessMask.Write | BufferAccessMask.InvalidateBuffer,
                MapMode.ReadWrite => BufferAccessMask.Read | BufferAccessMask.Write,
                _ => throw Illegal.Value<MapMode>(),
            };
        }

        internal static VertexAttribPointerType VdToGLVertexAttribPointerType(
            VertexElementFormat format,
            out bool normalized,
            out bool isInteger)
        {
            switch (format)
            {
                case VertexElementFormat.Float1:
                case VertexElementFormat.Float2:
                case VertexElementFormat.Float3:
                case VertexElementFormat.Float4:
                    normalized = false;
                    isInteger = false;
                    return VertexAttribPointerType.Float;

                case VertexElementFormat.Half1:
                case VertexElementFormat.Half2:
                case VertexElementFormat.Half4:
                    normalized = false;
                    isInteger = false;
                    return VertexAttribPointerType.HalfFloat;

                case VertexElementFormat.Byte2Norm:
                case VertexElementFormat.Byte4Norm:
                    normalized = true;
                    isInteger = true;
                    return VertexAttribPointerType.UnsignedByte;

                case VertexElementFormat.Byte2:
                case VertexElementFormat.Byte4:
                    normalized = false;
                    isInteger = true;
                    return VertexAttribPointerType.UnsignedByte;

                case VertexElementFormat.SByte2Norm:
                case VertexElementFormat.SByte4Norm:
                    normalized = true;
                    isInteger = true;
                    return VertexAttribPointerType.Byte;

                case VertexElementFormat.SByte2:
                case VertexElementFormat.SByte4:
                    normalized = false;
                    isInteger = true;
                    return VertexAttribPointerType.Byte;

                case VertexElementFormat.UShort2Norm:
                case VertexElementFormat.UShort4Norm:
                    normalized = true;
                    isInteger = true;
                    return VertexAttribPointerType.UnsignedShort;

                case VertexElementFormat.UShort2:
                case VertexElementFormat.UShort4:
                    normalized = false;
                    isInteger = true;
                    return VertexAttribPointerType.UnsignedShort;

                case VertexElementFormat.Short2Norm:
                case VertexElementFormat.Short4Norm:
                    normalized = true;
                    isInteger = true;
                    return VertexAttribPointerType.Short;

                case VertexElementFormat.Short2:
                case VertexElementFormat.Short4:
                    normalized = false;
                    isInteger = true;
                    return VertexAttribPointerType.Short;

                case VertexElementFormat.UInt1:
                case VertexElementFormat.UInt2:
                case VertexElementFormat.UInt3:
                case VertexElementFormat.UInt4:
                    normalized = false;
                    isInteger = true;
                    return VertexAttribPointerType.UnsignedInt;

                case VertexElementFormat.Int1:
                case VertexElementFormat.Int2:
                case VertexElementFormat.Int3:
                case VertexElementFormat.Int4:
                    normalized = false;
                    isInteger = true;
                    return VertexAttribPointerType.Int;

                default:
                    throw Illegal.Value<VertexElementFormat>();
            }
        }

        internal static bool IsFormatSupported(OpenGLExtensions extensions, PixelFormat format, GraphicsBackend backend)
        {
            return format switch
            {
                PixelFormat.Etc2R8G8B8UNorm or PixelFormat.Etc2R8G8B8A1UNorm or PixelFormat.Etc2R8G8B8A8UNorm => extensions.GLESVersion(3, 0) || extensions.GLVersion(4, 3),
                PixelFormat.Bc1RgbUNorm or PixelFormat.Bc1RgbUNormSRgb or PixelFormat.Bc1RgbaUNorm or PixelFormat.Bc1RgbaUNormSRgb or PixelFormat.Bc2UNorm or PixelFormat.Bc2UNormSRgb or PixelFormat.Bc3UNorm or PixelFormat.Bc3UNormSRgb => extensions.IsExtensionSupported("GL_EXT_texture_compression_s3tc"),
                PixelFormat.Bc4UNorm or PixelFormat.Bc4SNorm or PixelFormat.Bc5UNorm or PixelFormat.Bc5SNorm => extensions.GLVersion(3, 0) || extensions.IsExtensionSupported("GL_ARB_texture_compression_rgtc"),
                PixelFormat.Bc7UNorm or PixelFormat.Bc7UNormSRgb => extensions.GLVersion(4, 2) || extensions.IsExtensionSupported("GL_ARB_texture_compression_bptc")
                                                                                                 || extensions.IsExtensionSupported("GL_EXT_texture_compression_bptc"),
                PixelFormat.B8G8R8A8UNorm or PixelFormat.B8G8R8A8UNormSRgb or PixelFormat.R10G10B10A2UInt or PixelFormat.R10G10B10A2UNorm => backend == GraphicsBackend.OpenGL,
                PixelFormat.D24UNormS8UInt => extensions.GLVersion(3, 0) || extensions.GLESVersion(3, 0)
                                                                          || extensions.IsExtensionSupported("GL_OES_packed_depth_stencil")
                                                                          || extensions.IsExtensionSupported("GL_ARB_framebuffer_object"),
                _ => true,
            };
        }

        internal static DepthFunction VdToGLDepthFunction(ComparisonKind value)
        {
            return value switch
            {
                ComparisonKind.Never => DepthFunction.Never,
                ComparisonKind.Less => DepthFunction.Less,
                ComparisonKind.Equal => DepthFunction.Equal,
                ComparisonKind.LessEqual => DepthFunction.Lequal,
                ComparisonKind.Greater => DepthFunction.Greater,
                ComparisonKind.NotEqual => DepthFunction.Notequal,
                ComparisonKind.GreaterEqual => DepthFunction.Gequal,
                ComparisonKind.Always => DepthFunction.Always,
                _ => throw Illegal.Value<ComparisonKind>(),
            };
        }

        internal static BlendingFactorSrc VdToGLBlendFactorSrc(BlendFactor factor)
        {
            return factor switch
            {
                BlendFactor.Zero => BlendingFactorSrc.Zero,
                BlendFactor.One => BlendingFactorSrc.One,
                BlendFactor.SourceAlpha => BlendingFactorSrc.SrcAlpha,
                BlendFactor.InverseSourceAlpha => BlendingFactorSrc.OneMinusSrcAlpha,
                BlendFactor.DestinationAlpha => BlendingFactorSrc.DstAlpha,
                BlendFactor.InverseDestinationAlpha => BlendingFactorSrc.OneMinusDstAlpha,
                BlendFactor.SourceColor => BlendingFactorSrc.SrcColor,
                BlendFactor.InverseSourceColor => BlendingFactorSrc.OneMinusSrcColor,
                BlendFactor.DestinationColor => BlendingFactorSrc.DstColor,
                BlendFactor.InverseDestinationColor => BlendingFactorSrc.OneMinusDstColor,
                BlendFactor.BlendFactor => BlendingFactorSrc.ConstantColor,
                BlendFactor.InverseBlendFactor => BlendingFactorSrc.OneMinusConstantColor,
                _ => throw Illegal.Value<BlendFactor>(),
            };
        }

        internal static BlendEquationMode VdToGLBlendEquationMode(BlendFunction function)
        {
            return function switch
            {
                BlendFunction.Add => BlendEquationMode.FuncAdd,
                BlendFunction.Subtract => BlendEquationMode.FuncSubtract,
                BlendFunction.ReverseSubtract => BlendEquationMode.FuncReverseSubtract,
                BlendFunction.Minimum => BlendEquationMode.Min,
                BlendFunction.Maximum => BlendEquationMode.Max,
                _ => throw Illegal.Value<BlendFunction>(),
            };
        }

        internal static PolygonMode VdToGLPolygonMode(PolygonFillMode fillMode)
        {
            return fillMode switch
            {
                PolygonFillMode.Solid => PolygonMode.Fill,
                PolygonFillMode.Wireframe => PolygonMode.Line,
                _ => throw Illegal.Value<PolygonFillMode>(),
            };
        }

        internal static StencilFunction VdToGLStencilFunction(ComparisonKind comparison)
        {
            return comparison switch
            {
                ComparisonKind.Never => StencilFunction.Never,
                ComparisonKind.Less => StencilFunction.Less,
                ComparisonKind.Equal => StencilFunction.Equal,
                ComparisonKind.LessEqual => StencilFunction.Lequal,
                ComparisonKind.Greater => StencilFunction.Greater,
                ComparisonKind.NotEqual => StencilFunction.Notequal,
                ComparisonKind.GreaterEqual => StencilFunction.Gequal,
                ComparisonKind.Always => StencilFunction.Always,
                _ => throw Illegal.Value<ComparisonKind>(),
            };
        }

        internal static StencilOp VdToGLStencilOp(StencilOperation op)
        {
            return op switch
            {
                StencilOperation.Keep => StencilOp.Keep,
                StencilOperation.Zero => StencilOp.Zero,
                StencilOperation.Replace => StencilOp.Replace,
                StencilOperation.IncrementAndClamp => StencilOp.Incr,
                StencilOperation.DecrementAndClamp => StencilOp.Decr,
                StencilOperation.Invert => StencilOp.Invert,
                StencilOperation.IncrementAndWrap => StencilOp.IncrWrap,
                StencilOperation.DecrementAndWrap => StencilOp.DecrWrap,
                _ => throw Illegal.Value<StencilOperation>(),
            };
        }

        internal static CullFaceMode VdToGLCullFaceMode(FaceCullMode cullMode)
        {
            return cullMode switch
            {
                FaceCullMode.Back => CullFaceMode.Back,
                FaceCullMode.Front => CullFaceMode.Front,
                _ => throw Illegal.Value<FaceCullMode>(),
            };
        }

        internal static PrimitiveType VdToGLPrimitiveType(PrimitiveTopology primitiveTopology)
        {
            return primitiveTopology switch
            {
                PrimitiveTopology.TriangleList => PrimitiveType.Triangles,
                PrimitiveTopology.TriangleStrip => PrimitiveType.TriangleStrip,
                PrimitiveTopology.LineList => PrimitiveType.Lines,
                PrimitiveTopology.LineStrip => PrimitiveType.LineStrip,
                PrimitiveTopology.PointList => PrimitiveType.Points,
                _ => throw Illegal.Value<PrimitiveTopology>(),
            };
        }

        internal static FrontFaceDirection VdToGLFrontFaceDirection(FrontFace frontFace)
        {
            return frontFace switch
            {
                FrontFace.Clockwise => FrontFaceDirection.Cw,
                FrontFace.CounterClockwise => FrontFaceDirection.Ccw,
                _ => throw Illegal.Value<FrontFace>(),
            };
        }

        internal static BlendingFactorDest VdToGLBlendFactorDest(BlendFactor factor)
        {
            return factor switch
            {
                BlendFactor.Zero => BlendingFactorDest.Zero,
                BlendFactor.One => BlendingFactorDest.One,
                BlendFactor.SourceAlpha => BlendingFactorDest.SrcAlpha,
                BlendFactor.InverseSourceAlpha => BlendingFactorDest.OneMinusSrcAlpha,
                BlendFactor.DestinationAlpha => BlendingFactorDest.DstAlpha,
                BlendFactor.InverseDestinationAlpha => BlendingFactorDest.OneMinusDstAlpha,
                BlendFactor.SourceColor => BlendingFactorDest.SrcColor,
                BlendFactor.InverseSourceColor => BlendingFactorDest.OneMinusSrcColor,
                BlendFactor.DestinationColor => BlendingFactorDest.DstColor,
                BlendFactor.InverseDestinationColor => BlendingFactorDest.OneMinusDstColor,
                BlendFactor.BlendFactor => BlendingFactorDest.ConstantColor,
                BlendFactor.InverseBlendFactor => BlendingFactorDest.OneMinusConstantColor,
                _ => throw Illegal.Value<BlendFactor>(),
            };
        }
    }
}
