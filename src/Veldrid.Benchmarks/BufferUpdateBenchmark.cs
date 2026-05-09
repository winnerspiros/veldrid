// BufferUpdateBenchmark — measures the CPU overhead of UpdateBuffer + WaitForIdle.
//
// Methodology (ANGLE angle_perftests / VK-GL-CTS deGetMicroseconds pattern):
//   ANGLE measures buffer upload cost via a tight loop of glBufferSubData calls followed by
//   glFinish, parametrized by upload size.  We replicate this in Veldrid terms:
//     UpdateBuffer(staging, 0, ptr, size) → WaitForIdle()
//   This captures: staging-buffer map/unmap, host→GPU transfer scheduling, and the
//   synchronization cost of waiting for the GPU to finish.
//
//   Sizes chosen to span three tiers:
//     1 KB  — per-object constant data (UBO / push constant alternative)
//    64 KB  — typical per-frame dynamic vertex buffer
//     4 MB  — streaming texture mip level or large dynamic mesh

using System;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Veldrid;

namespace Veldrid.Benchmarks
{
    [MemoryDiagnoser]
    public class BufferUpdateBenchmark
    {
        [Params(1024, 65536, 4 * 1024 * 1024)]
        public int SizeInBytes { get; set; }

        private GraphicsDevice gd = null!;
        private DeviceBuffer stagingBuf = null!;
        private byte[] srcData = null!;

        [GlobalSetup]
        public void Setup()
        {
            gd = BackendHelper.CreateHeadlessDevice(BenchmarkContext.BackendName);
            Console.WriteLine(
                $"[bench] BufferUpdate device=\"{gd.DeviceName}\" backend={gd.BackendType}");

            // Allocate both staging buffer and source data at maximum param size so that
            // BenchmarkDotNet can reuse this Setup across all Params values without re-init.
            const int maxSize = 4 * 1024 * 1024;
            stagingBuf = gd.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)maxSize, BufferUsage.Staging));

            srcData = new byte[maxSize];
            // Fill with a non-trivial pattern to prevent the driver zeroing the transfer.
            for (int i = 0; i < maxSize; i++) srcData[i] = (byte)(i ^ (i >> 8));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            stagingBuf.Dispose();
            gd.Dispose();
        }

        // VK-GL-CTS pattern: upload data, call WaitForIdle (equivalent to vkQueueWaitIdle after
        // a vkCmdCopyBuffer), measure total round-trip including CPU→GPU synchronisation cost.
        [Benchmark]
        public unsafe void UpdateAndWait()
        {
            fixed (byte* ptr = srcData)
                gd.UpdateBuffer(stagingBuf, 0, (IntPtr)ptr, (uint)SizeInBytes);

            gd.WaitForIdle();
        }
    }
}
