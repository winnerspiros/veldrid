using System;

namespace Veldrid
{
    /// <summary>
    ///     A structure describing Direct3D12-specific device creation options.
    /// </summary>
    public struct D3D12DeviceOptions
    {
        /// <summary>
        ///     Native pointer to a DXGI adapter to use. If <see cref="IntPtr.Zero"/>, the default adapter is used.
        /// </summary>
        public IntPtr AdapterPtr;

        /// <summary>
        ///     Whether to enable the D3D12 debug layer for validation.
        /// </summary>
        public bool EnableDebugLayer;

        /// <summary>
        ///     Whether to enable GPU-based validation (requires debug layer).
        ///     This is more thorough but significantly slower.
        /// </summary>
        public bool EnableGpuValidation;
    }
}
