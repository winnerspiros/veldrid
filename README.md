# ppy.Veldrid

Veldrid is a cross-platform, graphics API-agnostic rendering and compute library for .NET. It provides a powerful, unified interface to a system's GPU and includes more advanced features than any other .NET library. Unlike other platform- or vendor-specific technologies, Veldrid can be used to create high-performance 3D applications that are truly portable.

As of April 2024, this repository no longer tracks and is incompatible with the upstream [Veldrid](https://github.com/veldrid/veldrid) repository. This decision has been made to allow for more agile development without concerns of breaking changes.

## Changes from ppy fork

### New: Direct3D 12 Backend
- Full D3D12 backend implementation (`GraphicsBackend.Direct3D12`) with 18+ source files
- Complete resource lifecycle: Buffer, Texture, TextureView, Sampler, Shader, Fence, ResourceLayout, ResourceSet, Framebuffer, Pipeline, Swapchain
- D3D12CommandList with command allocator pooling for efficient multi-frame recording
- D3D12DescriptorAllocator with thread-safe free-list descriptor heap management
- D3D12CommandAllocatorPool with fence-gated allocator reuse
- D3D12Pipeline with root signature generation (CBV/SRV/UAV + Sampler descriptor tables)
- D3D12Swapchain with flip-discard model and tearing support
- Public API: `GraphicsDevice.CreateD3D12()` factory methods, `BackendInfoD3D12`, `D3D12DeviceOptions`
- `GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D12)` support

### Vortice.Windows Upgrade (2.4.2 → 3.8.3)
- Native .NET 10 support (previously relied on compatibility shims)
- Correct `uint` type mapping for C++ UINT types (was incorrectly `int`)
- Improved `Span<T>` usage and marshalling in Vortice internals
- AOT and trimming support
- All D3D11 and D3D12 backend code updated for the new type mappings

### Performance & Modernization
- All backends now use `System.Threading.Lock` instead of `object` locks (including OpenGL `StagingMemoryPool`)
- Vulkan backend: push descriptors (`VK_KHR_push_descriptor`), dynamic rendering (`VK_KHR_dynamic_rendering`), memory budget (`VK_EXT_memory_budget`), host image copy (`VK_EXT_host_image_copy`)
- Vulkan backend: `stackalloc` for descriptor sets and dynamic offsets in hot paths
- Vulkan backend: UTF-8 `u8` string literals for all proc address lookups (zero runtime encoding)
- D3D11 backend: pre-allocated arrays for vertex strides/offsets in draw calls, deferred context command recording
- Target framework upgraded to `net10.0` with `LangVersion 14.0`

### Bug Fixes from Upstream Issues & PRs
- **Vulkan: Remove `[Conditional("DEBUG")]` from `VulkanUtil.CheckResult`** — was silently swallowing Vulkan errors in release builds, causing untraceable segfaults ([ppy#61](https://github.com/ppy/veldrid/issues/61))
- **Vulkan: Fix depth texture copy** — `CopyTexture` between depth/stencil textures was using `VkImageAspectFlags.Color` instead of `Depth|Stencil`, causing validation errors or silent failures ([veldrid#462](https://github.com/veldrid/veldrid/pull/462))
- **`DisposeWhenIdle` now flushes on `SubmitCommands`** — previously resources passed to `DisposeWhenIdle` were only disposed when `WaitForIdle` was called, causing memory leaks in apps that never call `WaitForIdle` ([veldrid#476](https://github.com/veldrid/veldrid/issues/476))
- **Metal: Fix depth test when disabled** — `DepthComparison` forced to `Always` when `DepthTestEnabled` is false, preventing fragments from being incorrectly rejected ([ppy#75](https://github.com/ppy/veldrid/pull/75))
- **OpenGL: Fix `ClearDepthStencil` mask mutation** — was leaving stencil mask and depth write in incorrect state after clear, preventing `SetPipeline` from restoring them ([veldrid#481](https://github.com/veldrid/veldrid/pull/481))
- **Vulkan: Fix `CommandBufferCompleted` race condition** — `submittedCommandBuffers.Add` in `End()` now protected by the same lock as `CommandBufferCompleted` ([veldrid#495](https://github.com/veldrid/veldrid/pull/495))
- **D3D11: FlipDiscard swapchain** — uses modern flip model on DXGI 1.4+, reducing frame latency ([veldrid#484](https://github.com/veldrid/veldrid/pull/484), [veldrid#515](https://github.com/veldrid/veldrid/issues/515))
- **GLES: Stencil buffer init + 64-bit eglGetDisplay** — GLES now properly initializes stencil buffers and uses `IntPtr` for `eglGetDisplay` on 64-bit targets ([ppy#71](https://github.com/ppy/veldrid/pull/71))

### CI / Build
- CI workflow updated to .NET 10 SDK
- Multi-platform CI: Windows, Linux, and macOS build verification
- 0 errors, 0 warnings across all 3 projects in the solution

### Supported Backends
| Backend | Platforms | Status |
|---------|-----------|--------|
| **Direct3D 12** | Windows | ✅ New |
| **Direct3D 11** | Windows | ✅ Updated (Vortice 3.8.3) |
| **Vulkan** | Windows, Linux, Android | ✅ Optimized (push descriptors, dynamic rendering) |
| **Metal** | macOS, iOS | ✅ Maintained |
| **OpenGL** | Windows, Linux, macOS | ✅ Updated (modern locks) |
| **OpenGL ES** | Android, Linux | ✅ Maintained |
