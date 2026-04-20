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
- **Vulkan pipeline cache** — all graphics and compute pipeline creation now uses a shared `VkPipelineCache`, enabling driver-side caching and deduplication of compiled shaders (was `VkPipelineCache.Null` everywhere)
- Vulkan backend: push descriptors (`VK_KHR_push_descriptor`), dynamic rendering (`VK_KHR_dynamic_rendering`), memory budget (`VK_EXT_memory_budget`), host image copy (`VK_EXT_host_image_copy`)
- Vulkan backend: `VK_EXT_descriptor_indexing` detection (core in Vulkan 1.2) — enables future bindless descriptor patterns
- Vulkan backend: `stackalloc` for descriptor sets and dynamic offsets in hot paths
- Vulkan backend: UTF-8 `u8` string literals for all proc address lookups (zero runtime encoding)
- **Vulkan memory allocator** — block split on allocation now updates in-place instead of RemoveAt+Insert, eliminating O(n) array shifts in the memory allocation hot path
- **D3D12 command list** — redundant state tracking for scissor rects, blend factors, viewports, and framebuffers; skips GPU calls when state is unchanged (matching Vulkan backend's proven pattern)
- **D3D12 staging buffer pool** — swap-remove O(1) instead of RemoveAt O(n) for staging buffer reuse
- **D3D11 resource binding** — merged four sequential base-offset accumulation loops into a single pass per resource set activation, improving cache locality and reducing per-draw overhead; removed superseded dead code
- **D3D11 staging buffer pool** — swap-remove O(1) instead of foreach+Remove O(n²) for staging buffer lookup
- **OpenGL/ES pipeline state caching** — skips all blend, depth, stencil, rasterizer, and shader program GL calls when the same pipeline is re-activated (eliminates 30–50 redundant GL calls per draw in typical scenes)
- **OpenGL/ES resource set clear** — only clears used resource set slots instead of the full array on every draw call
- **Metal resource binding** — merged three per-resource O(n) layout offset loops (buffer, texture, sampler) into a single pass per resource set activation; hoisted vertex buffer index calculation to avoid redundant per-VB evaluation
- Vulkan backend: pre-sized sampled image list (capacity 32) to avoid hot-path reallocations during draw/dispatch
- D3D11 backend: pre-allocated arrays for vertex strides/offsets in draw calls, deferred context command recording
- Target framework upgraded to `net10.0` with `LangVersion 14.0`

### Mobile / Android Optimizations (Adreno / Mali / PowerVR tilers)
The following changes target tile-based mobile GPUs. They are also harmless (or beneficial) on desktop GPUs, so they're enabled unconditionally where the relevant API/extension is present.

- **Vulkan swapchain present-mode preference** — under `SwapchainDescription.SyncToVerticalBlank=true`, the swapchain now prefers `VK_PRESENT_MODE_MAILBOX_KHR` over `VK_PRESENT_MODE_FIFO_RELAXED_KHR`. Both are vsync-locked (no tearing), but `MAILBOX` *replaces* the queued frame instead of *queueing* it, eliminating one full frame of input-to-photon latency on Android's Choreographer pipeline. Fallback chain is `MAILBOX → FIFO_RELAXED → FIFO`; `FIFO` is mandatory per spec, so the choice is safe on every device. See `src/Veldrid/Vk/VkSwapchain.cs`.
- **OpenGL/GLES dithering disabled at context init** — `glDisable(GL_DITHER)` is issued in `OpenGLGraphicsDevice.init()`. Dither is on by default in the GL spec, costs fragment-shader cycles on tilers, and produces no perceptible difference on the ≥8-bpc color targets every modern display uses. Recommended by both Arm (Mali Best Practices) and Qualcomm (Adreno OpenGL ES Developer Guide). See `src/Veldrid/OpenGL/OpenGLGraphicsDevice.cs`.
- **OpenGL/GLES tile-store skip via `glInvalidateFramebuffer` at swap time** — when the swapchain has a depth attachment and the context exposes `glInvalidateFramebuffer` (core in GL 4.3+ / GLES 3.0+), the depth and stencil attachments of the default framebuffer are invalidated immediately before `SwapBuffers`. On tile-based GPUs this allows the driver to drop the per-tile depth/stencil writeback to main memory entirely. At 1440×3088×120 Hz (S23 Ultra), this saves on the order of 2 GB/s of DRAM bandwidth — directly reducing power draw, thermal throttling, and (transitively) sustained frame rate. The optimization is unconditionally safe: the GL spec leaves the default framebuffer's depth/stencil contents undefined across `SwapBuffers`, so any caller depending on them across frames would already be broken on every tiler. Implementation:
  - `glInvalidateFramebuffer` binding added to `Veldrid.OpenGLBindings.OpenGLNative` (single-pointer, GL 4.3+ / GLES 3.0+ core; no `EXT_discard_framebuffer` fallback path).
  - Capability flag `OpenGLExtensions.InvalidateFramebuffer` (`GLVersion(4, 3) || GLESVersion(3, 0)`).
  - `OpenGLGraphicsDevice.invalidateSwapchainDepthOnSwap` set during `init()` when `SwapchainDepthFormat != null` and the capability is present; the `WorkItemType.SwapBuffers` handler in the GL execution thread binds default FB and invalidates `Depth`+`Stencil` before the platform `swapBuffers()` call.
  - Color is **not** invalidated — that's what we're presenting.

#### Mobile optimizations explicitly *not* shipped (yet) and why
- **Vulkan `VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT` + `VK_MEMORY_PROPERTY_LAZILY_ALLOCATED_BIT` for the swapchain depth target.** The biggest remaining tiler win — would let Adreno/Mali keep the entire depth/stencil buffer in tile memory with zero physical-memory backing. Blocked on a wider API change: `VkFormats.VdToVkTextureUsage` currently always adds `TransferSrc | TransferDst`, which is incompatible with lazy allocation. Would need a new `TextureUsage.Transient` flag plumbed through to opt textures out of the transfer-usage default.
- **Persisted `VkPipelineCache`.** Veldrid already creates a `VkPipelineCache` (no longer `VkPipelineCache.Null`) so within-process pipeline creation is deduplicated, but the cache is not persisted across application launches. Persisting it would require a deliberate public API: a path or stream to read/write the blob, plus pipeline-cache UUID and driver-version validation per the Vulkan spec.
- **`VK_KHR_swapchain_maintenance1`** (Adreno 740 supports it). Allows changing present mode without recreating the swapchain — useful for runtime "low-latency mode" toggles. Out of scope for this round; needs new public API.
- **`glInvalidateFramebuffer` for offscreen FBOs.** Same mechanism, larger potential win, but requires a render-pass concept Veldrid's GL backend doesn't currently model. Mis-invalidating an attachment that the user later samples (shadow maps, ping-pong post-processing) would silently corrupt rendering. Not safe to ship without per-attachment usage tracking.

### Variable Rate Shading (VRS)
- Cross-backend per-draw shading rate control via `CommandList.SetShadingRate(ShadingRate)`
- **D3D12**: Uses `ID3D12GraphicsCommandList5.RSSetShadingRate` (requires Options6, Tier 1+)
- **Vulkan**: Uses `VK_KHR_fragment_shading_rate` / `vkCmdSetFragmentShadingRateKHR`
- Rates from 1×1 (default) through 4×4 (sixteenth-rate) — reduce fragment shader workload for non-critical regions
- Feature detection: `GraphicsDeviceFeatures.VariableRateShading`

### Mesh Shader Dispatch
- Cross-backend mesh shader dispatch via `CommandList.DispatchMesh(groupCountX, groupCountY, groupCountZ)`
- **D3D12**: Uses `ID3D12GraphicsCommandList6.DispatchMesh` (requires Options7, Tier 1+)
- **Vulkan**: Uses `VK_EXT_mesh_shader` / `vkCmdDrawMeshTasksEXT`
- New shader stages: `ShaderStages.Task` (amplification) and `ShaderStages.Mesh`
- Feature detection: `GraphicsDeviceFeatures.MeshShader`

### GPU Hardware Capability Detection
- **D3D12 Enhanced Barriers** (Options12) — detected at device creation, exposed via `BackendInfoD3D12.SupportsEnhancedBarriers`
- **D3D12 Mesh Shaders** (Options7) — `BackendInfoD3D12.SupportsMeshShaders`
- **D3D12 Variable Rate Shading** (Options6) — `BackendInfoD3D12.SupportsVariableRateShading`
- **D3D12 DXR Raytracing** (Options5) — `BackendInfoD3D12.SupportsRaytracing`
- **Vulkan Descriptor Indexing** — `VkGraphicsDevice.HasDescriptorIndexing` (bindless descriptors)
- **Vulkan Fragment Shading Rate** — `BackendInfoVulkan.HasFragmentShadingRate`
- **Vulkan Mesh Shaders** — `BackendInfoVulkan.HasMeshShader`

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
