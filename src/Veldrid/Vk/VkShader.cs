using System;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using static Veldrid.Vk.VulkanUtil;

namespace Veldrid.Vk
{
    internal unsafe class VkShader : Shader
    {
        public VkShaderModule ShaderModule => shaderModule;

        public override bool IsDisposed => disposed;

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
        private readonly VkShaderModule shaderModule;
        private bool disposed;
        private string name;

        public VkShader(VkGraphicsDevice gd, ref ShaderDescription description)
            : base(description.Stage, description.EntryPoint)
        {
            this.gd = gd;

            var shaderModuleCi = new VkShaderModuleCreateInfo();

            fixed (byte* codePtr = description.ShaderBytes)
            {
                shaderModuleCi.codeSize = (UIntPtr)description.ShaderBytes.Length;
                shaderModuleCi.pCode = (uint*)codePtr;
                var result = gd.DeviceApi.vkCreateShaderModule(&shaderModuleCi, null, out shaderModule);
                CheckResult(result);
            }
        }

        #region Disposal

        public override void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                gd.DeviceApi.vkDestroyShaderModule(ShaderModule, null);
            }
        }

        #endregion
    }
}
