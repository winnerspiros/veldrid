using System.Collections.Generic;
using Vulkan;

namespace Veldrid.Vk
{
    internal abstract class VkFramebufferBase : Framebuffer
    {
        public ResourceRefCount RefCount { get; }

        public abstract uint RenderableWidth { get; }
        public abstract uint RenderableHeight { get; }

        public abstract Vulkan.VkFramebuffer CurrentFramebuffer { get; }
        public abstract VkRenderPass RenderPassNoClearInit { get; }
        public abstract VkRenderPass RenderPassNoClearLoad { get; }
        public abstract VkRenderPass RenderPassClear { get; }
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

        protected abstract void DisposeCore();
    }
}
