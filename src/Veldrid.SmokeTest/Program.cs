// Veldrid.SmokeTest — headless backend sanity check for CI.
//
// Usage:
//   Veldrid.SmokeTest --backend <name> [--validation]
//
// Supported backend names:
//   vulkan         Vulkan (use VK_ICD_FILENAMES=.../lvp_icd.json for lavapipe on Linux)
//   d3d11          D3D11 on the default hardware adapter (Windows only)
//   d3d11-warp     D3D11 on the WARP software adapter  (Windows only)
//   d3d12          D3D12 on the default hardware adapter (Windows only)
//   d3d12-warp     D3D12 on the WARP software adapter  (Windows only)
//   metal          Metal (macOS / iOS only)
//
// Options:
//   --validation   Enable the Vulkan validation layer (VK_LAYER_KHRONOS_validation).
//                  Requires the vulkan-validationlayers package to be installed.
//                  Ignored for non-Vulkan backends.
//
// The test:
//   1. Creates a headless GraphicsDevice (no window / no swapchain).
//   2. Allocates a staging DeviceBuffer, uploads a known pattern, reads it back, verifies.
//   3. Allocates a 64x64 RGBA8_UNorm render-target Texture (no draw, just allocation).
//   4. Clears the render-target to a known color via a CommandList, copies it to a staging
//      texture, reads back the pixel data and verifies the expected RGBA values.
//      This exercises: Framebuffer creation, CommandList recording, SetFramebuffer,
//      ClearColorTarget, CopyTexture, and staging-texture readback — the full render path
//      without needing any shaders.
//   5. Prints a PASS line and exits 0, or prints the exception and exits 1.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Veldrid;

return Run(args);

static int Run(string[] args)
{
    string? backendArg = null;
    bool validation = false;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--backend" && i + 1 < args.Length)
            backendArg = args[++i].ToLowerInvariant();
        else if (args[i] == "--validation")
            validation = true;
    }

    if (backendArg == null)
    {
        Console.Error.WriteLine("Usage: Veldrid.SmokeTest --backend <vulkan|d3d11|d3d11-warp|d3d12|d3d12-warp|metal> [--validation]");
        return 1;
    }

    try
    {
        using GraphicsDevice gd = CreateDevice(backendArg, validation);
        Console.WriteLine($"[smoke] backend={gd.BackendType} device=\"{gd.DeviceName}\" vendor=\"{gd.VendorName}\"");
        RunBufferRoundTrip(gd);
        RunTextureAllocation(gd);
        RunClearAndReadback(gd);
        Console.WriteLine($"[smoke] PASS — {gd.BackendType} ({gd.DeviceName})");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[smoke] FAIL — {ex}");
        return 1;
    }
}

static GraphicsDevice CreateDevice(string backend, bool validation)
{
    var options = new GraphicsDeviceOptions
    {
        // Enable the Vulkan validation layer (VK_LAYER_KHRONOS_validation) when requested.
        // For other backends Debug=true enables backend-specific debug features.
        Debug = validation,
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

// Clear a 16x16 render-target to a known color (orange: R=255, G=128, B=0, A=255),
// copy it to a staging texture, read back the first pixel, and verify.
//
// This exercises (without needing any shaders):
//   - Framebuffer creation
//   - CommandList recording: Begin, SetFramebuffer, ClearColorTarget, End
//   - A second CommandList: CopyTexture (image-to-buffer via vkCmdCopyImageToBuffer)
//   - GPU submission (SubmitCommands + WaitForIdle) for each pass
//   - Staging texture mapping and pixel readback
//
// Note: ClearColorTarget queues a clear value that is applied at render-pass BEGIN
// (as VkAttachmentDescription.loadOp = Clear). In Veldrid's Vulkan backend the render
// pass starts lazily — it is opened by End() when no draw commands were recorded.
// The copy must therefore happen in a SEPARATE command list submitted after the clear
// has completed on the GPU, otherwise CopyTexture would capture the pre-clear content.
static unsafe void RunClearAndReadback(GraphicsDevice gd)
{
    const uint size = 16;
    const PixelFormat fmt = PixelFormat.R8G8B8A8UNorm;
    var clearColor = new RgbaFloat(1.0f, 0.5f, 0.0f, 1.0f); // orange

    // Render-target texture
    using Texture rtTex = gd.ResourceFactory.CreateTexture(
        TextureDescription.Texture2D(size, size, 1, 1, fmt, TextureUsage.RenderTarget | TextureUsage.Sampled));

    // Framebuffer wrapping the render-target
    using Framebuffer fb = gd.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, rtTex));

    // Staging texture for CPU readback
    using Texture stagingTex = gd.ResourceFactory.CreateTexture(
        TextureDescription.Texture2D(size, size, 1, 1, fmt, TextureUsage.Staging));

    // Pass 1: clear the render-target.
    // ClearColorTarget queues the color as a render-pass loadOp=Clear value; the render
    // pass itself is opened (and immediately closed) by End() when no draw commands were
    // recorded.  The GPU must finish this pass before we can safely copy the result.
    using (CommandList cl = gd.ResourceFactory.CreateCommandList())
    {
        cl.Begin();
        cl.SetFramebuffer(fb);
        cl.ClearColorTarget(0, clearColor);
        cl.End();
        gd.SubmitCommands(cl);
    }

    gd.WaitForIdle();

    // Pass 2: copy the cleared render-target into the staging texture.
    using (CommandList cl = gd.ResourceFactory.CreateCommandList())
    {
        cl.Begin();
        cl.CopyTexture(rtTex, stagingTex);
        cl.End();
        gd.SubmitCommands(cl);
    }

    gd.WaitForIdle();

    // Read back and verify the first pixel.
    var mapped = gd.Map(stagingTex, MapMode.Read, 0);
    try
    {
        var pixels = new ReadOnlySpan<byte>((void*)mapped.Data, (int)(size * size * 4));
        byte r = pixels[0];
        byte g = pixels[1];
        byte b = pixels[2];
        byte a = pixels[3];

        // Allow ±2 to account for rounding in UNorm conversion (255 * 0.5f = 127.5 → 127 or 128).
        static void assertChannel(string name, byte actual, int expected)
        {
            if (Math.Abs(actual - expected) > 2)
                throw new Exception($"Clear+readback pixel mismatch on channel {name}: expected ~{expected}, got {actual}");
        }

        assertChannel("R", r, 255);
        assertChannel("G", g, 128);
        assertChannel("B", b, 0);
        assertChannel("A", a, 255);
    }
    finally
    {
        gd.Unmap(stagingTex, 0);
    }

    Console.WriteLine($"[smoke] clear+readback OK (pixel R={clearColor.R * 255:F0} G={clearColor.G * 255:F0} B={clearColor.B * 255:F0} A={clearColor.A * 255:F0})");
}

