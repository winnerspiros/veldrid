// Veldrid.SmokeTest — headless backend sanity check for CI.
//
// Usage:
//   Veldrid.SmokeTest --backend <name>
//
// Supported backend names:
//   vulkan         Vulkan (use VK_ICD_FILENAMES=.../lvp_icd.json for lavapipe on Linux)
//   d3d11          D3D11 on the default hardware adapter (Windows only)
//   d3d11-warp     D3D11 on the WARP software adapter  (Windows only)
//   d3d12          D3D12 on the default hardware adapter (Windows only)
//   d3d12-warp     D3D12 on the WARP software adapter  (Windows only)
//   metal          Metal (macOS / iOS only)
//
// The test:
//   1. Creates a headless GraphicsDevice (no window / no swapchain).
//   2. Allocates a staging DeviceBuffer, uploads a known pattern, reads it back, verifies.
//   3. Allocates a 64x64 RGBA8_UNorm render-target Texture (no draw, just allocation).
//   4. Prints a PASS line and exits 0, or prints the exception and exits 1.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Veldrid;

return Run(args);

static int Run(string[] args)
{
    string? backendArg = null;
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--backend")
        {
            backendArg = args[i + 1].ToLowerInvariant();
            break;
        }
    }

    if (backendArg == null)
    {
        Console.Error.WriteLine("Usage: Veldrid.SmokeTest --backend <vulkan|d3d11|d3d11-warp|d3d12|d3d12-warp|metal>");
        return 1;
    }

    try
    {
        using GraphicsDevice gd = CreateDevice(backendArg);
        Console.WriteLine($"[smoke] backend={gd.BackendType} device=\"{gd.DeviceName}\" vendor=\"{gd.VendorName}\"");
        RunBufferRoundTrip(gd);
        RunTextureAllocation(gd);
        Console.WriteLine($"[smoke] PASS — {gd.BackendType} ({gd.DeviceName})");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[smoke] FAIL — {ex}");
        return 1;
    }
}

static GraphicsDevice CreateDevice(string backend)
{
    var options = new GraphicsDeviceOptions
    {
        Debug = false,
        HasMainSwapchain = false,
    };

    switch (backend)
    {
        case "vulkan":
            return GraphicsDevice.CreateVulkan(options);

        case "d3d11":
            EnsureWindows(backend);
            return CreateD3D11(options, warp: false);

        case "d3d11-warp":
            EnsureWindows(backend);
            return CreateD3D11(options, warp: true);

        case "d3d12":
            EnsureWindows(backend);
            return CreateD3D12(options, warp: false);

        case "d3d12-warp":
            EnsureWindows(backend);
            return CreateD3D12(options, warp: true);

        case "metal":
            EnsureMac(backend);
            return CreateMetal(options);

        default:
            throw new ArgumentException($"Unknown backend '{backend}'. Valid choices: vulkan, d3d11, d3d11-warp, d3d12, d3d12-warp, metal.");
    }
}

static void EnsureWindows(string backend)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        throw new PlatformNotSupportedException($"Backend '{backend}' is only supported on Windows.");
}

static void EnsureMac(string backend)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        throw new PlatformNotSupportedException($"Backend '{backend}' is only supported on macOS/iOS.");
}

[SupportedOSPlatform("windows")]
static GraphicsDevice CreateD3D11(GraphicsDeviceOptions options, bool warp)
{
    var d3d11Options = new D3D11DeviceOptions { UseWarpAdapter = warp };
    return GraphicsDevice.CreateD3D11(options, d3d11Options);
}

[SupportedOSPlatform("windows")]
static GraphicsDevice CreateD3D12(GraphicsDeviceOptions options, bool warp)
{
    var d3d12Options = new D3D12DeviceOptions { UseWarpAdapter = warp };
    return GraphicsDevice.CreateD3D12(options, d3d12Options);
}

[SupportedOSPlatform("osx")]
static GraphicsDevice CreateMetal(GraphicsDeviceOptions options)
{
    return GraphicsDevice.CreateMetal(options);
}

// Upload a known uint[] pattern to a staging buffer, read it back, verify.
static unsafe void RunBufferRoundTrip(GraphicsDevice gd)
{
    const uint count = 256;
    const uint sizeInBytes = count * sizeof(uint);

    // Source data: ascending integers.
    var sourceData = new uint[count];
    for (uint i = 0; i < count; i++) sourceData[i] = i * 7u + 13u;

    // Create a Staging buffer (CPU-readable + writable).
    var bufDesc = new BufferDescription(sizeInBytes, BufferUsage.Staging);
    using DeviceBuffer stagingBuf = gd.ResourceFactory.CreateBuffer(bufDesc);

    // Upload via UpdateBuffer then Map+read back.
    fixed (uint* ptr = sourceData)
        gd.UpdateBuffer(stagingBuf, 0, (IntPtr)ptr, sizeInBytes);

    gd.WaitForIdle();

    var mapped = gd.Map(stagingBuf, MapMode.Read);
    try
    {
        var readback = new ReadOnlySpan<uint>((void*)mapped.Data, (int)count);
        for (int i = 0; i < (int)count; i++)
        {
            if (readback[i] != sourceData[i])
                throw new Exception($"Buffer round-trip mismatch at index {i}: expected {sourceData[i]}, got {readback[i]}");
        }
    }
    finally
    {
        gd.Unmap(stagingBuf);
    }

    Console.WriteLine("[smoke] buffer round-trip OK");
}

// Just allocate a render-target texture — exercises the resource-creation path.
static void RunTextureAllocation(GraphicsDevice gd)
{
    var texDesc = TextureDescription.Texture2D(
        64, 64,
        mipLevels: 1,
        arrayLayers: 1,
        PixelFormat.R8G8B8A8UNorm,
        TextureUsage.RenderTarget);

    using Texture tex = gd.ResourceFactory.CreateTexture(texDesc);
    Console.WriteLine($"[smoke] texture allocation OK (64x64 RenderTarget, format={tex.Format})");
}
