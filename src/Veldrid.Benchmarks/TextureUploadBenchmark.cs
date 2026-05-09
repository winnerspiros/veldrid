// TextureUploadBenchmark — measures the CPU overhead of UpdateTexture + WaitForIdle.
//
// Methodology (ANGLE angle_perftests texSubImage pattern / VK-GL-CTS host-image-copy tests):
//   ANGLE benchmarks texSubImage2D with varying dimensions to isolate staging-buffer mapping
//   cost from transfer scheduling.  Veldrid's UpdateTexture goes through the same staging-buffer
//   → CopyTexture path on all backends, making the measurement backend-agnostic.
//
//   Dimensions chosen:
//     64×64   —  thumbnail / icon / small atlas region (~16 KB)
//    512×512  —  typical material texture (~1 MB)
//   2048×2048 —  4K streaming mip level (~16 MB)

using System;
using BenchmarkDotNet.Attributes;
using Veldrid;

namespace Veldrid.Benchmarks
{
    [MemoryDiagnoser]
    public class TextureUploadBenchmark
    {
        [Params(64, 512, 2048)]
        public int TextureSize { get; set; }

        private GraphicsDevice gd = null!;
        private Texture texture = null!;
        private byte[] srcData = null!;

        [GlobalSetup]
        public void Setup()
        {
            gd = BackendHelper.CreateHeadlessDevice(BenchmarkContext.BackendName);
            Console.WriteLine(
                $"[bench] TextureUpload device=\"{gd.DeviceName}\" backend={gd.BackendType}");

            // Allocate a 2D RGBA8_UNorm texture at maximum benchmark size so the texture object
            // can be reused across parameter variants without re-creation.
            const int maxSize = 2048;
            texture = gd.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    (uint)maxSize, (uint)maxSize,
                    mipLevels: 1, arrayLayers: 1,
                    PixelFormat.R8G8B8A8UNorm,
                    TextureUsage.Sampled));

            // RGBA8 = 4 bytes per pixel.
            srcData = new byte[maxSize * maxSize * 4];
            for (int i = 0; i < srcData.Length; i++) srcData[i] = (byte)i;
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            texture.Dispose();
            gd.Dispose();
        }

        // Each iteration uploads a square region of 'TextureSize' pixels.
        // UpdateTexture creates a staging buffer, copies data, and schedules a GPU transfer —
        // identical to VK-GL-CTS's vkCmdCopyBufferToImage path used in host-image-copy tests.
        [Benchmark]
        public unsafe void UpdateAndWait()
        {
            uint sz = (uint)TextureSize;
            uint byteCount = sz * sz * 4; // RGBA8

            fixed (byte* ptr = srcData)
                gd.UpdateTexture(
                    texture,
                    (IntPtr)ptr,
                    byteCount,
                    x: 0, y: 0, z: 0,
                    width: sz, height: sz, depth: 1,
                    mipLevel: 0, arrayLayer: 0);

            gd.WaitForIdle();
        }
    }
}
