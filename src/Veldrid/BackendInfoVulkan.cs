#if !EXCLUDE_VULKAN_BACKEND
using System;
using System.Collections.ObjectModel;
using Veldrid.Vk;
using Vulkan;

namespace Veldrid
{
    /// <summary>
    ///     Exposes Vulkan-specific functionality,
    ///     useful for interoperating with native components which interface directly with Vulkan.
    ///     Can only be used on <see cref="GraphicsBackend.Vulkan" />.
    /// </summary>
    public class BackendInfoVulkan
    {
        /// <summary>
        ///     Gets the underlying VkInstance used by the GraphicsDevice.
        /// </summary>
        public IntPtr Instance => gd.Instance.Handle;

        /// <summary>
        ///     Gets the underlying VkDevice used by the GraphicsDevice.
        /// </summary>
        public IntPtr Device => gd.Device.Handle;

        /// <summary>
        ///     Gets the underlying VkPhysicalDevice used by the GraphicsDevice.
        /// </summary>
        public IntPtr PhysicalDevice => gd.PhysicalDevice.Handle;

        /// <summary>
        ///     Gets the VkQueue which is used by the GraphicsDevice to submit graphics work.
        /// </summary>
        public IntPtr GraphicsQueue => gd.GraphicsQueue.Handle;

        /// <summary>
        ///     Gets the queue family index of the graphics VkQueue.
        /// </summary>
        public uint GraphicsQueueFamilyIndex => gd.GraphicsQueueIndex;

        /// <summary>
        ///     Gets the driver name of the device. May be null.
        /// </summary>
        public string DriverName => gd.DriverName;

        /// <summary>
        ///     Gets the driver information of the device. May be null.
        /// </summary>
        public string DriverInfo => gd.DriverInfo;

        /// <summary>
        ///     Indicates whether the device supports VK_KHR_fragment_shading_rate (Variable Rate Shading).
        /// </summary>
        public bool HasFragmentShadingRate => gd.HasFragmentShadingRate;

        /// <summary>
        ///     Indicates whether the device supports VK_EXT_mesh_shader (Mesh Shaders).
        /// </summary>
        public bool HasMeshShader => gd.HasMeshShader;

        /// <summary>
        ///     Gets the available Vulkan instance layers.
        /// </summary>
        public ReadOnlyCollection<string> AvailableInstanceLayers => instanceLayers.Value;

        /// <summary>
        ///     Gets the available Vulkan instance extensions.
        /// </summary>
        public ReadOnlyCollection<string> AvailableInstanceExtensions { get; }

        /// <summary>
        ///     Gets the available Vulkan device extensions with their spec versions.
        /// </summary>
        public ReadOnlyCollection<ExtensionProperties> AvailableDeviceExtensions => deviceExtensions.Value;

        /// <summary>
        ///     Describes a Vulkan extension with its name and specification version.
        /// </summary>
        public readonly struct ExtensionProperties
        {
            /// <summary>
            ///     The extension name (e.g. "VK_KHR_swapchain").
            /// </summary>
            public readonly string Name;

            /// <summary>
            ///     The specification version of the extension.
            /// </summary>
            public readonly uint SpecVersion;

            /// <summary>
            ///     Creates a new <see cref="ExtensionProperties" /> instance.
            /// </summary>
            /// <param name="name">The extension name.</param>
            /// <param name="specVersion">The specification version.</param>
            public ExtensionProperties(string name, uint specVersion)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
                SpecVersion = specVersion;
            }

            /// <inheritdoc />
            public override string ToString()
            {
                return Name;
            }
        }

        private readonly VkGraphicsDevice gd;
        private readonly Lazy<ReadOnlyCollection<string>> instanceLayers;
        private readonly Lazy<ReadOnlyCollection<ExtensionProperties>> deviceExtensions;

        internal BackendInfoVulkan(VkGraphicsDevice gd)
        {
            this.gd = gd;
            instanceLayers = new Lazy<ReadOnlyCollection<string>>(() => new ReadOnlyCollection<string>(VulkanUtil.EnumerateInstanceLayers()));
            AvailableInstanceExtensions = new ReadOnlyCollection<string>(VulkanUtil.GetInstanceExtensions());
            deviceExtensions = new Lazy<ReadOnlyCollection<ExtensionProperties>>(enumerateDeviceExtensions);
        }

        /// <summary>
        ///     Overrides the current VkImageLayout tracked by the given Texture. This should be used when a VkImage is created by
        ///     an external library to inform Veldrid about its initial layout.
        /// </summary>
        /// <param name="texture">The Texture whose currently-tracked VkImageLayout will be overridden.</param>
        /// <param name="layout">The new VkImageLayout value.</param>
        public void OverrideImageLayout(Texture texture, uint layout)
        {
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);

            for (uint layer = 0; layer < vkTex.ArrayLayers; layer++)
            {
                for (uint level = 0; level < vkTex.MipLevels; level++) vkTex.SetImageLayout(level, layer, (VkImageLayout)layout);
            }
        }

        /// <summary>
        ///     Gets the underlying VkImage wrapped by the given Veldrid Texture. This method can not be used on Textures with
        ///     TextureUsage.Staging.
        /// </summary>
        /// <param name="texture">The Texture whose underlying VkImage will be returned.</param>
        /// <returns>The underlying VkImage for the given Texture.</returns>
        public ulong GetVkImage(Texture texture)
        {
            var vkTexture = Util.AssertSubtype<Texture, VkTexture>(texture);

            if ((vkTexture.Usage & TextureUsage.Staging) != 0)
            {
                throw new VeldridException(
                    $"{nameof(GetVkImage)} cannot be used if the {nameof(Texture)} " +
                    $"has {nameof(TextureUsage)}.{nameof(TextureUsage.Staging)}.");
            }

            return vkTexture.OptimalDeviceImage.Handle;
        }

        /// <summary>
        ///     Transitions the given Texture's underlying VkImage into a new layout.
        /// </summary>
        /// <param name="texture">The Texture whose underlying VkImage will be transitioned.</param>
        /// <param name="layout">The new VkImageLayout value.</param>
        public void TransitionImageLayout(Texture texture, uint layout)
        {
            gd.TransitionImageLayout(Util.AssertSubtype<Texture, VkTexture>(texture), (VkImageLayout)layout);
        }

        private unsafe ReadOnlyCollection<ExtensionProperties> enumerateDeviceExtensions()
        {
            var vkProps = gd.GetDeviceExtensionProperties();
            var veldridProps = new ExtensionProperties[vkProps.Length];

            for (int i = 0; i < vkProps.Length; i++)
            {
                var prop = vkProps[i];
                veldridProps[i] = new ExtensionProperties(Util.GetString(prop.extensionName), prop.specVersion);
            }

            return new ReadOnlyCollection<ExtensionProperties>(veldridProps);
        }
    }
}
#endif
