#if !EXCLUDE_D3D12_BACKEND
using System;
using System.Runtime.Versioning;
using Veldrid.D3D12;

namespace Veldrid
{
    /// <summary>
    ///     Exposes Direct3D 12-specific functionality,
    ///     useful for interoperating with native components which interface directly with Direct3D 12.
    ///     Can only be used on <see cref="GraphicsBackend.Direct3D12" />.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class BackendInfoD3D12
    {
        /// <summary>
        ///     Gets a pointer to the ID3D12Device controlled by the GraphicsDevice.
        /// </summary>
        public IntPtr Device => gd.Device.NativePointer;

        /// <summary>
        ///     Gets a pointer to the ID3D12CommandQueue used by the GraphicsDevice.
        /// </summary>
        public IntPtr CommandQueue => gd.CommandQueue.NativePointer;

        /// <summary>
        ///     Gets a pointer to the IDXGIFactory4 used by the GraphicsDevice.
        /// </summary>
        public IntPtr DxgiFactory => gd.DxgiFactory.NativePointer;

        private readonly D3D12GraphicsDevice gd;

        internal BackendInfoD3D12(D3D12GraphicsDevice gd)
        {
            this.gd = gd;
        }

        /// <summary>
        ///     Gets a pointer to the native ID3D12Resource wrapped by the given Veldrid Texture.
        /// </summary>
        /// <returns>A pointer to the Veldrid Texture's underlying ID3D12Resource.</returns>
        public IntPtr GetTexturePointer(Texture texture)
        {
            return Util.AssertSubtype<Texture, D3D12Texture>(texture).Resource.NativePointer;
        }

        /// <summary>
        ///     Gets a pointer to the native ID3D12Resource wrapped by the given Veldrid DeviceBuffer.
        /// </summary>
        /// <returns>A pointer to the Veldrid DeviceBuffer's underlying ID3D12Resource.</returns>
        public IntPtr GetBufferPointer(DeviceBuffer buffer)
        {
            return Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(buffer).Resource.NativePointer;
        }

        /// <summary>
        ///     Indicates whether the GPU supports Enhanced Barriers (D3D12 Options12).
        /// </summary>
        public bool SupportsEnhancedBarriers => gd.SupportsEnhancedBarriers;

        /// <summary>
        ///     Indicates whether the GPU supports Mesh Shaders (D3D12 Options7, Tier 1+).
        /// </summary>
        public bool SupportsMeshShaders => gd.SupportsMeshShaders;

        /// <summary>
        ///     Indicates whether the GPU supports Variable Rate Shading (D3D12 Options6, Tier 1+).
        /// </summary>
        public bool SupportsVariableRateShading => gd.SupportsVariableRateShading;

        /// <summary>
        ///     Indicates whether the GPU supports DXR Raytracing (D3D12 Options5, Tier 1.0+).
        /// </summary>
        public bool SupportsRaytracing => gd.SupportsRaytracing;
    }
}
#endif
