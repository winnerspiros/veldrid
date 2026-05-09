using System;

namespace Veldrid
{
    /// <summary>
    ///     A structure describing Direct3D11-specific device creation options.
    /// </summary>
    public struct D3D11DeviceOptions
    {
        /// <summary>
        ///     Native pointer to an adapter.
        /// </summary>
        public IntPtr AdapterPtr;

        /// <summary>
        ///     Set of device specific flags.
        ///     See <see cref="Vortice.Direct3D11.DeviceCreationFlags" /> for details.
        /// </summary>
        public uint DeviceCreationFlags;

        /// <summary>
        ///     When true, the device is created on the WARP software adapter instead of any hardware GPU.
        ///     Useful for headless CI environments where no physical GPU is available.
        /// </summary>
        public bool UseWarpAdapter;
    }
}
