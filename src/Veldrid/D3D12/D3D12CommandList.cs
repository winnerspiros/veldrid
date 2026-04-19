using System;
using System.Runtime.Versioning;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12CommandList : CommandList
    {
        public override bool IsDisposed => disposed;

        public override string Name
        {
            get => name;
            set
            {
                name = value;

                if (commandList != null)
                    commandList.Name = value;
            }
        }

        internal ID3D12GraphicsCommandList CommandListHandle => commandList;

        private readonly D3D12GraphicsDevice gd;
        private readonly ID3D12GraphicsCommandList commandList;
        private ID3D12CommandAllocator currentAllocator;
        private D3D12Pipeline currentGraphicsPipeline;
        private D3D12Pipeline currentComputePipeline;
        private D3D12Framebuffer currentFramebuffer;
        private bool disposed;
        private string name;

        public D3D12CommandList(D3D12GraphicsDevice gd, ref CommandListDescription description)
            : base(ref description, gd.Features, 256u, 16u)
        {
            this.gd = gd;

            currentAllocator = gd.CommandAllocatorPool.GetAllocator(gd.FrameFence.CompletedValue);
            commandList = gd.Device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct,
                currentAllocator);

            // The command list starts in the recording state; close it so Begin() can reset it.
            commandList.Close();
        }

        public override void Begin()
        {
            currentAllocator = gd.CommandAllocatorPool.GetAllocator(gd.FrameFence.CompletedValue);
            commandList.Reset(currentAllocator);
        }

        public override void End()
        {
            commandList.Close();
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            commandList.RSSetViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            commandList.RSSetScissorRect(new RawRect((int)x, (int)y, (int)(x + width), (int)(y + height)));
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            commandList.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public override void Dispose()
        {
            if (!disposed)
            {
                commandList.Dispose();
                disposed = true;
            }
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            var d3d12Pipeline = Util.AssertSubtype<Pipeline, D3D12Pipeline>(pipeline);

            commandList.SetPipelineState(d3d12Pipeline.PipelineState);

            if (d3d12Pipeline.IsComputePipeline)
            {
                commandList.SetComputeRootSignature(d3d12Pipeline.RootSignature);
                currentComputePipeline = d3d12Pipeline;
            }
            else
            {
                commandList.SetGraphicsRootSignature(d3d12Pipeline.RootSignature);
                commandList.IASetPrimitiveTopology(d3d12Pipeline.PrimitiveTopology);
                commandList.OMSetStencilRef(d3d12Pipeline.StencilReference);
                commandList.OMSetBlendFactor(new Color4(
                    d3d12Pipeline.BlendFactor[0],
                    d3d12Pipeline.BlendFactor[1],
                    d3d12Pipeline.BlendFactor[2],
                    d3d12Pipeline.BlendFactor[3]));
                currentGraphicsPipeline = d3d12Pipeline;
            }
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            var d3d12Buffer = Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(buffer);

            uint stride = 0;

            if (currentGraphicsPipeline != null
                && currentGraphicsPipeline.VertexStrides != null
                && index < currentGraphicsPipeline.VertexStrides.Length)
            {
                stride = currentGraphicsPipeline.VertexStrides[index];
            }

            var vbv = new VertexBufferView(
                d3d12Buffer.Resource.GPUVirtualAddress + offset,
                d3d12Buffer.SizeInBytes - offset,
                stride);

            commandList.IASetVertexBuffers(index, vbv);
        }

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            var d3d12Buffer = Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(buffer);

            var dxgiFormat = format == IndexFormat.UInt16
                ? Format.R16_UInt
                : Format.R32_UInt;

            commandList.IASetIndexBuffer(
                d3d12Buffer.Resource.GPUVirtualAddress + offset,
                d3d12Buffer.SizeInBytes - offset,
                dxgiFormat);
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            currentFramebuffer = Util.AssertSubtype<Framebuffer, D3D12Framebuffer>(fb);

            commandList.OMSetRenderTargets(
                currentFramebuffer.RenderTargetViews,
                currentFramebuffer.DepthStencilView);
        }

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
        {
            commandList.ClearRenderTargetView(
                currentFramebuffer.RenderTargetViews[index],
                new Color4(clearColor.R, clearColor.G, clearColor.B, clearColor.A));
        }

        private protected override void ClearDepthStencilCore(float depth, byte stencil)
        {
            if (currentFramebuffer.DepthStencilView.HasValue)
            {
                commandList.ClearDepthStencilView(
                    currentFramebuffer.DepthStencilView.Value,
                    ClearFlags.Depth | ClearFlags.Stencil,
                    depth,
                    stencil);
            }
        }

        private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            commandList.DrawInstanced(vertexCount, instanceCount, vertexStart, instanceStart);
        }

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            commandList.DrawIndexedInstanced(indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
        }

        protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            throw new NotImplementedException(
                "DrawIndirect requires an ID3D12CommandSignature, which is not yet implemented for D3D12.");
        }

        protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            throw new NotImplementedException(
                "DrawIndexedIndirect requires an ID3D12CommandSignature, which is not yet implemented for D3D12.");
        }

        protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
        {
            throw new NotImplementedException(
                "DispatchIndirect requires an ID3D12CommandSignature, which is not yet implemented for D3D12.");
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            // Resource binding in D3D12 requires GPU-visible descriptor heaps and root parameter mapping.
            // Full implementation pending descriptor heap management.
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet set, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            // Resource binding in D3D12 requires GPU-visible descriptor heaps and root parameter mapping.
            // Full implementation pending descriptor heap management.
        }

        protected override void CopyBufferCore(
            DeviceBuffer source, uint sourceOffset,
            DeviceBuffer destination, uint destinationOffset,
            uint sizeInBytes)
        {
            var srcD3D12 = Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(source);
            var dstD3D12 = Util.AssertSubtype<DeviceBuffer, D3D12Buffer>(destination);

            commandList.CopyBufferRegion(
                dstD3D12.Resource, destinationOffset,
                srcD3D12.Resource, sourceOffset,
                sizeInBytes);
        }

        protected override void CopyTextureCore(
            Texture source,
            uint srcX, uint srcY, uint srcZ,
            uint srcMipLevel, uint srcBaseArrayLayer,
            Texture destination,
            uint dstX, uint dstY, uint dstZ,
            uint dstMipLevel, uint dstBaseArrayLayer,
            uint width, uint height, uint depth,
            uint layerCount)
        {
            var srcD3D12 = Util.AssertSubtype<Texture, D3D12Texture>(source);
            var dstD3D12 = Util.AssertSubtype<Texture, D3D12Texture>(destination);

            for (uint layer = 0; layer < layerCount; layer++)
            {
                uint srcSubresource = source.CalculateSubresource(srcMipLevel, srcBaseArrayLayer + layer);
                uint dstSubresource = destination.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer);

                var srcLocation = new TextureCopyLocation(srcD3D12.Resource, srcSubresource);
                var dstLocation = new TextureCopyLocation(dstD3D12.Resource, dstSubresource);

                commandList.CopyTextureRegion(
                    dstLocation,
                    dstX, dstY, dstZ,
                    srcLocation,
                    new Box((int)srcX, (int)srcY, (int)srcZ,
                        (int)(srcX + width), (int)(srcY + height), (int)(srcZ + depth)));
            }
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            var srcD3D12 = Util.AssertSubtype<Texture, D3D12Texture>(source);
            var dstD3D12 = Util.AssertSubtype<Texture, D3D12Texture>(destination);

            commandList.ResolveSubresource(
                dstD3D12.Resource, 0,
                srcD3D12.Resource, 0,
                dstD3D12.DxgiFormat);
        }

        private protected override void UpdateBufferCore(
            DeviceBuffer buffer,
            uint bufferOffsetInBytes,
            IntPtr source,
            uint sizeInBytes)
        {
            throw new NotImplementedException(
                "UpdateBuffer via CommandList is not yet supported in D3D12. Use GraphicsDevice.UpdateBuffer instead.");
        }

        private protected override void GenerateMipmapsCore(Texture texture)
        {
            throw new NotImplementedException(
                "GenerateMipmaps is not natively supported in D3D12 and requires a compute shader implementation.");
        }

        private protected override void PushDebugGroupCore(string name)
        {
            commandList.BeginEvent(name);
        }

        private protected override void PopDebugGroupCore()
        {
            commandList.EndEvent();
        }

        private protected override void InsertDebugMarkerCore(string name)
        {
            commandList.SetMarker(name);
        }

        /// <summary>
        ///     Called after the command list has been submitted to the GPU queue.
        ///     Returns the current command allocator to the pool for reuse once the GPU work completes.
        /// </summary>
        internal void OnSubmitted(ulong fenceValue)
        {
            if (currentAllocator != null)
            {
                gd.CommandAllocatorPool.ReturnAllocator(fenceValue, currentAllocator);
                currentAllocator = null;
            }
        }
    }
}
