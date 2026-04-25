using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Vulkan;
using static Veldrid.Vk.VulkanUtil;
using static Vulkan.VulkanNative;

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

        public VkCmdDebugMarkerBeginExtT MarkerBegin { get; private set; }

        public VkCmdDebugMarkerEndExtT MarkerEnd { get; private set; }

        public VkCmdDebugMarkerInsertExtT MarkerInsert { get; private set; }

        public VkGetBufferMemoryRequirements2T GetBufferMemoryRequirements2 { get; private set; }

        public VkGetImageMemoryRequirements2T GetImageMemoryRequirements2 { get; private set; }

        public VkCreateMetalSurfaceExtT CreateMetalSurfaceExt { get; private set; }

        // VK_KHR_push_descriptor
        public bool HasPushDescriptors { get; private set; }
        public uint MaxPushDescriptors { get; private set; }

        // VK_KHR_dynamic_rendering
        public bool HasDynamicRendering { get; private set; }
        public VkCmdBeginRenderingT CmdBeginRendering { get; private set; }
        public VkCmdEndRenderingT CmdEndRendering { get; private set; }

        // VK_EXT_memory_budget
        public bool HasMemoryBudget { get; private set; }

        // VK_EXT_host_image_copy
        public bool HasHostImageCopy { get; private set; }
        public VkCopyMemoryToImageExtT CopyMemoryToImageExt { get; private set; }
        public VkTransitionImageLayoutExtT TransitionImageLayoutExt { get; private set; }

        // VK_EXT_descriptor_indexing (core in Vulkan 1.2)
        public bool HasDescriptorIndexing { get; private set; }

        // VK_KHR_fragment_shading_rate
        public bool HasFragmentShadingRate { get; private set; }
        public VkCmdSetFragmentShadingRateT CmdSetFragmentShadingRate { get; private set; }

        // VK_EXT_mesh_shader
        public bool HasMeshShader { get; private set; }
        public VkCmdDrawMeshTasksExtT CmdDrawMeshTasksExt { get; private set; }

        // VK_KHR_get_surface_capabilities2 + VK_EXT_surface_maintenance1 (instance) +
        // VK_EXT_swapchain_maintenance1 (device).
        // When true, the swapchain can hot-swap present modes at vkQueuePresentKHR
        // time without rebuilding — used by VkSwapchain to make SyncToVerticalBlank /
        // AllowTearing toggles near-free.
        public bool HasSwapchainMaintenance1 { get; private set; }
        public VkGetPhysicalDeviceSurfaceCapabilities2KhrT GetPhysicalDeviceSurfaceCapabilities2 { get; private set; }

        /// <summary>
        ///     The Vulkan API version supported by the selected physical device.
        /// </summary>
        internal VkVersion DeviceApiVersion { get; private set; }

        public override ResourceFactory ResourceFactory { get; }
        private static readonly FixedUtf8String s_name = "Veldrid-VkGraphicsDevice";
        private static readonly Lazy<bool> s_is_supported = new Lazy<bool>(checkIsSupported, true);
        private readonly Lock graphicsCommandPoolLock = new Lock();
        private readonly Lock graphicsQueueLock = new Lock();
        private readonly ConcurrentDictionary<VkFormat, VkFilter> filters = new ConcurrentDictionary<VkFormat, VkFilter>();
        private readonly BackendInfoVulkan vulkanInfo;

        private const int shared_command_pool_count = 4;

        // Staging Resources
        private const uint min_staging_buffer_size = 64;
        private const uint max_staging_buffer_size = 512;

        private readonly Lock stagingResourcesLock = new Lock();
        private readonly List<VkTexture> availableStagingTextures = new List<VkTexture>();
        private readonly List<VkBuffer> availableStagingBuffers = new List<VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, VkTexture> submittedStagingTextures
            = new Dictionary<VkCommandBuffer, VkTexture>();

        private readonly Dictionary<VkCommandBuffer, VkBuffer> submittedStagingBuffers
            = new Dictionary<VkCommandBuffer, VkBuffer>();

        private readonly Dictionary<VkCommandBuffer, SharedCommandPool> submittedSharedCommandPools
            = new Dictionary<VkCommandBuffer, SharedCommandPool>();

        private readonly Lock submittedFencesLock = new Lock();
        private readonly ConcurrentQueue<Vulkan.VkFence> availableSubmissionFences = new ConcurrentQueue<Vulkan.VkFence>();
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
        private PFN_vkDebugReportCallbackEXT debugCallbackFunc;
        private bool debugMarkerEnabled;
        private VkDebugMarkerSetObjectNameExtT setObjectNameDelegate;
        private readonly Stack<SharedCommandPool> sharedGraphicsCommandPools = new Stack<SharedCommandPool>();
        private bool standardValidationSupported;
        private bool khronosValidationSupported;
        private bool standardClipYDirection;
        private VkGetPhysicalDeviceProperties2T getPhysicalDeviceProperties2;
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
            createInstance(options.Debug, vkOptions);

            var surface = VkSurfaceKHR.Null;
            if (scDesc != null) surface = VkSurfaceUtil.CreateSurface(this, instance, scDesc.Value.Source);

            createPhysicalDevice();
            createLogicalDevice(surface, options.PreferStandardClipSpaceYDirection, vkOptions);

            MemoryManager = new VkDeviceMemoryManager(
                device,
                physicalDeviceProperties.limits.bufferImageGranularity,
                GetBufferMemoryRequirements2,
                GetImageMemoryRequirements2);

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
            var pipelineCacheCi = VkPipelineCacheCreateInfo.New();
            var cacheResult = vkCreatePipelineCache(device, ref pipelineCacheCi, null, out pipelineCache);
            CheckResult(cacheResult);

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

        public void EnableDebugCallback(VkDebugReportFlagsEXT flags = VkDebugReportFlagsEXT.WarningEXT | VkDebugReportFlagsEXT.ErrorEXT)
        {
            Debug.WriteLine("Enabling Vulkan Debug callbacks.");
            debugCallbackFunc = debugCallback;
            IntPtr debugFunctionPtr = Marshal.GetFunctionPointerForDelegate(debugCallbackFunc);
            var debugCallbackCi = VkDebugReportCallbackCreateInfoEXT.New();
            debugCallbackCi.flags = flags;
            debugCallbackCi.pfnCallback = debugFunctionPtr;
            IntPtr createFnPtr = getInstanceProcAddr("vkCreateDebugReportCallbackEXT"u8);

            if (createFnPtr == IntPtr.Zero) return;

            var createDelegate = Marshal.GetDelegateForFunctionPointer<VkCreateDebugReportCallbackExtD>(createFnPtr);
            var result = createDelegate(instance, &debugCallbackCi, IntPtr.Zero, out debugCallbackHandle);
            CheckResult(result);
        }

        public VkExtensionProperties[] GetDeviceExtensionProperties()
        {
            uint propertyCount = 0;
            var result = vkEnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, null);
            CheckResult(result);
            var props = new VkExtensionProperties[(int)propertyCount];

            fixed (VkExtensionProperties* properties = props)
            {
                result = vkEnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, properties);
                CheckResult(result);
            }

            return props;
        }

        public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
        {
            var usageFlags = VkImageUsageFlags.Sampled;
            usageFlags |= depthFormat ? VkImageUsageFlags.DepthStencilAttachment : VkImageUsageFlags.ColorAttachment;

            vkGetPhysicalDeviceImageFormatProperties(
                PhysicalDevice,
                VkFormats.VdToVkPixelFormat(format),
                VkImageType.Image2D,
                VkImageTiling.Optimal,
                usageFlags,
                VkImageCreateFlags.None,
                out var formatProperties);

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
            vkResetFences(device, 1, ref vkFence);
        }

        public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
        {
            var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            var result = vkWaitForFences(device, 1, ref vkFence, true, nanosecondTimeout);
            return result == VkResult.Success;
        }

        public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
        {
            int fenceCount = fences.Length;
            var fencesPtr = stackalloc Vulkan.VkFence[fenceCount];
            for (int i = 0; i < fenceCount; i++) fencesPtr[i] = Util.AssertSubtype<Fence, VkFence>(fences[i]).DeviceFence;

            var result = vkWaitForFences(device, (uint)fenceCount, fencesPtr, waitAll, nanosecondTimeout);
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
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.BufferEXT, buffer.DeviceBuffer.Handle, name);
                        break;

                    case VkCommandList commandList:
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.CommandBufferEXT,
                            (ulong)commandList.CommandBuffer.Handle,
                            $"{name}_CommandBuffer");
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.CommandPoolEXT,
                            commandList.CommandPool.Handle,
                            $"{name}_CommandPool");
                        break;

                    case VkFramebuffer framebuffer:
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.FramebufferEXT,
                            framebuffer.CurrentFramebuffer.Handle,
                            name);
                        break;

                    case VkPipeline pipeline:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.PipelineEXT, pipeline.DevicePipeline.Handle, name);
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.PipelineLayoutEXT, pipeline.PipelineLayout.Handle, name);
                        break;

                    case VkResourceLayout resourceLayout:
                        setDebugMarkerName(
                            VkDebugReportObjectTypeEXT.DescriptorSetLayoutEXT,
                            resourceLayout.DescriptorSetLayout.Handle,
                            name);
                        break;

                    case VkResourceSet resourceSet:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.DescriptorSetEXT, resourceSet.DescriptorSet.Handle, name);
                        break;

                    case VkSampler sampler:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.SamplerEXT, sampler.DeviceSampler.Handle, name);
                        break;

                    case VkShader shader:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.ShaderModuleEXT, shader.ShaderModule.Handle, name);
                        break;

                    case VkTexture tex:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.ImageEXT, tex.OptimalDeviceImage.Handle, name);
                        break;

                    case VkTextureView texView:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.ImageViewEXT, texView.ImageView.Handle, name);
                        break;

                    case VkFence fence:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.FenceEXT, fence.DeviceFence.Handle, name);
                        break;

                    case VkSwapchain sc:
                        setDebugMarkerName(VkDebugReportObjectTypeEXT.SwapchainKHREXT, sc.DeviceSwapchain.Handle, name);
                        break;
                }
            }
        }

        internal VkFilter GetFormatFilter(VkFormat format)
        {
            if (!filters.TryGetValue(format, out var filter))
            {
                vkGetPhysicalDeviceFormatProperties(PhysicalDevice, format, out var vkFormatProps);
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
            vkCmdClearColorImage(cb, texture.OptimalDeviceImage, VkImageLayout.TransferDstOptimal, &color, 1, &range);
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
            vkCmdClearDepthStencilImage(
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
                vkUnmapMemory(device, memoryBlock.DeviceMemory);
        }

        protected override void PlatformDispose()
        {
            Debug.Assert(submittedFences.Count == 0);
            foreach (var fence in availableSubmissionFences) vkDestroyFence(device, fence, null);

            mainSwapchain?.Dispose();

            if (debugCallbackFunc != null)
            {
                debugCallbackFunc = null;
                IntPtr destroyFuncPtr = getInstanceProcAddr("vkDestroyDebugReportCallbackEXT"u8);
                var destroyDel
                    = Marshal.GetDelegateForFunctionPointer<VkDestroyDebugReportCallbackExtD>(destroyFuncPtr);
                destroyDel(instance, debugCallbackHandle, null);
            }

            DescriptorPoolManager.DestroyAll();
            vkDestroyCommandPool(device, graphicsCommandPool, null);

            if (pipelineCache != VkPipelineCache.Null)
                vkDestroyPipelineCache(device, pipelineCache, null);

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

            var result = vkDeviceWaitIdle(device);
            CheckResult(result);
            vkDestroyDevice(device, null);
            vkDestroyInstance(instance, null);
        }

        private static bool checkIsSupported()
        {
            if (!IsVulkanLoaded()) return false;

            var instanceCi = VkInstanceCreateInfo.New();
            var applicationInfo = new VkApplicationInfo
            {
                apiVersion = new VkVersion(1, 0, 0),
                applicationVersion = new VkVersion(1, 0, 0),
                engineVersion = new VkVersion(1, 0, 0),
                pApplicationName = s_name,
                pEngineName = s_name
            };

            instanceCi.pApplicationInfo = &applicationInfo;

            var result = vkCreateInstance(ref instanceCi, null, out var testInstance);
            if (result != VkResult.Success) return false;

            uint physicalDeviceCount = 0;
            result = vkEnumeratePhysicalDevices(testInstance, ref physicalDeviceCount, null);

            if (result != VkResult.Success || physicalDeviceCount == 0)
            {
                vkDestroyInstance(testInstance, null);
                return false;
            }

            vkDestroyInstance(testInstance, null);

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
            var si = VkSubmitInfo.New();
            si.commandBufferCount = 1;
            si.pCommandBuffers = &vkCb;
            var waitDstStageMask = VkPipelineStageFlags.ColorAttachmentOutput;
            si.pWaitDstStageMask = &waitDstStageMask;

            si.pWaitSemaphores = waitSemaphoresPtr;
            si.waitSemaphoreCount = waitSemaphoreCount;
            si.pSignalSemaphores = signalSemaphoresPtr;
            si.signalSemaphoreCount = signalSemaphoreCount;

            Vulkan.VkFence vkFence;
            Vulkan.VkFence submissionFence;

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

            lock (graphicsQueueLock)
            {
                var result = vkQueueSubmit(graphicsQueue, 1, ref si, vkFence);
                CheckResult(result);

                if (useExtraFence)
                {
                    result = vkQueueSubmit(graphicsQueue, 0, null, submissionFence);
                    CheckResult(result);
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
                if (vkGetFenceStatus(device, submittedFences[0].Fence) != VkResult.Success)
                    return;

                for (int i = 0; i < submittedFences.Count; i++)
                {
                    var fsi = submittedFences[i];

                    if (vkGetFenceStatus(device, fsi.Fence) == VkResult.Success)
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
            var resetResult = vkResetFences(device, 1, ref fence);
            CheckResult(resetResult);
            returnSubmissionFence(fence);

            lock (stagingResourcesLock)
            {
                if (submittedStagingTextures.Remove(completedCb, out var stagingTex))
                    availableStagingTextures.Add(stagingTex);

                if (submittedStagingBuffers.Remove(completedCb, out var stagingBuffer))
                {
                    if (stagingBuffer.SizeInBytes <= max_staging_buffer_size)
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

        private void returnSubmissionFence(Vulkan.VkFence fence)
        {
            availableSubmissionFences.Enqueue(fence);
        }

        private Vulkan.VkFence getFreeSubmissionFence()
        {
            if (availableSubmissionFences.TryDequeue(out var availableFence))
                return availableFence;

            var fenceCi = VkFenceCreateInfo.New();
            var result = vkCreateFence(device, ref fenceCi, null, out var newFence);
            CheckResult(result);
            return newFence;
        }

        private void setDebugMarkerName(VkDebugReportObjectTypeEXT type, ulong target, string name)
        {
            Debug.Assert(setObjectNameDelegate != null);

            var nameInfo = VkDebugMarkerObjectNameInfoEXT.New();
            nameInfo.objectType = type;
            nameInfo.@object = target;

            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];
            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            nameInfo.pObjectName = utf8Ptr;
            var result = setObjectNameDelegate(device, &nameInfo);
            CheckResult(result);
        }

        private void createInstance(bool debug, VulkanDeviceOptions options)
        {
            var availableInstanceLayers = new HashSet<string>(EnumerateInstanceLayers());
            var availableInstanceExtensions = new HashSet<string>(GetInstanceExtensions());

            var instanceCi = VkInstanceCreateInfo.New();

            // Query the highest supported Vulkan instance version.
            // vkEnumerateInstanceVersion is a Vulkan 1.1 function; if absent we fall back to 1.0.
            uint instanceApiVersion = new VkVersion(1, 0, 0);

            fixed (byte* fnName = "vkEnumerateInstanceVersion\0"u8)
            {
                IntPtr fnPtr = vkGetInstanceProcAddr(new VkInstance(), fnName);

                if (fnPtr != IntPtr.Zero)
                {
                    var enumerateInstanceVersion =
                        Marshal.GetDelegateForFunctionPointer<VkEnumerateInstanceVersionT>(fnPtr);

                    uint supportedVersion;
                    if (enumerateInstanceVersion(&supportedVersion) == VkResult.Success)
                        instanceApiVersion = supportedVersion;
                }
            }

            var applicationInfo = new VkApplicationInfo
            {
                apiVersion = instanceApiVersion,
                applicationVersion = new VkVersion(1, 0, 0),
                engineVersion = new VkVersion(1, 0, 0),
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
                instanceCi.flags |= vk_instance_create_enumerate_portability_bit_khr;
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

            var result = vkCreateInstance(ref instanceCi, null, out instance);
            CheckResult(result);

            if (HasSurfaceExtension(CommonStrings.VkExtMetalSurfaceExtensionName)) CreateMetalSurfaceExt = getInstanceProcAddr<VkCreateMetalSurfaceExtT>("vkCreateMetalSurfaceEXT"u8);

            if (debug && debugReportExtensionAvailable) EnableDebugCallback();

            if (hasDeviceProperties2)
            {
                getPhysicalDeviceProperties2 = getInstanceProcAddr<VkGetPhysicalDeviceProperties2T>("vkGetPhysicalDeviceProperties2"u8)
                                               ?? getInstanceProcAddr<VkGetPhysicalDeviceProperties2T>("vkGetPhysicalDeviceProperties2KHR"u8);
            }

            // Load vkGetPhysicalDeviceSurfaceCapabilities2KHR for VK_EXT_surface_maintenance1
            // queries. Required before we can validate VK_EXT_swapchain_maintenance1 below.
            if (hasSurfaceMaintenance1)
            {
                GetPhysicalDeviceSurfaceCapabilities2 =
                    getInstanceProcAddr<VkGetPhysicalDeviceSurfaceCapabilities2KhrT>("vkGetPhysicalDeviceSurfaceCapabilities2KHR"u8);
            }

            foreach (var tempStr in tempStrings) tempStr.Dispose();
        }

        private uint debugCallback(
            uint flags,
            VkDebugReportObjectTypeEXT objectType,
            ulong @object,
            UIntPtr location,
            int messageCode,
            byte* pLayerPrefix,
            byte* pMessage,
            void* pUserData)
        {
            string message = Util.GetString(pMessage);
            var debugReportFlags = (VkDebugReportFlagsEXT)flags;

#if DEBUG
            if (Debugger.IsAttached) Debugger.Break();
#endif

            string fullMessage = $"[{debugReportFlags}] ({objectType}) {message}";

            if (debugReportFlags == VkDebugReportFlagsEXT.ErrorEXT) throw new VeldridException("A Vulkan validation error was encountered: " + fullMessage);

            Console.WriteLine(fullMessage);
            return 0;
        }

        private void createPhysicalDevice()
        {
            uint deviceCount = 0;
            vkEnumeratePhysicalDevices(instance, ref deviceCount, null);
            if (deviceCount == 0) throw new InvalidOperationException("No physical devices exist.");

            var physicalDevices = new VkPhysicalDevice[deviceCount];
            vkEnumeratePhysicalDevices(instance, ref deviceCount, ref physicalDevices[0]);

            // Prefer discrete GPU over integrated/virtual/CPU, falling back to the first device.
            PhysicalDevice = physicalDevices[0];

            for (int i = 0; i < physicalDevices.Length; i++)
            {
                vkGetPhysicalDeviceProperties(physicalDevices[i], out var props);

                if (props.deviceType == VkPhysicalDeviceType.DiscreteGpu)
                {
                    PhysicalDevice = physicalDevices[i];
                    break;
                }

                if (props.deviceType == VkPhysicalDeviceType.IntegratedGpu && physicalDevices[0] != physicalDevices[i])
                {
                    // Prefer integrated over anything worse, but keep looking for discrete.
                    PhysicalDevice = physicalDevices[i];
                }
            }

            vkGetPhysicalDeviceProperties(PhysicalDevice, out physicalDeviceProperties);
            fixed (byte* utf8NamePtr = physicalDeviceProperties.deviceName) deviceName = Encoding.UTF8.GetString(utf8NamePtr, (int)MaxPhysicalDeviceNameSize).TrimEnd('\0');

            DeviceApiVersion = VkVersion.FromPacked(physicalDeviceProperties.apiVersion);
            vendorName = "id:" + physicalDeviceProperties.vendorID.ToString("x8");
            apiVersion = new GraphicsApiVersion((int)DeviceApiVersion.Major, (int)DeviceApiVersion.Minor, 0, (int)DeviceApiVersion.Patch);
            DriverInfo = "version:" + physicalDeviceProperties.driverVersion.ToString("x8");

            vkGetPhysicalDeviceFeatures(PhysicalDevice, out physicalDeviceFeatures);

            vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out physicalDeviceMemProperties);
        }

        private void createLogicalDevice(VkSurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
        {
            getQueueFamilyIndices(surface);

            var familyIndices = new HashSet<uint> { GraphicsQueueIndex, PresentQueueIndex };
            var queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[familyIndices.Count];
            uint queueCreateInfosCount = (uint)familyIndices.Count;

            int i = 0;

            foreach (uint _ in familyIndices)
            {
                var queueCreateInfo = VkDeviceQueueCreateInfo.New();
                queueCreateInfo.queueFamilyIndex = GraphicsQueueIndex;
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
                             && GetPhysicalDeviceSurfaceCapabilities2 != null)
                    {
                        activeExtensions[activeExtensionCount++] = (IntPtr)properties[property].extensionName;
                        requiredInstanceExtensions.Remove(extensionName);
                        hasSwapchainMaintenance1 = true;
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

            var deviceCreateInfo = VkDeviceCreateInfo.New();
            deviceCreateInfo.queueCreateInfoCount = queueCreateInfosCount;
            deviceCreateInfo.pQueueCreateInfos = queueCreateInfos;

            deviceCreateInfo.pEnabledFeatures = &deviceFeatures;

            // Chain feature structs via pNext for extensions that require opt-in.
            VkPhysicalDeviceDynamicRenderingFeatures dynamicRenderingFeatures;
            VkPhysicalDeviceHostImageCopyFeaturesEXT hostImageCopyFeatures;
            VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT swapchainMaintenance1Features;

            if (hasDynamicRendering)
            {
                dynamicRenderingFeatures = VkPhysicalDeviceDynamicRenderingFeatures.New();
                dynamicRenderingFeatures.dynamicRendering = true;
                dynamicRenderingFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &dynamicRenderingFeatures;
            }

            if (hasHostImageCopy)
            {
                hostImageCopyFeatures = VkPhysicalDeviceHostImageCopyFeaturesEXT.New();
                hostImageCopyFeatures.hostImageCopy = true;
                hostImageCopyFeatures.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &hostImageCopyFeatures;
            }

            if (hasSwapchainMaintenance1)
            {
                swapchainMaintenance1Features = VkPhysicalDeviceSwapchainMaintenance1FeaturesEXT.New();
                swapchainMaintenance1Features.swapchainMaintenance1 = true;
                swapchainMaintenance1Features.pNext = deviceCreateInfo.pNext;
                deviceCreateInfo.pNext = &swapchainMaintenance1Features;
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

                var result = vkCreateDevice(PhysicalDevice, ref deviceCreateInfo, null, out device);
                CheckResult(result);
            }

            vkGetDeviceQueue(device, GraphicsQueueIndex, 0, out graphicsQueue);

            if (debugMarkerEnabled)
            {
                setObjectNameDelegate = Marshal.GetDelegateForFunctionPointer<VkDebugMarkerSetObjectNameExtT>(
                    getInstanceProcAddr("vkDebugMarkerSetObjectNameEXT"u8));
                MarkerBegin = Marshal.GetDelegateForFunctionPointer<VkCmdDebugMarkerBeginExtT>(
                    getInstanceProcAddr("vkCmdDebugMarkerBeginEXT"u8));
                MarkerEnd = Marshal.GetDelegateForFunctionPointer<VkCmdDebugMarkerEndExtT>(
                    getInstanceProcAddr("vkCmdDebugMarkerEndEXT"u8));
                MarkerInsert = Marshal.GetDelegateForFunctionPointer<VkCmdDebugMarkerInsertExtT>(
                    getInstanceProcAddr("vkCmdDebugMarkerInsertEXT"u8));
            }

            if (hasDedicatedAllocation && hasMemReqs2)
            {
                // On Vulkan 1.1+ the core entry points are available directly.
                if (DeviceApiVersion.IsAtLeast(1, 1))
                {
                    GetBufferMemoryRequirements2 = getDeviceProcAddr<VkGetBufferMemoryRequirements2T>("vkGetBufferMemoryRequirements2"u8);
                    GetImageMemoryRequirements2 = getDeviceProcAddr<VkGetImageMemoryRequirements2T>("vkGetImageMemoryRequirements2"u8);
                }
                else
                {
                    GetBufferMemoryRequirements2 = getDeviceProcAddr<VkGetBufferMemoryRequirements2T>("vkGetBufferMemoryRequirements2"u8)
                                                   ?? getDeviceProcAddr<VkGetBufferMemoryRequirements2T>("vkGetBufferMemoryRequirements2KHR"u8);
                    GetImageMemoryRequirements2 = getDeviceProcAddr<VkGetImageMemoryRequirements2T>("vkGetImageMemoryRequirements2"u8)
                                                  ?? getDeviceProcAddr<VkGetImageMemoryRequirements2T>("vkGetImageMemoryRequirements2KHR"u8);
                }
            }

            if (getPhysicalDeviceProperties2 != null && hasDriverProperties)
            {
                var deviceProps = VkPhysicalDeviceProperties2KHR.New();
                var driverProps = VkPhysicalDeviceDriverProperties.New();

                deviceProps.pNext = &driverProps;
                getPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);

                string driverName = Encoding.UTF8.GetString(
                    driverProps.DriverName, VkPhysicalDeviceDriverProperties.DRIVER_NAME_LENGTH).TrimEnd('\0');

                string driverInfo = Encoding.UTF8.GetString(
                    driverProps.DriverInfo, VkPhysicalDeviceDriverProperties.DRIVER_INFO_LENGTH).TrimEnd('\0');

                var conforming = driverProps.ConformanceVersion;
                apiVersion = new GraphicsApiVersion(conforming.Major, conforming.Minor, conforming.Subminor, conforming.Patch);
                DriverName = driverName;
                DriverInfo = driverInfo;
            }

            // VK_KHR_push_descriptor: query maxPushDescriptors.
            if (hasPushDescriptors && getPhysicalDeviceProperties2 != null)
            {
                var deviceProps2 = VkPhysicalDeviceProperties2KHR.New();
                var pushDescProps = VkPhysicalDevicePushDescriptorPropertiesKHR.New();
                deviceProps2.pNext = &pushDescProps;
                getPhysicalDeviceProperties2(PhysicalDevice, &deviceProps2);
                MaxPushDescriptors = pushDescProps.maxPushDescriptors;
                HasPushDescriptors = MaxPushDescriptors > 0;
            }

            // VK_KHR_dynamic_rendering: load function pointers.
            if (hasDynamicRendering)
            {
                if (DeviceApiVersion.IsAtLeast(1, 3))
                {
                    CmdBeginRendering = getDeviceProcAddr<VkCmdBeginRenderingT>("vkCmdBeginRendering"u8);
                    CmdEndRendering = getDeviceProcAddr<VkCmdEndRenderingT>("vkCmdEndRendering"u8);
                }
                else
                {
                    CmdBeginRendering = getDeviceProcAddr<VkCmdBeginRenderingT>("vkCmdBeginRendering"u8)
                                        ?? getDeviceProcAddr<VkCmdBeginRenderingT>("vkCmdBeginRenderingKHR"u8);
                    CmdEndRendering = getDeviceProcAddr<VkCmdEndRenderingT>("vkCmdEndRendering"u8)
                                      ?? getDeviceProcAddr<VkCmdEndRenderingT>("vkCmdEndRenderingKHR"u8);
                }

                HasDynamicRendering = CmdBeginRendering != null && CmdEndRendering != null;
            }

            HasMemoryBudget = hasMemoryBudget;

            // VK_EXT_host_image_copy: load function pointers.
            if (hasHostImageCopy)
            {
                CopyMemoryToImageExt = getDeviceProcAddr<VkCopyMemoryToImageExtT>("vkCopyMemoryToImageEXT"u8);
                TransitionImageLayoutExt = getDeviceProcAddr<VkTransitionImageLayoutExtT>("vkTransitionImageLayoutEXT"u8);
                HasHostImageCopy = CopyMemoryToImageExt != null && TransitionImageLayoutExt != null;
            }

            // VK_EXT_descriptor_indexing: detection only (core in Vulkan 1.2).
            // No function pointers to load — just expose the flag for future bindless usage.
            HasDescriptorIndexing = hasDescriptorIndexing;

            // VK_KHR_fragment_shading_rate: load function pointer for per-draw VRS.
            if (hasFragmentShadingRate)
            {
                CmdSetFragmentShadingRate = getDeviceProcAddr<VkCmdSetFragmentShadingRateT>("vkCmdSetFragmentShadingRateKHR"u8);
                HasFragmentShadingRate = CmdSetFragmentShadingRate != null;
            }

            // VK_EXT_mesh_shader: load function pointer for mesh shader dispatch.
            if (hasMeshShader)
            {
                CmdDrawMeshTasksExt = getDeviceProcAddr<VkCmdDrawMeshTasksExtT>("vkCmdDrawMeshTasksEXT"u8);
                HasMeshShader = CmdDrawMeshTasksExt != null;
            }

            // VK_EXT_swapchain_maintenance1: no device function pointers required by
            // our usage (only struct chaining at create- and present-time).
            HasSwapchainMaintenance1 = hasSwapchainMaintenance1;
        }

        // UTF-8 literal overloads: zero runtime encoding cost; 'u8' string literals are null-terminated.
        private IntPtr getInstanceProcAddr(ReadOnlySpan<byte> nameUtf8)
        {
            fixed (byte* utf8Ptr = nameUtf8)
                return vkGetInstanceProcAddr(instance, utf8Ptr);
        }

        private T getInstanceProcAddr<T>(ReadOnlySpan<byte> nameUtf8)
        {
            IntPtr funcPtr = getInstanceProcAddr(nameUtf8);
            if (funcPtr != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);

            return default;
        }

        private IntPtr getInstanceProcAddr(string name)
        {
            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];

            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            return vkGetInstanceProcAddr(instance, utf8Ptr);
        }

        private T getInstanceProcAddr<T>(string name)
        {
            IntPtr funcPtr = getInstanceProcAddr(name);
            if (funcPtr != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);

            return default;
        }

        // UTF-8 literal overloads: zero runtime encoding cost; 'u8' string literals are null-terminated.
        private IntPtr getDeviceProcAddr(ReadOnlySpan<byte> nameUtf8)
        {
            fixed (byte* utf8Ptr = nameUtf8)
                return vkGetDeviceProcAddr(device, utf8Ptr);
        }

        private T getDeviceProcAddr<T>(ReadOnlySpan<byte> nameUtf8)
        {
            IntPtr funcPtr = getDeviceProcAddr(nameUtf8);
            if (funcPtr != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);

            return default;
        }

        private IntPtr getDeviceProcAddr(string name)
        {
            int byteCount = Encoding.UTF8.GetByteCount(name);
            byte* utf8Ptr = stackalloc byte[byteCount + 1];

            fixed (char* namePtr = name) Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
            utf8Ptr[byteCount] = 0;

            return vkGetDeviceProcAddr(device, utf8Ptr);
        }

        private T getDeviceProcAddr<T>(string name)
        {
            IntPtr funcPtr = getDeviceProcAddr(name);
            if (funcPtr != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);

            return default;
        }

        private void getQueueFamilyIndices(VkSurfaceKHR surface)
        {
            uint queueFamilyCount = 0;
            vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, null);
            var qfp = new VkQueueFamilyProperties[queueFamilyCount];
            vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref queueFamilyCount, out qfp[0]);

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
                    vkGetPhysicalDeviceSurfaceSupportKHR(PhysicalDevice, i, surface, out var presentSupported);

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
            var commandPoolCi = VkCommandPoolCreateInfo.New();
            commandPoolCi.flags = VkCommandPoolCreateFlags.ResetCommandBuffer;
            commandPoolCi.queueFamilyIndex = GraphicsQueueIndex;
            var result = vkCreateCommandPool(device, ref commandPoolCi, null, out graphicsCommandPool);
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
            var result = vkMapMemory(Device, buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, 0, &mappedPtr);
            CheckResult(result);
            return (IntPtr)mappedPtr;
        }

        private void unmapBuffer(VkBuffer buffer)
        {
            if (!buffer.Memory.IsPersistentMapped) vkUnmapMemory(Device, buffer.Memory.DeviceMemory);
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

            uint newBufferSize = Math.Max(min_staging_buffer_size, size);
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
            var deviceSwapchain = vkSc.DeviceSwapchain;
            var presentInfo = VkPresentInfoKHR.New();
            presentInfo.swapchainCount = 1;
            presentInfo.pSwapchains = &deviceSwapchain;
            uint imageIndex = vkSc.ImageIndex;
            presentInfo.pImageIndices = &imageIndex;

            // VK_EXT_swapchain_maintenance1: chain the per-present mode override so the
            // driver applies any pending hot-swap (vsync ↔ low-latency) without a
            // swapchain rebuild. The pointed-to mode must remain valid until vkQueuePresentKHR
            // returns, which is satisfied by the stack-local `currentMode` below.
            VkSwapchainPresentModeInfoEXT presentModeInfo;
            VkPresentModeKHR currentMode;
            if (HasSwapchainMaintenance1 && vkSc.HasPresentModeHotSwap)
            {
                currentMode = vkSc.CurrentPresentMode;
                presentModeInfo = VkSwapchainPresentModeInfoEXT.New();
                presentModeInfo.swapchainCount = 1;
                presentModeInfo.pPresentModes = &currentMode;
                presentInfo.pNext = &presentModeInfo;
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
                    presentResult = vkQueuePresentKHR(vkSc.PresentQueue, ref presentInfo);
                    handlePresentResult(vkSc, presentResult);
                    acquireAndWaitNextImage(vkSc);
                }
            }
            else
            {
                lock (vkSc)
                {
                    presentResult = vkQueuePresentKHR(vkSc.PresentQueue, ref presentInfo);
                    handlePresentResult(vkSc, presentResult);
                    acquireAndWaitNextImage(vkSc);
                }
            }
        }

        // VK_ERROR_OUT_OF_DATE_KHR / VK_SUBOPTIMAL_KHR are expected on Android
        // (rotation, fold, DeX attach, system bars showing/hiding). Treat them as
        // a needs-rebuild signal rather than a hard failure: silently recreate
        // and re-acquire. This avoids the per-rotate managed exception that the
        // osu! framework retry loop would otherwise have to swallow every frame
        // until the surface settled.
        private static void handlePresentResult(VkSwapchain vkSc, VkResult result)
        {
            if (result == VkResult.Success)
                return;

            if (result == VkResult.ErrorOutOfDateKHR || result == VkResult.SuboptimalKHR)
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
            {
                var fence = vkSc.ImageAvailableFence;
                vkWaitForFences(device, 1, ref fence, true, ulong.MaxValue);
                vkResetFences(device, 1, ref fence);
            }
        }

        private protected override void WaitForIdleCore()
        {
            lock (graphicsQueueLock) vkQueueWaitIdle(graphicsQueue);

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

            var result = vkGetPhysicalDeviceImageFormatProperties(
                PhysicalDevice,
                vkFormat,
                vkType,
                tiling,
                vkUsage,
                VkImageCreateFlags.None,
                out var vkProps);

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
                vkCmdCopyBuffer(cb, copySrcVkBuffer.DeviceBuffer, vkBuffer.DeviceBuffer, 1, ref copyRegion);

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
            var transitionToTransfer = VkHostImageLayoutTransitionInfoEXT.New();
            transitionToTransfer.image = vkTex.OptimalDeviceImage;
            transitionToTransfer.oldLayout = oldLayout;
            transitionToTransfer.newLayout = VkImageLayout.TransferDstOptimal;
            transitionToTransfer.subresourceRange = new VkImageSubresourceRange(aspectMask, mipLevel, 1, arrayLayer, 1);
            var tResult = TransitionImageLayoutExt(device, 1, &transitionToTransfer);
            VulkanUtil.CheckResult(tResult);

            var region = VkMemoryToImageCopyEXT.New();
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

            var copyInfo = VkCopyMemoryToImageInfoEXT.New();
            copyInfo.dstImage = vkTex.OptimalDeviceImage;
            copyInfo.dstImageLayout = VkImageLayout.TransferDstOptimal;
            copyInfo.regionCount = 1;
            copyInfo.pRegions = &region;

            var cResult = CopyMemoryToImageExt(device, &copyInfo);
            VulkanUtil.CheckResult(cResult);

            // Transition back to the layout the image was in before.
            var transitionBack = VkHostImageLayoutTransitionInfoEXT.New();
            transitionBack.image = vkTex.OptimalDeviceImage;
            transitionBack.oldLayout = VkImageLayout.TransferDstOptimal;
            transitionBack.newLayout = oldLayout;
            transitionBack.subresourceRange = new VkImageSubresourceRange(aspectMask, mipLevel, 1, arrayLayer, 1);
            tResult = TransitionImageLayoutExt(device, 1, &transitionBack);
            VulkanUtil.CheckResult(tResult);
        }

        private class SharedCommandPool
        {
            public bool IsCached { get; }
            private readonly VkGraphicsDevice gd;
            private readonly VkCommandPool pool;
            private readonly VkCommandBuffer cb;

            public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
            {
                this.gd = gd;
                IsCached = isCached;

                var commandPoolCi = VkCommandPoolCreateInfo.New();
                commandPoolCi.flags = VkCommandPoolCreateFlags.Transient | VkCommandPoolCreateFlags.ResetCommandBuffer;
                commandPoolCi.queueFamilyIndex = this.gd.GraphicsQueueIndex;
                var result = vkCreateCommandPool(this.gd.Device, ref commandPoolCi, null, out pool);
                CheckResult(result);

                var allocateInfo = VkCommandBufferAllocateInfo.New();
                allocateInfo.commandBufferCount = 1;
                allocateInfo.level = VkCommandBufferLevel.Primary;
                allocateInfo.commandPool = pool;
                result = vkAllocateCommandBuffers(this.gd.Device, ref allocateInfo, out cb);
                CheckResult(result);
            }

            public VkCommandBuffer BeginNewCommandBuffer()
            {
                var beginInfo = VkCommandBufferBeginInfo.New();
                beginInfo.flags = VkCommandBufferUsageFlags.OneTimeSubmit;
                var result = vkBeginCommandBuffer(cb, ref beginInfo);
                CheckResult(result);

                return cb;
            }

            public void EndAndSubmit(VkCommandBuffer cb)
            {
                var result = vkEndCommandBuffer(cb);
                CheckResult(result);
                gd.submitCommandBuffer(null, cb, 0, null, 0, null, null);
                lock (gd.stagingResourcesLock) gd.submittedSharedCommandPools.Add(cb, this);
            }

            internal void Destroy()
            {
                vkDestroyCommandPool(gd.Device, pool, null);
            }
        }

        private struct FenceSubmissionInfo
        {
            public readonly Vulkan.VkFence Fence;
            public readonly VkCommandList CommandList;
            public readonly VkCommandBuffer CommandBuffer;

            public FenceSubmissionInfo(Vulkan.VkFence fence, VkCommandList commandList, VkCommandBuffer commandBuffer)
            {
                Fence = fence;
                CommandList = commandList;
                CommandBuffer = commandBuffer;
            }
        }
    }

    internal unsafe delegate VkResult VkCreateDebugReportCallbackExtD(
        VkInstance instance,
        VkDebugReportCallbackCreateInfoEXT* createInfo,
        IntPtr allocatorPtr,
        out VkDebugReportCallbackEXT ret);

    internal unsafe delegate void VkDestroyDebugReportCallbackExtD(
        VkInstance instance,
        VkDebugReportCallbackEXT callback,
        VkAllocationCallbacks* pAllocator);

    internal unsafe delegate VkResult VkDebugMarkerSetObjectNameExtT(VkDevice device, VkDebugMarkerObjectNameInfoEXT* pNameInfo);

    internal unsafe delegate void VkCmdDebugMarkerBeginExtT(VkCommandBuffer commandBuffer, VkDebugMarkerMarkerInfoEXT* pMarkerInfo);

    internal delegate void VkCmdDebugMarkerEndExtT(VkCommandBuffer commandBuffer);

    internal unsafe delegate void VkCmdDebugMarkerInsertExtT(VkCommandBuffer commandBuffer, VkDebugMarkerMarkerInfoEXT* pMarkerInfo);

    internal unsafe delegate void VkGetBufferMemoryRequirements2T(VkDevice device, VkBufferMemoryRequirementsInfo2KHR* pInfo, VkMemoryRequirements2KHR* pMemoryRequirements);

    internal unsafe delegate void VkGetImageMemoryRequirements2T(VkDevice device, VkImageMemoryRequirementsInfo2KHR* pInfo, VkMemoryRequirements2KHR* pMemoryRequirements);

    internal unsafe delegate void VkGetPhysicalDeviceProperties2T(VkPhysicalDevice physicalDevice, void* properties);

    internal unsafe delegate VkResult VkEnumerateInstanceVersionT(uint* pApiVersion);

    // VK_EXT_metal_surface

    internal unsafe delegate VkResult VkCreateMetalSurfaceExtT(
        VkInstance instance,
        VkMetalSurfaceCreateInfoExt* pCreateInfo,
        VkAllocationCallbacks* pAllocator,
        VkSurfaceKHR* pSurface);

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    internal unsafe struct VkMetalSurfaceCreateInfoExt
    {
        public const VkStructureType VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT = (VkStructureType)1000217000;

        public VkStructureType SType;
        public void* PNext;
        public uint Flags;
        public void* PLayer;
    }

    internal unsafe struct VkPhysicalDeviceDriverProperties
    {
        public const int DRIVER_NAME_LENGTH = 256;
        public const int DRIVER_INFO_LENGTH = 256;
        public const VkStructureType VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES = (VkStructureType)1000196000;

        public VkStructureType SType;
        public void* PNext;
        public VkDriverId DriverID;
        public fixed byte DriverName[DRIVER_NAME_LENGTH];
        public fixed byte DriverInfo[DRIVER_INFO_LENGTH];
        public VkConformanceVersion ConformanceVersion;

        public static VkPhysicalDeviceDriverProperties New()
        {
            return new VkPhysicalDeviceDriverProperties { SType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES };
        }
    }

    internal enum VkDriverId
    {
    }

    internal struct VkConformanceVersion
    {
        public byte Major;
        public byte Minor;
        public byte Subminor;
        public byte Patch;
    }
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
}
