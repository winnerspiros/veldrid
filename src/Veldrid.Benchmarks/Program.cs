// Veldrid.Benchmarks — GPU-backend CPU-overhead benchmark suite.
//
// Methodology (derived from ANGLE angle_perftests and VK-GL-CTS calibrated-timestamp patterns):
//   • BenchmarkDotNet controls warmup, iteration count, and outlier removal.
//   • Each benchmark creates one headless GraphicsDevice per backend, runs setup once
//     (GlobalSetup), and tears down in GlobalCleanup — matching ANGLE's per-test fixture model.
//   • GPU timing is not the focus here: these measure CPU-side Veldrid API overhead (driver
//     call dispatch, resource tracking, command recording). Add VK_KHR_calibrated_timestamps
//     queries in a future pass for GPU-time breakdowns (see VK-GL-CTS vktPipelineTimestampTests).
//
// Usage:
//   # Run all benchmarks against the Vulkan backend (lavapipe in CI):
//   dotnet run -c Release -- --backend vulkan
//
//   # Run only buffer benchmarks against D3D12-WARP (Windows CI):
//   dotnet run -c Release -- --backend d3d12-warp --filter *Buffer*
//
//   # List all benchmark cases without running:
//   dotnet run -c Release -- --backend vulkan --list flat
//
// Supported backend arguments:
//   vulkan | d3d11 | d3d11-warp | d3d12 | d3d12-warp | metal
//
// CI integration:
//   See .github/workflows/benchmarks.yml — weekly scheduled job exporting JSON via
//   BenchmarkDotNet --exporters JSON and archiving as a workflow artifact for trend tracking.

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Veldrid;
using Veldrid.Benchmarks;

return Run(args);

static int Run(string[] args)
{
    // Parse --backend <name> and strip it from args before BenchmarkDotNet sees them.
    string backendName = "vulkan"; // default
    var remainingArgs = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            backendName = args[++i];
        else
            remainingArgs.Add(args[i]);
    }

    if (!BackendHelper.TryResolve(backendName, out GraphicsBackend backend, out string? skipReason))
    {
        Console.Error.WriteLine($"[bench] Skipping — {skipReason}");
        return 0;
    }

    BenchmarkContext.Backend = backend;
    BenchmarkContext.BackendName = backendName;

    var config = ManualConfig.Create(DefaultConfig.Instance)
        .AddExporter(JsonExporter.Full)   // JSON for trend tracking in CI
        .AddJob(Job.Default
            .WithWarmupCount(3)           // match ANGLE's ~1.5 s warmup floor
            .WithIterationCount(10));      // matches ANGLE's 10-sample default

    Console.WriteLine($"[bench] backend={backendName} ({backend})");

    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
        .Run(remainingArgs.ToArray(), config);

    return 0;
}

// Entry point class (needed for top-level statements to be found by reflection).
namespace Veldrid.Benchmarks
{
    // Re-export for the namespace to resolve.
    internal static class Program { }
}
