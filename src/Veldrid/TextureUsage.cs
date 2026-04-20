using System;

namespace Veldrid
{
    /// <summary>
    ///     A bitmask indicating how a <see cref="Texture" /> is permitted to be used.
    /// </summary>
    [Flags]
    public enum TextureUsage : byte
    {
        /// <summary>
        ///     The Texture can be used as the target of a read-only <see cref="TextureView" />, and can be accessed from a shader.
        /// </summary>
        Sampled = 1 << 0,

        /// <summary>
        ///     The Texture can be used as the target of a read-write <see cref="TextureView" />, and can be accessed from a
        ///     shader.
        /// </summary>
        Storage = 1 << 1,

        /// <summary>
        ///     The Texture can be used as the color target of a <see cref="Framebuffer" />.
        /// </summary>
        RenderTarget = 1 << 2,

        /// <summary>
        ///     The Texture can be used as the depth target of a <see cref="Framebuffer" />.
        /// </summary>
        DepthStencil = 1 << 3,

        /// <summary>
        ///     The Texture is a two-dimensional cubemap.
        /// </summary>
        Cubemap = 1 << 4,

        /// <summary>
        ///     The Texture is used as a read-write staging resource for uploading Texture data.
        ///     With this flag, a Texture can be mapped using the
        ///     <see cref="GraphicsDevice.Map(IMappableResource, MapMode, uint)" />
        ///     method.
        /// </summary>
        Staging = 1 << 5,

        /// <summary>
        ///     The Texture supports automatic generation of mipmaps through <see cref="CommandList.GenerateMipmaps(Texture)" />.
        /// </summary>
        GenerateMipmaps = 1 << 6,

        /// <summary>
        ///     The Texture's contents are not needed outside of a render pass — i.e. the texture is
        ///     written to and read from only as an attachment, never sampled, copied, or persisted
        ///     across frames. On tile-based mobile GPUs (Adreno / Mali / PowerVR) this lets the
        ///     Vulkan backend allocate the image with <c>VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT</c>
        ///     and back it with <c>VK_MEMORY_PROPERTY_LAZILY_ALLOCATED_BIT</c> memory, so the image
        ///     never occupies physical memory and lives entirely in tile RAM. Saves both VRAM and
        ///     DRAM bandwidth — the canonical use is the swapchain depth/stencil buffer (the Veldrid
        ///     swapchain framebuffer opts in automatically). Implies the texture cannot be sampled,
        ///     used as a copy source/destination, mapped, or used as compute storage. Mutually
        ///     exclusive with <see cref="Sampled" />, <see cref="Storage" />, <see cref="Staging" />,
        ///     and <see cref="GenerateMipmaps" />. Backends that don't model lazy allocation
        ///     (D3D11, D3D12, Metal, OpenGL) silently ignore this flag.
        /// </summary>
        Transient = 1 << 7
    }
}
