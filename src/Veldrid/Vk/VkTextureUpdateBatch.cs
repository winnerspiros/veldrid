using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
namespace Veldrid.Vk
{
    /// <summary>
    ///     Vulkan implementation of <see cref="TextureUpdateBatch" />: stages every pending region into a single
    ///     growable host-visible buffer, then on <see cref="Submit" /> issues the buffer→image copies in sequential
    ///     <c>gd.DeviceApi.vkQueueSubmit</c> calls capped at <c>MaxCopiesPerSubmit</c> copies each. Submitting very large batches
    ///     in one call can stall the calling thread for several seconds on some drivers, so the work is split across
    ///     multiple submissions while still collapsing the per-call <c>gd.DeviceApi.vkQueueSubmit</c> storm in
    ///     <see cref="VkGraphicsDevice.UpdateTextureCore" /> for callers that opt in.
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

        private VkBuffer stagingBuffer;
        private uint stagingOffset;

        // Reused across Submit() calls to track unique subresources touched per chunk.
        // Stored as a field to avoid a heap allocation on every Submit() call (the batch
        // object is pooled and reused across frames via ReturnTextureUpdateBatch/Reopen).
        private readonly HashSet<SubresourceKey> chunkSubresources = new HashSet<SubresourceKey>();

        public VkTextureUpdateBatch(VkGraphicsDevice gd)
        {
            this.gd = gd;
            MarkOpen();
        }

        internal void Reopen()
        {
            Debug.Assert(stagingBuffer == null);
            Debug.Assert(pendingCopies.Count == 0);
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

            // Compute alignment requirements. gd.DeviceApi.vkCmdCopyBufferToImage requires bufferOffset to be a multiple of
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

            // Use IsStencilFormat to decide whether to include the stencil aspect —
            // using Depth|Stencil for a depth-only format (e.g. D16_UNorm, D32_Float)
            // is a Vulkan spec violation and triggers validation errors.
            var aspect = (vkTex.Usage & TextureUsage.DepthStencil) != 0
                ? FormatHelpers.IsStencilFormat(vkTex.Format)
                    ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil
                    : VkImageAspectFlags.Depth
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
        }

        // Maximum number of texture copies to include in a single gd.DeviceApi.vkQueueSubmit. Submitting very large batches
        // in one call can block the calling thread for several seconds on some drivers (e.g. Adreno 740
        // driver 512.676.73), causing watchdog timeouts. Splitting into smaller submissions keeps the
        // GPU pipeline fed while bounding per-submit latency.
        private const int MaxCopiesPerSubmit = 64;

        public override void Submit()
        {
            CheckOpen();

            if (pendingCopies.Count == 0) return;

            int total = pendingCopies.Count;

            for (int start = 0; start < total; start += MaxCopiesPerSubmit)
            {
                int end = Math.Min(start + MaxCopiesPerSubmit, total);
                bool isLastChunk = end == total;

                // Collect distinct subresources touched by this chunk.
                chunkSubresources.Clear();
                for (int i = start; i < end; i++)
                {
                    var c = pendingCopies[i];
                    chunkSubresources.Add(new SubresourceKey(c.Texture, c.MipLevel, c.ArrayLayer));
                }

                var pool = gd.GetFreeCommandPool();
                var cb = pool.BeginNewCommandBuffer();

                // Transition each subresource in this chunk to TransferDstOptimal. VkTexture maintains
                // per-subresource layout state internally; TransitionImageLayout reads that state, so
                // subresources that appear in a later chunk will be transitioned from whatever layout the
                // previous chunk left them in (ShaderReadOnlyOptimal for sampled, TransferDstOptimal otherwise).
                foreach (var key in chunkSubresources)
                    key.Texture.TransitionImageLayout(cb, key.MipLevel, 1, key.ArrayLayer, 1, VkImageLayout.TransferDstOptimal);

                // Issue all buffer→image copies for this chunk.
                for (int i = start; i < end; i++)
                {
                    var copy = pendingCopies[i];
                    var region = copy.Region;
                    gd.DeviceApi.vkCmdCopyBufferToImage(
                        cb,
                        stagingBuffer.DeviceBuffer,
                        copy.Texture.OptimalDeviceImage,
                        VkImageLayout.TransferDstOptimal,
                        1,
                        &region);
                }

                // Transition sampled subresources back to ShaderReadOnlyOptimal, matching the single-call
                // UpdateTextureCore semantics. Non-sampled subresources stay in TransferDstOptimal.
                foreach (var key in chunkSubresources)
                {
                    if ((key.Texture.Usage & TextureUsage.Sampled) != 0)
                        key.Texture.TransitionImageLayout(cb, key.MipLevel, 1, key.ArrayLayer, 1, VkImageLayout.ShaderReadOnlyOptimal);
                }

                pool.EndAndSubmit(cb);

                // Register the staging buffer only with the last command buffer. All chunks share the same
                // staging buffer (copies reference offsets within it), so it must not be recycled until every
                // chunk has completed. Vulkan queue ordering guarantees that later submissions on the same
                // queue complete after earlier ones, so the last fence signals only after all chunks finish.
                if (isLastChunk)
                    gd.RegisterSubmittedStagingBuffer(cb, stagingBuffer);
            }

            stagingBuffer = null;
            stagingOffset = 0;
            pendingCopies.Clear();
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
