# winnerspiros/veldrid

> **Cross-platform, graphics API-agnostic rendering & compute library for .NET**

A high-performance fork of [ppy/veldrid](https://github.com/ppy/veldrid), aggressively optimized for low latency and maximum throughput across all backends. As of April 2024 this repository is intentionally **incompatible with upstream** to allow unrestricted modernization.

---

## Supported Backends

| Backend | Platforms | Notes |
|---|---|---|
| **Direct3D 12** | Windows | ✅ Full implementation — new in this fork |
| **Direct3D 11** | Windows | ✅ Vortice 3.8.3, flip-discard, optimized |
| **Vulkan** | Windows · Linux · Android | ✅ Dynamic rendering, push descriptors, optimized |
| **Metal** | macOS · iOS | ✅ TBDR-aware, optimized |
| **OpenGL 4.3+** | Windows · Linux · macOS | ✅ Pipeline cache, invalidation, optimized |
| **OpenGL ES 3.0+** | Android · Linux | ✅ Full tiler optimizations |

---

## What's New

### Direct3D 12 Backend

A full D3D12 backend added from scratch.

- Complete resource lifecycle: Buffer, Texture, TextureView, Sampler, Shader, Fence, ResourceLayout, ResourceSet, Framebuffer, Pipeline, Swapchain
- `D3D12CommandList` with command allocator pooling for efficient multi-frame recording
- `D3D12DescriptorAllocator` — thread-safe free-list descriptor heap management
- `D3D12CommandAllocatorPool` — fence-gated allocator reuse
- `D3D12Pipeline` — root signature generation (CBV/SRV/UAV + Sampler descriptor tables)
- `D3D12Swapchain` — flip-discard model, tearing support
- Public API: `GraphicsDevice.CreateD3D12()`, `BackendInfoD3D12`, `D3D12DeviceOptions`
- `GraphicsDevice.IsBackendSupported(GraphicsBackend.Direct3D12)`

### Variable Rate Shading (VRS)

Cross-backend per-draw shading rate control via `CommandList.SetShadingRate(ShadingRate)`.

| Backend | Implementation |
|---|---|
| D3D12 | `ID3D12GraphicsCommandList5.RSSetShadingRate` (Options6, Tier 1+) |
| Vulkan | `VK_KHR_fragment_shading_rate` / `vkCmdSetFragmentShadingRateKHR` |

Rates: `1×1` (full-rate, default) → `4×4` (sixteenth-rate). Feature detection: `GraphicsDeviceFeatures.VariableRateShading`.

### Mesh Shader Dispatch

Cross-backend mesh shader support via `CommandList.DispatchMesh(groupCountX, groupCountY, groupCountZ)`.

| Backend | Implementation |
|---|---|
| D3D12 | `ID3D12GraphicsCommandList6.DispatchMesh` (Options7, Tier 1+) |
| Vulkan | `VK_EXT_mesh_shader` / `vkCmdDrawMeshTasksEXT` |

New shader stages: `ShaderStages.Task` (amplification) and `ShaderStages.Mesh`. Feature detection: `GraphicsDeviceFeatures.MeshShader`.

### Hardware Capability Detection

| Capability | API |
|---|---|
| D3D12 Enhanced Barriers (Options12) | `BackendInfoD3D12.SupportsEnhancedBarriers` |
| D3D12 DXR Raytracing (Options5) | `BackendInfoD3D12.SupportsRaytracing` |
| D3D12 Variable Rate Shading (Options6) | `BackendInfoD3D12.SupportsVariableRateShading` |
| D3D12 Mesh Shaders (Options7) | `BackendInfoD3D12.SupportsMeshShaders` |
| Vulkan Descriptor Indexing (bindless) | `VkGraphicsDevice.HasDescriptorIndexing` |
| Vulkan Fragment Shading Rate | `BackendInfoVulkan.HasFragmentShadingRate` |
| Vulkan Mesh Shaders | `BackendInfoVulkan.HasMeshShader` |
| Vulkan Swapchain Maintenance 1 | `VkGraphicsDevice.HasSwapchainMaintenance1` |
| Vulkan Synchronization2 (core 1.3) | `BackendInfoVulkan.HasSynchronization2` |
| Vulkan Timeline Semaphores (core 1.2) | `BackendInfoVulkan.HasTimelineSemaphore` |

### Vortice.Windows Upgrade (2.4.2 → 3.8.3)

- Native .NET 10 support — no compatibility shims
- Correct `uint` mapping for C++ `UINT` (was `int`)
- Improved `Span<T>` usage, AOT and trimming support
- All D3D11 and D3D12 backend code updated for new type mappings

---

## Performance Optimizations

### Cross-Backend

| Change | Impact |
|---|---|
| `System.Threading.Lock` everywhere (was `object`) | Lower lock overhead, better diagnostics |
| `System.HashCode` (xxHash3/Marvin32) for all description structs | Better hash distribution, no custom code |
| Switch expressions for all ≈50 format conversion tables | Clearer JIT value-propagation, less branch pressure |
| `Array.Empty<T>()` instead of `new T[0]` | Eliminates allocation per command list |
| `$""` string interpolation throughout | No intermediate `string.Concat` allocs on error paths |
| Staging buffer pool floors: 64 KiB min / 4 MiB max-recycle | Reuses buffers across frames, reduces allocator churn |

### Vulkan

<details>
<summary>Expand details</summary>

- **Pipeline cache** — all graphics/compute pipeline creation uses a shared `VkPipelineCache`. Cache can be **persisted across launches**: supply a blob via `VulkanDeviceOptions.PipelineCacheData`; retrieve it via `BackendInfoVulkan.GetPipelineCacheData()`. Driver validates the blob automatically — always safe to pass stale data.
- **Vertex & index buffer caching** — `vkCmdBindVertexBuffers` and `vkCmdBindIndexBuffer` are skipped when the same buffer+offset is already bound in that slot. This eliminates a significant fraction of driver dispatch overhead in typical draw-heavy scenes.
- **Dynamic rendering** (`VK_KHR_dynamic_rendering`) — replaces `VkRenderPass` objects with inline `vkCmdBeginRendering` calls. Depth store op set to `DontCare` for transient targets, avoiding DRAM flush on TBDR GPUs.
- **Push descriptors** (`VK_KHR_push_descriptor`) — resource sets written inline into the command buffer for frequently-changing bindings.
- **Memory allocator** — block-split on allocation updates in-place (no `RemoveAt + Insert`), eliminating O(n) shifts.
- **Staging buffer pool** — swap-remove O(1) instead of `List.Remove` O(n) when recycling used buffers.
- **Pre-sized sampled image list** (capacity 32) — no hot-path reallocation during draw/dispatch.
- `stackalloc` for descriptor sets and dynamic offsets in hot paths.
- UTF-8 `u8` string literals for all proc address lookups — zero runtime encoding overhead.
- Extension detection: `VK_EXT_memory_budget`, `VK_EXT_host_image_copy`, `VK_EXT_descriptor_indexing`.

</details>

### Direct3D 12

<details>
<summary>Expand details</summary>

- **Redundant state tracking** — `SetViewport`, `SetScissorRect`, `SetBlendFactor`, `SetStencilRef`, `SetFramebuffer` all check against cached state and skip the GPU call when unchanged.
- **Staging buffer pool** — swap-remove O(1) instead of `RemoveAt` O(n).

</details>

### Direct3D 11

<details>
<summary>Expand details</summary>

- **Resource binding** — four sequential base-offset accumulation loops merged into a single pass per resource-set activation, improving cache locality and reducing per-draw overhead; dead code removed.
- **Pipeline state caching** — blend state, depth/stencil state, rasterizer state, primitive topology, input layout, all five shader stages, and index buffer checked before issuing driver calls.
- **Uniform buffer & texture view caching** — vertex and fragment stage bindings tracked and skipped when unchanged (slots 0–14 for UBOs, 0–15 for SRVs, 0–3 for samplers).
- **Staging buffer pool** — swap-remove O(1) instead of `foreach + Remove` O(n²).
- **Vertex buffer arrays** — pre-allocated stride/offset arrays, no per-draw allocation.
- **Deferred context command recording** — `ID3D11DeviceContext1` feature used for partial constant-buffer updates.
- **FlipDiscard swapchain** — modern DXGI flip model on 1.4+, lower frame latency.

</details>

### Metal

<details>
<summary>Expand details</summary>

- **Framebuffer short-circuit** — `SetFramebufferCore` skips encoder tear-down when the same framebuffer is rebound and the encoder has seen at least one draw. Preserves the render encoder, preventing a tile flush on TBDR GPUs.
- **Resource binding** — three per-resource O(n) layout offset loops (buffer, texture, sampler) merged into a single pass; vertex buffer index calculation hoisted to avoid redundant per-VB evaluation.
- **Staging buffer pool** — swap-remove O(1) instead of `List.Remove` O(n). 64 KiB floor / 4 MiB recycle ceiling mirrors Vulkan defaults.

</details>

### OpenGL / OpenGL ES

<details>
<summary>Expand details</summary>

- **Pipeline state cache** — all blend, depth, stencil, rasterizer, and shader-program GL calls skipped when the same pipeline is re-activated. Eliminates 30–50 redundant GL calls per draw in typical scenes.
- **Per-command-list redundancy caches** — `SetFramebuffer`, `SetViewport`, and `SetScissorRect` all maintain per-recording caches and early-return on repeated identical calls. Cleared in `Begin()` so out-of-band GL state changes (swap-time `glBindFramebuffer(0)`, async `UpdateBuffer`/`UpdateTexture`) cannot produce stale hits.
- **Resource set clear** — only used slots cleared on draw, not the full array.
- **`glClearBufferfv` / `glClearBufferfi` fast paths** — `ClearColorTarget` and `ClearDepthStencil` use single-attachment clear calls (GL 3.0+ / GLES 3.0+) instead of the `glDrawBuffers + glClear + glDrawBuffers` save/restore dance.
- **Dithering disabled** — `glDisable(GL_DITHER)` at context init. On by default in the spec; costs fragment cycles on tilers; imperceptible on ≥8 bpc targets.
- **`BufferStorage` detection** (`GL_ARB_buffer_storage` / `GL_EXT_buffer_storage` / GL 4.4+) — capability flag available for persistent-mapped buffer paths.

</details>

---

## Mobile / Tiler Optimizations

These changes specifically target tile-based GPUs (Adreno, Mali, PowerVR). Most are also beneficial on desktop and enabled unconditionally when the relevant API/extension is available.

### `TextureUsage.Transient` — Zero-Cost Depth Buffers

New public flag `TextureUsage.Transient` marks a texture as attachment-only. On Vulkan, the backend allocates it with `VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT` + `VK_MEMORY_PROPERTY_LAZILY_ALLOCATED_BIT`, so it lives entirely in tile RAM and occupies **zero physical memory**. At 1440×3088 D32S8 (Samsung S23 Ultra), this saves ~35.6 MB of LPDDR.

The Vulkan swapchain framebuffer sets this flag automatically on its depth/stencil target. Other backends (D3D11, D3D12, Metal, OpenGL) silently ignore it.

`Transient` is mutually exclusive with `Sampled`, `Storage`, `Staging`, and `GenerateMipmaps`.

### Vulkan: Swapchain Present-Mode Selection

Under `SyncToVerticalBlank = true`, the swapchain prefers `FIFO_RELAXED` and falls back to `FIFO`. **`MAILBOX` is intentionally skipped** even when advertised: on Adreno 7xx drivers it stalls `vkAcquireNextImageKHR`/`vkQueuePresentKHR` under submission pressure (texture-upload bursts → black-screen ANR). It also requires an extra in-flight image and an extra compositor round-trip — net negative on tilers. Matches Khronos guidance, Google's Android Vulkan samples, and ANGLE.

### Vulkan: Present-Mode Hot-Swap

Toggling `SyncToVerticalBlank` or `AllowTearing` previously required a full swapchain rebuild (multi-ms stall, one dropped frame). When `VK_EXT_swapchain_maintenance1` is available, Veldrid creates the swapchain with the full compatibility set of present modes and chains a per-present `VkSwapchainPresentModeInfoEXT` to switch modes at `vkQueuePresentKHR` time — **no rebuild, no dropped frame**. Transparent fallback to recreate-and-reacquire on older devices.

Works today on Adreno 740, recent Mali, NVIDIA, Intel, and Mesa. No new public API required.

### OpenGL/GLES: Swapchain Depth Invalidation

Before `SwapBuffers`, the depth and stencil attachments of the default framebuffer are invalidated via `glInvalidateFramebuffer` (GL 4.3+ / GLES 3.0+). On tilers this drops the per-tile depth/stencil writeback to main memory entirely. Color is not invalidated — that's what gets presented.

At 1440×3088×120 Hz this saves ~2 GB/s of DRAM bandwidth, directly reducing power draw and thermal throttling.

### OpenGL/GLES: Offscreen FBO Invalidation

When `CommandList.SetFramebuffer` switches away from a named FBO, attachments without `TextureUsage.Sampled` or `TextureUsage.Storage` are automatically invalidated. On tilers this skips the tile-store writeback for purely transient render targets (depth prepass buffers, intermediate compositing targets).

Safety is enforced statically: shadow-map depth targets (`Sampled | DepthStencil`) and ping-pong color targets (`Sampled | RenderTarget`) are never invalidated.

---

## Bug Fixes

| Fix | Ref |
|---|---|
| Vulkan: `[Conditional("DEBUG")]` removed from `CheckResult` — was silently swallowing errors in release builds | [ppy#61](https://github.com/ppy/veldrid/issues/61) |
| Vulkan: `CopyTexture` between depth/stencil textures used wrong aspect flags (`Color` instead of `Depth\|Stencil`) | [veldrid#462](https://github.com/veldrid/veldrid/pull/462) |
| Vulkan: `CommandBufferCompleted` race — `submittedCommandBuffers.Add` in `End()` now inside the same lock | [veldrid#495](https://github.com/veldrid/veldrid/pull/495) |
| Vulkan: `clearIfRenderTarget` spec violation for transient images — no longer calls `vkCmdClearDepthStencilImage` on images without `TRANSFER_DST_BIT` | — |
| `DisposeWhenIdle` now flushes on `SubmitCommands` — previously leaked resources in apps that never call `WaitForIdle` | [veldrid#476](https://github.com/veldrid/veldrid/issues/476) |
| D3D11: FlipDiscard swapchain on DXGI 1.4+ | [veldrid#484](https://github.com/veldrid/veldrid/pull/484) |
| Metal: `DepthComparison` forced to `Always` when depth test is disabled | [ppy#75](https://github.com/ppy/veldrid/pull/75) |
| OpenGL: `ClearDepthStencil` was leaving stencil mask and depth-write in incorrect state | [veldrid#481](https://github.com/veldrid/veldrid/pull/481) |
| GLES: Stencil buffer init + `IntPtr` for `eglGetDisplay` on 64-bit | [ppy#71](https://github.com/ppy/veldrid/pull/71) |

---

## CI / Build / Publishing

- **.NET 10 SDK** with `LangVersion 14.0`; multi-platform CI (Windows · Linux · macOS)
- **Zero warnings** across all three projects in the solution
- `actions/cache@v5` for `~/.nuget/packages`; `concurrency:` group cancels superseded non-tag runs
- **Automatic publishing** — push to default branch → prerelease to GitHub Packages; tagged ref → release to nuget.org + GitHub Release
- **One-click manual publish** via `workflow_dispatch` (Actions → *Publish NuGet Packages* → *Run workflow*):
  - `destination`: `github` · `nuget.org` · `both`
  - `create_release`: attach `.nupkg` files to a GitHub Release
  - `dry_run`: build + pack only, skip all push steps
  - `.nupkg` artifacts uploaded regardless of publish outcome (30-day retention)

---

## Batch Upload APIs

Two coalescing upload helpers reduce driver overhead for scenes that upload many resources per frame.

### `TextureUpdateBatch`

```csharp
using var batch = device.BeginTextureUpdateBatch();
batch.UpdateTexture(tex1, data1, ...);
batch.UpdateTexture(tex2, data2, ...);
batch.Submit(); // single vkQueueSubmit on Vulkan
```

### `BufferUpdateBatch`

```csharp
using var batch = device.BeginBufferUpdateBatch();
batch.UpdateBuffer(buf1, 0, data1);
batch.UpdateBuffer(buf2, 0, data2);
batch.Submit(); // single vkQueueSubmit on Vulkan
```

Both are pooled per device. Persistent-mapped buffer destinations bypass the batch and write directly into the mapped pointer. Default implementations forward to `UpdateTexture`/`UpdateBuffer` with a no-op `Submit` on non-Vulkan backends.

