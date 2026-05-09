// Shared context passed from Program.cs to benchmark classes via static fields.
// BenchmarkDotNet constructs benchmark classes via reflection and cannot pass constructor
// arguments, so we use a static context — the same pattern as ANGLE's perf test fixtures which
// store a global TestParams pointer accessible from the test's SetUp/TearDown methods.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Veldrid;

namespace Veldrid.Benchmarks
{
    /// <summary>
    ///     Backend selected on the command line; set by Program.cs before BenchmarkRunner runs.
    /// </summary>
    internal static class BenchmarkContext
    {
        internal static GraphicsBackend Backend { get; set; } = GraphicsBackend.Vulkan;
        internal static string BackendName { get; set; } = "vulkan";
    }

    /// <summary>
    ///     Resolves a backend name string to a <see cref="GraphicsBackend" /> enum value,
    ///     checking platform availability before returning.
    /// </summary>
    internal static class BackendHelper
    {
        internal static bool TryResolve(
            string name,
            out GraphicsBackend backend,
            out string? skipReason)
        {
            backend = GraphicsBackend.Vulkan;
            skipReason = null;

            switch (name.ToLowerInvariant())
            {
                case "vulkan":
                    backend = GraphicsBackend.Vulkan;
                    return true;

                case "d3d11":
                case "d3d11-warp":
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        skipReason = $"Backend '{name}' is only supported on Windows.";
                        return false;
                    }

                    backend = GraphicsBackend.Direct3D11;
                    return true;

                case "d3d12":
                case "d3d12-warp":
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        skipReason = $"Backend '{name}' is only supported on Windows.";
                        return false;
                    }

                    backend = GraphicsBackend.Direct3D12;
                    return true;

                case "metal":
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        skipReason = $"Backend 'metal' is only supported on macOS/iOS.";
                        return false;
                    }

                    backend = GraphicsBackend.Metal;
                    return true;

                default:
                    skipReason =
                        $"Unknown backend '{name}'. Valid: vulkan, d3d11, d3d11-warp, d3d12, d3d12-warp, metal.";
                    return false;
            }
        }

        /// <summary>Creates a headless (no swapchain) <see cref="GraphicsDevice" />.</summary>
        internal static GraphicsDevice CreateHeadlessDevice(string backendName, bool debug = false)
        {
            var options = new GraphicsDeviceOptions
            {
                Debug = debug,
                HasMainSwapchain = false,
            };

            switch (backendName.ToLowerInvariant())
            {
                case "vulkan":
                    return GraphicsDevice.CreateVulkan(options);

                case "d3d11":
                    return CreateD3D11(options, warp: false);

                case "d3d11-warp":
                    return CreateD3D11(options, warp: true);

                case "d3d12":
                    return CreateD3D12(options, warp: false);

                case "d3d12-warp":
                    return CreateD3D12(options, warp: true);

                case "metal":
                    return CreateMetal(options);

                default:
                    throw new ArgumentException(
                        $"Unknown backend '{backendName}'.", nameof(backendName));
            }
        }

        [SupportedOSPlatform("windows")]
        private static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options, bool warp)
            => GraphicsDevice.CreateD3D11(options, new D3D11DeviceOptions { UseWarpAdapter = warp });

        [SupportedOSPlatform("windows")]
        private static GraphicsDevice CreateD3D12(GraphicsDeviceOptions options, bool warp)
            => GraphicsDevice.CreateD3D12(options, new D3D12DeviceOptions { UseWarpAdapter = warp });

        [SupportedOSPlatform("osx")]
        private static GraphicsDevice CreateMetal(GraphicsDeviceOptions options)
            => GraphicsDevice.CreateMetal(options);
    }
}
