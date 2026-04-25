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
        ///     Indicates whether the device supports VK_KHR_synchronization2 (core in Vulkan 1.3).
        ///     Detection-only at present; submission paths still use legacy vkQueueSubmit. Surfaced so
        ///     a follow-up change can migrate the per-CL fence pool to vkQueueSubmit2 + timeline
        ///     semaphores without re-touching device-creation code.
        /// </summary>
        public bool HasSynchronization2 => gd.HasSynchronization2;

        /// <summary>
        ///     Indicates whether the device supports VK_KHR_timeline_semaphore (core in Vulkan 1.2).
        ///     See <see cref="HasSynchronization2" /> for context.
        /// </summary>
        public bool HasTimelineSemaphore => gd.HasTimelineSemaphore;

        /// <summary>
        ///     Returns the current contents of the device's <c>VkPipelineCache</c> as a serialised blob,
        ///     suitable for persisting to disk and feeding back into
        ///     <see cref="VulkanDeviceOptions.PipelineCacheData" /> on the next launch. The blob's first
        ///     bytes are a driver-validated header (vendorID / deviceID / driver UUID), so it is always
        ///     safe to round-trip stale data — the driver silently discards mismatched blobs at create
        ///     time. Should typically be called once just before disposing the GraphicsDevice. Returns
        ///     an empty array if the cache is empty or the driver returns no data.
        /// </summary>
        /// <remarks>
        ///     This call is a two-pass query (size, then data) and may briefly contend with concurrent
        ///     pipeline creation. Avoid calling it on the render hot-path.
        /// </remarks>
        public unsafe byte[] GetPipelineCacheData()
        {
            var pipelineCache = gd.PipelineCache;
            if (pipelineCache.Handle == 0)
                return Array.Empty<byte>();

            UIntPtr size = UIntPtr.Zero;
            var sizeResult = Vulkan.VulkanNative.vkGetPipelineCacheData(gd.Device, pipelineCache, ref size, null);
            if (sizeResult != Vulkan.VkResult.Success || size == UIntPtr.Zero)
                return Array.Empty<byte>();

            byte[] data = new byte[(int)size];
            fixed (byte* dataPtr = data)
            {
                var dataResult = Vulkan.VulkanNative.vkGetPipelineCacheData(gd.Device, pipelineCache, ref size, dataPtr);
                // VK_INCOMPLETE means the cache grew between the two calls — treat the partial as truncated.
                if (dataResult != Vulkan.VkResult.Success && dataResult != Vulkan.VkResult.Incomplete)
                    return Array.Empty<byte>();
            }

            // size may have been clamped down by the driver between calls — slice if so.
            int actual = (int)size;
            if (actual == data.Length) return data;
            byte[] trimmed = new byte[actual];
            Array.Copy(data, trimmed, actual);
            return trimmed;
        }

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
