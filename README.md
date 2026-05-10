# winnerspiros/veldrid

> **Cross-platform, graphics API-agnostic rendering & compute library for .NET**

A high-performance fork of [ppy/veldrid](https://github.com/ppy/veldrid), aggressively modernized and optimized for low latency and maximum throughput. Intentionally **incompatible with upstream** since April 2024 to allow unrestricted modernization.

---

## Table of Contents

- [Supported Backends](#supported-backends)
- [New Features vs ppy/veldrid](#new-features-vs-ppyveldrid)
- [Performance Optimizations](#performance-optimizations)
- [Mobile & Tiler Optimizations](#mobile--tiler-optimizations)
- [Bug Fixes](#bug-fixes)
- [Batch Upload APIs](#batch-upload-apis)
- [CI / Build / Publishing](#ci--build--publishing)

---

## Supported Backends

| Backend | Platforms | Status |
|---|---|---|
| **Direct3D 12** | Windows | ✅ Full implementation — new in this fork |
| **Direct3D 11** | Windows | ✅ Vortice 3.8.3, flip-discard, optimized |
| **Vulkan** | Windows · Linux · Android | ✅ Dynamic rendering, push descriptors, maintenance1 |
| **Metal** | macOS · iOS | ✅ TBDR-aware, optimized |
| **OpenGL 4.3+** | Windows · Linux · macOS | ✅ Pipeline cache, full invalidation |
| **OpenGL ES 3.0+** | Android · Linux | ✅ Full tiler optimizations |

---

## New Features vs ppy/veldrid

### Direct3D 12 Backend

A complete D3D12 backend built from scratch — the most significant addition to the fork.

| Component | Description |
|---|---|
| `D3D12GraphicsDevice` | Full device lifetime, adapter selection, queue management |
| `D3D12CommandList` | Command allocator pooling, cached GPU state (viewport, scissor, blend, stencil, framebuffer, VBV, IBV) |
| `D3D12Buffer` / `D3D12Texture` | Full resource lifecycle with `D3D12MA`-style staging |
| `D3D12Pipeline` | Root signature generation (CBV/SRV/UAV + Sampler descriptor tables) |
| `D3D12Swapchain` | Flip-discard model, tearing support (`AllowTearing`) |
| `D3D12DescriptorAllocator` | Thread-safe free-list descriptor heap management |
| `D3D12CommandAllocatorPool` | Fence-gated allocator reuse — zero allocation on hot path |

**Public API additions:** `GraphicsDevice.CreateD3D12()`, `BackendInfoD3D12`, `D3D12DeviceOptions`,
`GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D12)`.

---

### Variable Rate Shading (VRS)

Cross-backend per-draw shading rate control.

```csharp
commandList.SetShadingRate(ShadingRate.Rate2x2); // quarter-rate for background geometry
```

| Backend | API |
|---|---|
| D3D12 | `ID3D12GraphicsCommandList5.RSSetShadingRate` (D3D12 Options6, Tier 1+) |
| Vulkan | `VK_KHR_fragment_shading_rate` / `vkCmdSetFragmentShadingRateKHR` |

Rates: `1×1` (full, default) → `1×2` → `2×1` → `2×2` → `2×4` → `4×2` → `4×4`.
Feature query: `GraphicsDeviceFeatures.VariableRateShading`.

---

### Mesh Shader Dispatch

Cross-backend mesh + task (amplification) shader support.

```csharp
commandList.DispatchMesh(groupCountX, groupCountY, groupCountZ);
```

| Backend | API |
|---|---|
| D3D12 | `ID3D12GraphicsCommandList6.DispatchMesh` (D3D12 Options7, Tier 1+) |
| Vulkan | `VK_EXT_mesh_shader` / `vkCmdDrawMeshTasksEXT` |

New shader stages: `ShaderStages.Task` (amplification) and `ShaderStages.Mesh`.
Feature query: `GraphicsDeviceFeatures.MeshShader`.

---

### `TextureUsage.Transient` — Zero-Cost GPU-Only Attachments

A new `TextureUsage` flag marks a texture as attachment-only — it will never be read back to the CPU and its contents may be discarded between render passes.

On **Vulkan** the backend allocates the texture with `VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT` +
`VK_MEMORY_PROPERTY_LAZILY_ALLOCATED_BIT`. The texture lives entirely in tile RAM and occupies
**zero physical memory** on TBDR GPUs. At 1440×3088 D32S8 (Samsung S23 Ultra) this saves ~35.6 MB of LPDDR per frame.

The Vulkan swapchain depth/stencil target is set `Transient` automatically.
Other backends (D3D11, D3D12, Metal, OpenGL) silently ignore the flag.

> `Transient` is mutually exclusive with `Sampled`, `Storage`, `Staging`, and `GenerateMipmaps`.

---

### Batch Upload APIs

Two coalescing upload helpers that reduce driver overhead for scenes with many per-frame resource uploads.

#### `TextureUpdateBatch`

```csharp
using var batch = device.BeginTextureUpdateBatch();
batch.UpdateTexture(tex1, data1, ...);
batch.UpdateTexture(tex2, data2, ...);
batch.Submit(); // single vkQueueSubmit on Vulkan
```

#### `BufferUpdateBatch`

```csharp
using var batch = device.BeginBufferUpdateBatch();
batch.UpdateBuffer(buf1, 0, data1);
batch.UpdateBuffer(buf2, 0, data2);
batch.Submit(); // single vkQueueSubmit on Vulkan
```

Both types are pooled per device. Persistent-mapped buffer destinations bypass the batch and write directly into the mapped region. Non-Vulkan backends forward each call to the single-item path with a no-op `Submit`.

---

### Hardware Capability Detection

Granular feature queries beyond what the base `GraphicsDeviceFeatures` struct exposes.

#### Vulkan

| Query | API |
|---|---|
| Descriptor indexing (bindless) | `VkGraphicsDevice.HasDescriptorIndexing` |
| Fragment shading rate | `BackendInfoVulkan.HasFragmentShadingRate` |
| Mesh shaders | `BackendInfoVulkan.HasMeshShader` |
| Swapchain maintenance 1 | `VkGraphicsDevice.HasSwapchainMaintenance1` |
| Synchronization2 (core 1.3) | `BackendInfoVulkan.HasSynchronization2` |
| Timeline semaphores (core 1.2) | `BackendInfoVulkan.HasTimelineSemaphore` |
| Pipeline cache retrieval | `BackendInfoVulkan.GetPipelineCacheData()` |

#### Direct3D 12

| Query | API |
|---|---|
| Enhanced barriers (Options12) | `BackendInfoD3D12.SupportsEnhancedBarriers` |
| DXR raytracing (Options5) | `BackendInfoD3D12.SupportsRaytracing` |
| Variable rate shading (Options6) | `BackendInfoD3D12.SupportsVariableRateShading` |
| Mesh shaders (Options7) | `BackendInfoD3D12.SupportsMeshShaders` |
| GPU timestamp frequency | `BackendInfoD3D12.TimestampFrequencyHz` |

#### Direct3D 11

| Query | API |
|---|---|
| GPU timestamp frequency | `BackendInfoD3D11.TimestampFrequencyHz` |

---

### Vortice.Vulkan Migration

The Vulkan backend was migrated from the ppy fork of `vulkan-net` (1.0.26) to `Vortice.Vulkan 3.2.1`.

**Correctness improvements:**
- All Vulkan struct `sType` fields set automatically by Vortice constructors — the entire class of missing-`.New()` silent corruption bugs is eliminated by construction.
- Debug callback dispatch uses `InstanceApi` — a null proc-addr returns `VK_ERROR_EXTENSION_NOT_PRESENT` instead of throwing a late `NullReferenceException`.
- `VkPhysicalDeviceDriverProperties`, `VkDriverId`, `VkConformanceVersion` are now Vortice-provided types, removing hand-rolled structs with hardcoded `sType` casts.
- `VK_EXT_swapchain_maintenance1` present-mode hot-swap was blocked by a leftover `if (false)` migration guard — now active on all supporting drivers.

**Performance improvements:**
- All ppy.Vk extension dispatch delegates removed (12 delegate types). Extension calls go through `VkDeviceApi`/`VkInstanceApi` direct function-pointer dispatch — same cost as core calls, JIT-inlineable, zero GC pressure.
- `VkPipeline` shader entry-point strings: `FixedUtf8String` (heap GCHandle per entry-point) replaced with `stackalloc byte[n * 256]` — zero heap allocation per pipeline creation.
- `vkEnumerateInstanceVersion` calls the Vortice global function directly — removes a `Marshal.GetDelegateForFunctionPointer` trampoline on the device-creation path.
- 8 dead `getInstanceProcAddr` / `getDeviceProcAddr` helper methods removed.

---

### Vortice.Windows Upgrade (2.4.2 → 3.8.3)

- Native .NET 10 support — no compatibility shims needed.
- Correct `uint` mapping for C++ `UINT` (was `int` in 2.x).
- Improved `Span<T>` / AOT / trimming support.
- All D3D11 and D3D12 backend code updated for the new type mappings.

---

## Performance Optimizations

### Cross-Backend

| Change | Impact |
|---|---|
| `System.Threading.Lock` everywhere (was `object`) | Lower lock overhead, better tooling integration |
| `System.HashCode` (xxHash3/Marvin32) for all description hash codes | Better distribution, no custom code |
| Switch expressions for all ≈50 format conversion tables | Cleaner JIT value-propagation, reduced branch pressure |
| `Array.Empty<T>()` instead of `new T[0]` everywhere | Eliminates one allocation per command-list construction |
| `$""` string interpolation on all error paths | No intermediate `string.Concat` allocations |
| Staging buffer pool: 64 KiB min / 4 MiB max-recycle | Reuses buffers across frames, reduces allocator churn |

---

### Vulkan

<details>
<summary>Expand</summary>

**Command recording hot-path**
- `vkCmdBindVertexBuffers` and `vkCmdBindIndexBuffer` skipped when the same buffer+offset is already bound in that slot. Eliminates a significant fraction of driver dispatch overhead in draw-heavy scenes.
- `stackalloc` for descriptor sets, dynamic offsets, and entry-point name encoding in `flushNewResourceSets` — zero heap allocation per draw.
- UTF-8 `u8` string literals for all `vkGetInstanceProcAddr` / `vkGetDeviceProcAddr` lookups — zero runtime encoding overhead.
- Pre-sized sampled image list (capacity 32) — no reallocation during draw/dispatch.

**Pipeline**
- Shared `VkPipelineCache` for all graphics/compute pipeline creation. **Persistent across launches:** supply a blob via `VulkanDeviceOptions.PipelineCacheData`; retrieve via `BackendInfoVulkan.GetPipelineCacheData()`. Driver validates the blob — always safe to pass stale data.
- Shared `VkPipelineLayout` cache keyed by ordered `VkDescriptorSetLayout` handles. Layouts that differ only in set order share the same object; `SetPipelineCore` skips `clearSets` when layout handle is identical.
- Dynamic rendering (`VK_KHR_dynamic_rendering`) — replaces `VkRenderPass` objects with inline `vkCmdBeginRendering`. Depth store op `DontCare` for transient targets, avoiding DRAM flush on TBDR.
- Push descriptors (`VK_KHR_push_descriptor`) — resource sets written inline into the command buffer.

**Memory**
- `VkDeviceMemoryManager.Free()` — inline O(1) neighbor coalescing replaces the previous O(n²) `mergeContiguousBlocks` full-list scan. The freed block coalesces with its adjacent neighbors in constant time; no `RemoveRange + Insert` sweep over the entire free list.
- Block-split on allocation updates the free block in-place — avoids O(n) `RemoveAt + Insert` shifts.
- Staging buffer pool uses swap-remove O(1) instead of `List.Remove` O(n).

**Swapchain**
- `imageAvailableSemaphore` deferral: `UsesSwapchainFramebuffer` gates attachment in `SubmitCommandsCore` — buffer/texture-update submits carry no wait semaphore.
- `renderFinishedSemaphore` (binary): a null submit inside `graphicsQueueLock` signals it; `vkQueuePresentKHR` waits on it — eliminates Adreno's CPU-blocking implicit present sync.
- `minImageCount + 1` for all present modes including IMMEDIATE — avoids `vkAcquireNextImageKHR` blocking the render thread for ~8–16 ms per frame on Android SurfaceFlinger.

**Extension detection**
- `VK_EXT_memory_budget`, `VK_EXT_host_image_copy`, `VK_EXT_descriptor_indexing`.
- `VK_KHR_get_surface_capabilities2` + `VK_EXT_surface_maintenance1` added to the surface-extension list before the gate that enables `HasSwapchainMaintenance1`.

</details>

---

### Direct3D 12

<details>
<summary>Expand</summary>

- **Vertex buffer caching** — `SetVertexBufferCore` compares GPU virtual address, size, and stride against cached values and skips `IASetVertexBuffers` when unchanged.
- **Index buffer caching** — `SetIndexBufferCore` compares address, size, and format; skips `IASetIndexBuffer` when unchanged.
- **Blend factor deferral** — `SetPipelineCore` compares blend factors directly against the pipeline's raw float array before constructing a `Color4` struct; the struct is only built when `OMSetBlendFactor` will actually be called.
- **Redundant state tracking** — `SetViewport`, `SetScissorRect`, `SetStencilRef`, and `SetFramebuffer` all skip the GPU call when cached state matches.
- **Staging buffer pool** — swap-remove O(1) instead of `RemoveAt` O(n).

</details>

---

### Direct3D 11

<details>
<summary>Expand</summary>

- **Resource binding** — four sequential base-offset accumulation loops merged into a single pass per resource-set activation; dead code removed.
- **Pipeline state caching** — blend state, depth/stencil state, rasterizer state, primitive topology, input layout, all five shader stages, and index buffer checked before issuing driver calls.
- **Uniform buffer & texture view caching** — vertex and fragment stage bindings tracked and skipped when unchanged (slots 0–14 for UBOs, 0–15 for SRVs, 0–3 for samplers).
- **Staging buffer pool** — swap-remove O(1) instead of `foreach + Remove` O(n²).
- **Vertex buffer arrays** — pre-allocated stride/offset arrays, no per-draw allocation.
- **FlipDiscard swapchain** — modern DXGI flip model on 1.4+, lower frame latency.

</details>

---

### Metal

<details>
<summary>Expand</summary>

- **Framebuffer short-circuit** — `SetFramebufferCore` skips encoder tear-down when the same framebuffer is rebound and at least one draw has been recorded. Preserves the render encoder, preventing a tile flush on TBDR GPUs.
- **Resource binding** — three per-resource O(n) layout offset loops (buffer, texture, sampler) merged into a single pass; vertex buffer index calculation hoisted to avoid redundant per-VB evaluation.
- **Staging buffer pool** — swap-remove O(1); 64 KiB floor / 4 MiB recycle ceiling mirrors Vulkan defaults.

</details>

---

### OpenGL / OpenGL ES

<details>
<summary>Expand</summary>

- **Pipeline state cache** — all blend, depth, stencil, rasterizer, and shader-program GL calls skipped when the same pipeline is re-activated. Eliminates 30–50 redundant GL calls per draw in typical scenes.
- **Per-command-list redundancy caches** — `SetFramebuffer`, `SetViewport`, and `SetScissorRect` maintain per-recording caches and early-return on repeated identical calls. Caches are cleared in `Begin()` so out-of-band GL state changes (swap-time `glBindFramebuffer(0)`, async `UpdateBuffer`/`UpdateTexture`) cannot produce stale hits.
- **`glClearBufferfv` / `glClearBufferfi` fast paths** — `ClearColorTarget` and `ClearDepthStencil` use single-attachment clear calls (GL 3.0+ / GLES 3.0+) instead of the `glDrawBuffers + glClear + glDrawBuffers` save/restore dance.
- **Narrowed post-compute barrier** — `glMemoryBarrier` after dispatch uses exactly the 9 GPU-relevant bits (SSBO, image, UBO, vertex/index fetch, indirect, framebuffer, atomic counter) rather than `GL_ALL_BARRIER_BITS`. Drops 6 CPU-side synchronization bits that have no meaning after a GPU-side dispatch.
- **Dithering disabled** — `glDisable(GL_DITHER)` at context init. On by default in the spec; costs fragment cycles on tilers; imperceptible on ≥8 bpc targets.
- **`BufferStorage` detection** (`GL_ARB_buffer_storage` / `GL_EXT_buffer_storage` / GL 4.4+) — capability flag available for persistent-mapped buffer paths.
- **Resource set clear** — only used slots cleared on draw, not the full array.

</details>

---

## Mobile & Tiler Optimizations

Targeted at tile-based deferred renderers (Adreno, Mali, PowerVR, Apple GPU). Most are enabled unconditionally on desktop too when the relevant API is available.

### Vulkan: Swapchain Present-Mode Selection

Under `SyncToVerticalBlank = true`, the swapchain prefers `FIFO_RELAXED` and falls back to `FIFO`.
**`MAILBOX` is intentionally skipped** even when advertised: on Adreno 7xx drivers it stalls
`vkAcquireNextImageKHR`/`vkQueuePresentKHR` under submission pressure (texture-upload bursts → black-screen ANR). It also requires an extra in-flight image and an extra compositor round-trip — net negative on tilers. Consistent with Khronos guidance, Google's Android Vulkan samples, and ANGLE.

### Vulkan: Present-Mode Hot-Swap

Toggling `SyncToVerticalBlank` or `AllowTearing` previously required a full swapchain rebuild (multi-ms
stall, one dropped frame). When `VK_EXT_swapchain_maintenance1` is present, Veldrid creates the
swapchain with the full compatibility set of present modes and chains a per-present
`VkSwapchainPresentModeInfoEXT` to switch modes at `vkQueuePresentKHR` time —
**no rebuild, no dropped frame**. Transparent fallback to recreate-and-reacquire on older devices.

Works today on Adreno 740, recent Mali, NVIDIA, Intel, and Mesa.

### Vulkan: TBDR Clear-on-First-Use

`beginCurrentDynamicRendering` uses `loadOp = Clear(0,0,0,0)` for all non-transient color attachments
when the framebuffer is bound for the first time in a frame and the application has not queued an
explicit clear. Transient attachments keep `DontCare`. Prevents stale tile-RAM data from bleeding into
the first render pass on TBDR GPUs.

### OpenGL/GLES: Swapchain Depth Invalidation

`glInvalidateFramebuffer` is called on depth and stencil attachments before `SwapBuffers`. On tilers this drops the per-tile depth/stencil writeback to main memory entirely. Color is not invalidated.

At 1440×3088×120 Hz this saves ~2 GB/s of DRAM bandwidth.

### OpenGL/GLES: Offscreen FBO Invalidation

When `CommandList.SetFramebuffer` switches away from a named FBO, attachments without `Sampled` or `Storage` usage flags are automatically invalidated. On tilers this skips the tile-store writeback for transient render targets (depth prepass buffers, intermediate compositing targets, etc.).

Shadow-map depth targets (`Sampled | DepthStencil`) and ping-pong color targets (`Sampled | RenderTarget`) are never invalidated.

---

## Bug Fixes

| Fix | Reference |
|---|---|
| Vulkan: `[Conditional("DEBUG")]` removed from `CheckResult` — was silently swallowing Vulkan errors in release builds | [ppy#61](https://github.com/ppy/veldrid/issues/61) |
| Vulkan: `CopyTexture` between depth/stencil textures used wrong aspect flags (`Color` instead of `Depth\|Stencil`) | [veldrid#462](https://github.com/veldrid/veldrid/pull/462) |
| Vulkan: `CommandBufferCompleted` race — `submittedCommandBuffers.Add` in `End()` now inside the submission lock | [veldrid#495](https://github.com/veldrid/veldrid/pull/495) |
| Vulkan: `clearIfRenderTarget` spec violation — `vkCmdClearDepthStencilImage` no longer called on transient images that lack `TRANSFER_DST_BIT` | — |
| Vulkan: `VkCommandList` reset — `vkResetCommandBuffer` called immediately after fence signals; `SharedCommandPool.Reset()` calls `vkResetCommandPool` before reuse. Prevents `VUID-vkDestroyImage-image-01000` when resources are destroyed after `WaitForIdle` | — |
| `DisposeWhenIdle` now flushes on `SubmitCommands` — previously leaked resources in apps that never call `WaitForIdle` | [veldrid#476](https://github.com/veldrid/veldrid/issues/476) |
| D3D11: FlipDiscard swapchain on DXGI 1.4+ | [veldrid#484](https://github.com/veldrid/veldrid/pull/484) |
| Metal: `DepthComparison` forced to `Always` when depth test is disabled | [ppy#75](https://github.com/ppy/veldrid/pull/75) |
| Metal: `ResolveTexture` now uses `MTLStoreActionStoreAndMultisampleResolve` — the previous `MultisampleResolve`-only action left the source MSAA texture contents undefined after the render pass, corrupting subsequent reads of the MSAA surface | — |
| OpenGL: `ClearDepthStencil` was leaving stencil mask and depth-write in incorrect state after the call | [veldrid#481](https://github.com/veldrid/veldrid/pull/481) |
| GLES: Stencil buffer init + `IntPtr` for `eglGetDisplay` on 64-bit | [ppy#71](https://github.com/ppy/veldrid/pull/71) |
| Vulkan: Android Adreno 740 — dynamic rendering and sync2 crash fixed. Root cause was the pNext feature chain being skipped for Adreno, not broken driver stubs; core 1.3 features work correctly. | — |

---

## CI / Build / Publishing

- **.NET 10 SDK**, `LangVersion 14.0`
- **Zero warnings** on all platforms (`-p:TreatWarningsAsErrors=true`)
- Multi-platform matrix: Windows · Linux · macOS · Android (net10.0-android) · iOS (net10.0-ios)
- `actions/cache@v5` for `~/.nuget/packages`
- `concurrency:` group cancels superseded non-tag runs

### Smoke Tests (every push)

| Backend | Runner | Adapter |
|---|---|---|
| Vulkan | Ubuntu | Mesa lavapipe (CPU ICD) + `VK_LAYER_KHRONOS_validation` |
| D3D11 / D3D12 | Windows | WARP software adapter |
| Metal | macOS | macOS runner |

### Benchmarks (per PR + default-branch)

BenchmarkDotNet runs on lavapipe; results are parsed and written to the Actions step summary automatically. Runs weekly on a schedule too.

### Publishing

| Trigger | Destination |
|---|---|
| Push to default branch | Prerelease → GitHub Packages |
| Pushed tag | Release → nuget.org + GitHub Release |
| Manual `workflow_dispatch` | `github` / `nuget.org` / `both`; optional `dry_run` mode |

`.nupkg` artifacts are uploaded on every build (30-day retention).

