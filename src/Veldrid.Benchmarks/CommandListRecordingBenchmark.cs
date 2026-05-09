// CommandListRecordingBenchmark — measures CommandList recording CPU overhead.
//
// Methodology (ANGLE angle_perftests draw-call-overhead pattern):
//   ANGLE's draw_call_perf_tests measure the CPU time to record N glClear / glDrawArrays calls
//   in a single frame.  We replicate this without shaders using ClearColorTarget calls —
//   these exercise the full Veldrid command-recording path (Begin, state, End) without
//   requiring any shader compilation or pipeline creation, making them runnable on all
//   backends in CI with software renderers.
//
//   Measuring N=100 and N=1000 calls per CommandList isolates:
//   - Fixed per-Begin/End overhead (amortized differently at N=100 vs N=1000)
//   - Per-command dispatch cost (the slope of the N→ cost line)
//   Both calls include SubmitCommands+WaitForIdle to measure the full GPU-side flush cost.

using System;
using BenchmarkDotNet.Attributes;
using Veldrid;

namespace Veldrid.Benchmarks
{
    [MemoryDiagnoser]
    public class CommandListRecordingBenchmark
    {
        [Params(100, 1000)]
        public int ClearCallsPerList { get; set; }

        private GraphicsDevice gd = null!;
        private CommandList cl = null!;
        private Texture renderTarget = null!;
        private Framebuffer framebuffer = null!;

        [GlobalSetup]
        public void Setup()
        {
            gd = BackendHelper.CreateHeadlessDevice(BenchmarkContext.BackendName);
            Console.WriteLine(
                $"[bench] CommandListRecording device=\"{gd.DeviceName}\" backend={gd.BackendType}");

            // 64×64 render-target — smallest usable size; resolution does not affect the
            // recording CPU overhead we are measuring.
            renderTarget = gd.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    64, 64,
                    mipLevels: 1, arrayLayers: 1,
                    PixelFormat.R8G8B8A8UNorm,
                    TextureUsage.RenderTarget));

            framebuffer = gd.ResourceFactory.CreateFramebuffer(
                new FramebufferDescription(null, renderTarget));

            cl = gd.ResourceFactory.CreateCommandList();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            cl.Dispose();
            framebuffer.Dispose();
            renderTarget.Dispose();
            gd.Dispose();
        }

        // Records ClearCallsPerList ClearColorTarget commands in a single CommandList.
        // Cycling through four different clear colors prevents the driver from optimizing
        // away identical clears — matching ANGLE's practice of using varying draw parameters
        // to prevent command-buffer deduplication by the driver.
        [Benchmark]
        public void RecordAndSubmit()
        {
            cl.Begin();
            cl.SetFramebuffer(framebuffer);

            int n = ClearCallsPerList;
            for (int i = 0; i < n; i++)
            {
                // Rotate colour across 4 values to defeat driver dedup.
                var color = (i & 3) switch
                {
                    0 => RgbaFloat.RED,
                    1 => RgbaFloat.GREEN,
                    2 => RgbaFloat.BLUE,
                    _ => RgbaFloat.WHITE,
                };
                cl.ClearColorTarget(0, color);
            }

            cl.End();
            gd.SubmitCommands(cl);
            gd.WaitForIdle();
        }
    }
}
