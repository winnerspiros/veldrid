using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Pipeline : Pipeline
    {
        public ID3D12PipelineState PipelineState { get; }
        public ID3D12RootSignature RootSignature { get; }
        public new D3D12ResourceLayout[] ResourceLayouts { get; }
        public Vortice.Direct3D.PrimitiveTopology PrimitiveTopology { get; }
        public int[] VertexStrides { get; }
        public uint StencilReference { get; }
        public float[] BlendFactor { get; }

        public override bool IsComputePipeline { get; }
        public override bool IsDisposed => disposed;
        public override string Name { get; set; }

        private bool disposed;

        public D3D12Pipeline(ID3D12Device device, ref GraphicsPipelineDescription description)
            : base(ref description)
        {
            var genericLayouts = description.ResourceLayouts;
            ResourceLayouts = new D3D12ResourceLayout[genericLayouts.Length];

            for (int i = 0; i < ResourceLayouts.Length; i++)
                ResourceLayouts[i] = Util.AssertSubtype<ResourceLayout, D3D12ResourceLayout>(genericLayouts[i]);

            RootSignature = createRootSignature(device, ResourceLayouts);
            StencilReference = description.DepthStencilState.StencilReference;
            PrimitiveTopology = D3D12Formats.VdToD3D12PrimitiveTopology(description.PrimitiveTopology);

            var bf = description.BlendState.BlendFactor;
            BlendFactor = new[] { bf.R, bf.G, bf.B, bf.A };

            // Build shader bytecodes.
            byte[] vsBytecode = null;
            byte[] psBytecode = null;
            byte[] gsBytecode = null;
            byte[] hsBytecode = null;
            byte[] dsBytecode = null;
            var stages = description.ShaderSet.Shaders;

            for (int i = 0; i < stages.Length; i++)
            {
                var d3d12Shader = Util.AssertSubtype<Shader, D3D12Shader>(stages[i]);

                switch (stages[i].Stage)
                {
                    case ShaderStages.Vertex:
                        vsBytecode = d3d12Shader.Bytecode;
                        break;

                    case ShaderStages.Fragment:
                        psBytecode = d3d12Shader.Bytecode;
                        break;

                    case ShaderStages.Geometry:
                        gsBytecode = d3d12Shader.Bytecode;
                        break;

                    case ShaderStages.TessellationControl:
                        hsBytecode = d3d12Shader.Bytecode;
                        break;

                    case ShaderStages.TessellationEvaluation:
                        dsBytecode = d3d12Shader.Bytecode;
                        break;
                }
            }

            // Build input layout.
            var inputElements = createInputLayout(description.ShaderSet.VertexLayouts);

            // Build vertex strides.
            if (description.ShaderSet.VertexLayouts.Length > 0)
            {
                VertexStrides = new int[description.ShaderSet.VertexLayouts.Length];

                for (int i = 0; i < VertexStrides.Length; i++)
                    VertexStrides[i] = (int)description.ShaderSet.VertexLayouts[i].Stride;
            }
            else
            {
                VertexStrides = Array.Empty<int>();
            }

            // Build blend state.
            var blendDesc = new BlendDescription
            {
                AlphaToCoverageEnable = description.BlendState.AlphaToCoverageEnabled,
                IndependentBlendEnable = true
            };

            for (int i = 0; i < description.BlendState.AttachmentStates.Length && i < 8; i++)
            {
                var attachment = description.BlendState.AttachmentStates[i];
                blendDesc.RenderTarget[i] = new RenderTargetBlendDescription
                {
                    BlendEnable = attachment.BlendEnabled,
                    SourceBlend = D3D12Formats.VdToD3D12BlendFactor(attachment.SourceColorFactor),
                    DestinationBlend = D3D12Formats.VdToD3D12BlendFactor(attachment.DestinationColorFactor),
                    BlendOperation = D3D12Formats.VdToD3D12BlendOperation(attachment.ColorFunction),
                    SourceBlendAlpha = D3D12Formats.VdToD3D12BlendFactor(attachment.SourceAlphaFactor),
                    DestinationBlendAlpha = D3D12Formats.VdToD3D12BlendFactor(attachment.DestinationAlphaFactor),
                    BlendOperationAlpha = D3D12Formats.VdToD3D12BlendOperation(attachment.AlphaFunction),
                    RenderTargetWriteMask = toD3D12ColorWriteMask(attachment.ColorWriteMask ?? Veldrid.ColorWriteMask.All)
                };
            }

            // Build rasterizer state.
            var rasterizerDesc = new RasterizerDescription
            {
                FillMode = D3D12Formats.VdToD3D12FillMode(description.RasterizerState.FillMode),
                CullMode = D3D12Formats.VdToD3D12CullMode(description.RasterizerState.CullMode),
                FrontCounterClockwise = description.RasterizerState.FrontFace == FrontFace.CounterClockwise,
                DepthClipEnable = description.RasterizerState.DepthClipEnabled,
                MultisampleEnable = description.Outputs.SampleCount != TextureSampleCount.Count1
            };

            // Build depth stencil state.
            var dsDesc = description.DepthStencilState;
            var depthStencilDesc = new DepthStencilDescription
            {
                DepthEnable = dsDesc.DepthTestEnabled,
                DepthWriteMask = dsDesc.DepthWriteEnabled ? DepthWriteMask.All : DepthWriteMask.Zero,
                DepthFunc = D3D12Formats.VdToD3D12ComparisonFunc(dsDesc.DepthComparison),
                StencilEnable = dsDesc.StencilTestEnabled,
                StencilReadMask = dsDesc.StencilReadMask,
                StencilWriteMask = dsDesc.StencilWriteMask,
                FrontFace = new DepthStencilOperationDescription
                {
                    StencilFailOp = D3D12Formats.VdToD3D12StencilOp(dsDesc.StencilFront.Fail),
                    StencilDepthFailOp = D3D12Formats.VdToD3D12StencilOp(dsDesc.StencilFront.DepthFail),
                    StencilPassOp = D3D12Formats.VdToD3D12StencilOp(dsDesc.StencilFront.Pass),
                    StencilFunc = D3D12Formats.VdToD3D12ComparisonFunc(dsDesc.StencilFront.Comparison)
                },
                BackFace = new DepthStencilOperationDescription
                {
                    StencilFailOp = D3D12Formats.VdToD3D12StencilOp(dsDesc.StencilBack.Fail),
                    StencilDepthFailOp = D3D12Formats.VdToD3D12StencilOp(dsDesc.StencilBack.DepthFail),
                    StencilPassOp = D3D12Formats.VdToD3D12StencilOp(dsDesc.StencilBack.Pass),
                    StencilFunc = D3D12Formats.VdToD3D12ComparisonFunc(dsDesc.StencilBack.Comparison)
                }
            };

            // Build render target formats.
            var rtvFormats = new Format[description.Outputs.ColorAttachments.Length];

            for (int i = 0; i < rtvFormats.Length; i++)
                rtvFormats[i] = D3D12Formats.ToDxgiFormat(description.Outputs.ColorAttachments[i].Format, false);

            var dsvFormat = description.Outputs.DepthAttachment != null
                ? D3D12Formats.ToDxgiFormat(description.Outputs.DepthAttachment.Value.Format, true)
                : Format.Unknown;

            var psoDesc = new GraphicsPipelineStateDescription
            {
                RootSignature = RootSignature,
                VertexShader = vsBytecode,
                PixelShader = psBytecode,
                GeometryShader = gsBytecode,
                HullShader = hsBytecode,
                DomainShader = dsBytecode,
                BlendState = blendDesc,
                RasterizerState = rasterizerDesc,
                DepthStencilState = depthStencilDesc,
                InputLayout = new InputLayoutDescription(inputElements),
                PrimitiveTopologyType = D3D12Formats.VdToD3D12PrimitiveTopologyType(description.PrimitiveTopology),
                SampleMask = uint.MaxValue,
                SampleDescription = new SampleDescription(D3D12Formats.ToDxgiSampleCount(description.Outputs.SampleCount), 0),
                DepthStencilFormat = dsvFormat
            };

            for (int i = 0; i < rtvFormats.Length && i < 8; i++)
                psoDesc.RenderTargetFormats[i] = rtvFormats[i];

            PipelineState = device.CreateGraphicsPipelineState(psoDesc);
        }

        public D3D12Pipeline(ID3D12Device device, ref ComputePipelineDescription description)
            : base(ref description)
        {
            IsComputePipeline = true;

            var genericLayouts = description.ResourceLayouts;
            ResourceLayouts = new D3D12ResourceLayout[genericLayouts.Length];

            for (int i = 0; i < ResourceLayouts.Length; i++)
                ResourceLayouts[i] = Util.AssertSubtype<ResourceLayout, D3D12ResourceLayout>(genericLayouts[i]);

            RootSignature = createRootSignature(device, ResourceLayouts);

            var d3d12Shader = Util.AssertSubtype<Shader, D3D12Shader>(description.ComputeShader);

            var psoDesc = new ComputePipelineStateDescription
            {
                RootSignature = RootSignature,
                ComputeShader = d3d12Shader.Bytecode
            };

            PipelineState = device.CreateComputePipelineState(psoDesc);
            VertexStrides = Array.Empty<int>();
            BlendFactor = new float[] { 0, 0, 0, 0 };
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                PipelineState.Dispose();
                RootSignature.Dispose();
                disposed = true;
            }
        }

        #endregion

        private static ID3D12RootSignature createRootSignature(ID3D12Device device, D3D12ResourceLayout[] resourceLayouts)
        {
            var rootParameters = new List<RootParameter1>();

            for (int setIndex = 0; setIndex < resourceLayouts.Length; setIndex++)
            {
                var layout = resourceLayouts[setIndex];
                var elements = layout.Elements;

                var cbvRanges = new List<DescriptorRange1>();
                var srvRanges = new List<DescriptorRange1>();
                var uavRanges = new List<DescriptorRange1>();
                var samplerRanges = new List<DescriptorRange1>();

                int cbvCount = 0;
                int srvCount = 0;
                int uavCount = 0;
                int samplerCount = 0;

                for (int i = 0; i < elements.Length; i++)
                {
                    switch (elements[i].Kind)
                    {
                        case ResourceKind.UniformBuffer:
                            cbvRanges.Add(new DescriptorRange1(
                                DescriptorRangeType.ConstantBufferView, 1, cbvCount, setIndex));
                            cbvCount++;
                            break;

                        case ResourceKind.StructuredBufferReadOnly:
                        case ResourceKind.TextureReadOnly:
                            srvRanges.Add(new DescriptorRange1(
                                DescriptorRangeType.ShaderResourceView, 1, srvCount, setIndex));
                            srvCount++;
                            break;

                        case ResourceKind.StructuredBufferReadWrite:
                        case ResourceKind.TextureReadWrite:
                            uavRanges.Add(new DescriptorRange1(
                                DescriptorRangeType.UnorderedAccessView, 1, uavCount, setIndex));
                            uavCount++;
                            break;

                        case ResourceKind.Sampler:
                            samplerRanges.Add(new DescriptorRange1(
                                DescriptorRangeType.Sampler, 1, samplerCount, setIndex));
                            samplerCount++;
                            break;
                    }
                }

                // Create a descriptor table for CBV/SRV/UAV ranges.
                var cbvSrvUavRanges = new List<DescriptorRange1>();
                cbvSrvUavRanges.AddRange(cbvRanges);
                cbvSrvUavRanges.AddRange(srvRanges);
                cbvSrvUavRanges.AddRange(uavRanges);

                if (cbvSrvUavRanges.Count > 0)
                {
                    rootParameters.Add(new RootParameter1(
                        new RootDescriptorTable1(cbvSrvUavRanges.ToArray()),
                        ShaderVisibility.All));
                }

                // Create a separate descriptor table for sampler ranges.
                if (samplerRanges.Count > 0)
                {
                    rootParameters.Add(new RootParameter1(
                        new RootDescriptorTable1(samplerRanges.ToArray()),
                        ShaderVisibility.All));
                }
            }

            var rootSigDesc = new RootSignatureDescription1(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                rootParameters.ToArray());

            return device.CreateRootSignature(rootSigDesc);
        }

        private static InputElementDescription[] createInputLayout(VertexLayoutDescription[] vertexLayouts)
        {
            var elements = new List<InputElementDescription>();
            var semanticIndices = new SemanticIndices();

            for (int layoutIndex = 0; layoutIndex < vertexLayouts.Length; layoutIndex++)
            {
                var layout = vertexLayouts[layoutIndex];

                for (int elementIndex = 0; elementIndex < layout.Elements.Length; elementIndex++)
                {
                    var desc = layout.Elements[elementIndex];
                    elements.Add(new InputElementDescription(
                        getSemanticString(desc.Semantic),
                        SemanticIndices.GetAndIncrement(ref semanticIndices, desc.Semantic),
                        vertexElementToDxgiFormat(desc.Format),
                        (int)desc.Offset,
                        layoutIndex,
                        layout.InstanceStepRate == 0
                            ? InputClassification.PerVertexData
                            : InputClassification.PerInstanceData,
                        (int)layout.InstanceStepRate));
                }
            }

            return elements.ToArray();
        }

        private static string getSemanticString(VertexElementSemantic semantic)
        {
            switch (semantic)
            {
                case VertexElementSemantic.Position:
                    return "POSITION";

                case VertexElementSemantic.Normal:
                    return "NORMAL";

                case VertexElementSemantic.TextureCoordinate:
                    return "TEXCOORD";

                case VertexElementSemantic.Color:
                    return "COLOR";

                default:
                    throw Illegal.Value<VertexElementSemantic>();
            }
        }

        private static Format vertexElementToDxgiFormat(VertexElementFormat format)
        {
            switch (format)
            {
                case VertexElementFormat.Float1:
                    return Format.R32_Float;

                case VertexElementFormat.Float2:
                    return Format.R32G32_Float;

                case VertexElementFormat.Float3:
                    return Format.R32G32B32_Float;

                case VertexElementFormat.Float4:
                    return Format.R32G32B32A32_Float;

                case VertexElementFormat.Byte2Norm:
                    return Format.R8G8_UNorm;

                case VertexElementFormat.Byte2:
                    return Format.R8G8_UInt;

                case VertexElementFormat.Byte4Norm:
                    return Format.R8G8B8A8_UNorm;

                case VertexElementFormat.Byte4:
                    return Format.R8G8B8A8_UInt;

                case VertexElementFormat.SByte2Norm:
                    return Format.R8G8_SNorm;

                case VertexElementFormat.SByte2:
                    return Format.R8G8_SInt;

                case VertexElementFormat.SByte4Norm:
                    return Format.R8G8B8A8_SNorm;

                case VertexElementFormat.SByte4:
                    return Format.R8G8B8A8_SInt;

                case VertexElementFormat.UShort2Norm:
                    return Format.R16G16_UNorm;

                case VertexElementFormat.UShort2:
                    return Format.R16G16_UInt;

                case VertexElementFormat.UShort4Norm:
                    return Format.R16G16B16A16_UNorm;

                case VertexElementFormat.UShort4:
                    return Format.R16G16B16A16_UInt;

                case VertexElementFormat.Short2Norm:
                    return Format.R16G16_SNorm;

                case VertexElementFormat.Short2:
                    return Format.R16G16_SInt;

                case VertexElementFormat.Short4Norm:
                    return Format.R16G16B16A16_SNorm;

                case VertexElementFormat.Short4:
                    return Format.R16G16B16A16_SInt;

                case VertexElementFormat.UInt1:
                    return Format.R32_UInt;

                case VertexElementFormat.UInt2:
                    return Format.R32G32_UInt;

                case VertexElementFormat.UInt3:
                    return Format.R32G32B32_UInt;

                case VertexElementFormat.UInt4:
                    return Format.R32G32B32A32_UInt;

                case VertexElementFormat.Int1:
                    return Format.R32_SInt;

                case VertexElementFormat.Int2:
                    return Format.R32G32_SInt;

                case VertexElementFormat.Int3:
                    return Format.R32G32B32_SInt;

                case VertexElementFormat.Int4:
                    return Format.R32G32B32A32_SInt;

                case VertexElementFormat.Half1:
                    return Format.R16_Float;

                case VertexElementFormat.Half2:
                    return Format.R16G16_Float;

                case VertexElementFormat.Half4:
                    return Format.R16G16B16A16_Float;

                default:
                    throw Illegal.Value<VertexElementFormat>();
            }
        }

        private static ColorWriteEnable toD3D12ColorWriteMask(Veldrid.ColorWriteMask mask)
        {
            var result = ColorWriteEnable.None;

            if ((mask & Veldrid.ColorWriteMask.Red) != 0)
                result |= ColorWriteEnable.Red;

            if ((mask & Veldrid.ColorWriteMask.Green) != 0)
                result |= ColorWriteEnable.Green;

            if ((mask & Veldrid.ColorWriteMask.Blue) != 0)
                result |= ColorWriteEnable.Blue;

            if ((mask & Veldrid.ColorWriteMask.Alpha) != 0)
                result |= ColorWriteEnable.Alpha;

            return result;
        }

        private struct SemanticIndices
        {
            private int position;
            private int texCoord;
            private int normal;
            private int color;

            public static int GetAndIncrement(ref SemanticIndices si, VertexElementSemantic type)
            {
                switch (type)
                {
                    case VertexElementSemantic.Position:
                        return si.position++;

                    case VertexElementSemantic.TextureCoordinate:
                        return si.texCoord++;

                    case VertexElementSemantic.Normal:
                        return si.normal++;

                    case VertexElementSemantic.Color:
                        return si.color++;

                    default:
                        throw Illegal.Value<VertexElementSemantic>();
                }
            }
        }
    }
}
