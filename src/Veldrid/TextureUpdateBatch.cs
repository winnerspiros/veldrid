using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Veldrid
{
    /// <summary>
    ///     A scoped, single-threaded helper that coalesces many small
    ///     <see cref="GraphicsDevice.UpdateTexture(Texture, IntPtr, uint, uint, uint, uint, uint, uint, uint, uint, uint)" />
    ///     calls into one staging-buffer copy and one queue submission per <see cref="Submit" />.
    ///
    ///     <para>
    ///         Acquire one with <see cref="GraphicsDevice.BeginTextureUpdateBatch" /> at the start of a per-frame
    ///         upload section, call <see cref="Add(Texture, IntPtr, uint, uint, uint, uint, uint, uint, uint, uint, uint)" />
    ///         once per region, then dispose it (a <c>using</c> statement is the idiomatic shape) — disposing flushes
    ///         any pending uploads and returns the batch object to the device's free-list, so per-frame use allocates
    ///         no managed garbage after warmup. <see cref="Submit" /> may also be called explicitly to flush mid-frame
    ///         while keeping the batch open for further additions.
    ///     </para>
    ///
    ///     <para>
    ///         Instances are <strong>not</strong> thread-safe and must be used from a single thread between
    ///         acquisition and disposal, the same way <see cref="CommandList" /> is used.
    ///     </para>
    /// </summary>
    public abstract class TextureUpdateBatch : IDisposable
    {
        private bool isOpen;
        private bool isDisposed;

        /// <summary>
        ///     <c>true</c> once <see cref="Dispose" /> has been called and the batch has been returned to its pool.
        ///     Further calls to <see cref="Add(Texture, IntPtr, uint, uint, uint, uint, uint, uint, uint, uint, uint)" />
        ///     or <see cref="Submit" /> will throw.
        /// </summary>
        public bool IsDisposed => isDisposed;

        /// <summary>
        ///     Adds a single texture region upload to the batch. Parameters mirror
        ///     <see cref="GraphicsDevice.UpdateTexture(Texture, IntPtr, uint, uint, uint, uint, uint, uint, uint, uint, uint)" />
        ///     exactly. The data at <paramref name="source" /> is copied into the batch's staging memory before this
        ///     method returns, so the caller may free or reuse the source buffer immediately afterwards.
        /// </summary>
        public abstract void Add(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x, uint y, uint z,
            uint width, uint height, uint depth,
            uint mipLevel, uint arrayLayer);

        /// <summary>
        ///     Adds a single texture region upload to the batch from a <see cref="ReadOnlySpan{T}" />. See
        ///     <see cref="Add(Texture, IntPtr, uint, uint, uint, uint, uint, uint, uint, uint, uint)" /> for semantics.
        /// </summary>
        public unsafe void Add<T>(
            Texture texture,
            ReadOnlySpan<T> source,
            uint x, uint y, uint z,
            uint width, uint height, uint depth,
            uint mipLevel, uint arrayLayer) where T : unmanaged
        {
            uint sizeInBytes = (uint)(sizeof(T) * source.Length);
            fixed (void* pin = &MemoryMarshal.GetReference(source))
            {
                Add(texture, (IntPtr)pin, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
            }
        }

        /// <summary>
        ///     Adds a single texture region upload to the batch from an array. See
        ///     <see cref="Add(Texture, IntPtr, uint, uint, uint, uint, uint, uint, uint, uint, uint)" /> for semantics.
        /// </summary>
        public void Add<T>(
            Texture texture,
            T[] source,
            uint x, uint y, uint z,
            uint width, uint height, uint depth,
            uint mipLevel, uint arrayLayer) where T : unmanaged
        {
            Add(texture, (ReadOnlySpan<T>)source, x, y, z, width, height, depth, mipLevel, arrayLayer);
        }

        /// <summary>
        ///     Flushes all pending region uploads as a single GPU submission. Safe to call multiple times: subsequent
        ///     calls with no intervening <c>Add</c> are no-ops. The batch remains open for further additions.
        /// </summary>
        public abstract void Submit();

        /// <summary>
        ///     Flushes any pending uploads (<see cref="Submit" />) and returns the batch object to the device's
        ///     free-list. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed) return;
            try { Submit(); }
            finally
            {
                isDisposed = true;
                isOpen = false;
                ReleaseToPool();
            }
        }

        /// <summary>
        ///     Implementations override this to return themselves to whichever per-device pool they were acquired from.
        /// </summary>
        protected abstract void ReleaseToPool();

        /// <summary>
        ///     Implementations call this from their pool-acquire path to reset the open/disposed flags for reuse.
        /// </summary>
        protected void MarkOpen()
        {
            isOpen = true;
            isDisposed = false;
        }

        /// <summary>
        ///     Throws <see cref="ObjectDisposedException" /> if the batch has already been disposed. Cheap; called from
        ///     every public entry point.
        /// </summary>
        [Conditional("DEBUG")]
        protected void CheckOpen()
        {
            if (!isOpen) throw new ObjectDisposedException(nameof(TextureUpdateBatch));
        }
    }
}
