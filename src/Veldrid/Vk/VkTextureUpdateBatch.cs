using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vulkan;
using static Vulkan.VulkanNative;

namespace Veldrid.Vk
{
    /// <summary>
    ///     Vulkan implementation of <see cref="TextureUpdateBatch" />: stages every pending region into a single
    ///     growable host-visible buffer, then on <see cref="Submit" /> records one command buffer that performs
    ///     all the buffer→image copies and ends with a single <c>vkQueueSubmit</c>. Replaces the per-call
    ///     <c>vkQueueSubmit</c> storm in <see cref="VkGraphicsDevice.UpdateTextureCore" /> for callers that opt in.
    ///
    ///     <para>
    ///         The fast paths from <c>UpdateTextureCore</c> (destination is itself a staging texture; or
    ///         VK_EXT_host_image_copy is available) bypass the batch and run synchronously, since they do not
    ///         submit and so gain nothing from coalescing.
    ///     </para>
    /// </summary>
    internal sealed unsafe class VkTextureUpdateBatch : TextureUpdateBatch
    {
        private readonly VkGraphicsDevice gd;
        private readonly List<PendingCopy> pendingCopies = new List<PendingCopy>();
        private readonly Dictionary<SubresourceKey, VkImageLayout> touchedSubresources = new Dictionary<SubresourceKey, VkImageLayout>();

        private VkBuffer stagingBuffer;
        private uint stagingOffset;

        public VkTextureUpdateBatch(VkGraphicsDevice gd)
        {
            this.gd = gd;
            MarkOpen();
        }

        internal void Reopen()
        {
            Debug.Assert(stagingBuffer == null);
            Debug.Assert(pendingCopies.Count == 0);
            Debug.Assert(touchedSubresources.Count == 0);
            stagingOffset = 0;
            MarkOpen();
        }

        public override void Add(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x, uint y, uint z,
            uint width, uint height, uint depth,
            uint mipLevel, uint arrayLayer)
        {
            CheckOpen();

            var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
            bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;

            // Fast paths from VkGraphicsDevice.UpdateTextureCore that don't submit a command buffer
            // gain nothing from batching — fall through to the synchronous path.
            if (isStaging || gd.HasHostImageCopy)
            {
                gd.UpdateTexture(texture, source, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
                return;
            }

            // Compute alignment requirements. vkCmdCopyBufferToImage requires bufferOffset to be a multiple of
            // the texel block size and of optimalBufferCopyOffsetAlignment. For uncompressed formats the texel
            // block size is the pixel size in bytes; for compressed formats it's the block size in bytes.
            uint texelBlockSize = FormatHelpers.IsCompressedFormat(vkTex.Format)
                ? FormatHelpers.GetBlockSizeInBytes(vkTex.Format)
                : FormatSizeHelpers.GetSizeInBytes(vkTex.Format);
            uint copyAlignment = lcm((uint)gd.OptimalBufferCopyOffsetAlignment, texelBlockSize);
            stagingOffset = align(stagingOffset, copyAlignment);

            ensureStagingCapacity(stagingOffset + sizeInBytes);

            byte* dst = (byte*)stagingBuffer.Memory.BlockMappedPointer + stagingOffset;
            Unsafe.CopyBlock(dst, source.ToPointer(), sizeInBytes);

            uint blockSize = FormatHelpers.IsCompressedFormat(vkTex.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(width, blockSize);
            uint bufferImageHeight = Math.Max(height, blockSize);

            var aspect = (vkTex.Usage & TextureUsage.DepthStencil) != 0
                ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                : VkImageAspectFlags.Color;

            var region = new VkBufferImageCopy
            {
                bufferOffset = stagingOffset,
                bufferRowLength = bufferRowLength,
                bufferImageHeight = bufferImageHeight,
                imageOffset = new VkOffset3D { x = (int)x, y = (int)y, z = (int)z },
                imageExtent = new VkExtent3D { width = width, height = height, depth = depth },
                imageSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = aspect,
                    mipLevel = mipLevel,
                    baseArrayLayer = arrayLayer,
                    layerCount = 1
                }
            };

            stagingOffset += sizeInBytes;

            pendingCopies.Add(new PendingCopy
            {
                Texture = vkTex,
                Region = region,
                MipLevel = mipLevel,
                ArrayLayer = arrayLayer
            });

            // Track the original layout for each touched subresource exactly once so that on Submit we can
            // transition back to whatever it was before (typically ShaderReadOnlyOptimal for sampled textures).
            var key = new SubresourceKey(vkTex, mipLevel, arrayLayer);
            if (!touchedSubresources.ContainsKey(key))
                touchedSubresources.Add(key, vkTex.GetImageLayout(mipLevel, arrayLayer));
        }

        public override void Submit()
        {
            CheckOpen();

            if (pendingCopies.Count == 0) return;

            // Acquire one shared command pool for the whole batch.
            var pool = gd.GetFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();

            // Transition every touched subresource to TransferDstOptimal. We use the per-subresource transition
            // helper rather than batching a single vkCmdPipelineBarrier because VkTexture maintains per-subresource
            // layout state internally; this keeps that state consistent with no-batch UpdateTexture callers.
            foreach (var kvp in touchedSubresources)
            {
                var key = kvp.Key;
                key.Texture.TransitionImageLayout(cb, key.MipLevel, 1, key.ArrayLayer, 1, VkImageLayout.TransferDstOptimal);
            }

            // Issue all the buffer→image copies. We don't bother grouping by image because vkCmdCopyBufferToImage
            // already lets the driver schedule them freely and the per-call overhead of an already-recorded command
            // buffer is negligible compared to the cost of submission (which is what we're collapsing).
            foreach (var copy in pendingCopies)
            {
                var region = copy.Region;
                vkCmdCopyBufferToImage(
                    cb,
                    stagingBuffer.DeviceBuffer,
                    copy.Texture.OptimalDeviceImage,
                    VkImageLayout.TransferDstOptimal,
                    1,
                    ref region);
            }

            // Transition every touched subresource back to ShaderReadOnlyOptimal where appropriate, matching the
            // single-call UpdateTextureCore semantics. Subresources on textures that aren't sampled keep their
            // TransferDstOptimal layout (the existing code path does the same).
            foreach (var kvp in touchedSubresources)
            {
                var key = kvp.Key;
                if ((key.Texture.Usage & TextureUsage.Sampled) != 0)
                    key.Texture.TransitionImageLayout(cb, key.MipLevel, 1, key.ArrayLayer, 1, VkImageLayout.ShaderReadOnlyOptimal);
            }

            // Single vkQueueSubmit for the entire batch. Register the staging buffer for fence-completion recycling
            // via the existing submittedStagingBuffers mechanism; it will return to the pool once the fence signals.
            pool.EndAndSubmit(cb);
            gd.RegisterSubmittedStagingBuffer(cb, stagingBuffer);

            stagingBuffer = null;
            stagingOffset = 0;
            pendingCopies.Clear();
            touchedSubresources.Clear();
        }

        protected override void ReleaseToPool()
        {
            // If Dispose was called before Submit ever ran (or before any Add), make sure any rented staging buffer
            // is returned even though we have no command buffer to associate it with. The simplest correct path is
            // to just discard it: getFreeStagingBuffer can re-allocate. But we'd rather recycle if possible.
            if (stagingBuffer != null)
            {
                // No command buffer was submitted for this rent, so the buffer is safe to return immediately.
                gd.ReturnUnusedStagingBuffer(stagingBuffer);
                stagingBuffer = null;
            }

            stagingOffset = 0;
            pendingCopies.Clear();
            touchedSubresources.Clear();
            gd.ReturnTextureUpdateBatch(this);
        }

        private void ensureStagingCapacity(uint requiredSize)
        {
            if (stagingBuffer != null && stagingBuffer.SizeInBytes >= requiredSize) return;

            uint newSize = stagingBuffer == null
                ? requiredSize
                : Math.Max(stagingBuffer.SizeInBytes * 2, requiredSize);

            var newBuffer = gd.RentStagingBuffer(newSize);

            if (stagingBuffer != null)
            {
                // Copy already-staged bytes to the new (larger) buffer so previously-recorded VkBufferImageCopy
                // offsets remain valid. This path is only hit if Add() outgrows the initial rental mid-batch.
                Buffer.MemoryCopy(
                    stagingBuffer.Memory.BlockMappedPointer,
                    newBuffer.Memory.BlockMappedPointer,
                    newBuffer.SizeInBytes,
                    stagingOffset);
                gd.ReturnUnusedStagingBuffer(stagingBuffer);
            }

            stagingBuffer = newBuffer;
        }

        private static uint align(uint value, uint alignment)
        {
            if (alignment <= 1) return value;
            uint remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }

        private static uint lcm(uint a, uint b)
        {
            if (a == 0 || b == 0) return Math.Max(a, b);
            return a / gcd(a, b) * b;
        }

        private static uint gcd(uint a, uint b)
        {
            while (b != 0)
            {
                uint t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        private struct PendingCopy
        {
            public VkTexture Texture;
            public VkBufferImageCopy Region;
            public uint MipLevel;
            public uint ArrayLayer;
        }

        private readonly struct SubresourceKey : IEquatable<SubresourceKey>
        {
            public readonly VkTexture Texture;
            public readonly uint MipLevel;
            public readonly uint ArrayLayer;

            public SubresourceKey(VkTexture texture, uint mipLevel, uint arrayLayer)
            {
                Texture = texture;
                MipLevel = mipLevel;
                ArrayLayer = arrayLayer;
            }

            public bool Equals(SubresourceKey other) =>
                ReferenceEquals(Texture, other.Texture)
                && MipLevel == other.MipLevel
                && ArrayLayer == other.ArrayLayer;

            public override bool Equals(object obj) => obj is SubresourceKey o && Equals(o);

            public override int GetHashCode() =>
                HashCode.Combine(RuntimeHelpers.GetHashCode(Texture), MipLevel, ArrayLayer);
        }
    }
}
