using System;
using System.Runtime.CompilerServices;
using System.Text;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkPipeline : Pipeline
    {
        public Vortice.Vulkan.VkPipeline DevicePipeline => devicePipeline;

        public VkPipelineLayout PipelineLayout => pipelineLayout;

        public uint ResourceSetCount { get; }
        public uint DynamicOffsetsCount { get; }
        public bool ScissorTestEnabled { get; }

        public override bool IsComputePipeline { get; }

        public ResourceRefCount RefCount { get; }

        public override bool IsDisposed => destroyed;

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                gd.SetResourceName(this, value);
            }
        }

        private readonly VkGraphicsDevice gd;
        private readonly Vortice.Vulkan.VkPipeline devicePipeline;
        private readonly VkPipelineLayout pipelineLayout;
        private readonly VkRenderPass renderPass;
        private bool destroyed;
        private string name;

        public VkPipeline(VkGraphicsDevice gd, ref GraphicsPipelineDescription description)
            : base(ref description)
        {
            this.gd = gd;
            IsComputePipeline = false;
            RefCount = new ResourceRefCount(disposeCore);

            var pipelineCi = new VkGraphicsPipelineCreateInfo();

            // Blend State
            var blendStateCi = new VkPipelineColorBlendStateCreateInfo();
            int attachmentsCount = description.BlendState.AttachmentStates.Length;
            var attachmentsPtr
                = stackalloc VkPipelineColorBlendAttachmentState[attachmentsCount];

            for (int i = 0; i < attachmentsCount; i++)
            {
                var vdDesc = description.BlendState.AttachmentStates[i];
                var attachmentState = new VkPipelineColorBlendAttachmentState
                {
                    srcColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceColorFactor),
                    dstColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationColorFactor),
                    colorBlendOp = VkFormats.VdToVkBlendOp(vdDesc.ColorFunction),
                    srcAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceAlphaFactor),
                    dstAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationAlphaFactor),
                    alphaBlendOp = VkFormats.VdToVkBlendOp(vdDesc.AlphaFunction),
                    colorWriteMask = VkFormats.VdToVkColorWriteMask(vdDesc.ColorWriteMask.GetOrDefault()),
                    blendEnable = vdDesc.BlendEnabled
                };
                attachmentsPtr[i] = attachmentState;
            }

            blendStateCi.attachmentCount = (uint)attachmentsCount;
            blendStateCi.pAttachments = attachmentsPtr;
            var blendFactor = description.BlendState.BlendFactor;
            blendStateCi.blendConstants[0] = blendFactor.R;
            blendStateCi.blendConstants[1] = blendFactor.G;
            blendStateCi.blendConstants[2] = blendFactor.B;
            blendStateCi.blendConstants[3] = blendFactor.A;

            pipelineCi.pColorBlendState = &blendStateCi;

            // Rasterizer State
            var rsDesc = description.RasterizerState;
            var rsCi = new VkPipelineRasterizationStateCreateInfo();
            rsCi.cullMode = VkFormats.VdToVkCullMode(rsDesc.CullMode);
            rsCi.polygonMode = VkFormats.VdToVkPolygonMode(rsDesc.FillMode);
            rsCi.depthClampEnable = !rsDesc.DepthClipEnabled;
            rsCi.frontFace = rsDesc.FrontFace == FrontFace.Clockwise ? VkFrontFace.Clockwise : VkFrontFace.CounterClockwise;
            rsCi.lineWidth = 1f;

            pipelineCi.pRasterizationState = &rsCi;

            ScissorTestEnabled = rsDesc.ScissorTestEnabled;

            // Dynamic State
            var dynamicStateCi = new VkPipelineDynamicStateCreateInfo();
            var dynamicStates = stackalloc VkDynamicState[2];
            dynamicStates[0] = VkDynamicState.Viewport;
            dynamicStates[1] = VkDynamicState.Scissor;
            dynamicStateCi.dynamicStateCount = 2;
            dynamicStateCi.pDynamicStates = dynamicStates;

            pipelineCi.pDynamicState = &dynamicStateCi;

            // Depth Stencil State
            var vdDssDesc = description.DepthStencilState;
            var dssCi = new VkPipelineDepthStencilStateCreateInfo();
            dssCi.depthWriteEnable = vdDssDesc.DepthWriteEnabled;
            dssCi.depthTestEnable = vdDssDesc.DepthTestEnabled;
            dssCi.depthCompareOp = VkFormats.VdToVkCompareOp(vdDssDesc.DepthComparison);
            dssCi.stencilTestEnable = vdDssDesc.StencilTestEnabled;

            dssCi.front.failOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Fail);
            dssCi.front.passOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Pass);
            dssCi.front.depthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.DepthFail);
            dssCi.front.compareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilFront.Comparison);
            dssCi.front.compareMask = vdDssDesc.StencilReadMask;
            dssCi.front.writeMask = vdDssDesc.StencilWriteMask;
            dssCi.front.reference = vdDssDesc.StencilReference;

            dssCi.back.failOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Fail);
            dssCi.back.passOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Pass);
            dssCi.back.depthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.DepthFail);
            dssCi.back.compareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilBack.Comparison);
            dssCi.back.compareMask = vdDssDesc.StencilReadMask;
            dssCi.back.writeMask = vdDssDesc.StencilWriteMask;
            dssCi.back.reference = vdDssDesc.StencilReference;

            pipelineCi.pDepthStencilState = &dssCi;

            // Multisample
            var multisampleCi = new VkPipelineMultisampleStateCreateInfo();
            var vkSampleCount = VkFormats.VdToVkSampleCount(description.Outputs.SampleCount);
            multisampleCi.rasterizationSamples = vkSampleCount;
            multisampleCi.alphaToCoverageEnable = description.BlendState.AlphaToCoverageEnabled;

            pipelineCi.pMultisampleState = &multisampleCi;

            // Input Assembly
            var inputAssemblyCi = new VkPipelineInputAssemblyStateCreateInfo();
            inputAssemblyCi.topology = VkFormats.VdToVkPrimitiveTopology(description.PrimitiveTopology);

            pipelineCi.pInputAssemblyState = &inputAssemblyCi;

            // Vertex Input State
            var vertexInputCi = new VkPipelineVertexInputStateCreateInfo();

            var inputDescriptions = description.ShaderSet.VertexLayouts;
            uint bindingCount = (uint)inputDescriptions.Length;
            uint attributeCount = 0;
            for (int i = 0; i < inputDescriptions.Length; i++) attributeCount += (uint)inputDescriptions[i].Elements.Length;
            var bindingDescs = stackalloc VkVertexInputBindingDescription[(int)bindingCount];
            var attributeDescs = stackalloc VkVertexInputAttributeDescription[(int)attributeCount];

            int targetIndex = 0;
            int targetLocation = 0;

            for (int binding = 0; binding < inputDescriptions.Length; binding++)
            {
                var inputDesc = inputDescriptions[binding];
                bindingDescs[binding] = new VkVertexInputBindingDescription
                {
                    binding = (uint)binding,
                    inputRate = inputDesc.InstanceStepRate != 0 ? VkVertexInputRate.Instance : VkVertexInputRate.Vertex,
                    stride = inputDesc.Stride
                };

                uint currentOffset = 0;

                for (int location = 0; location < inputDesc.Elements.Length; location++)
                {
                    var inputElement = inputDesc.Elements[location];

                    attributeDescs[targetIndex] = new VkVertexInputAttributeDescription
                    {
                        format = VkFormats.VdToVkVertexElementFormat(inputElement.Format),
                        binding = (uint)binding,
                        location = (uint)(targetLocation + location),
                        offset = inputElement.Offset != 0 ? inputElement.Offset : currentOffset
                    };

                    targetIndex += 1;
                    currentOffset += FormatSizeHelpers.GetSizeInBytes(inputElement.Format);
                }

                targetLocation += inputDesc.Elements.Length;
            }

            vertexInputCi.vertexBindingDescriptionCount = bindingCount;
            vertexInputCi.pVertexBindingDescriptions = bindingDescs;
            vertexInputCi.vertexAttributeDescriptionCount = attributeCount;
            vertexInputCi.pVertexAttributeDescriptions = attributeDescs;

            pipelineCi.pVertexInputState = &vertexInputCi;

            // Shader Stage

            VkSpecializationInfo specializationInfo;
            var specDescs = description.ShaderSet.Specializations;

            if (specDescs != null)
            {
                uint specDataSize = 0;
                foreach (var spec in specDescs) specDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
                byte* fullSpecData = stackalloc byte[(int)specDataSize];
                int specializationCount = specDescs.Length;
                var mapEntries = stackalloc VkSpecializationMapEntry[specializationCount];
                uint specOffset = 0;

                for (int i = 0; i < specializationCount; i++)
                {
                    ulong data = specDescs[i].Data;
                    byte* srcData = (byte*)&data;
                    uint dataSize = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
                    Unsafe.CopyBlock(fullSpecData + specOffset, srcData, dataSize);
                    mapEntries[i].constantID = specDescs[i].ID;
                    mapEntries[i].offset = specOffset;
                    mapEntries[i].size = dataSize;
                    specOffset += dataSize;
                }

                specializationInfo.dataSize = specDataSize;
                specializationInfo.pData = fullSpecData;
                specializationInfo.mapEntryCount = (uint)specializationCount;
                specializationInfo.pMapEntries = mapEntries;
            }

            var shaders = description.ShaderSet.Shaders;
            var stages = new StackList<VkPipelineShaderStageCreateInfo>();
            // Encode entry-point names as null-terminated UTF-8 on the stack (max 256 bytes each).
            // This avoids a GCHandle-pinned heap allocation per shader stage.
            const int maxEntryPointBytes = 256;
            byte* entryPointsBuf = stackalloc byte[shaders.Length * maxEntryPointBytes];

            for (int si = 0; si < shaders.Length; si++)
            {
                var shader = shaders[si];
                var vkShader = Util.AssertSubtype<Shader, VkShader>(shader);
                byte* nameDst = entryPointsBuf + si * maxEntryPointBytes;
                int written = Encoding.UTF8.GetBytes(shader.EntryPoint, new Span<byte>(nameDst, maxEntryPointBytes - 1));
                nameDst[written] = 0;

                var stageCi = new VkPipelineShaderStageCreateInfo();
                stageCi.module = vkShader.ShaderModule;
                stageCi.stage = VkFormats.VdToVkShaderStages(shader.Stage);
                stageCi.pName = nameDst;
                // Pass null when there are no specialization constants — a pointer to a
                // zero-initialized VkSpecializationInfo is technically valid (mapEntryCount=0)
                // but null is the correct representation of "no specialization" and avoids
                // the driver having to dereference an unnecessary struct.
                stageCi.pSpecializationInfo = specDescs != null ? &specializationInfo : null;
                stages.Add(stageCi);
            }

            pipelineCi.stageCount = stages.Count;
            pipelineCi.pStages = (VkPipelineShaderStageCreateInfo*)stages.Data;

            // ViewportState
            var viewportStateCi = new VkPipelineViewportStateCreateInfo();
            viewportStateCi.viewportCount = 1;
            viewportStateCi.scissorCount = 1;

            pipelineCi.pViewportState = &viewportStateCi;

            // Pipeline Layout
            var resourceLayouts = description.ResourceLayouts;
            var pipelineLayoutCi = new VkPipelineLayoutCreateInfo();
            pipelineLayoutCi.setLayoutCount = (uint)resourceLayouts.Length;
            var dsls = stackalloc VkDescriptorSetLayout[resourceLayouts.Length];
            for (int i = 0; i < resourceLayouts.Length; i++) dsls[i] = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
            pipelineLayoutCi.pSetLayouts = dsls;

            gd.DeviceApi.vkCreatePipelineLayout(&pipelineLayoutCi, null, out pipelineLayout);
            pipelineCi.layout = pipelineLayout;

            var outputDesc = description.Outputs;

            if (this.gd.HasDynamicRendering)
            {
                // Dynamic rendering path: chain VkPipelineRenderingCreateInfo, no VkRenderPass needed.
                var colorFormats = stackalloc VkFormat[outputDesc.ColorAttachments.Length];

                for (int i = 0; i < outputDesc.ColorAttachments.Length; i++)
                    colorFormats[i] = VkFormats.VdToVkPixelFormat(outputDesc.ColorAttachments[i].Format);

                var pipelineRenderingCi = new VkPipelineRenderingCreateInfo();
                pipelineRenderingCi.colorAttachmentCount = (uint)outputDesc.ColorAttachments.Length;
                pipelineRenderingCi.pColorAttachmentFormats = colorFormats;

                if (outputDesc.DepthAttachment is OutputAttachmentDescription depthAttachmentDR)
                {
                    var depthVkFormat = VkFormats.VdToVkPixelFormat(depthAttachmentDR.Format, true);
                    pipelineRenderingCi.depthAttachmentFormat = depthVkFormat;

                    if (FormatHelpers.IsStencilFormat(depthAttachmentDR.Format))
                        pipelineRenderingCi.stencilAttachmentFormat = depthVkFormat;
                }

                pipelineRenderingCi.pNext = pipelineCi.pNext;
                pipelineCi.pNext = &pipelineRenderingCi;
                pipelineCi.renderPass = VkRenderPass.Null;

                Vortice.Vulkan.VkPipeline localPipeline;
                var result = gd.DeviceApi.vkCreateGraphicsPipelines(gd.PipelineCache, 1, &pipelineCi, null, &localPipeline);
                devicePipeline = localPipeline;
                CheckResult(result);
            }
            else
            {
                // Traditional path: create a fake VkRenderPass for pipeline compatibility.
                var renderPassCi = new VkRenderPassCreateInfo();
                var attachments = new StackList<VkAttachmentDescription, Size512Bytes>();

                // TODO: A huge portion of this next part is duplicated in VkFramebuffer.cs.

                // colorAttachmentRefs: VkAttachmentReference is 8 bytes; 256-byte StackList holds up to 32 — enough
                // for the Vulkan maximum of 8 color attachments.  We build the attachment descriptions inline
                // and add them directly to `attachments` to avoid a separate bounded buffer.
                var colorAttachmentRefs = new StackList<VkAttachmentReference>();

                for (uint i = 0; i < outputDesc.ColorAttachments.Length; i++)
                {
                    var colorAttachmentDesc = new VkAttachmentDescription
                    {
                        format = VkFormats.VdToVkPixelFormat(outputDesc.ColorAttachments[i].Format),
                        samples = vkSampleCount,
                        loadOp = VkAttachmentLoadOp.DontCare,
                        storeOp = VkAttachmentStoreOp.Store,
                        stencilLoadOp = VkAttachmentLoadOp.DontCare,
                        stencilStoreOp = VkAttachmentStoreOp.DontCare,
                        initialLayout = VkImageLayout.Undefined,
                        finalLayout = VkImageLayout.ShaderReadOnlyOptimal
                    };
                    attachments.Add(colorAttachmentDesc);

                    colorAttachmentRefs[i].attachment = i;
                    colorAttachmentRefs[i].layout = VkImageLayout.ColorAttachmentOptimal;
                }

                var depthAttachmentDesc = new VkAttachmentDescription();
                var depthAttachmentRef = new VkAttachmentReference();

                if (outputDesc.DepthAttachment is OutputAttachmentDescription depthAttachment)
                {
                    var depthFormat = depthAttachment.Format;
                    bool hasStencil = FormatHelpers.IsStencilFormat(depthFormat);
                    depthAttachmentDesc.format = VkFormats.VdToVkPixelFormat(depthAttachment.Format, true);
                    depthAttachmentDesc.samples = vkSampleCount;
                    depthAttachmentDesc.loadOp = VkAttachmentLoadOp.DontCare;
                    depthAttachmentDesc.storeOp = VkAttachmentStoreOp.Store;
                    depthAttachmentDesc.stencilLoadOp = VkAttachmentLoadOp.DontCare;
                    depthAttachmentDesc.stencilStoreOp = hasStencil ? VkAttachmentStoreOp.Store : VkAttachmentStoreOp.DontCare;
                    depthAttachmentDesc.initialLayout = VkImageLayout.Undefined;
                    depthAttachmentDesc.finalLayout = VkImageLayout.DepthStencilAttachmentOptimal;

                    depthAttachmentRef.attachment = (uint)outputDesc.ColorAttachments.Length;
                    depthAttachmentRef.layout = VkImageLayout.DepthStencilAttachmentOptimal;
                }

                var subpass = new VkSubpassDescription
                {
                    pipelineBindPoint = VkPipelineBindPoint.Graphics,
                    colorAttachmentCount = (uint)outputDesc.ColorAttachments.Length,
                    pColorAttachments = (VkAttachmentReference*)colorAttachmentRefs.Data
                };

                if (outputDesc.DepthAttachment != null)
                {
                    subpass.pDepthStencilAttachment = &depthAttachmentRef;
                    attachments.Add(depthAttachmentDesc);
                }

                var subpassDependency = new VkSubpassDependency
                {
                    srcSubpass = VK_SUBPASS_EXTERNAL,
                    srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
                    srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
                    dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
                    dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite
                };

                // Extend the external dependency to cover depth/stencil stages when present,
                // matching the fix applied to VkFramebuffer. Without this, the validation layer
                // reports that the srcStageMask does not include stages that use the attachment's
                // initialLayout (Undefined → DepthStencilAttachmentOptimal transition).
                if (outputDesc.DepthAttachment != null)
                {
                    subpassDependency.srcStageMask |= VkPipelineStageFlags.EarlyFragmentTests
                                                      | VkPipelineStageFlags.LateFragmentTests;
                    subpassDependency.srcAccessMask |= VkAccessFlags.DepthStencilAttachmentWrite;
                    subpassDependency.dstStageMask |= VkPipelineStageFlags.EarlyFragmentTests
                                                      | VkPipelineStageFlags.LateFragmentTests;
                    subpassDependency.dstAccessMask |= VkAccessFlags.DepthStencilAttachmentRead
                                                       | VkAccessFlags.DepthStencilAttachmentWrite;
                }

                renderPassCi.attachmentCount = attachments.Count;
                renderPassCi.pAttachments = (VkAttachmentDescription*)attachments.Data;
                renderPassCi.subpassCount = 1;
                renderPassCi.pSubpasses = &subpass;
                renderPassCi.dependencyCount = 1;
                renderPassCi.pDependencies = &subpassDependency;

                var creationResult = gd.DeviceApi.vkCreateRenderPass(&renderPassCi, null, out renderPass);
                CheckResult(creationResult);

                pipelineCi.renderPass = renderPass;

                Vortice.Vulkan.VkPipeline localPipeline;
                var result = gd.DeviceApi.vkCreateGraphicsPipelines(gd.PipelineCache, 1, &pipelineCi, null, &localPipeline);
                devicePipeline = localPipeline;
                CheckResult(result);
            }

            ResourceSetCount = (uint)description.ResourceLayouts.Length;
            DynamicOffsetsCount = 0;
            foreach (ResourceLayout layout in description.ResourceLayouts)
                DynamicOffsetsCount += layout.DynamicBufferCount;
        }

        public VkPipeline(VkGraphicsDevice gd, ref ComputePipelineDescription description)
            : base(ref description)
        {
            this.gd = gd;
            IsComputePipeline = true;
            RefCount = new ResourceRefCount(disposeCore);

            var pipelineCi = new VkComputePipelineCreateInfo();

            // Pipeline Layout
            var resourceLayouts = description.ResourceLayouts;
            var pipelineLayoutCi = new VkPipelineLayoutCreateInfo();
            pipelineLayoutCi.setLayoutCount = (uint)resourceLayouts.Length;
            var dsls = stackalloc VkDescriptorSetLayout[resourceLayouts.Length];
            for (int i = 0; i < resourceLayouts.Length; i++) dsls[i] = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
            pipelineLayoutCi.pSetLayouts = dsls;

            gd.DeviceApi.vkCreatePipelineLayout(&pipelineLayoutCi, null, out pipelineLayout);
            pipelineCi.layout = pipelineLayout;

            // Shader Stage

            VkSpecializationInfo specializationInfo;
            var specDescs = description.Specializations;

            if (specDescs != null)
            {
                uint specDataSize = 0;
                foreach (var spec in specDescs) specDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
                byte* fullSpecData = stackalloc byte[(int)specDataSize];
                int specializationCount = specDescs.Length;
                var mapEntries = stackalloc VkSpecializationMapEntry[specializationCount];
                uint specOffset = 0;

                for (int i = 0; i < specializationCount; i++)
                {
                    ulong data = specDescs[i].Data;
                    byte* srcData = (byte*)&data;
                    uint dataSize = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
                    Unsafe.CopyBlock(fullSpecData + specOffset, srcData, dataSize);
                    mapEntries[i].constantID = specDescs[i].ID;
                    mapEntries[i].offset = specOffset;
                    mapEntries[i].size = dataSize;
                    specOffset += dataSize;
                }

                specializationInfo.dataSize = specDataSize;
                specializationInfo.pData = fullSpecData;
                specializationInfo.mapEntryCount = (uint)specializationCount;
                specializationInfo.pMapEntries = mapEntries;
            }

            var shader = description.ComputeShader;
            var vkShader = Util.AssertSubtype<Shader, VkShader>(shader);
            var stageCi = new VkPipelineShaderStageCreateInfo();
            stageCi.module = vkShader.ShaderModule;
            stageCi.stage = VkFormats.VdToVkShaderStages(shader.Stage);

            // Encode the entry-point name as a null-terminated UTF-8 string on the stack,
            // matching the graphics pipeline path. Previously this hardcoded CommonStrings.Main
            // ("main"), which silently broke any compute shader with a custom entry point name.
            const int maxComputeEntryPointBytes = 256;
            byte* computeEntryPointBuf = stackalloc byte[maxComputeEntryPointBytes];
            int computeNameWritten = Encoding.UTF8.GetBytes(
                shader.EntryPoint,
                new Span<byte>(computeEntryPointBuf, maxComputeEntryPointBytes - 1));
            computeEntryPointBuf[computeNameWritten] = 0;
            stageCi.pName = computeEntryPointBuf;
            stageCi.pSpecializationInfo = specDescs != null ? &specializationInfo : null;
            pipelineCi.stage = stageCi;

            Vortice.Vulkan.VkPipeline localPipeline;
            var result = gd.DeviceApi.vkCreateComputePipelines(gd.PipelineCache,
                1,
                &pipelineCi,
                null,
                &localPipeline);
            devicePipeline = localPipeline;
            CheckResult(result);

            ResourceSetCount = (uint)description.ResourceLayouts.Length;
            DynamicOffsetsCount = 0;
            foreach (ResourceLayout layout in description.ResourceLayouts)
                DynamicOffsetsCount += layout.DynamicBufferCount;
        }

        #region Disposal

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        #endregion

        private void disposeCore()
        {
            if (!destroyed)
            {
                destroyed = true;
                gd.DeviceApi.vkDestroyPipelineLayout(pipelineLayout, null);
                gd.DeviceApi.vkDestroyPipeline(devicePipeline, null);
                if (!IsComputePipeline) gd.DeviceApi.vkDestroyRenderPass(renderPass, null);
            }
        }
    }
}
