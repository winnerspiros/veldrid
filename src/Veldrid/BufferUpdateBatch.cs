using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Veldrid
{
    /// <summary>
    ///     A scoped, single-threaded helper that coalesces many small
    ///     <see cref="GraphicsDevice.UpdateBuffer(DeviceBuffer, uint, IntPtr, uint)" /> calls into one
    ///     staging-buffer copy and one queue submission per <see cref="Submit" />. Mirrors
    ///     <see cref="TextureUpdateBatch" /> for the buffer-upload path.
    ///
    ///     <para>
    ///         On the Vulkan backend this collapses what would otherwise be one <c>vkQueueSubmit</c> per
    ///         <c>UpdateBuffer</c> call into a single submission, which on Android matters because each
    ///         submit contends with <c>vkQueuePresentKHR</c> on the graphics queue. On other backends the
    ///         default implementation simply forwards each <c>Add</c> to <c>UpdateBuffer</c>, so callers
    ///         can use this API portably without per-backend branching.
    ///     </para>
    ///
    ///     <para>
    ///         Updates against persistently-mapped buffers (e.g., dynamic constant buffers) bypass the
    ///         batch and apply immediately, since they don't submit and so gain nothing from coalescing.
    ///     </para>
    ///
    ///     <para>
    ///         Instances are <strong>not</strong> thread-safe. Use one from a single thread between
    ///         <see cref="GraphicsDevice.BeginBufferUpdateBatch" /> and disposal.
    ///     </para>
    /// </summary>
    public abstract class BufferUpdateBatch : IDisposable
    {
        private bool isOpen;
        private bool isDisposed;

        /// <summary>
        ///     <c>true</c> once <see cref="Dispose" /> has been called. Further calls to
        ///     <see cref="Add(DeviceBuffer, uint, IntPtr, uint)" /> or <see cref="Submit" /> will throw in DEBUG.
        /// </summary>
        public bool IsDisposed => isDisposed;

        /// <summary>
        ///     Adds a single buffer-region upload to the batch. Parameters mirror
        ///     <see cref="GraphicsDevice.UpdateBuffer(DeviceBuffer, uint, IntPtr, uint)" /> exactly. The
        ///     <paramref name="source" /> bytes are copied into staging memory before this method returns.
        /// </summary>
        public abstract void Add(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes);

        /// <summary>
        ///     Adds a single buffer-region upload from a <see cref="ReadOnlySpan{T}" />.
        /// </summary>
        public unsafe void Add<T>(DeviceBuffer buffer, uint bufferOffsetInBytes, ReadOnlySpan<T> source)
            where T : unmanaged
        {
            uint sizeInBytes = (uint)(sizeof(T) * source.Length);
            fixed (void* pin = &MemoryMarshal.GetReference(source))
            {
                Add(buffer, bufferOffsetInBytes, (IntPtr)pin, sizeInBytes);
            }
        }

        /// <summary>
        ///     Adds a single buffer-region upload from an array.
        /// </summary>
        public void Add<T>(DeviceBuffer buffer, uint bufferOffsetInBytes, T[] source) where T : unmanaged
        {
            Add(buffer, bufferOffsetInBytes, (ReadOnlySpan<T>)source);
        }

        /// <summary>
        ///     Adds a single value upload. Equivalent to <see cref="GraphicsDevice.UpdateBuffer{T}(DeviceBuffer, uint, ref T)" />.
        /// </summary>
        public unsafe void Add<T>(DeviceBuffer buffer, uint bufferOffsetInBytes, ref T source) where T : unmanaged
        {
            fixed (T* ptr = &source)
            {
                Add(buffer, bufferOffsetInBytes, (IntPtr)ptr, (uint)sizeof(T));
            }
        }

        /// <summary>
        ///     Flushes all pending uploads as a single GPU submission. Safe to call multiple times: subsequent
        ///     calls with no intervening <c>Add</c> are no-ops. The batch remains open for further additions.
        /// </summary>
        public abstract void Submit();

        /// <summary>
        ///     Flushes pending uploads (<see cref="Submit" />) and returns the batch to the device's free-list.
        ///     Idempotent.
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
        ///     Implementations override this to return themselves to the per-device pool.
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
        ///     Throws <see cref="ObjectDisposedException" /> if the batch has already been disposed.
        /// </summary>
        [Conditional("DEBUG")]
        protected void CheckOpen()
        {
            if (!isOpen) throw new ObjectDisposedException(nameof(BufferUpdateBatch));
        }
    }
}
