namespace Veldrid
{
    /// <summary>
    ///     A structure describing Vulkan-specific device creation options.
    /// </summary>
    public struct VulkanDeviceOptions
    {
        /// <summary>
        ///     An array of required Vulkan instance extensions. Entries in this array will be enabled in the GraphicsDevice's
        ///     created VkInstance.
        /// </summary>
        public string[] InstanceExtensions;

        /// <summary>
        ///     An array of required Vulkan device extensions. Entries in this array will be enabled in the GraphicsDevice's
        ///     created VkDevice.
        /// </summary>
        public string[] DeviceExtensions;

        /// <summary>
        ///     Optional pre-warmed VkPipelineCache blob, typically obtained from a previous run via
        ///     <see cref="BackendInfoVulkan.GetPipelineCacheData" /> and persisted to disk. When supplied, the data is
        ///     passed through <c>VkPipelineCacheCreateInfo.pInitialData</c> at device creation, letting the driver
        ///     skip recompiling pipelines whose SPIR-V matches a previously-seen entry. The driver validates the blob
        ///     header (vendorID / deviceID / driver UUID) and silently discards it if it does not match the current
        ///     device, so it is always safe to pass through stale data without manual versioning. Pass <c>null</c> on
        ///     first launch.
        /// </summary>
        public byte[] PipelineCacheData;

        /// <summary>
        ///     Constructs a new VulkanDeviceOptions.
        /// </summary>
        /// <param name="instanceExtensions">
        ///     An array of required Vulkan instance extensions. Entries in this array will be
        ///     enabled in the GraphicsDevice's created VkInstance.
        /// </param>
        /// <param name="deviceExtensions">
        ///     An array of required Vulkan device extensions. Entries in this array will be enabled
        ///     in the GraphicsDevice's created VkDevice.
        /// </param>
        public VulkanDeviceOptions(string[] instanceExtensions, string[] deviceExtensions)
        {
            InstanceExtensions = instanceExtensions;
            DeviceExtensions = deviceExtensions;
        }
    }
}
