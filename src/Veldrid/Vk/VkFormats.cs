using Vulkan;

namespace Veldrid.Vk
{
    internal static partial class VkFormats
    {
        internal static VkSamplerAddressMode VdToVkSamplerAddressMode(SamplerAddressMode mode)
        {
            return mode switch
            {
                SamplerAddressMode.Wrap => VkSamplerAddressMode.Repeat,
                SamplerAddressMode.Mirror => VkSamplerAddressMode.MirroredRepeat,
                SamplerAddressMode.Clamp => VkSamplerAddressMode.ClampToEdge,
                SamplerAddressMode.Border => VkSamplerAddressMode.ClampToBorder,
                _ => throw Illegal.Value<SamplerAddressMode>(),
            };
        }

        internal static void GetFilterParams(
            SamplerFilter filter,
            out VkFilter minFilter,
            out VkFilter magFilter,
            out VkSamplerMipmapMode mipmapMode)
        {
            switch (filter)
            {
                case SamplerFilter.Anisotropic:
                    minFilter = VkFilter.Linear;
                    magFilter = VkFilter.Linear;
                    mipmapMode = VkSamplerMipmapMode.Linear;
                    break;

                case SamplerFilter.MinPointMagPointMipPoint:
                    minFilter = VkFilter.Nearest;
                    magFilter = VkFilter.Nearest;
                    mipmapMode = VkSamplerMipmapMode.Nearest;
                    break;

                case SamplerFilter.MinPointMagPointMipLinear:
                    minFilter = VkFilter.Nearest;
                    magFilter = VkFilter.Nearest;
                    mipmapMode = VkSamplerMipmapMode.Linear;
                    break;

                case SamplerFilter.MinPointMagLinearMipPoint:
                    minFilter = VkFilter.Nearest;
                    magFilter = VkFilter.Linear;
                    mipmapMode = VkSamplerMipmapMode.Nearest;
                    break;

                case SamplerFilter.MinPointMagLinearMipLinear:
                    minFilter = VkFilter.Nearest;
                    magFilter = VkFilter.Linear;
                    mipmapMode = VkSamplerMipmapMode.Linear;
                    break;

                case SamplerFilter.MinLinearMagPointMipPoint:
                    minFilter = VkFilter.Linear;
                    magFilter = VkFilter.Nearest;
                    mipmapMode = VkSamplerMipmapMode.Nearest;
                    break;

                case SamplerFilter.MinLinearMagPointMipLinear:
                    minFilter = VkFilter.Linear;
                    magFilter = VkFilter.Nearest;
                    mipmapMode = VkSamplerMipmapMode.Linear;
                    break;

                case SamplerFilter.MinLinearMagLinearMipPoint:
                    minFilter = VkFilter.Linear;
                    magFilter = VkFilter.Linear;
                    mipmapMode = VkSamplerMipmapMode.Nearest;
                    break;

                case SamplerFilter.MinLinearMagLinearMipLinear:
                    minFilter = VkFilter.Linear;
                    magFilter = VkFilter.Linear;
                    mipmapMode = VkSamplerMipmapMode.Linear;
                    break;

                default:
                    throw Illegal.Value<SamplerFilter>();
            }
        }

        internal static VkImageUsageFlags VdToVkTextureUsage(TextureUsage vdUsage)
        {
            // TextureUsage.Transient: the image is only ever used as a render-pass attachment —
            // never sampled, copied, mapped, or persisted across frames. Strip TransferSrc/Dst
            // and Sampled so the Vulkan driver can place the image in tile-only / lazily-allocated
            // memory on tile-based GPUs. Per the Vulkan spec, an image with TRANSIENT_ATTACHMENT_BIT
            // may only have *Attachment + InputAttachment usages.
            bool isTransient = (vdUsage & TextureUsage.Transient) == TextureUsage.Transient;

            var vkUsage = isTransient
                ? VkImageUsageFlags.TransientAttachment
                : (VkImageUsageFlags.TransferDst | VkImageUsageFlags.TransferSrc);
            bool isDepthStencil = (vdUsage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil;

            if (!isTransient && (vdUsage & TextureUsage.Sampled) == TextureUsage.Sampled)
                vkUsage |= VkImageUsageFlags.Sampled;

            if (isDepthStencil)
                vkUsage |= VkImageUsageFlags.DepthStencilAttachment;

            if ((vdUsage & TextureUsage.RenderTarget) == TextureUsage.RenderTarget)
                vkUsage |= VkImageUsageFlags.ColorAttachment;

            if (!isTransient && (vdUsage & TextureUsage.Storage) == TextureUsage.Storage)
                vkUsage |= VkImageUsageFlags.Storage;

            return vkUsage;
        }

        internal static VkImageType VdToVkTextureType(TextureType type)
        {
            return type switch
            {
                TextureType.Texture1D => VkImageType.Image1D,
                TextureType.Texture2D => VkImageType.Image2D,
                TextureType.Texture3D => VkImageType.Image3D,
                _ => throw Illegal.Value<TextureType>(),
            };
        }

        internal static VkDescriptorType VdToVkDescriptorType(ResourceKind kind, ResourceLayoutElementOptions options)
        {
            bool dynamicBinding = (options & ResourceLayoutElementOptions.DynamicBinding) != 0;

            return kind switch
            {
                ResourceKind.UniformBuffer => dynamicBinding ? VkDescriptorType.UniformBufferDynamic : VkDescriptorType.UniformBuffer,
                ResourceKind.StructuredBufferReadWrite or ResourceKind.StructuredBufferReadOnly => dynamicBinding ? VkDescriptorType.StorageBufferDynamic : VkDescriptorType.StorageBuffer,
                ResourceKind.TextureReadOnly => VkDescriptorType.SampledImage,
                ResourceKind.TextureReadWrite => VkDescriptorType.StorageImage,
                ResourceKind.Sampler => VkDescriptorType.Sampler,
                _ => throw Illegal.Value<ResourceKind>(),
            };
        }

        internal static VkSampleCountFlags VdToVkSampleCount(TextureSampleCount sampleCount)
        {
            return sampleCount switch
            {
                TextureSampleCount.Count1 => VkSampleCountFlags.Count1,
                TextureSampleCount.Count2 => VkSampleCountFlags.Count2,
                TextureSampleCount.Count4 => VkSampleCountFlags.Count4,
                TextureSampleCount.Count8 => VkSampleCountFlags.Count8,
                TextureSampleCount.Count16 => VkSampleCountFlags.Count16,
                TextureSampleCount.Count32 => VkSampleCountFlags.Count32,
                _ => throw Illegal.Value<TextureSampleCount>(),
            };
        }

        internal static VkStencilOp VdToVkStencilOp(StencilOperation op)
        {
            return op switch
            {
                StencilOperation.Keep => VkStencilOp.Keep,
                StencilOperation.Zero => VkStencilOp.Zero,
                StencilOperation.Replace => VkStencilOp.Replace,
                StencilOperation.IncrementAndClamp => VkStencilOp.IncrementAndClamp,
                StencilOperation.DecrementAndClamp => VkStencilOp.DecrementAndClamp,
                StencilOperation.Invert => VkStencilOp.Invert,
                StencilOperation.IncrementAndWrap => VkStencilOp.IncrementAndWrap,
                StencilOperation.DecrementAndWrap => VkStencilOp.DecrementAndWrap,
                _ => throw Illegal.Value<StencilOperation>(),
            };
        }

        internal static VkPolygonMode VdToVkPolygonMode(PolygonFillMode fillMode)
        {
            return fillMode switch
            {
                PolygonFillMode.Solid => VkPolygonMode.Fill,
                PolygonFillMode.Wireframe => VkPolygonMode.Line,
                _ => throw Illegal.Value<PolygonFillMode>(),
            };
        }

        internal static VkCullModeFlags VdToVkCullMode(FaceCullMode cullMode)
        {
            return cullMode switch
            {
                FaceCullMode.Back => VkCullModeFlags.Back,
                FaceCullMode.Front => VkCullModeFlags.Front,
                FaceCullMode.None => VkCullModeFlags.None,
                _ => throw Illegal.Value<FaceCullMode>(),
            };
        }

        internal static VkBlendOp VdToVkBlendOp(BlendFunction func)
        {
            return func switch
            {
                BlendFunction.Add => VkBlendOp.Add,
                BlendFunction.Subtract => VkBlendOp.Subtract,
                BlendFunction.ReverseSubtract => VkBlendOp.ReverseSubtract,
                BlendFunction.Minimum => VkBlendOp.Min,
                BlendFunction.Maximum => VkBlendOp.Max,
                _ => throw Illegal.Value<BlendFunction>(),
            };
        }

        internal static VkColorComponentFlags VdToVkColorWriteMask(ColorWriteMask mask)
        {
            var flags = VkColorComponentFlags.None;

            if ((mask & ColorWriteMask.Red) == ColorWriteMask.Red)
                flags |= VkColorComponentFlags.R;
            if ((mask & ColorWriteMask.Green) == ColorWriteMask.Green)
                flags |= VkColorComponentFlags.G;
            if ((mask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
                flags |= VkColorComponentFlags.B;
            if ((mask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
                flags |= VkColorComponentFlags.A;

            return flags;
        }

        internal static VkPrimitiveTopology VdToVkPrimitiveTopology(PrimitiveTopology topology)
        {
            return topology switch
            {
                PrimitiveTopology.TriangleList => VkPrimitiveTopology.TriangleList,
                PrimitiveTopology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
                PrimitiveTopology.LineList => VkPrimitiveTopology.LineList,
                PrimitiveTopology.LineStrip => VkPrimitiveTopology.LineStrip,
                PrimitiveTopology.PointList => VkPrimitiveTopology.PointList,
                _ => throw Illegal.Value<PrimitiveTopology>(),
            };
        }

        internal static uint GetSpecializationConstantSize(ShaderConstantType type)
        {
            return type switch
            {
                ShaderConstantType.Bool => 4,
                ShaderConstantType.UInt16 => 2,
                ShaderConstantType.Int16 => 2,
                ShaderConstantType.UInt32 => 4,
                ShaderConstantType.Int32 => 4,
                ShaderConstantType.UInt64 => 8,
                ShaderConstantType.Int64 => 8,
                ShaderConstantType.Float => 4,
                ShaderConstantType.Double => 8,
                _ => throw Illegal.Value<ShaderConstantType>(),
            };
        }

        internal static VkBlendFactor VdToVkBlendFactor(BlendFactor factor)
        {
            return factor switch
            {
                BlendFactor.Zero => VkBlendFactor.Zero,
                BlendFactor.One => VkBlendFactor.One,
                BlendFactor.SourceAlpha => VkBlendFactor.SrcAlpha,
                BlendFactor.InverseSourceAlpha => VkBlendFactor.OneMinusSrcAlpha,
                BlendFactor.DestinationAlpha => VkBlendFactor.DstAlpha,
                BlendFactor.InverseDestinationAlpha => VkBlendFactor.OneMinusDstAlpha,
                BlendFactor.SourceColor => VkBlendFactor.SrcColor,
                BlendFactor.InverseSourceColor => VkBlendFactor.OneMinusSrcColor,
                BlendFactor.DestinationColor => VkBlendFactor.DstColor,
                BlendFactor.InverseDestinationColor => VkBlendFactor.OneMinusDstColor,
                BlendFactor.BlendFactor => VkBlendFactor.ConstantColor,
                BlendFactor.InverseBlendFactor => VkBlendFactor.OneMinusConstantColor,
                _ => throw Illegal.Value<BlendFactor>(),
            };
        }

        internal static VkFormat VdToVkVertexElementFormat(VertexElementFormat format)
        {
            return format switch
            {
                VertexElementFormat.Float1 => VkFormat.R32Sfloat,
                VertexElementFormat.Float2 => VkFormat.R32g32Sfloat,
                VertexElementFormat.Float3 => VkFormat.R32g32b32Sfloat,
                VertexElementFormat.Float4 => VkFormat.R32g32b32a32Sfloat,
                VertexElementFormat.Byte2Norm => VkFormat.R8g8Unorm,
                VertexElementFormat.Byte2 => VkFormat.R8g8Uint,
                VertexElementFormat.Byte4Norm => VkFormat.R8g8b8a8Unorm,
                VertexElementFormat.Byte4 => VkFormat.R8g8b8a8Uint,
                VertexElementFormat.SByte2Norm => VkFormat.R8g8Snorm,
                VertexElementFormat.SByte2 => VkFormat.R8g8Sint,
                VertexElementFormat.SByte4Norm => VkFormat.R8g8b8a8Snorm,
                VertexElementFormat.SByte4 => VkFormat.R8g8b8a8Sint,
                VertexElementFormat.UShort2Norm => VkFormat.R16g16Unorm,
                VertexElementFormat.UShort2 => VkFormat.R16g16Uint,
                VertexElementFormat.UShort4Norm => VkFormat.R16g16b16a16Unorm,
                VertexElementFormat.UShort4 => VkFormat.R16g16b16a16Uint,
                VertexElementFormat.Short2Norm => VkFormat.R16g16Snorm,
                VertexElementFormat.Short2 => VkFormat.R16g16Sint,
                VertexElementFormat.Short4Norm => VkFormat.R16g16b16a16Snorm,
                VertexElementFormat.Short4 => VkFormat.R16g16b16a16Sint,
                VertexElementFormat.UInt1 => VkFormat.R32Uint,
                VertexElementFormat.UInt2 => VkFormat.R32g32Uint,
                VertexElementFormat.UInt3 => VkFormat.R32g32b32Uint,
                VertexElementFormat.UInt4 => VkFormat.R32g32b32a32Uint,
                VertexElementFormat.Int1 => VkFormat.R32Sint,
                VertexElementFormat.Int2 => VkFormat.R32g32Sint,
                VertexElementFormat.Int3 => VkFormat.R32g32b32Sint,
                VertexElementFormat.Int4 => VkFormat.R32g32b32a32Sint,
                VertexElementFormat.Half1 => VkFormat.R16Sfloat,
                VertexElementFormat.Half2 => VkFormat.R16g16Sfloat,
                VertexElementFormat.Half4 => VkFormat.R16g16b16a16Sfloat,
                _ => throw Illegal.Value<VertexElementFormat>(),
            };
        }

        internal static VkShaderStageFlags VdToVkShaderStages(ShaderStages stage)
        {
            var ret = VkShaderStageFlags.None;

            if ((stage & ShaderStages.Vertex) == ShaderStages.Vertex)
                ret |= VkShaderStageFlags.Vertex;

            if ((stage & ShaderStages.Geometry) == ShaderStages.Geometry)
                ret |= VkShaderStageFlags.Geometry;

            if ((stage & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
                ret |= VkShaderStageFlags.TessellationControl;

            if ((stage & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
                ret |= VkShaderStageFlags.TessellationEvaluation;

            if ((stage & ShaderStages.Fragment) == ShaderStages.Fragment)
                ret |= VkShaderStageFlags.Fragment;

            if ((stage & ShaderStages.Compute) == ShaderStages.Compute)
                ret |= VkShaderStageFlags.Compute;

            return ret;
        }

        internal static VkBorderColor VdToVkSamplerBorderColor(SamplerBorderColor borderColor)
        {
            return borderColor switch
            {
                SamplerBorderColor.TransparentBlack => VkBorderColor.FloatTransparentBlack,
                SamplerBorderColor.OpaqueBlack => VkBorderColor.FloatOpaqueBlack,
                SamplerBorderColor.OpaqueWhite => VkBorderColor.FloatOpaqueWhite,
                _ => throw Illegal.Value<SamplerBorderColor>(),
            };
        }

        internal static VkIndexType VdToVkIndexFormat(IndexFormat format)
        {
            return format switch
            {
                IndexFormat.UInt16 => VkIndexType.Uint16,
                IndexFormat.UInt32 => VkIndexType.Uint32,
                _ => throw Illegal.Value<IndexFormat>(),
            };
        }

        internal static VkCompareOp VdToVkCompareOp(ComparisonKind comparisonKind)
        {
            return comparisonKind switch
            {
                ComparisonKind.Never => VkCompareOp.Never,
                ComparisonKind.Less => VkCompareOp.Less,
                ComparisonKind.Equal => VkCompareOp.Equal,
                ComparisonKind.LessEqual => VkCompareOp.LessOrEqual,
                ComparisonKind.Greater => VkCompareOp.Greater,
                ComparisonKind.NotEqual => VkCompareOp.NotEqual,
                ComparisonKind.GreaterEqual => VkCompareOp.GreaterOrEqual,
                ComparisonKind.Always => VkCompareOp.Always,
                _ => throw Illegal.Value<ComparisonKind>(),
            };
        }

        internal static PixelFormat VkToVdPixelFormat(VkFormat vkFormat)
        {
            return vkFormat switch
            {
                VkFormat.R8Unorm => PixelFormat.R8UNorm,
                VkFormat.R8Snorm => PixelFormat.R8SNorm,
                VkFormat.R8Uint => PixelFormat.R8UInt,
                VkFormat.R8Sint => PixelFormat.R8SInt,
                VkFormat.R16Unorm => PixelFormat.R16UNorm,
                VkFormat.R16Snorm => PixelFormat.R16SNorm,
                VkFormat.R16Uint => PixelFormat.R16UInt,
                VkFormat.R16Sint => PixelFormat.R16SInt,
                VkFormat.R16Sfloat => PixelFormat.R16Float,
                VkFormat.R32Uint => PixelFormat.R32UInt,
                VkFormat.R32Sint => PixelFormat.R32SInt,
                VkFormat.R32Sfloat or VkFormat.D32Sfloat => PixelFormat.R32Float,
                VkFormat.R8g8Unorm => PixelFormat.R8G8UNorm,
                VkFormat.R8g8Snorm => PixelFormat.R8G8SNorm,
                VkFormat.R8g8Uint => PixelFormat.R8G8UInt,
                VkFormat.R8g8Sint => PixelFormat.R8G8SInt,
                VkFormat.R16g16Unorm => PixelFormat.R16G16UNorm,
                VkFormat.R16g16Snorm => PixelFormat.R16G16SNorm,
                VkFormat.R16g16Uint => PixelFormat.R16G16UInt,
                VkFormat.R16g16Sint => PixelFormat.R16G16SInt,
                VkFormat.R16g16Sfloat => PixelFormat.R16G16Float,
                VkFormat.R32g32Uint => PixelFormat.R32G32UInt,
                VkFormat.R32g32Sint => PixelFormat.R32G32SInt,
                VkFormat.R32g32Sfloat => PixelFormat.R32G32Float,
                VkFormat.R8g8b8a8Unorm => PixelFormat.R8G8B8A8UNorm,
                VkFormat.R8g8b8a8Srgb => PixelFormat.R8G8B8A8UNormSRgb,
                VkFormat.B8g8r8a8Unorm => PixelFormat.B8G8R8A8UNorm,
                VkFormat.B8g8r8a8Srgb => PixelFormat.B8G8R8A8UNormSRgb,
                VkFormat.R8g8b8a8Snorm => PixelFormat.R8G8B8A8SNorm,
                VkFormat.R8g8b8a8Uint => PixelFormat.R8G8B8A8UInt,
                VkFormat.R8g8b8a8Sint => PixelFormat.R8G8B8A8SInt,
                VkFormat.R16g16b16a16Unorm => PixelFormat.R16G16B16A16UNorm,
                VkFormat.R16g16b16a16Snorm => PixelFormat.R16G16B16A16SNorm,
                VkFormat.R16g16b16a16Uint => PixelFormat.R16G16B16A16UInt,
                VkFormat.R16g16b16a16Sint => PixelFormat.R16G16B16A16SInt,
                VkFormat.R16g16b16a16Sfloat => PixelFormat.R16G16B16A16Float,
                VkFormat.R32g32b32a32Uint => PixelFormat.R32G32B32A32UInt,
                VkFormat.R32g32b32a32Sint => PixelFormat.R32G32B32A32SInt,
                VkFormat.R32g32b32a32Sfloat => PixelFormat.R32G32B32A32Float,
                VkFormat.Bc1RgbUnormBlock => PixelFormat.Bc1RgbUNorm,
                VkFormat.Bc1RgbSrgbBlock => PixelFormat.Bc1RgbUNormSRgb,
                VkFormat.Bc1RgbaUnormBlock => PixelFormat.Bc1RgbaUNorm,
                VkFormat.Bc1RgbaSrgbBlock => PixelFormat.Bc1RgbaUNormSRgb,
                VkFormat.Bc2UnormBlock => PixelFormat.Bc2UNorm,
                VkFormat.Bc2SrgbBlock => PixelFormat.Bc2UNormSRgb,
                VkFormat.Bc3UnormBlock => PixelFormat.Bc3UNorm,
                VkFormat.Bc3SrgbBlock => PixelFormat.Bc3UNormSRgb,
                VkFormat.Bc4UnormBlock => PixelFormat.Bc4UNorm,
                VkFormat.Bc4SnormBlock => PixelFormat.Bc4SNorm,
                VkFormat.Bc5UnormBlock => PixelFormat.Bc5UNorm,
                VkFormat.Bc5SnormBlock => PixelFormat.Bc5SNorm,
                VkFormat.Bc7UnormBlock => PixelFormat.Bc7UNorm,
                VkFormat.Bc7SrgbBlock => PixelFormat.Bc7UNormSRgb,
                VkFormat.A2b10g10r10UnormPack32 => PixelFormat.R10G10B10A2UNorm,
                VkFormat.A2b10g10r10UintPack32 => PixelFormat.R10G10B10A2UInt,
                VkFormat.B10g11r11UfloatPack32 => PixelFormat.R11G11B10Float,
                _ => throw Illegal.Value<VkFormat>(),
            };
        }
    }
}
