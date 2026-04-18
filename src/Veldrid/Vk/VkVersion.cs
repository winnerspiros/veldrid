namespace Veldrid.Vk
{
    internal struct VkVersion
    {
        private readonly uint value;

        public VkVersion(uint major, uint minor, uint patch)
        {
            value = (major << 22) | (minor << 12) | patch;
        }

        /// <summary>
        ///     Wraps a raw Vulkan-packed version uint (e.g. from physicalDeviceProperties.apiVersion).
        /// </summary>
        public static VkVersion FromPacked(uint packed) => new VkVersion(packed);

        public uint Major => value >> 22;

        public uint Minor => (value >> 12) & 0x3ff;

        public uint Patch => value & 0xfff;

        public bool IsAtLeast(uint major, uint minor) => value >= new VkVersion(major, minor, 0);

        public static implicit operator uint(VkVersion version)
        {
            return version.value;
        }

        // Private constructor used by FromPacked.
        private VkVersion(uint raw) => value = raw;
    }
}
