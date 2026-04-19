using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Veldrid.D3D12
{
    [SupportedOSPlatform("windows")]
    internal class D3D12Texture : Texture
    {
        public override uint Width { get; }
        public override uint Height { get; }
        public override uint Depth { get; }
        public override uint MipLevels { get; }
        public override uint ArrayLayers { get; }
        public override PixelFormat Format { get; }
        public override TextureUsage Usage { get; }
        public override TextureType Type { get; }
        public override TextureSampleCount SampleCount { get; }
        public override bool IsDisposed => disposed;

        public ID3D12Resource Resource { get; }
        public Format DxgiFormat { get; }
        public Format TypelessDxgiFormat { get; }

        public override string Name
        {
            get => name;
            set
            {
                name = value;
                Resource.Name = value;
            }
        }

        private string name;
        private bool disposed;

        public D3D12Texture(ID3D12Device device, ref TextureDescription description)
        {
            Width = description.Width;
            Height = description.Height;
            Depth = description.Depth;
            MipLevels = description.MipLevels;
            ArrayLayers = description.ArrayLayers;
            Format = description.Format;
            Usage = description.Usage;
            Type = description.Type;
            SampleCount = description.SampleCount;

            DxgiFormat = D3D12Formats.ToDxgiFormat(
                description.Format,
                (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);
            TypelessDxgiFormat = D3D12Formats.GetTypelessFormat(DxgiFormat);

            var resourceFlags = ResourceFlags.None;

            if ((description.Usage & TextureUsage.RenderTarget) == TextureUsage.RenderTarget)
                resourceFlags |= ResourceFlags.AllowRenderTarget;

            if ((description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
                resourceFlags |= ResourceFlags.AllowDepthStencil;

            if ((description.Usage & TextureUsage.Storage) == TextureUsage.Storage)
                resourceFlags |= ResourceFlags.AllowUnorderedAccess;

            // If not sampled or storage, deny shader resource access for depth targets.
            if ((description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
                && (description.Usage & TextureUsage.Sampled) == 0)
                resourceFlags |= ResourceFlags.DenyShaderResource;

            uint arraySize = description.ArrayLayers;

            if ((description.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
                arraySize *= 6;

            uint roundedWidth = description.Width;
            uint roundedHeight = description.Height;

            if (FormatHelpers.IsCompressedFormat(description.Format))
            {
                roundedWidth = (roundedWidth + 3) / 4 * 4;
                roundedHeight = (roundedHeight + 3) / 4 * 4;
            }

            ResourceDescription resourceDesc;

            if (Type == TextureType.Texture1D)
            {
                resourceDesc = ResourceDescription.Texture1D(
                    TypelessDxgiFormat,
                    (uint)roundedWidth,
                    (ushort)arraySize,
                    (ushort)description.MipLevels,
                    resourceFlags);
            }
            else if (Type == TextureType.Texture2D)
            {
                resourceDesc = ResourceDescription.Texture2D(
                    TypelessDxgiFormat,
                    (uint)roundedWidth,
                    (uint)roundedHeight,
                    (ushort)arraySize,
                    (ushort)description.MipLevels,
                    (uint)D3D12Formats.ToDxgiSampleCount(SampleCount),
                    0u,
                    resourceFlags);
            }
            else
            {
                Debug.Assert(Type == TextureType.Texture3D);
                resourceDesc = ResourceDescription.Texture3D(
                    TypelessDxgiFormat,
                    (uint)roundedWidth,
                    (uint)roundedHeight,
                    (ushort)description.Depth,
                    (ushort)description.MipLevels,
                    resourceFlags);
            }

            if ((description.Usage & TextureUsage.Staging) == TextureUsage.Staging)
            {
                // Staging textures in D3D12 use a buffer in readback/upload heap.
                // For simplicity, we create a default heap resource and use copy operations.
                var heapProperties = new HeapProperties(HeapType.Default);
                Resource = device.CreateCommittedResource(heapProperties, HeapFlags.None, resourceDesc, ResourceStates.Common);
            }
            else
            {
                var heapProperties = new HeapProperties(HeapType.Default);
                var initialState = ResourceStates.Common;
                Resource = device.CreateCommittedResource(heapProperties, HeapFlags.None, resourceDesc, initialState);
            }
        }

        public D3D12Texture(ID3D12Resource existingResource, TextureType type, PixelFormat format)
        {
            Resource = existingResource;
            Type = type;
            Format = format;

            var desc = existingResource.Description;
            Width = (uint)desc.Width;
            Height = desc.Height;
            Depth = desc.DepthOrArraySize;
            MipLevels = desc.MipLevels;

            if (type == TextureType.Texture3D)
            {
                ArrayLayers = 1;
            }
            else
            {
                ArrayLayers = desc.DepthOrArraySize;
                Depth = 1;
            }

            SampleCount = FormatHelpers.GetSampleCount(desc.SampleDescription.Count);
            Usage = TextureUsage.RenderTarget;

            DxgiFormat = D3D12Formats.ToDxgiFormat(
                format,
                (Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);
            TypelessDxgiFormat = D3D12Formats.GetTypelessFormat(DxgiFormat);
        }

        private protected override void DisposeCore()
        {
            if (!disposed)
            {
                Resource.Dispose();
                disposed = true;
            }
        }
    }
}
