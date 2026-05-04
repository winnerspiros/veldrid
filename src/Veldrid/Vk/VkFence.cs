using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
namespace Veldrid.Vk
{
    internal unsafe class VkFence : Fence
    {
        public Vortice.Vulkan.VkFence DeviceFence => fence;

        public override bool Signaled => gd.DeviceApi.vkGetFenceStatus(fence) == VkResult.Success;
        public override bool IsDisposed => destroyed;

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                gd.SetResourceName(this, value);
            }
        }

        private readonly VkGraphicsDevice gd;
        private readonly Vortice.Vulkan.VkFence fence;
        private string name;
        private bool destroyed;

        public VkFence(VkGraphicsDevice gd, bool signaled)
        {
            this.gd = gd;
            var fenceCi = new VkFenceCreateInfo();
            fenceCi.flags = signaled ? VkFenceCreateFlags.Signaled : VkFenceCreateFlags.None;
            var result = gd.DeviceApi.vkCreateFence(ref fenceCi, null, out fence);
            VulkanUtil.CheckResult(result);
        }

        #region Disposal

        public override void Dispose()
        {
            if (!destroyed)
            {
                gd.DeviceApi.vkDestroyFence(fence, null);
                destroyed = true;
            }
        }

        #endregion

        public override void Reset()
        {
            gd.ResetFence(this);
        }
    }
}
