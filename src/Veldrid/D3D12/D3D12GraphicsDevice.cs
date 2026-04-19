using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using VorticeDXGI = Vortice.DXGI.DXGI;
using D3D12Feature = Vortice.Direct3D12.Feature;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12GraphicsDevice : GraphicsDevice
    {
        public override string DeviceName { get; }
        public override string VendorName { get; }
        public override GraphicsApiVersion ApiVersion { get; }
        public override GraphicsBackend BackendType => GraphicsBackend.Direct3D12;
        public override bool IsUvOriginTopLeft => true;
        public override bool IsDepthRangeZeroToOne => true;
        public override bool IsClipSpaceYInverted => false;
        public override ResourceFactory ResourceFactory => d3d12ResourceFactory;
        public override Swapchain MainSwapchain => mainSwapchain;
        public override GraphicsDeviceFeatures Features { get; }

        public ID3D12Device Device => device;
        public ID3D12CommandQueue CommandQueue => commandQueue;
        public IDXGIFactory4 DxgiFactory => dxgiFactory;
        public D3D12DescriptorAllocator RtvAllocator => rtvAllocator;
        public D3D12DescriptorAllocator DsvAllocator => dsvAllocator;
        public D3D12DescriptorAllocator CbvSrvUavAllocator => cbvSrvUavAllocator;
        public D3D12DescriptorAllocator SamplerAllocator => samplerAllocator;
        public D3D12CommandAllocatorPool CommandAllocatorPool => commandAllocatorPool;
        public ulong FrameFenceValue => Volatile.Read(ref frameFenceValue);
        public ID3D12Fence FrameFence => frameFence;

        public override bool AllowTearing
        {
            get => mainSwapchain?.AllowTearing ?? false;
            set
            {
                if (mainSwapchain != null)
                    mainSwapchain.AllowTearing = value;
            }
        }

        private readonly ID3D12Device device;
        private readonly IDXGIAdapter dxgiAdapter;
        private readonly IDXGIFactory4 dxgiFactory;
        private readonly ID3D12CommandQueue commandQueue;
        private readonly ID3D12Fence frameFence;
        private ulong frameFenceValue;
        private readonly AutoResetEvent frameFenceEvent;

        private readonly D3D12DescriptorAllocator rtvAllocator;
        private readonly D3D12DescriptorAllocator dsvAllocator;
        private readonly D3D12DescriptorAllocator cbvSrvUavAllocator;
        private readonly D3D12DescriptorAllocator samplerAllocator;

        private readonly D3D12ResourceFactory d3d12ResourceFactory;
        private readonly D3D12Swapchain mainSwapchain;
        private readonly D3D12CommandAllocatorPool commandAllocatorPool;

        private readonly Lock commandQueueLock = new Lock();
        private readonly Lock mappedResourceLock = new Lock();

        private readonly Dictionary<MappedResourceCacheKey, MappedResourceInfo> mappedResources
            = new Dictionary<MappedResourceCacheKey, MappedResourceInfo>();

        private readonly Lock stagingResourcesLock = new Lock();
        private readonly List<D3D12Buffer> availableStagingBuffers = new List<D3D12Buffer>();

        private readonly bool isDebugEnabled;

        public D3D12GraphicsDevice(GraphicsDeviceOptions options, D3D12DeviceOptions d3d12Options, SwapchainDescription? swapchainDesc)
        {
            bool enableDebug = options.Debug || d3d12Options.EnableDebugLayer;

#if DEBUG
            enableDebug = true;
#endif

            // 1. Enable debug layer if requested.
            if (enableDebug)
            {
                try
                {
                    if (Vortice.Direct3D12.D3D12.D3D12GetDebugInterface(out ID3D12Debug debugInterface).Success)
                    {
                        debugInterface.EnableDebugLayer();

                        if (d3d12Options.EnableGpuValidation)
                        {
                            var debug3 = debugInterface.QueryInterfaceOrNull<ID3D12Debug1>();
                            debug3?.SetEnableGPUBasedValidation(true);
                            debug3?.Dispose();
                        }

                        debugInterface.Dispose();
                        isDebugEnabled = true;
                    }
                }
                catch
                {
                    // Debug layer not available; continue without it.
                }
            }

            // 2. Create DXGI factory.
            dxgiFactory = VorticeDXGI.CreateDXGIFactory2<IDXGIFactory4>(isDebugEnabled);

            // 3. Select adapter.
            if (d3d12Options.AdapterPtr != IntPtr.Zero)
            {
                dxgiAdapter = new IDXGIAdapter(d3d12Options.AdapterPtr);
            }
            else
            {
                dxgiFactory.EnumAdapters(0, out dxgiAdapter).CheckError();
            }

            // 4. Create D3D12 device.
            Vortice.Direct3D12.D3D12.D3D12CreateDevice(
                dxgiAdapter.NativePointer,
                FeatureLevel.Level_11_0,
                out device).CheckError();

            // Read adapter description.
            var adapterDesc = dxgiAdapter.Description;
            DeviceName = adapterDesc.Description;
            VendorName = "id:" + ((uint)adapterDesc.VendorId).ToString("x8");
            ApiVersion = new GraphicsApiVersion(12, 0, 0, 0);

            // 5. Create command queue.
            var queueDesc = new CommandQueueDescription(CommandListType.Direct);
            commandQueue = device.CreateCommandQueue(queueDesc);

            // 6. Create frame fence.
            frameFenceValue = 0;
            frameFence = device.CreateFence(0);
            frameFenceEvent = new AutoResetEvent(false);

            // 7. Create descriptor allocators.
            rtvAllocator = new D3D12DescriptorAllocator(device, DescriptorHeapType.RenderTargetView, 256, false);
            dsvAllocator = new D3D12DescriptorAllocator(device, DescriptorHeapType.DepthStencilView, 64, false);
            cbvSrvUavAllocator = new D3D12DescriptorAllocator(device, DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 4096, false);
            samplerAllocator = new D3D12DescriptorAllocator(device, DescriptorHeapType.Sampler, 256, false);

            // 8. Create command allocator pool.
            commandAllocatorPool = new D3D12CommandAllocatorPool(device, CommandListType.Direct);

            // 9. Check features and create Features.
            bool supportsDoubles = false;
            try
            {
                var doubleData = device.CheckFeatureSupport<FeatureDataD3D12Options>(D3D12Feature.Options);
                supportsDoubles = doubleData.DoublePrecisionFloatShaderOps;
            }
            catch
            {
                // Feature query may fail on some drivers.
            }

            Features = new GraphicsDeviceFeatures(
                computeShader: true,
                geometryShader: true,
                tessellationShaders: true,
                multipleViewports: true,
                samplerLodBias: true,
                drawBaseVertex: true,
                drawBaseInstance: true,
                drawIndirect: true,
                drawIndirectBaseInstance: true,
                fillModeWireframe: true,
                samplerAnisotropy: true,
                depthClipDisable: true,
                texture1D: true,
                independentBlend: true,
                structuredBuffer: true,
                subsetTextureView: true,
                commandListDebugMarkers: true,
                bufferRangeBinding: true,
                shaderFloat64: supportsDoubles);

            // 10. Create ResourceFactory.
            d3d12ResourceFactory = new D3D12ResourceFactory(this);

            // 11. Create main swapchain if description provided.
            if (swapchainDesc != null)
            {
                var desc = swapchainDesc.Value;
                mainSwapchain = new D3D12Swapchain(this, ref desc);
            }

            // 12. Post-device created (creates common samplers).
            PostDeviceCreated();
        }

        public static bool IsSupported()
        {
            try
            {
                return Vortice.Direct3D12.D3D12.IsSupported(FeatureLevel.Level_11_0);
            }
            catch
            {
                return false;
            }
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            var dxgiFormat = D3D12Formats.ToDxgiFormat(format, depthFormat);
            if (checkFormatMultisample(dxgiFormat, 32)) return TextureSampleCount.Count32;
            if (checkFormatMultisample(dxgiFormat, 16)) return TextureSampleCount.Count16;
            if (checkFormatMultisample(dxgiFormat, 8)) return TextureSampleCount.Count8;
            if (checkFormatMultisample(dxgiFormat, 4)) return TextureSampleCount.Count4;
            if (checkFormatMultisample(dxgiFormat, 2)) return TextureSampleCount.Count2;
            return TextureSampleCount.Count1;
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            return Util.AssertSubtype<Fence, D3D12Fence>(fence).Wait(nanosecondTimeout);
        }

        public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
        {
            int msTimeout;
            if (nanosecondTimeout == ulong.MaxValue)
                msTimeout = -1;
            else
                msTimeout = (int)Math.Min(nanosecondTimeout / 1_000_000, int.MaxValue);

            var handles = new WaitHandle[fences.Length];
            for (int i = 0; i < fences.Length; i++)
            {
                var d3d12Fence = Util.AssertSubtype<Fence, D3D12Fence>(fences[i]);
                var mre = new ManualResetEvent(d3d12Fence.Signaled);
                if (!d3d12Fence.Signaled)
                    d3d12Fence.DeviceFence.SetEventOnCompletion(d3d12Fence.CurrentValue, mre);
                handles[i] = mre;
            }

            bool result;
            if (waitAll)
                result = WaitHandle.WaitAll(handles, msTimeout);
            else
            {
                int index = WaitHandle.WaitAny(handles, msTimeout);
                result = index != WaitHandle.WaitTimeout;
            }

            return result;
        }

        public override void ResetFence(Fence fence)
        {
            Util.AssertSubtype<Fence, D3D12Fence>(fence).Reset();
        }

        internal override uint GetUniformBufferMinOffsetAlignmentCore()
        {
            return 256u;
        }

        internal override uint GetStructuredBufferMinOffsetAlignmentCore()
        {
            return 16u;
        }

        protected override unsafe MappedResource MapCore(IMappableResource resource, MapMode mode, uint subresource)
        {
            var key = new MappedResourceCacheKey(resource, subresource);

            lock (mappedResourceLock)
            {
                if (mappedResources.TryGetValue(key, out var info))
                {
                    if (info.Mode != mode)
                        throw new VeldridException("The given resource was already mapped with a different MapMode.");

                    info.RefCount += 1;
                    mappedResources[key] = info;
                    return info.MappedResource;
                }

                if (resource is D3D12Buffer buffer)
                {
                    void* dataPtr;
                    buffer.Resource.Map(0, null, &dataPtr).CheckError();
                    info.MappedResource = new MappedResource(resource, mode, (IntPtr)dataPtr, buffer.SizeInBytes);
                    info.RefCount = 1;
                    info.Mode = mode;
                    mappedResources.Add(key, info);
                }
                else
                {
                    var texture = Util.AssertSubtype<IMappableResource, D3D12Texture>(resource);
                    // For D3D12 staging textures, we map the underlying resource.
                    // Staging textures in D3D12 are placed on Default heap, so direct mapping isn't
                    // straightforward. For now, we support mapping for buffers only.
                    throw new VeldridException("Direct mapping of D3D12 textures is not supported. Use UpdateTexture instead.");
                }

                return info.MappedResource;
            }
        }

        protected override void UnmapCore(IMappableResource resource, uint subresource)
        {
            var key = new MappedResourceCacheKey(resource, subresource);

            lock (mappedResourceLock)
            {
                if (!mappedResources.TryGetValue(key, out var info))
                    throw new VeldridException($"The given resource ({resource}) is not mapped.");

                info.RefCount -= 1;

                if (info.RefCount == 0)
                {
                    if (resource is D3D12Buffer buffer)
                        buffer.Resource.Unmap(0);

                    bool result = mappedResources.Remove(key);
                    Debug.Assert(result);
                }
                else
                {
                    mappedResources[key] = info;
                }
            }
        }

        protected override void PlatformDispose()
        {
            // Dispose staging buffers.
            foreach (var buffer in availableStagingBuffers)
                buffer.Dispose();

            availableStagingBuffers.Clear();

            d3d12ResourceFactory.Dispose();
            mainSwapchain?.Dispose();
            commandAllocatorPool.Dispose();

            rtvAllocator.Dispose();
            dsvAllocator.Dispose();
            cbvSrvUavAllocator.Dispose();
            samplerAllocator.Dispose();

            frameFence.Dispose();
            frameFenceEvent.Dispose();

            commandQueue.Dispose();
            device.Dispose();
            dxgiAdapter.Dispose();
            dxgiFactory.Dispose();
        }

        private protected override void SubmitCommandsCore(CommandList cl, Fence fence)
        {
            var d3d12CL = Util.AssertSubtype<CommandList, D3D12CommandList>(cl);

            lock (commandQueueLock)
            {
                commandQueue.ExecuteCommandList(d3d12CL.CommandListHandle);

                ulong fenceValueForSignal = Interlocked.Increment(ref frameFenceValue);
                commandQueue.Signal(frameFence, fenceValueForSignal);

                d3d12CL.OnSubmitted(fenceValueForSignal);

                if (fence is D3D12Fence d3d12Fence)
                {
                    ulong userFenceValue = d3d12Fence.IncrementAndGetValue();
                    commandQueue.Signal(d3d12Fence.DeviceFence, userFenceValue);
                }
            }
        }

        private protected override void SwapBuffersCore(Swapchain swapchain)
        {
            var d3d12Sc = Util.AssertSubtype<Swapchain, D3D12Swapchain>(swapchain);
            d3d12Sc.DxgiSwapChain.Present(d3d12Sc.SyncInterval, d3d12Sc.PresentFlags);
        }

        private protected override void WaitForIdleCore()
        {
            lock (commandQueueLock)
            {
                ulong fenceValue = Interlocked.Increment(ref frameFenceValue);
                commandQueue.Signal(frameFence, fenceValue);

                if (frameFence.CompletedValue < fenceValue)
                {
                    frameFence.SetEventOnCompletion(fenceValue, frameFenceEvent);
                    frameFenceEvent.WaitOne();
                }
            }
        }

        private protected override void WaitForNextFrameReadyCore()
        {
            // With flip-model swapchains, we rely on the frame latency waitable object
            // if available; otherwise, this is a no-op.
        }

        private protected override unsafe void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            var d3dBuffer = Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(buffer);
            if (sizeInBytes == 0)
                return;

            bool isDynamic = (buffer.Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;
            bool isStaging = (buffer.Usage & BufferUsage.Staging) == BufferUsage.Staging;

            if (isDynamic || isStaging)
            {
                // Upload/Readback heap: map directly, copy, unmap.
                var mr = MapCore(buffer, MapMode.Write, 0);

                if (sizeInBytes < 1024)
                    Unsafe.CopyBlock((byte*)mr.Data + bufferOffsetInBytes, source.ToPointer(), sizeInBytes);
                else
                {
                    Buffer.MemoryCopy(
                        source.ToPointer(),
                        (byte*)mr.Data + bufferOffsetInBytes,
                        buffer.SizeInBytes,
                        sizeInBytes);
                }

                UnmapCore(buffer, 0);
            }
            else
            {
                // Default heap: use an upload staging buffer and copy command.
                var staging = getFreeStagingBuffer(sizeInBytes, BufferUsage.Dynamic);
                UpdateBuffer(staging, 0, source, sizeInBytes);

                // For now, use an immediate copy approach with a temporary command list.
                executeBufferCopy(staging, d3dBuffer, sizeInBytes, bufferOffsetInBytes);

                lock (stagingResourcesLock)
                    availableStagingBuffers.Add(staging);
            }
        }

        private protected override unsafe void UpdateTextureCore(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x, uint y, uint z,
            uint width, uint height, uint depth,
            uint mipLevel, uint arrayLayer)
        {
            var d3dTex = Util.AssertSubtype<Texture, D3D12Texture>(texture);

            uint rowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
            // D3D12 requires 256-byte aligned row pitch for texture uploads.
            uint alignedRowPitch = (rowPitch + 255u) & ~255u;

            // Create an upload buffer, copy the data into it, then copy from upload buffer to texture.
            ulong uploadSize = (ulong)(alignedRowPitch * height * depth);
            // Align to 512 bytes for texture upload requirements.
            ulong alignedSize = (uploadSize + 511UL) & ~511UL;
            if (alignedSize < sizeInBytes) alignedSize = (ulong)((sizeInBytes + 511UL) & ~511UL);

            var uploadBufferDesc = ResourceDescription.Buffer(alignedSize);
            var uploadHeapProps = new HeapProperties(HeapType.Upload);

            var uploadBuffer = device.CreateCommittedResource(
                uploadHeapProps,
                HeapFlags.None,
                uploadBufferDesc,
                ResourceStates.GenericRead);

            // Map the upload buffer and copy source data into it.
            void* uploadPtr;
            uploadBuffer.Map(0, null, &uploadPtr).CheckError();

            if (alignedRowPitch == rowPitch)
            {
                // Row pitch matches - simple copy.
                Unsafe.CopyBlock(uploadPtr, source.ToPointer(), sizeInBytes);
            }
            else
            {
                // Row pitch doesn't match - copy row by row.
                uint blockHeight = FormatHelpers.IsCompressedFormat(texture.Format) ? 4u : 1u;
                uint numRows = (height + blockHeight - 1) / blockHeight;
                byte* srcPtr = (byte*)source.ToPointer();
                byte* dstPtr = (byte*)uploadPtr;
                for (uint slice = 0; slice < depth; slice++)
                {
                    for (uint row = 0; row < numRows; row++)
                    {
                        Unsafe.CopyBlock(dstPtr, srcPtr, rowPitch);
                        srcPtr += rowPitch;
                        dstPtr += alignedRowPitch;
                    }
                }
            }

            uploadBuffer.Unmap(0);

            // Create a temporary command allocator and command list for the copy.
            var tempAllocator = commandAllocatorPool.GetAllocator(frameFence.CompletedValue);
            var tempCmdList = device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct,
                tempAllocator);

            uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);

            var srcLocation = new TextureCopyLocation(uploadBuffer, new PlacedSubresourceFootPrint
            {
                Offset = 0,
                Footprint = new SubresourceFootPrint
                {
                    Format = d3dTex.DxgiFormat,
                    Width = width,
                    Height = height,
                    Depth = depth,
                    RowPitch = alignedRowPitch
                }
            });

            var dstLocation = new TextureCopyLocation(d3dTex.Resource, subresource);

            tempCmdList.CopyTextureRegion(dstLocation, x, y, z, srcLocation);

            tempCmdList.Close();
            commandQueue.ExecuteCommandList(tempCmdList);

            // Wait for the copy to complete.
            ulong fenceValue = Interlocked.Increment(ref frameFenceValue);
            commandQueue.Signal(frameFence, fenceValue);
            if (frameFence.CompletedValue < fenceValue)
            {
                frameFence.SetEventOnCompletion(fenceValue, frameFenceEvent);
                frameFenceEvent.WaitOne();
            }

            commandAllocatorPool.ReturnAllocator(fenceValue, tempAllocator);
            tempCmdList.Dispose();
            uploadBuffer.Dispose();
        }

        private protected override bool GetPixelFormatSupportCore(
            PixelFormat format,
            TextureType type,
            TextureUsage usage,
            out PixelFormatProperties properties)
        {
            var dxgiFormat = D3D12Formats.ToDxgiFormat(format, (usage & TextureUsage.DepthStencil) != 0);

            FormatSupport1 fs1;
            try
            {
                var featureData = new FeatureDataFormatSupport { Format = dxgiFormat };
                device.CheckFeatureSupport(D3D12Feature.FormatSupport, ref featureData);
                fs1 = featureData.Support1;
            }
            catch
            {
                properties = default;
                return false;
            }

            if (((usage & TextureUsage.RenderTarget) != 0 && (fs1 & FormatSupport1.RenderTarget) == 0)
                || ((usage & TextureUsage.DepthStencil) != 0 && (fs1 & FormatSupport1.DepthStencil) == 0)
                || ((usage & TextureUsage.Sampled) != 0 && (fs1 & FormatSupport1.ShaderSample) == 0)
                || ((usage & TextureUsage.Cubemap) != 0 && (fs1 & FormatSupport1.TextureCube) == 0)
                || ((usage & TextureUsage.Storage) != 0 && (fs1 & FormatSupport1.TypedUnorderedAccessView) == 0))
            {
                properties = default;
                return false;
            }

            const uint max_texture_dimension = 16384;
            const uint max_volume_extent = 2048;

            uint sampleCounts = 0;
            if (checkFormatMultisample(dxgiFormat, 1)) sampleCounts |= 1 << 0;
            if (checkFormatMultisample(dxgiFormat, 2)) sampleCounts |= 1 << 1;
            if (checkFormatMultisample(dxgiFormat, 4)) sampleCounts |= 1 << 2;
            if (checkFormatMultisample(dxgiFormat, 8)) sampleCounts |= 1 << 3;
            if (checkFormatMultisample(dxgiFormat, 16)) sampleCounts |= 1 << 4;
            if (checkFormatMultisample(dxgiFormat, 32)) sampleCounts |= 1 << 5;

            properties = new PixelFormatProperties(
                max_texture_dimension,
                type == TextureType.Texture1D ? 1 : max_texture_dimension,
                type != TextureType.Texture3D ? 1 : max_volume_extent,
                uint.MaxValue,
                type == TextureType.Texture3D ? 1 : max_volume_extent,
                sampleCounts);
            return true;
        }

        private bool checkFormatMultisample(Format format, uint sampleCount)
        {
            var featureData = new FeatureDataMultisampleQualityLevels
            {
                Format = format,
                SampleCount = sampleCount
            };

            try
            {
                device.CheckFeatureSupport(D3D12Feature.MultisampleQualityLevels, ref featureData);
                return featureData.NumQualityLevels > 0;
            }
            catch
            {
                return false;
            }
        }

        private D3D12Buffer getFreeStagingBuffer(uint sizeInBytes, BufferUsage usage = BufferUsage.Staging)
        {
            lock (stagingResourcesLock)
            {
                for (int i = 0; i < availableStagingBuffers.Count; i++)
                {
                    if (availableStagingBuffers[i].SizeInBytes >= sizeInBytes
                        && availableStagingBuffers[i].Usage == usage)
                    {
                        var buffer = availableStagingBuffers[i];
                        availableStagingBuffers.RemoveAt(i);
                        return buffer;
                    }
                }
            }

            var staging = ResourceFactory.CreateBuffer(
                new BufferDescription(sizeInBytes, usage));

            return Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(staging);
        }

        private void executeBufferCopy(D3D12Buffer source, D3D12Buffer destination, uint sizeInBytes, uint dstOffset)
        {
            var tempAllocator = commandAllocatorPool.GetAllocator(frameFence.CompletedValue);
            var tempCmdList = device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct,
                tempAllocator);

            tempCmdList.CopyBufferRegion(destination.Resource, dstOffset, source.Resource, 0, sizeInBytes);
            tempCmdList.Close();

            lock (commandQueueLock)
            {
                commandQueue.ExecuteCommandList(tempCmdList);

                ulong fenceValue = Interlocked.Increment(ref frameFenceValue);
                commandQueue.Signal(frameFence, fenceValue);

                if (frameFence.CompletedValue < fenceValue)
                {
                    frameFence.SetEventOnCompletion(fenceValue, frameFenceEvent);
                    frameFenceEvent.WaitOne();
                }

                commandAllocatorPool.ReturnAllocator(fenceValue, tempAllocator);
            }

            tempCmdList.Dispose();
        }
    }
}
