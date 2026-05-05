using System.Collections.Generic;
using System.Diagnostics;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkFramebuffer : VkFramebufferBase
    {
        public override Vortice.Vulkan.VkFramebuffer CurrentFramebuffer => deviceFramebuffer;
        public override VkRenderPass RenderPassNoClearInit => renderPassNoClear;
        public override VkRenderPass RenderPassNoClearLoad => renderPassNoClearLoad;
        public override VkRenderPass RenderPassClear => renderPassClear;
        public override VkRenderPass RenderPassClearSampledInit => renderPassClearSampledInit;

        public override uint RenderableWidth => Width;
        public override uint RenderableHeight => Height;

        public override uint AttachmentCount { get; }

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
        private readonly Vortice.Vulkan.VkFramebuffer deviceFramebuffer;
        private readonly VkRenderPass renderPassNoClearLoad;
        private readonly VkRenderPass renderPassNoClear;
        private readonly VkRenderPass renderPassClear;
        // Only non-Null when the framebuffer has at least one sampled color attachment (legacy path).
        private readonly VkRenderPass renderPassClearSampledInit;
        private readonly List<VkImageView> attachmentViews = new List<VkImageView>();
        private readonly List<VkImageView> colorViews = new List<VkImageView>();
        private VkImageView depthView;
        private bool destroyed;
        private string name;

        public override IReadOnlyList<VkImageView> ColorAttachmentViews => colorViews;
        public override VkImageView DepthAttachmentView => depthView;

        public VkFramebuffer(VkGraphicsDevice gd, ref FramebufferDescription description, bool isPresented)
            : base(description.DepthTarget, description.ColorTargets)
        {
            this.gd = gd;

            var renderPassCi = new VkRenderPassCreateInfo();

            // Size512Bytes holds 512/36 ≈ 14 VkAttachmentDescription entries — safely covers the
            // Vulkan maximum of 8 color + 1 depth = 9 attachments with room to spare.
            var attachments = new StackList<VkAttachmentDescription, Size512Bytes>();

            uint colorAttachmentCount = (uint)ColorTargets.Count;
            var colorAttachmentRefs = new StackList<VkAttachmentReference>();

            for (int i = 0; i < colorAttachmentCount; i++)
            {
                var vkColorTex = Util.AssertSubtype<Texture, VkTexture>(ColorTargets[i].Target);
                var colorAttachmentDesc = new VkAttachmentDescription
                {
                    format = vkColorTex.VkFormat,
                    samples = vkColorTex.VkSampleCount,
                    loadOp = VkAttachmentLoadOp.Load,
                    storeOp = VkAttachmentStoreOp.Store,
                    stencilLoadOp = VkAttachmentLoadOp.DontCare,
                    stencilStoreOp = VkAttachmentStoreOp.DontCare,
                    initialLayout = isPresented
                        ? VkImageLayout.PresentSrcKHR
                        : (vkColorTex.Usage & TextureUsage.Sampled) != 0
                            ? VkImageLayout.ShaderReadOnlyOptimal
                            : VkImageLayout.ColorAttachmentOptimal,
                    finalLayout = VkImageLayout.ColorAttachmentOptimal
                };
                attachments.Add(colorAttachmentDesc);

                var colorAttachmentRef = new VkAttachmentReference
                {
                    attachment = (uint)i,
                    layout = VkImageLayout.ColorAttachmentOptimal
                };
                colorAttachmentRefs.Add(colorAttachmentRef);
            }

            var depthAttachmentDesc = new VkAttachmentDescription();
            var depthAttachmentRef = new VkAttachmentReference();

            if (DepthTarget != null)
            {
                var vkDepthTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
                bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTex.Format);
                // Transient depth textures are backed by LAZILY_ALLOCATED (tile-only) memory
                // and are never sampled or read after the render pass. Using DONT_CARE for both
                // depth and stencil store ops tells the driver it does not need to flush tile
                // contents to main memory — the full TBDR win. With STORE the driver would still
                // perform a tile→DRAM writeback even though the data is never consumed.
                bool isTransient = (vkDepthTex.Usage & TextureUsage.Transient) != 0;
                depthAttachmentDesc.format = vkDepthTex.VkFormat;
                depthAttachmentDesc.samples = vkDepthTex.VkSampleCount;
                depthAttachmentDesc.loadOp = VkAttachmentLoadOp.Load;
                depthAttachmentDesc.storeOp = isTransient
                    ? VkAttachmentStoreOp.DontCare
                    : VkAttachmentStoreOp.Store;
                depthAttachmentDesc.stencilLoadOp = VkAttachmentLoadOp.DontCare;
                depthAttachmentDesc.stencilStoreOp = isTransient || !hasStencil
                    ? VkAttachmentStoreOp.DontCare
                    : VkAttachmentStoreOp.Store;
                depthAttachmentDesc.initialLayout = (vkDepthTex.Usage & TextureUsage.Sampled) != 0
                    ? VkImageLayout.ShaderReadOnlyOptimal
                    : VkImageLayout.DepthStencilAttachmentOptimal;
                depthAttachmentDesc.finalLayout = VkImageLayout.DepthStencilAttachmentOptimal;

                depthAttachmentRef.attachment = (uint)description.ColorTargets.Length;
                depthAttachmentRef.layout = VkImageLayout.DepthStencilAttachmentOptimal;
            }

            var subpass = new VkSubpassDescription
            {
                pipelineBindPoint = VkPipelineBindPoint.Graphics
            };

            if (ColorTargets.Count > 0)
            {
                subpass.colorAttachmentCount = colorAttachmentCount;
                subpass.pColorAttachments = (VkAttachmentReference*)colorAttachmentRefs.Data;
            }

            if (DepthTarget != null)
            {
                subpass.pDepthStencilAttachment = &depthAttachmentRef;
                attachments.Add(depthAttachmentDesc);
            }

            var subpassDependency = new VkSubpassDependency
            {
                srcSubpass = VK_SUBPASS_EXTERNAL,
                srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
                dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
                dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite
            };

            // Extend the external dependency to cover depth/stencil when the framebuffer has
            // a depth attachment.  The external-to-subpass transition for a depth attachment
            // occurs in EarlyFragmentTests/LateFragmentTests, not ColorAttachmentOutput.
            // Omitting these stage/access masks produces a Vulkan validation warning:
            // "vkCreateRenderPass: pDependency[0] srcStageMask does not include any stages that
            //  use the layout set in pAttachments[…].initialLayout."
            if (DepthTarget != null)
            {
                subpassDependency.srcStageMask |= VkPipelineStageFlags.EarlyFragmentTests
                                                  | VkPipelineStageFlags.LateFragmentTests;
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

            var creationResult = gd.DeviceApi.vkCreateRenderPass(&renderPassCi, null, out renderPassNoClear);
            CheckResult(creationResult);

            for (int i = 0; i < colorAttachmentCount; i++)
            {
                attachments[i].loadOp = VkAttachmentLoadOp.Load;
                attachments[i].initialLayout = VkImageLayout.ColorAttachmentOptimal;
            }

            if (DepthTarget != null)
            {
                attachments[attachments.Count - 1].loadOp = VkAttachmentLoadOp.Load;
                attachments[attachments.Count - 1].initialLayout = VkImageLayout.DepthStencilAttachmentOptimal;
                bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
                if (hasStencil) attachments[attachments.Count - 1].stencilLoadOp = VkAttachmentLoadOp.Load;
            }

            creationResult = gd.DeviceApi.vkCreateRenderPass(&renderPassCi, null, out renderPassNoClearLoad);
            CheckResult(creationResult);

            // Load version

            if (DepthTarget != null)
            {
                attachments[attachments.Count - 1].loadOp = VkAttachmentLoadOp.Clear;
                attachments[attachments.Count - 1].initialLayout = VkImageLayout.Undefined;
                bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
                if (hasStencil) attachments[attachments.Count - 1].stencilLoadOp = VkAttachmentLoadOp.Clear;
            }

            for (int i = 0; i < colorAttachmentCount; i++)
            {
                attachments[i].loadOp = VkAttachmentLoadOp.Clear;
                attachments[i].initialLayout = VkImageLayout.Undefined;
            }

            creationResult = gd.DeviceApi.vkCreateRenderPass(&renderPassCi, null, out renderPassClear);
            CheckResult(creationResult);

            // Build renderPassClearSampledInit: for sampled color attachments use loadOp=Clear /
            // initialLayout=Undefined (avoids loading stale TBR tile data on the first bind of a
            // sampled offscreen FBO per frame); non-sampled color attachments use loadOp=Load /
            // initialLayout=ColorAttachmentOptimal; depth is load-only.  Only created when at least
            // one sampled color attachment exists.
            bool hasSampledColorTarget = false;
            for (int i = 0; i < colorAttachmentCount; i++)
            {
                var vkColorTex = Util.AssertSubtype<Texture, VkTexture>(ColorTargets[i].Target);
                bool isSampled = (vkColorTex.Usage & TextureUsage.Sampled) != 0;
                if (isSampled)
                {
                    hasSampledColorTarget = true;
                    attachments[i].loadOp = VkAttachmentLoadOp.Clear;
                    attachments[i].initialLayout = VkImageLayout.Undefined;
                }
                else
                {
                    attachments[i].loadOp = VkAttachmentLoadOp.Load;
                    attachments[i].initialLayout = VkImageLayout.ColorAttachmentOptimal;
                }
            }

            if (DepthTarget != null)
            {
                int depthIdx = (int)(attachments.Count - 1);
                attachments[depthIdx].loadOp = VkAttachmentLoadOp.Load;
                attachments[depthIdx].initialLayout = VkImageLayout.DepthStencilAttachmentOptimal;
                bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
                if (hasStencil) attachments[depthIdx].stencilLoadOp = VkAttachmentLoadOp.Load;
            }

            if (hasSampledColorTarget)
            {
                creationResult = gd.DeviceApi.vkCreateRenderPass(&renderPassCi, null, out renderPassClearSampledInit);
                CheckResult(creationResult);
            }

            var fbCi = new VkFramebufferCreateInfo();
            uint fbAttachmentsCount = (uint)description.ColorTargets.Length;
            if (description.DepthTarget != null) fbAttachmentsCount += 1;

            var fbAttachments = stackalloc VkImageView[(int)fbAttachmentsCount];

            for (int i = 0; i < colorAttachmentCount; i++)
            {
                var vkColorTarget = Util.AssertSubtype<Texture, VkTexture>(description.ColorTargets[i].Target);
                var imageViewCi = new VkImageViewCreateInfo();
                imageViewCi.image = vkColorTarget.OptimalDeviceImage;
                imageViewCi.format = vkColorTarget.VkFormat;
                imageViewCi.viewType = VkImageViewType.Image2D;
                imageViewCi.subresourceRange = new VkImageSubresourceRange(
                    VkImageAspectFlags.Color,
                    description.ColorTargets[i].MipLevel,
                    1,
                    description.ColorTargets[i].ArrayLayer);
                var dest = fbAttachments + i;
                var result = gd.DeviceApi.vkCreateImageView(&imageViewCi, null, dest);
                CheckResult(result);
                attachmentViews.Add(*dest);
                colorViews.Add(*dest);
            }

            // Depth
            if (description.DepthTarget != null)
            {
                var vkDepthTarget = Util.AssertSubtype<Texture, VkTexture>(description.DepthTarget.Value.Target);
                bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTarget.Format);
                var depthViewCi = new VkImageViewCreateInfo();
                depthViewCi.image = vkDepthTarget.OptimalDeviceImage;
                depthViewCi.format = vkDepthTarget.VkFormat;
                depthViewCi.viewType = description.DepthTarget.Value.Target.ArrayLayers == 1
                    ? VkImageViewType.Image2D
                    : VkImageViewType.Image2DArray;
                depthViewCi.subresourceRange = new VkImageSubresourceRange(
                    hasStencil ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil : VkImageAspectFlags.Depth,
                    description.DepthTarget.Value.MipLevel,
                    1,
                    description.DepthTarget.Value.ArrayLayer);
                var dest = fbAttachments + (fbAttachmentsCount - 1);
                var result = gd.DeviceApi.vkCreateImageView(&depthViewCi, null, dest);
                CheckResult(result);
                attachmentViews.Add(*dest);
                depthView = *dest;
            }

            Texture dimTex;
            uint mipLevel;

            if (ColorTargets.Count > 0)
            {
                dimTex = ColorTargets[0].Target;
                mipLevel = ColorTargets[0].MipLevel;
            }
            else
            {
                Debug.Assert(DepthTarget != null);
                dimTex = DepthTarget.Value.Target;
                mipLevel = DepthTarget.Value.MipLevel;
            }

            Util.GetMipDimensions(
                dimTex,
                mipLevel,
                out uint mipWidth,
                out uint mipHeight,
                out _);

            fbCi.width = mipWidth;
            fbCi.height = mipHeight;

            fbCi.attachmentCount = fbAttachmentsCount;
            fbCi.pAttachments = fbAttachments;
            fbCi.layers = 1;
            fbCi.renderPass = renderPassNoClear;

            creationResult = gd.DeviceApi.vkCreateFramebuffer(&fbCi, null, out deviceFramebuffer);
            CheckResult(creationResult);

            if (DepthTarget != null) AttachmentCount += 1;
            AttachmentCount += (uint)ColorTargets.Count;
        }

        // Keep sampled color attachments in ColorAttachmentOptimal during mid-frame FBO switches,
        // mirroring VkSwapchainFramebuffer. This lets wasAlreadyColorAttachment=true fire on return
        // to an outer FBO (loadOp=Load), preserving any partial content accumulated before the inner
        // FBO was bound. TransitionToFinalLayout is still called from End() for the last active FBO,
        // and transitionImages() emits the ShaderReadOnlyOptimal barrier when textures are sampled.
        public override void TransitionToFBOSwitchLayout(VkCommandBuffer cb) { }

        public override void TransitionToIntermediateLayout(VkCommandBuffer cb)
        {
            for (int i = 0; i < ColorTargets.Count; i++)
            {
                var ca = ColorTargets[i];
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
                vkTex.SetImageLayout(ca.MipLevel, ca.ArrayLayer, VkImageLayout.ColorAttachmentOptimal);
            }

            if (DepthTarget != null)
            {
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
                vkTex.SetImageLayout(
                    DepthTarget.Value.MipLevel,
                    DepthTarget.Value.ArrayLayer,
                    VkImageLayout.DepthStencilAttachmentOptimal);
            }
        }

        public override void TransitionToFinalLayout(VkCommandBuffer cb)
        {
            for (int i = 0; i < ColorTargets.Count; i++)
            {
                var ca = ColorTargets[i];
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);

                if ((vkTex.Usage & TextureUsage.Sampled) != 0)
                {
                    vkTex.TransitionImageLayout(
                        cb,
                        ca.MipLevel, 1,
                        ca.ArrayLayer, 1,
                        VkImageLayout.ShaderReadOnlyOptimal);
                }
            }

            if (DepthTarget != null)
            {
                var vkTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);

                if ((vkTex.Usage & TextureUsage.Sampled) != 0)
                {
                    vkTex.TransitionImageLayout(
                        cb,
                        DepthTarget.Value.MipLevel, 1,
                        DepthTarget.Value.ArrayLayer, 1,
                        VkImageLayout.ShaderReadOnlyOptimal);
                }
            }
        }

        protected override void DisposeCore()
        {
            if (!destroyed)
            {
                gd.DeviceApi.vkDestroyFramebuffer(deviceFramebuffer, null);
                gd.DeviceApi.vkDestroyRenderPass(renderPassNoClear, null);
                gd.DeviceApi.vkDestroyRenderPass(renderPassNoClearLoad, null);
                gd.DeviceApi.vkDestroyRenderPass(renderPassClear, null);
                if (renderPassClearSampledInit != VkRenderPass.Null)
                    gd.DeviceApi.vkDestroyRenderPass(renderPassClearSampledInit, null);
                foreach (var view in attachmentViews) gd.DeviceApi.vkDestroyImageView(view, null);

                destroyed = true;
            }
        }
    }
}
