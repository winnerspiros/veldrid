using System.Collections.Generic;
using Vortice.Vulkan;
namespace Veldrid.Vk
{
    internal abstract class VkFramebufferBase : Framebuffer
    {
        public ResourceRefCount RefCount { get; }

        public abstract uint RenderableWidth { get; }
        public abstract uint RenderableHeight { get; }

        public abstract Vortice.Vulkan.VkFramebuffer CurrentFramebuffer { get; }
        public abstract VkRenderPass RenderPassNoClearInit { get; }
        public abstract VkRenderPass RenderPassNoClearLoad { get; }
        public abstract VkRenderPass RenderPassClear { get; }

        /// <summary>
        ///     A render pass variant for the legacy path that uses <c>loadOp=Clear / initialLayout=Undefined</c>
        ///     for <em>sampled</em> color attachments and <c>loadOp=Load / initialLayout=ColorAttachmentOptimal</c>
        ///     for non-sampled ones. Used on the first bind of a sampled offscreen FBO per frame to avoid
        ///     loading stale TBR tile data on TBDR GPUs (equivalent to the dynamic-rendering path behaviour).
        ///     Returns <see cref="VkRenderPass.Null"/> when the framebuffer has no sampled color attachments.
        /// </summary>
        public virtual VkRenderPass RenderPassClearSampledInit => VkRenderPass.Null;
        public abstract uint AttachmentCount { get; }

        /// <summary>
        ///     Color attachment image views for dynamic rendering. May return an empty list
        ///     if the framebuffer does not expose individual views.
        /// </summary>
        public abstract IReadOnlyList<VkImageView> ColorAttachmentViews { get; }

        /// <summary>
        ///     Depth attachment image view for dynamic rendering. Returns <see cref="VkImageView.Null"/>
        ///     when no depth target is present.
        /// </summary>
        public abstract VkImageView DepthAttachmentView { get; }

        protected VkFramebufferBase(
            FramebufferAttachmentDescription? depthTexture,
            IReadOnlyList<FramebufferAttachmentDescription> colorTextures)
            : base(depthTexture, colorTextures)
        {
            RefCount = new ResourceRefCount(DisposeCore);
        }

        protected VkFramebufferBase()
        {
            RefCount = new ResourceRefCount(DisposeCore);
        }

        #region Disposal

        public override void Dispose()
        {
            RefCount.Decrement();
        }

        #endregion

        public abstract void TransitionToIntermediateLayout(VkCommandBuffer cb);
        public abstract void TransitionToFinalLayout(VkCommandBuffer cb);

        /// <summary>
        ///     Called by <see cref="VkCommandList.SetFramebufferCore"/> when switching away from this
        ///     framebuffer mid-frame (i.e. not at end-of-frame). The default implementation falls
        ///     through to <see cref="TransitionToFinalLayout"/> so regular framebuffers (sampled
        ///     textures → ShaderReadOnly) are unaffected.
        /// </summary>
        public virtual void TransitionToFBOSwitchLayout(VkCommandBuffer cb) => TransitionToFinalLayout(cb);

        protected abstract void DisposeCore();
    }
}
