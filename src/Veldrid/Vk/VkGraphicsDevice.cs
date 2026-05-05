using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Vortice.Vulkan;
using static Veldrid.Vk.VulkanUtil;
using static Vortice.Vulkan.Vulkan;
namespace Veldrid.Vk
{
    internal unsafe class VkGraphicsDevice : GraphicsDevice
    {
        private const uint vk_instance_create_enumerate_portability_bit_khr = 0x00000001;

        public override string DeviceName => deviceName;

        public override string VendorName => vendorName;

        public override GraphicsApiVersion ApiVersion => apiVersion;

        public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

        public override bool IsUvOriginTopLeft => true;

        public override bool IsDepthRangeZeroToOne => true;

        public override bool IsClipSpaceYInverted => !standardClipYDirection;

        public override bool AllowTearing
        {
            get => mainSwapchain.AllowTearing;
            set => mainSwapchain.AllowTearing = value;
        }

        public override Swapchain MainSwapchain => mainSwapchain;

        public override GraphicsDeviceFeatures Features { get; }

        public VkInstance Instance => instance;
        public VkDevice Device => device;
        public VkPhysicalDevice PhysicalDevice { get; private set; }

        public VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties => physicalDeviceMemProperties;
        public VkQueue GraphicsQueue => graphicsQueue;
        public uint GraphicsQueueIndex { get; private set; }

        public uint PresentQueueIndex { get; private set; }

        public string DriverName { get; private set; }

        public string DriverInfo { get; private set; }

        public VkDeviceMemoryManager MemoryManager { get; }

        public VkDescriptorPoolManager DescriptorPoolManager { get; private set; }

        // VK_KHR_push_descriptor
        public bool HasPushDescriptors { get; private set; }
        public uint MaxPushDescriptors { get; private set; }

        // VK_KHR_dynamic_rendering
        public bool HasDynamicRendering { get; private set; }
        // When true, VkCommandList must call vkCmdBeginRenderingKHR/vkCmdEndRenderingKHR
        // instead of the core-1.3 vkCmdBeginRendering/vkCmdEndRendering.
        // Set when the driver exposes only the KHR extension alias (pre-1.3 extension).
        internal bool UseKhrDynamicRendering { get; private set; }

        // VK_EXT_memory_budget
        public bool HasMemoryBudget { get; private set; }

        // VK_EXT_host_image_copy
        public bool HasHostImageCopy { get; private set; }

        // VK_EXT_descriptor_indexing (core in Vulkan 1.2)
        public bool HasDescriptorIndexing { get; private set; }

        // VK_KHR_fragment_shading_rate
        public bool HasFragmentShadingRate { get; private set; }

        // VK_EXT_mesh_shader
        public bool HasMeshShader { get; private set; }

        // VK_KHR_get_surface_capabilities2 + VK_EXT_surface_maintenance1 (instance) +
        // VK_EXT_swapchain_maintenance1 (device).
        public bool HasSwapchainMaintenance1 { get; private set; }

        // VK_KHR_synchronization2 (core in Vulkan 1.3).
        public bool HasSynchronization2 { get; private set; }
        // When true, submitCommandsCore must call vkQueueSubmit2KHR instead of vkQueueSubmit2.
        // Set when the driver exposes vkQueueSubmit2KHR but not vkQueueSubmit2 (pre-1.3 extension).
        private bool useQueueSubmit2Khr;

        // VK_KHR_timeline_semaphore (core in Vulkan 1.2).
        public bool HasTimelineSemaphore { get; private set; }

        // VK_GOOGLE_display_timing
        public bool HasDisplayTiming { get; private set; }

        // VK_EXT_pipeline_creation_cache_control (core in Vulkan 1.3).
        // When true, pipeline creation calls may pass
        // VK_PIPELINE_CREATE_FAIL_ON_PIPELINE_COMPILE_REQUIRED_BIT to avoid blocking
        // the render thread when the pipeline is not already in the cache, receiving
        // VK_PIPELINE_COMPILE_REQUIRED and falling back to a simpler pipeline until
        // the real one is ready. Eliminates shader-compilation hitches in real-time use.
        public bool HasPipelineCreationCacheControl { get; private set; }

        /// <summary>
        ///     The Vulkan API version supported by the selected physical device.
        /// </summary>
        internal VkVersion DeviceApiVersion { get; private set; }

        /// <summary>Per-instance Vulkan function pointers (Vortice.Vulkan 3.x style).</summary>
        public VkInstanceApi InstanceApi { get; private set; }

        /// <summary>Per-device Vulkan function pointers (Vortice.Vulkan 3.x style).</summary>
        public VkDeviceApi DeviceApi { get; private set; }

        public override ResourceFactory ResourceFactory { get; }
        private static readonly FixedUtf8String s_name = "Veldrid-VkGraphicsDevice";
        private static readonly Lazy<bool> s_is_supported = new Lazy<bool>(checkIsSupported, true);
        private readonly Lock graphicsCommandPoolLock = new Lock();
        private readonly Lock graphicsQueueLock = new Lock();
        private readonly ConcurrentDictionary<VkFormat, VkFilter> filters = new ConcurrentDictionary<VkFormat, VkFilter>();
        private readonly BackendInfoVulkan vulkanInfo;

        private const int shared_command_pool_count = 4;

        // Staging Resources
        // Defaults are intentionally generous: the historical 64 B / 512 B values
        // from upstream Veldrid were vestigial and effectively bypassed the pool
        // entirely for every realistic workload (any UpdateBuffer larger than 512 B
        // was disposed instead of recycled). On Android-Vulkan in particular, the
        // resulting allocator churn was a measurable contributor to the per-frame
        // submission storm that starved DeviceApi.vkQueuePresentKHR. Override via
        // VulkanDeviceOptions.MinStagingBufferSize / MaxStagingBufferSize.
        private const uint default_min_staging_buffer_size = 64 * 1024;
        private const uint default_max_staging_buffer_size = 4 * 1024 * 1024;

        private readonly uint minStagingBufferSize;
        private readonly uint maxStagingBufferSize;

        private readonly Lock stagingResourcesLock = new Lock();
        private readonly List<VkTexture> availableStagingTextures = new List<VkTexture>();
        private readonly List<VkBuffer> availableStagingBuffers = new List<VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, VkTexture> submittedStagingTextures
            = new Dictionary<VkCommandBuffer, VkTexture>();

        private readonly Dictionary<VkCommandBuffer, VkBuffer> submittedStagingBuffers
            = new Dictionary<VkCommandBuffer, VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, SharedCommandPool> submittedSharedCommandPools
            = new Dictionary<VkCommandBuffer, SharedCommandPool>();

        // Free-list of VkTextureUpdateBatch instances. A new batch is pushed back here on Dispose so per-frame
        // BeginTextureUpdateBatch / using-block usage allocates no managed garbage after warmup.
        private readonly Stack<VkTextureUpdateBatch> textureUpdateBatchPool = new Stack<VkTextureUpdateBatch>();

        // Free-list of VkBufferUpdateBatch instances; same pooling rationale as textureUpdateBatchPool.
        private readonly Stack<VkBufferUpdateBatch> bufferUpdateBatchPool = new Stack<VkBufferUpdateBatch>();

        // Exposes the optimal alignment for DeviceApi.vkCmdCopyBufferToImage's bufferOffset to internal callers (the
        // texture-update batch needs it to pack many region uploads into one staging buffer correctly).
        internal ulong OptimalBufferCopyOffsetAlignment => physicalDeviceProperties.limits.optimalBufferCopyOffsetAlignment;

        // Internal accessors for VkTextureUpdateBatch: the batch lives in the same assembly so it goes straight
        // through the existing private helpers rather than duplicating the staging-buffer / shared-command-pool
        // bookkeeping. RentStagingBuffer / ReturnUnusedStagingBuffer mirror getFreeStagingBuffer plus the
        // size-bounded recycle from completeFenceSubmission.
        internal SharedCommandPool GetFreeCommandPool() => getFreeCommandPool();

        internal VkBuffer RentStagingBuffer(uint size) => getFreeStagingBuffer(size);

        internal void ReturnUnusedStagingBuffer(VkBuffer buffer)
        {
            if (buffer.SizeInBytes <= maxStagingBufferSize)
                lock (stagingResourcesLock) availableStagingBuffers.Add(buffer);
            else
                buffer.Dispose();
        }

        internal void RegisterSubmittedStagingBuffer(VkCommandBuffer cb, VkBuffer buffer)
        {
            lock (stagingResourcesLock) submittedStagingBuffers.Add(cb, buffer);
        }

        internal void ReturnTextureUpdateBatch(VkTextureUpdateBatch batch)
        {
            lock (stagingResourcesLock) textureUpdateBatchPool.Push(batch);
        }

        internal void ReturnBufferUpdateBatch(VkBufferUpdateBatch batch)
        {
            lock (stagingResourcesLock) bufferUpdateBatchPool.Push(batch);
        }

        public override TextureUpdateBatch BeginTextureUpdateBatch()
        {
            VkTextureUpdateBatch batch = null;
            lock (stagingResourcesLock)
            {
                if (textureUpdateBatchPool.Count > 0) batch = textureUpdateBatchPool.Pop();
            }

            if (batch == null) return new VkTextureUpdateBatch(this);

            batch.Reopen();
            return batch;
        }

        public override BufferUpdateBatch BeginBufferUpdateBatch()
        {
            VkBufferUpdateBatch batch = null;
            lock (stagingResourcesLock)
            {
                if (bufferUpdateBatchPool.Count > 0) batch = bufferUpdateBatchPool.Pop();
            }

            if (batch == null) return new VkBufferUpdateBatch(this);

            batch.Reopen();
            return batch;
        }

        private readonly Lock submittedFencesLock = new Lock();
        private readonly ConcurrentQueue<Vortice.Vulkan.VkFence> availableSubmissionFences = new ConcurrentQueue<Vortice.Vulkan.VkFence>();
        private readonly List<FenceSubmissionInfo> submittedFences = new List<FenceSubmissionInfo>();
        private readonly VkSwapchain mainSwapchain;

        private readonly List<FixedUtf8String> surfaceExtensions = new List<FixedUtf8String>();

        private VkInstance instance;
        private string deviceName;
        private string vendorName;
        private GraphicsApiVersion apiVersion;
        private VkPhysicalDeviceProperties physicalDeviceProperties;
        private VkPhysicalDeviceFeatures physicalDeviceFeatures;
        private VkPhysicalDeviceMemoryProperties physicalDeviceMemProperties;
        private VkDevice device;
        private VkCommandPool graphicsCommandPool;
        private VkQueue graphicsQueue;
        private VkDebugReportCallbackEXT debugCallbackHandle;
        internal bool debugMarkerEnabled;
        private readonly Stack<SharedCommandPool> sharedGraphicsCommandPools = new Stack<SharedCommandPool>();
        private bool standardValidationSupported;
        private bool khronosValidationSupported;
        private bool standardClipYDirection;
        private VkPipelineCache pipelineCache;

        /// <summary>
        ///     The shared VkPipelineCache for this device. Used by all pipeline creation calls
        ///     to enable driver-side pipeline caching and deduplication.
        /// </summary>
        public VkPipelineCache PipelineCache => pipelineCache;

        public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc)
            : this(options, scDesc, new VulkanDeviceOptions())
        {
        }

        public VkGraphicsDevice(GraphicsDeviceOptions options, SwapchainDescription? scDesc, VulkanDeviceOptions vkOptions)
        {
            minStagingBufferSize = vkOptions.MinStagingBufferSize ?? default_min_staging_buffer_size;
            maxStagingBufferSize = vkOptions.MaxStagingBufferSize ?? default_max_staging_buffer_size;
            if (maxStagingBufferSize < minStagingBufferSize) maxStagingBufferSize = minStagingBufferSize;

            createInstance(options.Debug, vkOptions);

            var surface = VkSurfaceKHR.Null;
            if (scDesc != null) surface = VkSurfaceUtil.CreateSurface(this, instance, scDesc.Value.Source);

            createPhysicalDevice();
            createLogicalDevice(surface, options.PreferStandardClipSpaceYDirection, vkOptions);

            MemoryManager = new VkDeviceMemoryManager(
                device,
                DeviceApi,
                physicalDeviceProperties.limits.bufferImageGranularity);

            Features = new GraphicsDeviceFeatures(
                true,
                physicalDeviceFeatures.geometryShader,
                physicalDeviceFeatures.tessellationShader,
                physicalDeviceFeatures.multiViewport,
                true,
                true,
                true,
                true,
                physicalDeviceFeatures.drawIndirectFirstInstance,
                physicalDeviceFeatures.fillModeNonSolid,
                physicalDeviceFeatures.samplerAnisotropy,
                physicalDeviceFeatures.depthClamp,
                true,
                physicalDeviceFeatures.independentBlend,
                true,
                true,
                debugMarkerEnabled,
                true,
                physicalDeviceFeatures.shaderFloat64,
                variableRateShading: HasFragmentShadingRate,
                meshShader: HasMeshShader);

            ResourceFactory = new VkResourceFactory(this);

            // Create pipeline cache for driver-side caching of compiled pipelines.
            // When the host supplies persisted PipelineCacheData (typically a blob saved
            // to disk on a previous run), feed it through pInitialData so the driver can
            // skip re-compiling matching pipelines. The driver header-validates the blob
            // (vendorID / deviceID / driver UUID) and silently discards on mismatch, so
            // it's always safe to pass through stale data without manual versioning.
            var pipelineCacheCi = new VkPipelineCacheCreateInfo();
            byte[] initialCacheData = vkOptions.PipelineCacheData;
            if (initialCacheData != null && initialCacheData.Length > 0)
            {
                fixed (byte* initialDataPtr = initialCacheData)
                {
                    pipelineCacheCi.initialDataSize = (UIntPtr)initialCacheData.Length;
                    pipelineCacheCi.pInitialData = initialDataPtr;
                    var cacheResult = DeviceApi.vkCreatePipelineCache(&pipelineCacheCi, null, out pipelineCache);
                    CheckResult(cacheResult);
                }
            }
            else
            {
                var cacheResult = DeviceApi.vkCreatePipelineCache(&pipelineCacheCi, null, out pipelineCache);
                CheckResult(cacheResult);
            }

            if (scDesc != null)
            {
                var desc = scDesc.Value;
                mainSwapchain = new VkSwapchain(this, ref desc, surface);
            }

            createDescriptorPool();
            createGraphicsCommandPool();
            for (int i = 0; i < shared_command_pool_count; i++) sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));

            vulkanInfo = new BackendInfoVulkan(this);

            PostDeviceCreated();
        }

        public override bool GetVulkanInfo(out BackendInfoVulkan info)
        {
            info = vulkanInfo;
            return true;
        }

        public bool HasSurfaceExtension(FixedUtf8String extension)
        {
            return surfaceExtensions.Contains(extension);
        }

        public void EnableDebugCallback(VkDebugReportFlagsEXT flags = VkDebugReportFlagsEXT.Warning | VkDebugReportFlagsEXT.Error)
        {
            Debug.WriteLine("Enabling Vulkan Debug callbacks.");
            var debugCallbackCi = new VkDebugReportCallbackCreateInfoEXT();
            debugCallbackCi.flags = flags;
            debugCallbackCi.pfnCallback = &staticDebugCallback;
            VkDebugReportCallbackEXT handle;
            var result = InstanceApi.vkCreateDebugReportCallbackEXT(&debugCallbackCi, null, &handle);
            if (result == VkResult.Success) debugCallbackHandle = handle;
        }

        public VkExtensionProperties[] GetDeviceExtensionProperties()
        {
            uint propertyCount = 0;
            var result = InstanceApi.vkEnumerateDeviceExtensionProperties(PhysicalDevice, null, &propertyCount, null);
            CheckResult(result);
            var props = new VkExtensionProperties[(int)propertyCount];

            fixed (VkExtensionProperties* properties = props)
            {
                result = InstanceApi.vkEnumerateDeviceExtensionProperties(PhysicalDevice, null, &propertyCount, properties);
                CheckResult(result);
            }

            return props;
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            var usageFlags = VkImageUsageFlags.Sampled;
            usageFlags |= depthFormat ? VkImageUsageFlags.DepthStencilAttachment : VkImageUsageFlags.ColorAttachment;

            VkImageFormatProperties formatProperties;
            InstanceApi.vkGetPhysicalDeviceImageFormatProperties(
                PhysicalDevice,
                VkFormats.VdToVkPixelFormat(format),
                VkImageType.Image2D,
                VkImageTiling.Optimal,
                usageFlags,
                0,
                &formatProperties);

            var vkSampleCounts = formatProperties.sampleCounts;
            if ((vkSampleCounts & VkSampleCountFlags.Count32) == VkSampleCountFlags.Count32)
                return TextureSampleCount.Count32;
            if ((vkSampleCounts & VkSampleCountFlags.Count16) == VkSampleCountFlags.Count16)
                return TextureSampleCount.Count16;
            if ((vkSampleCounts & VkSampleCountFlags.Count8) == VkSampleCountFlags.Count8)
                return TextureSampleCount.Count8;
            if ((vkSampleCounts & VkSampleCountFlags.Count4) == VkSampleCountFlags.Count4)
                return TextureSampleCount.Count4;
            if ((vkSampleCounts & VkSampleCountFlags.Count2) == VkSampleCountFlags.Count2) return TextureSampleCount.Count2;

            return TextureSampleCount.Count1;
        }

        public override void ResetFence(Fence fence)
        {
            var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            DeviceApi.vkResetFences(1, &vkFence);
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            var result = DeviceApi.vkWaitForFences(1, &vkFence, true, nanosecondTimeout);
            return result == VkResult.Success;
        }

        public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
        {
            int fenceCount = fences.Length;
            var fencesPtr = stackalloc Vortice.Vulkan.VkFence[fenceCount];
            for (int i = 0; i < fenceCount; i++) fencesPtr[i] = Util.AssertSubtype<Fence, VkFence>(fences[i]).DeviceFence;

            var result = DeviceApi.vkWaitForFences((uint)fenceCount, fencesPtr, waitAll, nanosecondTimeout);
            return result == VkResult.Success;
        }

        internal static bool IsSupported()
        {
            return s_is_supported.Value;
        }

        internal void SetResourceName(IDeviceResource resource, string name)
        {
            if (debugMarkerEnabled)
            {
                switch (resource)
                {
                    case VkBuffer buffer:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.Buffer, buffer.DeviceBuffer.Handle, name);
                        break;

                    case VkCommandList commandList:
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.CommandBuffer,
                            (ulong)commandList.CommandBuffer.Handle,
                            $"{name}_CommandBuffer");
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.CommandPool,
                            commandList.CommandPool.Handle,
                            $"{name}_CommandPool");
                        break;

                    case VkFramebuffer framebuffer:
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.Framebuffer,
                            framebuffer.CurrentFramebuffer.Handle,
                            name);
                        break;

                    case VkPipeline pipeline:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.Pipeline, pipeline.DevicePipeline.Handle, name);
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.PipelineLayout, pipeline.PipelineLayout.Handle, name);
                        break;

                    case VkResourceLayout resourceLayout:
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.DescriptorSetLayout,
                            resourceLayout.DescriptorSetLayout.Handle,
                            name);
                        break;

                    case VkResourceSet resourceSet:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.DescriptorSet, resourceSet.DescriptorSet.Handle, name);
                        break;

                    case VkSampler sampler:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.Sampler, sampler.DeviceSampler.Handle, name);
                        break;

                    case VkShader shader:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.ShaderModule, shader.ShaderModule.Handle, name);
                        break;

                    case VkTexture tex:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.Image, tex.OptimalDeviceImage.Handle, name);
                        break;

                    case VkTextureView texView:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.ImageView, texView.ImageView.Handle, name);
                        break;

                    case VkFence fence:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.Fence, fence.DeviceFence.Handle, name);
                        break;

                    case VkSwapchain sc:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.SwapchainKHR, sc.DeviceSwapchain.Handle, name);
                        break;
                }
            }
        }

        internal VkFilter GetFormatFilter(VkFormat format)
        {
            if (!filters.TryGetValue(format, out var filter))
            {
                VkFormatProperties vkFormatProps;
                InstanceApi.vkGetPhysicalDeviceFormatProperties(PhysicalDevice, format, &vkFormatProps);
                filter = (vkFormatProps.optimalTilingFeatures & VkFormatFeatureFlags.SampledImageFilterLinear) != 0
                    ? VkFilter.Linear
                    : VkFilter.Nearest;
                filters.TryAdd(format, filter);
            }

            return filter;
        }

        internal void ClearColorTexture(VkTexture texture, VkClearColorValue color)
        {
            uint effectiveLayers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0) effectiveLayers *= 6;
            var range = new VkImageSubresourceRange(
                VkImageAspectFlags.Color,
                0,
                texture.MipLevels,
                0,
                effectiveLayers);
            var pool = getFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.TransferDstOptimal);
            DeviceApi.vkCmdClearColorImage(cb, texture.OptimalDeviceImage, VkImageLayout.TransferDstOptimal, &color, 1, &range);
            var colorLayout = texture.IsSwapchainTexture ? VkImageLayout.PresentSrcKHR : VkImageLayout.ColorAttachmentOptimal;
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, colorLayout);
            pool.EndAndSubmit(cb);
        }

        internal void ClearDepthTexture(VkTexture texture, VkClearDepthStencilValue clearValue)
        {
            uint effectiveLayers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0) effectiveLayers *= 6;
            var aspect = FormatHelpers.IsStencilFormat(texture.Format)
                ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                : VkImageAspectFlags.Depth;
            var range = new VkImageSubresourceRange(
                aspect,
                0,
                texture.MipLevels,
                0,
                effectiveLayers);
            var pool = getFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.TransferDstOptimal);
            DeviceApi.vkCmdClearDepthStencilImage(
                cb,
                texture.OptimalDeviceImage,
                VkImageLayout.TransferDstOptimal,
                &clearValue,
                1,
                &range);
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.DepthStencilAttachmentOptimal);
            pool.EndAndSubmit(cb);
        }

        internal override uint GetUniformBufferMinOffsetAlignmentCore()
        {
            return (uint)physicalDeviceProperties.limits.minUniformBufferOffsetAlignment;
        }

        internal override uint GetStructuredBufferMinOffsetAlignmentCore()
        {
            return (uint)physicalDeviceProperties.limits.minStorageBufferOffsetAlignment;
        }

        internal void TransitionImageLayout(VkTexture texture, VkImageLayout layout)
        {
            var pool = getFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, texture.ActualArrayLayers, layout);
            pool.EndAndSubmit(cb);
        }

        protected override MappedResource MapCore(IMappableResource resource, MapMode mode, uint subresource)
        {
            VkMemoryBlock memoryBlock;
            IntPtr mappedPtr = IntPtr.Zero;
            uint sizeInBytes;
            uint offset = 0;
            uint rowPitch = 0;
            uint depthPitch = 0;

            if (resource is VkBuffer buffer)
            {
                memoryBlock = buffer.Memory;
                sizeInBytes = buffer.SizeInBytes;
            }
            else
            {
                var texture = Util.AssertSubtype<IMappableResource, VkTexture>(resource);
                var layout = texture.GetSubresourceLayout(subresource);
                memoryBlock = texture.Memory;
                sizeInBytes = (uint)layout.size;
                offset = (uint)layout.offset;
                rowPitch = (uint)layout.rowPitch;
                depthPitch = (uint)layout.depthPitch;
            }

            if (memoryBlock.DeviceMemory.Handle != 0)
            {
                if (memoryBlock.IsPersistentMapped)
                    mappedPtr = (IntPtr)memoryBlock.BlockMappedPointer;
                else
                    mappedPtr = MemoryManager.Map(memoryBlock);
            }

            byte* dataPtr = (byte*)mappedPtr.ToPointer() + offset;
            return new MappedResource(
                resource,
                mode,
                (IntPtr)dataPtr,
                sizeInBytes,
                subresource,
                rowPitch,
                depthPitch);
        }

        protected override void UnmapCore(IMappableResource resource, uint subresource)
        {
            VkMemoryBlock memoryBlock;

            if (resource is VkBuffer buffer)
                memoryBlock = buffer.Memory;
            else
            {
                var tex = Util.AssertSubtype<IMappableResource, VkTexture>(resource);
                memoryBlock = tex.Memory;
            }

            if (memoryBlock.DeviceMemory.Handle != 0 && !memoryBlock.IsPersistentMapped)
                DeviceApi.vkUnmapMemory(memoryBlock.DeviceMemory);
        }

        protected override void PlatformDispose()
        {
            Debug.Assert(submittedFences.Count == 0);
            foreach (var fence in availableSubmissionFences) DeviceApi.vkDestroyFence(fence, null);

            mainSwapchain?.Dispose();

            if (debugCallbackHandle.Handle != 0)
            {
                InstanceApi.vkDestroyDebugReportCallbackEXT(debugCallbackHandle, null);
                debugCallbackHandle = default;
            }

            DescriptorPoolManager.DestroyAll();
            DeviceApi.vkDestroyCommandPool(graphicsCommandPool, null);

            if (pipelineCache != VkPipelineCache.Null)
                DeviceApi.vkDestroyPipelineCache(pipelineCache, null);

            Debug.Assert(submittedStagingTextures.Count == 0);
            foreach (var tex in availableStagingTextures) tex.Dispose();

            Debug.Assert(submittedStagingBuffers.Count == 0);
            foreach (var buffer in availableStagingBuffers) buffer.Dispose();

            lock (graphicsCommandPoolLock)
            {
                while (sharedGraphicsCommandPools.Count > 0)
                {
                    var sharedPool = sharedGraphicsCommandPools.Pop();
                    sharedPool.Destroy();
                }
            }

            MemoryManager.Dispose();

            var result = DeviceApi.vkDeviceWaitIdle();
            CheckResult(result);
            DeviceApi.vkDestroyDevice(null);
            InstanceApi.vkDestroyInstance(null);
        }

        private static bool checkIsSupported()
        {
            if (!IsVulkanLoaded()) return false;

            var instanceCi = new VkInstanceCreateInfo();
            var applicationInfo = new VkApplicationInfo
            {
                apiVersion = new Vortice.Vulkan.VkVersion(1, 0, 0),
                applicationVersion = new Vortice.Vulkan.VkVersion(1, 0, 0),
                engineVersion = new Vortice.Vulkan.VkVersion(1, 0, 0),
                pApplicationName = s_name,
                pEngineName = s_name
            };

            instanceCi.pApplicationInfo = &applicationInfo;

            var result = vkCreateInstance(in instanceCi, null, out var testInstance);
            if (result != VkResult.Success) return false;

            var testInstApi = new VkInstanceApi(testInstance);
            uint physicalDeviceCount = 0;
            result = testInstApi.vkEnumeratePhysicalDevices(&physicalDeviceCount, null);

            if (result != VkResult.Success || physicalDeviceCount == 0)
            {
                testInstApi.vkDestroyInstance(null);
                return false;
            }

            testInstApi.vkDestroyInstance(null);

            var instanceExtensions = new HashSet<string>(GetInstanceExtensions());
            if (!instanceExtensions.Contains(CommonStrings.VkKhrSurfaceExtensionName)) return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return instanceExtensions.Contains(CommonStrings.VkKhrWin32SurfaceExtensionName);
#if NET5_0_OR_GREATER

            if (OperatingSystem.IsAndroid())
                return instanceExtensions.Contains(CommonStrings.VkKhrAndroidSurfaceExtensionName);
#endif

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (RuntimeInformation.OSDescription.Contains("Unix")) // Android
                    return instanceExtensions.Contains(CommonStrings.VkKhrAndroidSurfaceExtensionName);

                return instanceExtensions.Contains(CommonStrings.VkKhrXlibSurfaceExtensionName);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.OSDescription.Contains("Darwin")) // macOS
                    return instanceExtensions.Contains(CommonStrings.VkMvkMacosSurfaceExtensionName);
                // iOS
                return instanceExtensions.Contains(CommonStrings.VkMvkIOSSurfaceExtensionName);
            }

            return false;
        }

        private void submitCommandList(
            CommandList cl,
            uint waitSemaphoreCount,
            VkSemaphore* waitSemaphoresPtr,
            uint signalSemaphoreCount,
            VkSemaphore* signalSemaphoresPtr,
            Fence fence)
        {
            var vkCl = Util.AssertSubtype<CommandList, VkCommandList>(cl);
            var vkCb = vkCl.CommandBuffer;

            vkCl.CommandBufferSubmitted(vkCb);
            submitCommandBuffer(vkCl, vkCb, waitSemaphoreCount, waitSemaphoresPtr, signalSemaphoreCount, signalSemaphoresPtr, fence);
        }

        private void submitCommandBuffer(
            VkCommandList vkCl,
            VkCommandBuffer vkCb,
            uint waitSemaphoreCount,
            VkSemaphore* waitSemaphoresPtr,
            uint signalSemaphoreCount,
            VkSemaphore* signalSemaphoresPtr,
            Fence fence)
        {
            checkSubmittedFences();

            bool useExtraFence = fence != null;

            Vortice.Vulkan.VkFence vkFence;
            Vortice.Vulkan.VkFence submissionFence;

            if (useExtraFence)
            {
                vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
                submissionFence = getFreeSubmissionFence();
            }
            else
            {
                vkFence = getFreeSubmissionFence();
                submissionFence = vkFence;
            }

            if (HasSynchronization2)
            {
                // vkQueueSubmit2 path: per-semaphore pipeline stage masks, no shared
                // pWaitDstStageMask array. Requires VK_KHR_synchronization2 / Vulkan 1.3.
                var cbInfo = new VkCommandBufferSubmitInfo();
                cbInfo.commandBuffer = vkCb;
                cbInfo.deviceMask = 0;

                var waitInfos = stackalloc VkSemaphoreSubmitInfo[(int)waitSemaphoreCount];
                for (uint i = 0; i < waitSemaphoreCount; i++)
                {
                    waitInfos[i] = new VkSemaphoreSubmitInfo();
                    waitInfos[i].semaphore = waitSemaphoresPtr[i];
                    // Mirror the legacy pWaitDstStageMask = ColorAttachmentOutput.
                    waitInfos[i].stageMask = VkPipelineStageFlags2.ColorAttachmentOutput;
                }

                var signalInfos = stackalloc VkSemaphoreSubmitInfo[(int)signalSemaphoreCount];
                for (uint i = 0; i < signalSemaphoreCount; i++)
                {
                    signalInfos[i] = new VkSemaphoreSubmitInfo();
                    signalInfos[i].semaphore = signalSemaphoresPtr[i];
                    signalInfos[i].stageMask = VkPipelineStageFlags2.AllCommands;
                }

                var si2 = new VkSubmitInfo2();
                si2.waitSemaphoreInfoCount = waitSemaphoreCount;
                si2.pWaitSemaphoreInfos = waitSemaphoreCount > 0 ? waitInfos : null;
                si2.commandBufferInfoCount = 1;
                si2.pCommandBufferInfos = &cbInfo;
                si2.signalSemaphoreInfoCount = signalSemaphoreCount;
                si2.pSignalSemaphoreInfos = signalSemaphoreCount > 0 ? signalInfos : null;

                lock (graphicsQueueLock)
                {
                    var result = useQueueSubmit2Khr
                        ? DeviceApi.vkQueueSubmit2KHR(graphicsQueue, 1, &si2, vkFence)
                        : DeviceApi.vkQueueSubmit2(graphicsQueue, 1, &si2, vkFence);
                    CheckResult(result);

                    if (useExtraFence)
                    {
                        result = useQueueSubmit2Khr
                            ? DeviceApi.vkQueueSubmit2KHR(graphicsQueue, 0, null, submissionFence)
                            : DeviceApi.vkQueueSubmit2(graphicsQueue, 0, null, submissionFence);
                        CheckResult(result);
                    }
                }
            }
            else
            {
                // Legacy DeviceApi.vkQueueSubmit path: single pWaitDstStageMask shared across all
                // wait semaphores. Used on Vulkan < 1.3 without VK_KHR_synchronization2.
                var si = new VkSubmitInfo();
                si.commandBufferCount = 1;
                si.pCommandBuffers = &vkCb;
                var waitDstStageMask = VkPipelineStageFlags.ColorAttachmentOutput;
                si.pWaitDstStageMask = &waitDstStageMask;

                si.pWaitSemaphores = waitSemaphoresPtr;
                si.waitSemaphoreCount = waitSemaphoreCount;
                si.pSignalSemaphores = signalSemaphoresPtr;
                si.signalSemaphoreCount = signalSemaphoreCount;

                lock (graphicsQueueLock)
                {
                    var result = DeviceApi.vkQueueSubmit(graphicsQueue, 1, &si, vkFence);
                    CheckResult(result);

                    if (useExtraFence)
                    {
                        result = DeviceApi.vkQueueSubmit(graphicsQueue, 0, null, submissionFence);
                        CheckResult(result);
                    }
                }
            }

            lock (submittedFencesLock)
                submittedFences.Add(new FenceSubmissionInfo(submissionFence, vkCl, vkCb));
        }

        private void checkSubmittedFences()
        {
            lock (submittedFencesLock)
            {
                if (submittedFences.Count == 0)
                    return;

                // Quick-check the oldest fence first; if it's not ready, none after it will be.
                // vkGetFenceStatus is a non-blocking status query and is cheaper than vkWaitForFences(timeout=0)
                // on drivers that treat the wait path differently.
                if (DeviceApi.vkGetFenceStatus(submittedFences[0].Fence) != VkResult.Success)
                    return;

                for (int i = 0; i < submittedFences.Count; i++)
                {
                    var fsi = submittedFences[i];

                    if (DeviceApi.vkGetFenceStatus(fsi.Fence) == VkResult.Success)
                    {
                        completeFenceSubmission(fsi);
                        submittedFences.RemoveAt(i);
                        i -= 1;
                    }
                    else
                        break; // Submissions are in order; later submissions cannot complete if this one hasn't.
                }
            }
        }

        private void completeFenceSubmission(FenceSubmissionInfo fsi)
        {
            var fence = fsi.Fence;
            var completedCb = fsi.CommandBuffer;
            fsi.CommandList?.CommandBufferCompleted(completedCb);
            var resetResult = DeviceApi.vkResetFences(1, &fence);
            CheckResult(resetResult);
            returnSubmissionFence(fence);

            lock (stagingResourcesLock)
            {
                if (submittedStagingTextures.Remove(completedCb, out var stagingTex))
                    availableStagingTextures.Add(stagingTex);

                if (submittedStagingBuffers.Remove(completedCb, out var stagingBuffer))
                {
                    if (stagingBuffer.SizeInBytes <= maxStagingBufferSize)
                        availableStagingBuffers.Add(stagingBuffer);
                    else
                        stagingBuffer.Dispose();
                }

                if (submittedSharedCommandPools.Remove(completedCb, out var sharedPool))
                {
                    lock (graphicsCommandPoolLock)
                    {
                        if (sharedPool.IsCached)
                            sharedGraphicsCommandPools.Push(sharedPool);
                        else
                            sharedPool.Destroy();
                    }
                }
            }
        }

        private void returnSubmissionFence(Vortice.Vulkan.VkFence fence)
        {
            availableSubmissionFences.Enqueue(fence);
        }

        private Vortice.Vulkan.VkFence getFreeSubmissionFence()
        {
            if (availableSubmissionFences.TryDequeue(out var availableFence))
                return availableFence;

            var fenceCi = new VkFenceCreateInfo();
            var result = DeviceApi.vkCreateFence(&fenceCi, null, out var newFence);
            CheckResult(result);
            return newFence;
        }

        private void setDebugMarkerName(VkDebugReportObjectTypeEXT type, ulong target, string name)
        {
            Debug.Assert(debugMarkerEnabled);

            var nameInfo = new VkDebugMarkerObjectNameInfoEXT();
            nameInfo.objectType = type;
            nameInfo.@object = target;

            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            nameInfo.pObjectName = utf8Ptr;
            var result = DeviceApi.vkDebugMarkerSetObjectNameEXT(&nameInfo);
            CheckResult(result);
        }

        private void createInstance(bool debug, VulkanDeviceOptions options)
        {
            var availableInstanceLayers = new HashSet<string>(EnumerateInstanceLayers());
            var availableInstanceExtensions = new HashSet<string>(GetInstanceExtensions());

            var instanceCi = new VkInstanceCreateInfo();

            // Query the highest supported Vulkan instance version via the Vortice global function.
            // Vortice handles the Vulkan 1.0 fallback internally (VK_VERSION_1_0 when absent).
            var queriedVersion = vkEnumerateInstanceVersion();
            uint instanceApiVersion = queriedVersion.Value != 0
                ? queriedVersion.Value
                : new Vortice.Vulkan.VkVersion(1, 0, 0);

            var applicationInfo = new VkApplicationInfo
            {
                apiVersion = new Vortice.Vulkan.VkVersion(instanceApiVersion),
                applicationVersion = new Vortice.Vulkan.VkVersion(1, 0, 0),
                engineVersion = new Vortice.Vulkan.VkVersion(1, 0, 0),
                pApplicationName = s_name,
                pEngineName = s_name
            };

            instanceCi.pApplicationInfo = &applicationInfo;

            var instanceExtensions = new StackList<IntPtr, Size64Bytes>();
            var instanceLayers = new StackList<IntPtr, Size64Bytes>();

            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrPortabilitySubset))
                surfaceExtensions.Add(CommonStrings.VkKhrPortabilitySubset);

            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrSurfaceExtensionName))
                surfaceExtensions.Add(CommonStrings.VkKhrSurfaceExtensionName);

            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrPortabilityEnumeration))
            {
                instanceExtensions.Add(CommonStrings.VkKhrPortabilityEnumeration);
                instanceCi.flags |= VkInstanceCreateFlags.EnumeratePortabilityKHR;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VkKhrWin32SurfaceExtensionName)) surfaceExtensions.Add(CommonStrings.VkKhrWin32SurfaceExtensionName);
            }
            else if (
#if NET5_0_OR_GREATER
                OperatingSystem.IsAndroid() ||
#endif
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VkKhrAndroidSurfaceExtensionName)) surfaceExtensions.Add(CommonStrings.VkKhrAndroidSurfaceExtensionName);

                if (availableInstanceExtensions.Contains(CommonStrings.VkKhrXlibSurfaceExtensionName)) surfaceExtensions.Add(CommonStrings.VkKhrXlibSurfaceExtensionName);

                if (availableInstanceExtensions.Contains(CommonStrings.VkKhrWaylandSurfaceExtensionName)) surfaceExtensions.Add(CommonStrings.VkKhrWaylandSurfaceExtensionName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VkExtMetalSurfaceExtensionName))
                    surfaceExtensions.Add(CommonStrings.VkExtMetalSurfaceExtensionName);
                else // Legacy MoltenVK extensions
                {
                    if (availableInstanceExtensions.Contains(CommonStrings.VkMvkMacosSurfaceExtensionName)) surfaceExtensions.Add(CommonStrings.VkMvkMacosSurfaceExtensionName);

                    if (availableInstanceExtensions.Contains(CommonStrings.VkMvkIOSSurfaceExtensionName)) surfaceExtensions.Add(CommonStrings.VkMvkIOSSurfaceExtensionName);
                }
            }

            foreach (var ext in surfaceExtensions) instanceExtensions.Add(ext);

            bool hasDeviceProperties2 = availableInstanceExtensions.Contains(CommonStrings.VkKhrGetPhysicalDeviceProperties2);
            if (hasDeviceProperties2) instanceExtensions.Add(CommonStrings.VkKhrGetPhysicalDeviceProperties2);

            // VK_KHR_get_surface_capabilities2 + VK_EXT_surface_maintenance1 are required
            // by VK_EXT_swapchain_maintenance1 to query per-mode compatibility sets.
            // Both are instance-level; the device-level extension is detected later.
            bool hasSurfaceCapabilities2 = availableInstanceExtensions.Contains(CommonStrings.VkKhrGetSurfaceCapabilities2);
            if (hasSurfaceCapabilities2) instanceExtensions.Add(CommonStrings.VkKhrGetSurfaceCapabilities2);

            bool hasSurfaceMaintenance1 = hasSurfaceCapabilities2
                                          && availableInstanceExtensions.Contains(CommonStrings.VkExtSurfaceMaintenance1);
            if (hasSurfaceMaintenance1) instanceExtensions.Add(CommonStrings.VkExtSurfaceMaintenance1);

            string[] requestedInstanceExtensions = options.InstanceExtensions ?? Array.Empty<string>();
            var tempStrings = new List<FixedUtf8String>();

            foreach (string requiredExt in requestedInstanceExtensions)
            {
                if (!availableInstanceExtensions.Contains(requiredExt)) throw new VeldridException($"The required instance extension was not available: {requiredExt}");

                var utf8Str = new FixedUtf8String(requiredExt);
                instanceExtensions.Add(utf8Str);
                tempStrings.Add(utf8Str);
            }

            bool debugReportExtensionAvailable = false;

            if (debug)
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VkExtDebugReportExtensionName))
                {
                    debugReportExtensionAvailable = true;
                    instanceExtensions.Add(CommonStrings.VkExtDebugReportExtensionName);
                }

                if (availableInstanceLayers.Contains(CommonStrings.StandardValidationLayerName))
                {
                    standardValidationSupported = true;
                    instanceLayers.Add(CommonStrings.StandardValidationLayerName);
                }

                if (availableInstanceLayers.Contains(CommonStrings.KhronosValidationLayerName))
                {
                    khronosValidationSupported = true;
                    instanceLayers.Add(CommonStrings.KhronosValidationLayerName);
                }
            }

            instanceCi.enabledExtensionCount = instanceExtensions.Count;
            instanceCi.ppEnabledExtensionNames = (byte**)instanceExtensions.Data;

            instanceCi.enabledLayerCount = instanceLayers.Count;
            if (instanceLayers.Count > 0) instanceCi.ppEnabledLayerNames = (byte**)instanceLayers.Data;

            var result = vkCreateInstance(in instanceCi, null, out instance);
            CheckResult(result);
            InstanceApi = new VkInstanceApi(instance);

            if (debug && debugReportExtensionAvailable) EnableDebugCallback();

            foreach (var tempStr in tempStrings) tempStr.Dispose();
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static uint staticDebugCallback(
            VkDebugReportFlagsEXT flags,
            VkDebugReportObjectTypeEXT objectType,
            ulong @object,
            nuint location,
            int messageCode,
            byte* pLayerPrefix,
            byte* pMessage,
            void* pUserData)
        {
            string message = Util.GetString(pMessage);

#if DEBUG
            if (Debugger.IsAttached) Debugger.Break();
#endif

            string fullMessage = $"[{flags}] ({objectType}) {message}";

            if (flags == VkDebugReportFlagsEXT.Error) throw new VeldridException($"A Vulkan validation error was encountered: {fullMessage}");

            Console.WriteLine(fullMessage);
            return 0;
        }

        private void createPhysicalDevice()
        {
            uint deviceCount = 0;
            InstanceApi.vkEnumeratePhysicalDevices(&deviceCount, null);
            if (deviceCount == 0) throw new InvalidOperationException("No physical devices exist.");

            var physicalDevices = new VkPhysicalDevice[deviceCount];
            fixed (VkPhysicalDevice* pdPtr = physicalDevices)
                InstanceApi.vkEnumeratePhysicalDevices(&deviceCount, pdPtr);

            // Prefer discrete GPU over integrated/virtual/CPU, falling back to the first device.
            PhysicalDevice = physicalDevices[0];

            for (int i = 0; i < physicalDevices.Length; i++)
            {
                VkPhysicalDeviceProperties deviceProps;
                InstanceApi.vkGetPhysicalDeviceProperties(physicalDevices[i], &deviceProps);

                if (deviceProps.deviceType == VkPhysicalDeviceType.DiscreteGpu)
                {
                    PhysicalDevice = physicalDevices[i];
                    break;
                }

                if (deviceProps.deviceType == VkPhysicalDeviceType.IntegratedGpu && physicalDevices[0] != physicalDevices[i])
                {
                    // Prefer integrated over anything worse, but keep looking for discrete.
                    PhysicalDevice = physicalDevices[i];
                }
            }

            VkPhysicalDeviceProperties props;
            InstanceApi.vkGetPhysicalDeviceProperties(PhysicalDevice, &props);
            physicalDeviceProperties = props;
            fixed (byte* utf8NamePtr = physicalDeviceProperties.deviceName)
                deviceName = Encoding.UTF8.GetString(utf8NamePtr, 256).TrimEnd('\0');

            DeviceApiVersion = VkVersion.FromPacked(physicalDeviceProperties.apiVersion);
            vendorName = $"id:{physicalDeviceProperties.vendorID:x8}";
            apiVersion = new GraphicsApiVersion((int)DeviceApiVersion.Major, (int)DeviceApiVersion.Minor, 0, (int)DeviceApiVersion.Patch);
            DriverInfo = $"version:{physicalDeviceProperties.driverVersion:x8}";

            VkPhysicalDeviceFeatures features;
            InstanceApi.vkGetPhysicalDeviceFeatures(PhysicalDevice, &features);
            physicalDeviceFeatures = features;

            VkPhysicalDeviceMemoryProperties memProps;
            InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, &memProps);
            physicalDeviceMemProperties = memProps;
        }

        private void createLogicalDevice(VkSurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
        {
            getQueueFamilyIndices(surface);

            var familyIndices = new HashSet<uint> { GraphicsQueueIndex, PresentQueueIndex };
            var queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[familyIndices.Count];
            uint queueCreateInfosCount = (uint)familyIndices.Count;

            int i = 0;

            foreach (uint queueFamilyIndex in familyIndices)
            {
                var queueCreateInfo = new VkDeviceQueueCreateInfo();
                queueCreateInfo.queueFamilyIndex = queueFamilyIndex;
                queueCreateInfo.queueCount = 1;
                float priority = 1f;
                queueCreateInfo.pQueuePriorities = &priority;
                queueCreateInfos[i] = queueCreateInfo;
                i += 1;
            }

            var deviceFeatures = physicalDeviceFeatures;

            var props = GetDeviceExtensionProperties();

            var requiredInstanceExtensions = new HashSet<string>(options.DeviceExtensions ?? Array.Empty<string>());

            bool hasMemReqs2 = DeviceApiVersion.IsAtLeast(1, 1);
            bool hasDedicatedAllocation = DeviceApiVersion.IsAtLeast(1, 1);
            bool hasDriverProperties = DeviceApiVersion.IsAtLeast(1, 2);
            bool hasPushDescriptors = false;
            bool hasDynamicRendering = DeviceApiVersion.IsAtLeast(1, 3);
            bool hasMemoryBudget = false;
            bool hasHostImageCopy = false;
            bool hasDescriptorIndexing = DeviceApiVersion.IsAtLeast(1, 2); // Core in Vulkan 1.2
            bool hasFragmentShadingRate = false;
            bool hasMeshShader = false;
            bool hasSwapchainMaintenance1 = false;
            bool hasSynchronization2 = DeviceApiVersion.IsAtLeast(1, 3); // Core in Vulkan 1.3
            bool hasTimelineSemaphore = DeviceApiVersion.IsAtLeast(1, 2); // Core in Vulkan 1.2
            bool hasDisplayTiming = false; // VK_GOOGLE_display_timing (Android/Qualcomm)
            bool hasPipelineCreationCacheControl = DeviceApiVersion.IsAtLeast(1, 3); // Core in Vulkan 1.3
            IntPtr[] activeExtensions = new IntPtr[props.Length];
            uint activeExtensionCount = 0;

            // On Vulkan 1.1+, VK_KHR_maintenance1 is core.
            if (preferStandardClipY && DeviceApiVersion.IsAtLeast(1, 1))
                standardClipYDirection = true;

            fixed (VkExtensionProperties* properties = props)
            {
                for (int property = 0; property < props.Length; property++)
                {
                    string extensionName = Util.GetString(properties[property].extensionName);

                    if (extensionName == "VK_EXT_debug_marker")
                    {
                        activeExtensions[activeExtensionCount++] = CommonStrings.VkExtDebugMarkerExtensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        debugMarkerEnabled = true;
                    }
                    else if (extensionName == "VK_KHR_swapchain")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                    }
                    else if (preferStandardClipY && extensionName == "VK_KHR_maintenance1")
                    {
                        // On 1.1+ this is core, but enabling the extension is harmless and some drivers still list it.
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        standardClipYDirection = true;
                    }
                    else if (extensionName == "VK_KHR_get_memory_requirements2")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasMemReqs2 = true;
                    }
                    else if (extensionName == "VK_KHR_dedicated_allocation")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDedicatedAllocation = true;
                    }
                    else if (extensionName == "VK_KHR_driver_properties")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDriverProperties = true;
                    }
                    else if (extensionName == CommonStrings.VkKhrPortabilitySubset)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                    }
                    else if (extensionName == "VK_KHR_push_descriptor")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasPushDescriptors = true;
                    }
                    else if (extensionName == "VK_KHR_dynamic_rendering")
                    {
                        // On 1.3+ this is core; enabling the extension is harmless.
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDynamicRendering = true;
                    }
                    else if (extensionName == "VK_EXT_memory_budget")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasMemoryBudget = true;
                    }
                    else if (extensionName == "VK_EXT_host_image_copy")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasHostImageCopy = true;
                    }
                    else if (extensionName == "VK_EXT_descriptor_indexing")
                    {
                        // Core in Vulkan 1.2; enables bindless/partially-bound descriptors.
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDescriptorIndexing = true;
                    }
                    else if (extensionName == "VK_KHR_fragment_shading_rate")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasFragmentShadingRate = true;
                    }
                    else if (extensionName == "VK_EXT_mesh_shader")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasMeshShader = true;
                    }
                    // VK_EXT_swapchain_maintenance1 (and the promoted KHR variant in
                    // Vulkan 1.4) only become useful if the prerequisite instance
                    // extensions were enabled — otherwise we can't query the
                    // present-mode compatibility set, so the swapchain create call
                    // would have nothing valid to chain.
                    else if ((extensionName == "VK_EXT_swapchain_maintenance1"
                              || extensionName == "VK_KHR_swapchain_maintenance1")
                             && HasSurfaceExtension(CommonStrings.VkKhrGetSurfaceCapabilities2))
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasSwapchainMaintenance1 = true;
                    }
                    // VK_KHR_synchronization2: enables vkQueueSubmit2 with per-semaphore
                    // stage masks. Core in Vulkan 1.3; also available as KHR extension.
                    else if (extensionName == "VK_KHR_synchronization2")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasSynchronization2 = true;
                    }
                    // VK_KHR_timeline_semaphore: foundation for replacing the per-CL fence
                    // pool with a single monotonically-incrementing counter queried via
                    // vkGetSemaphoreCounterValue / vkWaitSemaphores. Detection-only.
                    // Core in Vulkan 1.2.
                    else if (extensionName == "VK_KHR_timeline_semaphore")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasTimelineSemaphore = true;
                    }
                    // VK_GOOGLE_display_timing: lets us schedule each present at a specific
                    // vblank target (desiredPresentTime) to minimise the time a rendered
                    // frame sits in the scanout buffer before it reaches the display. Used
                    // by Android/Qualcomm drivers (incl. Adreno 7xx) to shave ~0.5–1 frame
                    // of display-induced latency on FIFO / FIFO_RELAXED present modes.
                    else if (extensionName == "VK_GOOGLE_display_timing")
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasDisplayTiming = true;
                    }
                    else if (requiredInstanceExtensions.Remove(extensionName)) activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                }
            }

            if (requiredInstanceExtensions.Count != 0)
            {
                string missingList = string.Join(", ", requiredInstanceExtensions);
                throw new VeldridException(
                    $"The following Vulkan device extensions were not available: {missingList}");
            }

            var deviceCreateInfo = new VkDeviceCreateInfo();
            deviceCreateInfo.queueCreateInfoCount = queueCreateInfosCount;
            deviceCreateInfo.pQueueCreateInfos = queueCreateInfos;

            deviceCreateInfo.pEnabledFeatures = &deviceFeatures;

            // Chain feature structs via pNext for extensions that require opt-in.
            VkPhysicalDeviceDynamicRenderingFeatures dynamicRenderingFeatures;
            VkPhysicalDeviceHostImageCopyFeatures hostImageCopyFeatures;
            VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR swapchainMaintenance1Features;
            VkPhysicalDeviceSynchronization2Features synchronization2Features;
            VkPhysicalDeviceTimelineSemaphoreFeatures timelineSemaphoreFeatures;
            VkPhysicalDeviceFragmentShadingRateFeaturesKHR fragmentShadingRateFeatures;
            VkPhysicalDevicePipelineCreationCacheControlFeatures pipelineCreationCacheControlFeatures;

            if (hasDynamicRendering)
            {
                dynamicRenderingFeatures = new VkPhysicalDeviceDynamicRenderingFeatures();
                dynamicRenderingFeatures.dynamicRendering = true;
                dynamicRenderingFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &dynamicRenderingFeatures;
            }

            if (hasHostImageCopy)
            {
                hostImageCopyFeatures = new VkPhysicalDeviceHostImageCopyFeatures();
                hostImageCopyFeatures.hostImageCopy = true;
                hostImageCopyFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &hostImageCopyFeatures;
            }

            if (hasSwapchainMaintenance1)
            {
                swapchainMaintenance1Features = new VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR();
                swapchainMaintenance1Features.swapchainMaintenance1 = true;
                swapchainMaintenance1Features.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &swapchainMaintenance1Features;
            }

            if (hasSynchronization2)
            {
                synchronization2Features = new VkPhysicalDeviceSynchronization2Features();
                synchronization2Features.synchronization2 = true;
                synchronization2Features.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &synchronization2Features;
            }

            if (hasTimelineSemaphore)
            {
                timelineSemaphoreFeatures = new VkPhysicalDeviceTimelineSemaphoreFeatures();
                timelineSemaphoreFeatures.timelineSemaphore = true;
                timelineSemaphoreFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &timelineSemaphoreFeatures;
            }

            if (hasPipelineCreationCacheControl)
            {
                pipelineCreationCacheControlFeatures = new VkPhysicalDevicePipelineCreationCacheControlFeatures();
                pipelineCreationCacheControlFeatures.pipelineCreationCacheControl = true;
                pipelineCreationCacheControlFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &pipelineCreationCacheControlFeatures;
            }

            // VK_KHR_fragment_shading_rate: explicitly request pipelineFragmentShadingRate.
            // Without this pNext chain entry some Android drivers (e.g. Adreno) return a
            // non-null but broken vkCmdSetFragmentShadingRateKHR stub that crashes on call
            // (pc = 0x0, SIGSEGV on the draw thread).
            if (hasFragmentShadingRate)
            {
                fragmentShadingRateFeatures = new VkPhysicalDeviceFragmentShadingRateFeaturesKHR();
                fragmentShadingRateFeatures.pipelineFragmentShadingRate = true;
                fragmentShadingRateFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &fragmentShadingRateFeatures;
            }

            var layerNames = new StackList<IntPtr>();
            if (standardValidationSupported) layerNames.Add(CommonStrings.StandardValidationLayerName);

            if (khronosValidationSupported) layerNames.Add(CommonStrings.KhronosValidationLayerName);
            deviceCreateInfo.enabledLayerCount = layerNames.Count;
            deviceCreateInfo.ppEnabledLayerNames = (byte**)layerNames.Data;

            fixed (IntPtr* activeExtensionsPtr = activeExtensions)
            {
                deviceCreateInfo.enabledExtensionCount = activeExtensionCount;
                deviceCreateInfo.ppEnabledExtensionNames = (byte**)activeExtensionsPtr;

                VkDevice localDevice;
                var result = InstanceApi.vkCreateDevice(PhysicalDevice, &deviceCreateInfo, null, &localDevice);
                CheckResult(result);
                device = localDevice;
            }

            DeviceApi = new VkDeviceApi(InstanceApi, device);

            {
                VkQueue q;
                DeviceApi.vkGetDeviceQueue(GraphicsQueueIndex, 0, &q);
                graphicsQueue = q;
            }

            // DeviceApi is initialized above; debug-marker functions are accessed via DeviceApi directly.
            // VkDeviceMemoryRequirements2 functions are accessed via DeviceApi (no delegate loading needed).

            if (hasDriverProperties)
            {
                var deviceProps = new VkPhysicalDeviceProperties2();
                var driverProps = new Vortice.Vulkan.VkPhysicalDeviceDriverProperties();

                deviceProps.pNext = &driverProps;
                InstanceApi.vkGetPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);

                // driverProps is a local stack variable; take its address directly (no fixed needed).
                // Marshal.OffsetOf resolves the fixed-buffer field offsets at runtime.
                var dp = &driverProps;
                int nameOffset = (int)Marshal.OffsetOf<Vortice.Vulkan.VkPhysicalDeviceDriverProperties>("driverName");
                int infoOffset = (int)Marshal.OffsetOf<Vortice.Vulkan.VkPhysicalDeviceDriverProperties>("driverInfo");
                string driverName = Encoding.UTF8.GetString((byte*)dp + nameOffset, 256).TrimEnd('\0');
                string driverInfo = Encoding.UTF8.GetString((byte*)dp + infoOffset, 256).TrimEnd('\0');

                var conforming = driverProps.conformanceVersion;
                apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
                DriverName = driverName;
                DriverInfo = driverInfo;
            }

            // VK_KHR_push_descriptor: query maxPushDescriptors.
            if (hasPushDescriptors)
            {
                var deviceProps2 = new VkPhysicalDeviceProperties2();
                var pushDescProps = new VkPhysicalDevicePushDescriptorProperties();
                deviceProps2.pNext = &pushDescProps;
                InstanceApi.vkGetPhysicalDeviceProperties2(PhysicalDevice, &deviceProps2);
                MaxPushDescriptors = pushDescProps.maxPushDescriptors;
                HasPushDescriptors = MaxPushDescriptors > 0
                                  && DeviceApi.vkCmdPushDescriptorSetKHR_ptr.Value != null;
            }

            // VK_KHR_dynamic_rendering: validate that the actual function pointers were
            // loaded by vkGetDeviceProcAddr. On some drivers (observed on Adreno with
            // pre-release Android system images) the extension may be listed in device
            // properties but the function pointers silently return null.
            // Try core names first; fall back to KHR extension aliases.
            if (hasDynamicRendering)
            {
                bool beginOk = DeviceApi.vkCmdBeginRendering_ptr.Value != null;
                bool endOk   = DeviceApi.vkCmdEndRendering_ptr.Value != null;

                if (!beginOk && DeviceApi.vkCmdBeginRenderingKHR_ptr.Value != null)
                {
                    beginOk = true;
                    UseKhrDynamicRendering = true;
                }
                if (!endOk && DeviceApi.vkCmdEndRenderingKHR_ptr.Value != null)
                    endOk = true;

                HasDynamicRendering = beginOk && endOk;
                if (!HasDynamicRendering)
                    Debug.WriteLine("[Veldrid] VK_KHR_dynamic_rendering: extension listed but begin/end function pointers are null — disabled.");
            }

            HasMemoryBudget = hasMemoryBudget;

            // VK_EXT_host_image_copy: validate all required function pointers before enabling.
            // vkTransitionImageLayoutEXT is also needed by hostCopyToImage.
            if (hasHostImageCopy)
            {
                HasHostImageCopy = DeviceApi.vkCopyMemoryToImageEXT_ptr.Value != null
                                && DeviceApi.vkTransitionImageLayoutEXT_ptr.Value != null;
                if (!HasHostImageCopy)
                    Debug.WriteLine("[Veldrid] VK_EXT_host_image_copy: extension listed but required function pointers are null — disabled.");
            }

            // VK_EXT_descriptor_indexing: detection only (core in Vulkan 1.2).
            // No function pointers to load — just expose the flag for future bindless usage.
            HasDescriptorIndexing = hasDescriptorIndexing;

            // VK_KHR_fragment_shading_rate: validate the function pointer.
            // Without an explicit check some Android drivers (Adreno 7xx) return a non-null
            // but broken stub; with the pNext feature opt-in above the driver should now
            // return a correct pointer, but guard against the null case as well.
            if (hasFragmentShadingRate)
            {
                HasFragmentShadingRate = DeviceApi.vkCmdSetFragmentShadingRateKHR_ptr.Value != null;
                if (!HasFragmentShadingRate)
                    Debug.WriteLine("[Veldrid] VK_KHR_fragment_shading_rate: extension listed but vkCmdSetFragmentShadingRateKHR is null — disabled.");
            }

            // VK_EXT_mesh_shader: validate function pointer before enabling.
            if (hasMeshShader)
            {
                HasMeshShader = DeviceApi.vkCmdDrawMeshTasksEXT_ptr.Value != null;
                if (!HasMeshShader)
                    Debug.WriteLine("[Veldrid] VK_EXT_mesh_shader: extension listed but vkCmdDrawMeshTasksEXT is null — disabled.");
            }

            // VK_EXT_swapchain_maintenance1: no device function pointers required by
            // our usage (only struct chaining at create- and present-time).
            HasSwapchainMaintenance1 = hasSwapchainMaintenance1;

            // VK_KHR_synchronization2: validate vkQueueSubmit2. The core-1.3 variant
            // is tried first; fall back to the KHR extension alias for 1.2 drivers.
            // Disable entirely if neither pointer is available (shouldn't happen on a
            // conformant 1.3 device, but guard defensively).
            if (hasSynchronization2)
            {
                if (DeviceApi.vkQueueSubmit2_ptr.Value != null)
                {
                    HasSynchronization2 = true;
                }
                else if (DeviceApi.vkQueueSubmit2KHR_ptr.Value != null)
                {
                    HasSynchronization2 = true;
                    useQueueSubmit2Khr = true;
                }
                else
                {
                    HasSynchronization2 = false;
                    Debug.WriteLine("[Veldrid] VK_KHR_synchronization2: extension listed but neither vkQueueSubmit2 nor vkQueueSubmit2KHR is available — disabled.");
                }
            }

            HasTimelineSemaphore = hasTimelineSemaphore;

            HasPipelineCreationCacheControl = hasPipelineCreationCacheControl;

            // VK_GOOGLE_display_timing: validate both function pointers before enabling.
            // On some drivers (observed on Adreno + pre-release Android system images)
            // the extension is listed but vkGetDeviceProcAddr returns null for its
            // functions. Guard here so initDisplayTiming() doesn't crash through a null
            // pointer (PC = 0x0, SIGSEGV) at swapchain creation time.
            if (hasDisplayTiming)
            {
                HasDisplayTiming = DeviceApi.vkGetRefreshCycleDurationGOOGLE_ptr.Value != null
                                && DeviceApi.vkGetPastPresentationTimingGOOGLE_ptr.Value != null;
                if (!HasDisplayTiming)
                    Debug.WriteLine("[Veldrid] VK_GOOGLE_display_timing: extension listed but function pointers are null — disabled.");
            }

            // VK_EXT_debug_marker: validate all three function pointers before allowing
            // marker calls. Some drivers list the extension but fail to load its functions.
            if (debugMarkerEnabled)
            {
                debugMarkerEnabled = DeviceApi.vkDebugMarkerSetObjectNameEXT_ptr.Value != null
                                  && DeviceApi.vkCmdDebugMarkerBeginEXT_ptr.Value != null;
                if (!debugMarkerEnabled)
                    Debug.WriteLine("[Veldrid] VK_EXT_debug_marker: extension listed but function pointers are null — disabled.");
            }
        }

        private void getQueueFamilyIndices(VkSurfaceKHR surface)
        {
            uint queueFamilyCount = 0;
            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, null);
            var qfp = new VkQueueFamilyProperties[queueFamilyCount];
            fixed (VkQueueFamilyProperties* qfpPtr = qfp)
                InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, qfpPtr);

            bool foundGraphics = false;
            bool foundPresent = surface == VkSurfaceKHR.Null;

            for (uint i = 0; i < qfp.Length; i++)
            {
                if ((qfp[i].queueFlags & VkQueueFlags.Graphics) != 0)
                {
                    GraphicsQueueIndex = i;
                    foundGraphics = true;
                }

                if (!foundPresent)
                {
                    VkBool32 presentSupported;
                    InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(PhysicalDevice, i, surface, &presentSupported);

                    if (presentSupported)
                    {
                        PresentQueueIndex = i;
                        foundPresent = true;
                    }
                }

                if (foundGraphics && foundPresent) return;
            }
        }

        private void createDescriptorPool()
        {
            DescriptorPoolManager = new VkDescriptorPoolManager(this);
        }

        private void createGraphicsCommandPool()
        {
            var commandPoolCi = new VkCommandPoolCreateInfo();
            commandPoolCi.flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
            commandPoolCi.queueFamilyIndex = GraphicsQueueIndex;
            var result = DeviceApi.vkCreateCommandPool(&commandPoolCi, null, out graphicsCommandPool);
            CheckResult(result);
        }

        private SharedCommandPool getFreeCommandPool()
        {
            SharedCommandPool sharedPool = null;

            lock (graphicsCommandPoolLock)
            {
                if (sharedGraphicsCommandPools.Count > 0)
                    sharedPool = sharedGraphicsCommandPools.Pop();
            }

            return sharedPool ?? new SharedCommandPool(this, false);
        }

        private IntPtr mapBuffer(VkBuffer buffer, uint numBytes)
        {
            if (buffer.Memory.IsPersistentMapped)
                return (IntPtr)buffer.Memory.BlockMappedPointer;

            void* mappedPtr;
            var result = DeviceApi.vkMapMemory(buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, 0, &mappedPtr);
            CheckResult(result);
            return (IntPtr)mappedPtr;
        }

        private void unmapBuffer(VkBuffer buffer)
        {
            if (!buffer.Memory.IsPersistentMapped) DeviceApi.vkUnmapMemory(buffer.Memory.DeviceMemory);
        }

        private VkTexture getFreeStagingTexture(uint width, uint height, uint depth, PixelFormat format)
        {
            uint totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);

            lock (stagingResourcesLock)
            {
                for (int i = 0; i < availableStagingTextures.Count; i++)
                {
                    var tex = availableStagingTextures[i];

                    if (tex.Memory.Size >= totalSize)
                    {
                        availableStagingTextures.RemoveAt(i);
                        tex.SetStagingDimensions(width, height, depth, format);
                        return tex;
                    }
                }
            }

            uint texWidth = Math.Max(256, width);
            uint texHeight = Math.Max(256, height);
            var newTex = (VkTexture)ResourceFactory.CreateTexture(TextureDescription.Texture3D(
                texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
            newTex.SetStagingDimensions(width, height, depth, format);

            return newTex;
        }

        private VkBuffer getFreeStagingBuffer(uint size)
        {
            lock (stagingResourcesLock)
            {
                for (int i = 0; i < availableStagingBuffers.Count; i++)
                {
                    var buffer = availableStagingBuffers[i];

                    if (buffer.SizeInBytes >= size)
                    {
                        availableStagingBuffers.RemoveAt(i);
                        return buffer;
                    }
                }
            }

            uint newBufferSize = Math.Max(minStagingBufferSize, size);
            var newBuffer = (VkBuffer)ResourceFactory.CreateBuffer(
                new BufferDescription(newBufferSize, BufferUsage.Staging));
            return newBuffer;
        }

        private protected override void SubmitCommandsCore(CommandList cl, Fence fence)
        {
            submitCommandList(cl, 0, null, 0, null, fence);
        }

        private protected override void SwapBuffersCore(Swapchain swapchain)
        {
            var vkSc = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);

            // The previous frame couldn't (re)build the swapchain (transient zero-extent
            // surface, lost-surface waiting on host re-create, etc.). Retry the create
            // here instead of presenting against a stale image; the next acquire will
            // do the real work. Skipping the DeviceApi.vkQueuePresentKHR avoids feeding the
            // driver a stale image index against a possibly-swapped-out swapchain.
            if (vkSc.NeedsRecreation)
            {
                vkSc.RecreateAfterPresent();
                return;
            }

            var deviceSwapchain = vkSc.DeviceSwapchain;
            var presentInfo = new VkPresentInfoKHR();
            presentInfo.swapchainCount = 1;
            presentInfo.pSwapchains = &deviceSwapchain;
            uint imageIndex = vkSc.ImageIndex;
            presentInfo.pImageIndices = &imageIndex;

            // VK_EXT_swapchain_maintenance1: chain the per-present mode override so the
            // driver applies any pending hot-swap (vsync ↔ low-latency) without a
            // swapchain rebuild. The pointed-to mode must remain valid until DeviceApi.vkQueuePresentKHR
            // returns, which is satisfied by the stack-local `currentMode` below.
            VkSwapchainPresentModeInfoKHR presentModeInfo;
            VkPresentModeKHR currentMode;
            if (HasSwapchainMaintenance1 && vkSc.HasPresentModeHotSwap)
            {
                currentMode = vkSc.CurrentPresentMode;
                presentModeInfo = new VkSwapchainPresentModeInfoKHR();
                presentModeInfo.swapchainCount = 1;
                presentModeInfo.pPresentModes = &currentMode;
                presentInfo.pNext = &presentModeInfo;
            }

            // VK_GOOGLE_display_timing: target the next vblank slot to minimise the time
            // the rendered image sits in the scanout buffer before the display shows it.
            // desiredPresentTime = 0 is safe and lets the driver decide — used until
            // at least one past present has been recorded by drainPastPresentationTimings.
            // Stack-local structs remain valid for the duration of DeviceApi.vkQueuePresentKHR.
            VkPresentTimesInfoGOOGLE presentTimesInfo;
            VkPresentTimeGOOGLE presentTime;
            if (vkSc.HasDisplayTiming)
            {
                presentTime = new VkPresentTimeGOOGLE
                {
                    presentID = vkSc.NextPresentID,
                    desiredPresentTime = vkSc.GetDesiredPresentTime()
                };
                presentTimesInfo = new VkPresentTimesInfoGOOGLE();
                presentTimesInfo.swapchainCount = 1;
                presentTimesInfo.pTimes = &presentTime;
                // Append after any existing pNext chain (e.g. maintenance1 mode info).
                presentTimesInfo.pNext = presentInfo.pNext;
                presentInfo.pNext = &presentTimesInfo;
            }

            // VK_KHR_incremental_present is intentionally NOT chained here.
            // On Qualcomm/Adreno (vendorID 0x5143) it has been observed to deliver
            // stale tile contents after a surface rotation — the dirty-rect path
            // skips a full-frame composite that the rotated swapchain depends on.
            // If a future contributor wants to enable it for desktop drivers,
            // gate the chain on `vkSc.gd.PhysicalDeviceProperties.vendorID != 0x5143`
            // (or skip entirely on Android) before adding it back.
            VkResult presentResult;
            if (vkSc.PresentQueueIndex == GraphicsQueueIndex)
            {
                lock (graphicsQueueLock)
                {
                    presentResult = DeviceApi.vkQueuePresentKHR(vkSc.PresentQueue, &presentInfo);
                    // Advance timing state on successful presents so the next frame's
                    // desiredPresentTime targets the correct vblank slot.
                    if (vkSc.HasDisplayTiming
                        && (presentResult == VkResult.Success || presentResult == VkResult.SuboptimalKHR))
                    {
                        vkSc.AdvancePresentID();
                        vkSc.DrainPastPresentationTimings();
                    }
                    handlePresentResult(vkSc, presentResult);
                    acquireAndWaitNextImage(vkSc);
                }
            }
            else
            {
                lock (vkSc)
                {
                    presentResult = DeviceApi.vkQueuePresentKHR(vkSc.PresentQueue, &presentInfo);
                    if (vkSc.HasDisplayTiming
                        && (presentResult == VkResult.Success || presentResult == VkResult.SuboptimalKHR))
                    {
                        vkSc.AdvancePresentID();
                        vkSc.DrainPastPresentationTimings();
                    }
                    handlePresentResult(vkSc, presentResult);
                    acquireAndWaitNextImage(vkSc);
                }
            }
        }

        // VK_ERROR_OUT_OF_DATE_KHR / VK_SUBOPTIMAL_KHR / VK_ERROR_SURFACE_LOST_KHR are
        // expected on Android (rotation, fold, DeX attach, system bars showing/hiding,
        // surfaceDestroyed → surfaceCreated lifecycle). Treat them as a needs-rebuild
        // signal rather than a hard failure: silently recreate (and on SURFACE_LOST,
        // also recreate the underlying VkSurfaceKHR) and re-acquire. This avoids the
        // per-rotate managed exception that the osu! framework retry loop would
        // otherwise have to swallow every frame until the surface settled, and
        // critically converts SURFACE_LOST from a permanent black-screen condition
        // into a single recoverable frame stall.
        private static void handlePresentResult(VkSwapchain vkSc, VkResult result)
        {
            if (result == VkResult.Success)
                return;

            if (result == VkResult.ErrorOutOfDateKHR
                || result == VkResult.SuboptimalKHR
                || result == VkResult.ErrorSurfaceLostKHR)
            {
                vkSc.RecreateAfterPresent();
                return;
            }

            // Any other non-success result is genuinely unexpected (device lost,
            // OOM, etc.) — surface it the same way as the rest of the backend.
            CheckResult(result);
        }

        private void acquireAndWaitNextImage(VkSwapchain vkSc)
        {
            if (vkSc.AcquireNextImage(device, VkSemaphore.Null, vkSc.ImageAvailableFence))
                vkSc.WaitAndResetImageAvailableFence();
        }

        private protected override void WaitForIdleCore()
        {
            lock (graphicsQueueLock) DeviceApi.vkQueueWaitIdle(graphicsQueue);

            checkSubmittedFences();
        }

        private protected override void WaitForNextFrameReadyCore()
        {
        }

        private protected override bool GetPixelFormatSupportCore(
            PixelFormat format,
            TextureType type,
            TextureUsage usage,
            out PixelFormatProperties properties)
        {
            var vkFormat = VkFormats.VdToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
            var vkType = VkFormats.VdToVkTextureType(type);
            var tiling = usage == TextureUsage.Staging ? VkImageTiling.Linear : VkImageTiling.Optimal;
            var vkUsage = VkFormats.VdToVkTextureUsage(usage);

            VkImageFormatProperties vkProps;
            var result = InstanceApi.vkGetPhysicalDeviceImageFormatProperties(
                PhysicalDevice,
                vkFormat,
                vkType,
                tiling,
                vkUsage,
                VkImageCreateFlags.None,
                &vkProps);

            if (result == VkResult.ErrorFormatNotSupported)
            {
                properties = default;
                return false;
            }

            CheckResult(result);

            properties = new PixelFormatProperties(
                vkProps.maxExtent.width,
                vkProps.maxExtent.height,
                vkProps.maxExtent.depth,
                vkProps.maxMipLevels,
                vkProps.maxArrayLayers,
                (uint)vkProps.sampleCounts);
            return true;
        }

        private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
            VkBuffer copySrcVkBuffer = null;
            IntPtr mappedPtr;
            byte* destPtr;
            bool isPersistentMapped = vkBuffer.Memory.IsPersistentMapped;

            if (isPersistentMapped)
            {
                mappedPtr = (IntPtr)vkBuffer.Memory.BlockMappedPointer;
                destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
            }
            else
            {
                copySrcVkBuffer = getFreeStagingBuffer(sizeInBytes);
                mappedPtr = (IntPtr)copySrcVkBuffer.Memory.BlockMappedPointer;
                destPtr = (byte*)mappedPtr;
            }

            Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);

            if (!isPersistentMapped)
            {
                var pool = getFreeCommandPool();
                var cb = pool.BeginNewCommandBuffer();

                var copyRegion = new VkBufferCopy
                {
                    dstOffset = bufferOffsetInBytes,
                    size = sizeInBytes
                };
                DeviceApi.vkCmdCopyBuffer(cb, copySrcVkBuffer.DeviceBuffer, vkBuffer.DeviceBuffer, 1, &copyRegion);

                // Emit a memory barrier so the TRANSFER_WRITE is visible to all subsequent
                // GPU consumers of this buffer.  Without this barrier the data is technically
                // not guaranteed to be visible to the next command-buffer submission even though
                // both use the same queue (Vulkan spec 6.9 does NOT imply automatic memory
                // visibility across queue submissions).
                var dstAccess = VkAccessFlags.None;
                var dstStage = VkPipelineStageFlags.None;
                var destUsage = buffer.Usage;

                if ((destUsage & BufferUsage.UniformBuffer) != 0)
                {
                    dstAccess |= VkAccessFlags.UniformRead;
                    dstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
                }

                if ((destUsage & BufferUsage.VertexBuffer) != 0)
                {
                    dstAccess |= VkAccessFlags.VertexAttributeRead;
                    dstStage |= VkPipelineStageFlags.VertexInput;
                }

                if ((destUsage & BufferUsage.IndexBuffer) != 0)
                {
                    dstAccess |= VkAccessFlags.IndexRead;
                    dstStage |= VkPipelineStageFlags.VertexInput;
                }

                if ((destUsage & (BufferUsage.StructuredBufferReadOnly | BufferUsage.StructuredBufferReadWrite)) != 0)
                {
                    dstAccess |= VkAccessFlags.ShaderRead;
                    dstStage |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.ComputeShader;
                }

                if ((destUsage & BufferUsage.IndirectBuffer) != 0)
                {
                    dstAccess |= VkAccessFlags.IndirectCommandRead;
                    dstStage |= VkPipelineStageFlags.DrawIndirect;
                }

                if (dstAccess == VkAccessFlags.None)
                {
                    dstAccess = VkAccessFlags.MemoryRead;
                    dstStage = VkPipelineStageFlags.AllCommands;
                }

                var memBarrier = new VkMemoryBarrier();
                memBarrier.sType = VkStructureType.MemoryBarrier;
                memBarrier.srcAccessMask = VkAccessFlags.TransferWrite;
                memBarrier.dstAccessMask = dstAccess;
                DeviceApi.vkCmdPipelineBarrier(
                    cb,
                    VkPipelineStageFlags.Transfer,
                    dstStage,
                    VkDependencyFlags.None,
                    1, &memBarrier,
                    0, null,
                    0, null);

                pool.EndAndSubmit(cb);
                lock (stagingResourcesLock) submittedStagingBuffers.Add(cb, copySrcVkBuffer);
            }
        }

        private protected override void UpdateTextureCore(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x,
            uint y,
            uint z,
            uint width,
            uint height,
            uint depth,
            uint mipLevel,
            uint arrayLayer)
        {
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;

            if (isStaging)
            {
                var memBlock = vkTex.Memory;
                uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
                var layout = vkTex.GetSubresourceLayout(subresource);
                byte* imageBasePtr = (byte*)memBlock.BlockMappedPointer + layout.offset;

                uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
                uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
                Util.CopyTextureRegion(
                    source.ToPointer(),
                    0, 0, 0,
                    srcRowPitch, srcDepthPitch,
                    imageBasePtr,
                    x, y, z,
                    (uint)layout.rowPitch, (uint)layout.depthPitch,
                    width, height, depth,
                    texture.Format);
            }
            else if (HasHostImageCopy)
            {
                // VK_EXT_host_image_copy: upload directly from CPU memory, bypassing
                // staging buffers and command buffer overhead entirely. This path is
                // preferred over the staging-buffer path below because it avoids both
                // the staging texture allocation and the command buffer submit/wait,
                // reducing latency for texture uploads on supported drivers.
                hostCopyToImage(vkTex, source, x, y, z, width, height, depth, mipLevel, arrayLayer);
            }
            else
            {
                // Fallback: stage into a temporary texture then copy via a command buffer.
                var stagingTex = getFreeStagingTexture(width, height, depth, texture.Format);
                UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
                var pool = getFreeCommandPool();
                var cb = pool.BeginNewCommandBuffer();
                VkCommandList.CopyTextureCore_VkCommandBuffer(
                    cb,
                    DeviceApi,
                    stagingTex, 0, 0, 0, 0, 0,
                    texture, x, y, z, mipLevel, arrayLayer,
                    width, height, depth, 1);
                lock (stagingResourcesLock) submittedStagingTextures.Add(cb, stagingTex);
                pool.EndAndSubmit(cb);
            }
        }

        private void hostCopyToImage(
            VkTexture vkTex,
            IntPtr source,
            uint x, uint y, uint z,
            uint width, uint height, uint depth,
            uint mipLevel, uint arrayLayer)
        {
            bool hasStencil = FormatHelpers.IsStencilFormat(vkTex.Format);
            var aspectMask = (vkTex.Usage & TextureUsage.DepthStencil) != 0
                ? hasStencil
                    ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                    : VkImageAspectFlags.Depth
                : VkImageAspectFlags.Color;

            // Transition to TransferDstOptimal for the host copy.
            var oldLayout = vkTex.GetImageLayout(mipLevel, arrayLayer);
            var transitionToTransfer = new VkHostImageLayoutTransitionInfo();
            transitionToTransfer.image = vkTex.OptimalDeviceImage;
            transitionToTransfer.oldLayout = oldLayout;
            transitionToTransfer.newLayout = VkImageLayout.TransferDstOptimal;
            transitionToTransfer.subresourceRange = new VkImageSubresourceRange(aspectMask, mipLevel, 1, arrayLayer, 1);
            var tResult = DeviceApi.vkTransitionImageLayoutEXT( 1, &transitionToTransfer);
            VulkanUtil.CheckResult(tResult);

            var region = new VkMemoryToImageCopy();
            region.pHostPointer = source.ToPointer();
            region.imageSubresource.aspectMask = aspectMask;
            region.imageSubresource.mipLevel = mipLevel;
            region.imageSubresource.baseArrayLayer = arrayLayer;
            region.imageSubresource.layerCount = 1;
            region.imageOffset.x = (int)x;
            region.imageOffset.y = (int)y;
            region.imageOffset.z = (int)z;
            region.imageExtent.width = width;
            region.imageExtent.height = height;
            region.imageExtent.depth = depth;

            var copyInfo = new VkCopyMemoryToImageInfo();
            copyInfo.dstImage = vkTex.OptimalDeviceImage;
            copyInfo.dstImageLayout = VkImageLayout.TransferDstOptimal;
            copyInfo.regionCount = 1;
            copyInfo.pRegions = &region;

            var cResult = DeviceApi.vkCopyMemoryToImageEXT( &copyInfo);
            VulkanUtil.CheckResult(cResult);

            // Transition back to a stable layout.
            // VkImageLayout.Undefined is only valid as an oldLayout (discard sentinel); it may NOT
            // be used as newLayout.  When the texture has never been used before (tracked layout is
            // Undefined) choose the most appropriate initial layout based on the texture's usage.
            var finalLayout = oldLayout;
            if (finalLayout == VkImageLayout.Undefined)
            {
                if ((vkTex.Usage & TextureUsage.Sampled) != 0)
                    finalLayout = VkImageLayout.ShaderReadOnlyOptimal;
                else if ((vkTex.Usage & TextureUsage.DepthStencil) != 0)
                    finalLayout = VkImageLayout.DepthStencilAttachmentOptimal;
                else if ((vkTex.Usage & TextureUsage.RenderTarget) != 0)
                    finalLayout = VkImageLayout.ColorAttachmentOptimal;
                else
                    finalLayout = VkImageLayout.General;
            }

            var transitionBack = new VkHostImageLayoutTransitionInfo();
            transitionBack.image = vkTex.OptimalDeviceImage;
            transitionBack.oldLayout = VkImageLayout.TransferDstOptimal;
            transitionBack.newLayout = finalLayout;
            transitionBack.subresourceRange = new VkImageSubresourceRange(aspectMask, mipLevel, 1, arrayLayer, 1);
            tResult = DeviceApi.vkTransitionImageLayoutEXT( 1, &transitionBack);
            VulkanUtil.CheckResult(tResult);

            // Keep the CPU-side layout tracking in sync with the actual image state.
            vkTex.SetImageLayout(mipLevel, arrayLayer, finalLayout);
        }

        internal class SharedCommandPool
        {
            public bool IsCached { get; }
            private readonly VkGraphicsDevice gd;
            private readonly VkCommandPool pool;
            private readonly VkCommandBuffer cb;

            public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
            {
                this.gd = gd;
                IsCached = isCached;

                var commandPoolCi = new VkCommandPoolCreateInfo();
                commandPoolCi.flags = VkCommandPoolCreateFlags.Transient | VkCommandPoolCreateFlags.ResetCommandBuffer;
                commandPoolCi.queueFamilyIndex = this.gd.GraphicsQueueIndex;
                var result = gd.DeviceApi.vkCreateCommandPool(&commandPoolCi, null, out pool);
                CheckResult(result);

                var allocateInfo = new VkCommandBufferAllocateInfo();
                allocateInfo.commandBufferCount = 1;
                allocateInfo.level = VkCommandBufferLevel.Primary;
                allocateInfo.commandPool = pool;
                VkCommandBuffer localCb;
                result = gd.DeviceApi.vkAllocateCommandBuffers(&allocateInfo, &localCb);
                cb = localCb;
                CheckResult(result);
            }

            public VkCommandBuffer BeginNewCommandBuffer()
            {
                var beginInfo = new VkCommandBufferBeginInfo();
                beginInfo.flags = VkCommandBufferUsageFlags.OneTimeSubmit;
                var result = gd.DeviceApi.vkBeginCommandBuffer(cb, &beginInfo);
                CheckResult(result);

                return cb;
            }

            public void EndAndSubmit(VkCommandBuffer cb)
            {
                var result = gd.DeviceApi.vkEndCommandBuffer(cb);
                CheckResult(result);
                gd.submitCommandBuffer(null, cb, 0, null, 0, null, null);
                lock (gd.stagingResourcesLock) gd.submittedSharedCommandPools.Add(cb, this);
            }

            internal void Destroy()
            {
                gd.DeviceApi.vkDestroyCommandPool(pool, null);
            }
        }

        private struct FenceSubmissionInfo
        {
            public readonly Vortice.Vulkan.VkFence Fence;
            public readonly VkCommandList CommandList;
            public readonly VkCommandBuffer CommandBuffer;

            public FenceSubmissionInfo(Vortice.Vulkan.VkFence fence, VkCommandList commandList, VkCommandBuffer commandBuffer)
            {
                Fence = fence;
                CommandList = commandList;
                CommandBuffer = commandBuffer;
            }
        }
    }

}
