using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1.Utilities
{
    /// <summary>
    /// Utility class for writing binary files in parallel with .NET Framework 4.8 compatibility.
    /// Implementation opens a dedicated asynchronous FileStream per write chunk to allow
    /// overlapped positional writes on Windows. The file is preallocated to avoid fragmentation.
    /// </summary>
    public static class BinaryParallelWriter
    {
        /// <summary>
        /// Write a byte array to disk using parallel chunked writes.
        /// Opens one async FileStream per chunk; throttles concurrency with a SemaphoreSlim.
        /// Designed for .NET Framework 4.8 (no RandomAccess / ForEachAsync).
        /// Note: array size limit: 2,147,483,647
        /// </summary>
        /// <param name="path">Target file path.</param>
        /// <param name="data">Source data to write (byte[] length must fit in int).</param>
        /// <param name="dataLength">length of data array, if 0, actual length of data array will be used</param>
        /// <param name="chunkSize">Chunk size in bytes. Default 4 MiB.</param>
        /// <param name="maxDegreeOfParallelism">Max concurrent writers. Default = Environment.ProcessorCount.</param>
        /// <param name="append">If true, data is appended to the existing file (created if missing).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task WriteAsync(
            string path,
            byte[] data,
            long dataLength = 0,
            int chunkSize = 4 * 1024 * 1024,
            int maxDegreeOfParallelism = 0,
            bool append = false,
            CancellationToken cancellationToken = default)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
            if (data.LongLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(data), "data length must fit in an Int32");

            if (maxDegreeOfParallelism <= 0) maxDegreeOfParallelism = Environment.ProcessorCount;

            if (dataLength == 0) dataLength = data.LongLength;
            string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            Directory.CreateDirectory(directory);

            // Determine base offset for writes (0 for fresh write, existing length for append).
            long baseOffset = 0;
            if (append && File.Exists(path))
            {
                var fi = new FileInfo(path);
                baseOffset = fi.Length;
            }

            long totalLength = baseOffset + dataLength;

            // Preallocate file synchronously to the final size to avoid fragmentation.
            // Use OpenOrCreate when appending to preserve existing content.
            var preMode = append ? FileMode.OpenOrCreate : FileMode.Create;
            using (var pre = new FileStream(path, preMode, FileAccess.Write, FileShare.Read, 4096, FileOptions.None))
            {
                pre.SetLength(totalLength);
            }

            // Number of chunks is based on data being written (not the total file length).
            int chunkCount = (int)((dataLength + chunkSize - 1) / chunkSize);
            var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
            var tasks = new List<Task>(chunkCount);

            for (int i = 0; i < chunkCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long chunkDataOffset = (long)i * chunkSize;                 // offset inside `data`
                long fileOffset = baseOffset + chunkDataOffset;             // absolute file offset
                int currentChunkSize = (int)Math.Min(chunkSize, dataLength - chunkDataOffset);
                int bufferOffset = (int)chunkDataOffset; // safe because dataLength <= int.MaxValue

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                var t = Task.Run(async () =>
                {
                    try
                    {
                        // Open a dedicated async FileStream for this chunk so Seek + WriteAsync
                        // operate on an independent handle (allows overlapped writes).
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous))
                        {
                            fs.Seek(fileOffset, SeekOrigin.Begin);
                            await fs.WriteAsync(data, bufferOffset, currentChunkSize, cancellationToken).ConfigureAwait(false);
                            // Optionally call fs.FlushAsync(cancellationToken) if durability required.
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(t);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /////// <summary>
        /////// Write a set of (offset, buffer) pairs to file in parallel.
        /////// Each buffer is written at its specified file offset.
        /////// Uses per-buffer async FileStream instances to enable overlapped positional writes.
        /////// </summary>
        /////// <param name="path">Target file path.</param>
        /////// <param name="buffersWithOffset">Sequence of (offset, buffer) tuples.</param>
        /////// <param name="totalLength">
        /////// Optional total file length to preallocate. If 0, the file is preallocated to the maximum offset+length.
        /////// </param>
        /////// <param name="maxDegreeOfParallelism">Max concurrent writers.</param>
        /////// <param name="cancellationToken">Cancellation token.</param>
        ////public static async Task WriteAsync(
        ////    string path,
        ////    IEnumerable<(long Offset, byte[] Buffer)> buffersWithOffset,
        ////    long totalLength = 0,
        ////    int maxDegreeOfParallelism = 0,
        ////    CancellationToken cancellationToken = default)
        ////{
        ////    if (path == null) throw new ArgumentNullException(nameof(path));
        ////    if (buffersWithOffset == null) throw new ArgumentNullException(nameof(buffersWithOffset));

        ////    var buffers = buffersWithOffset.ToList();
        ////    long computedLength = totalLength;

        ////    foreach (var item in buffers)
        ////    {
        ////        if (item.Buffer == null) throw new ArgumentNullException(nameof(buffersWithOffset), "One of the buffers is null.");
        ////        long end = item.Offset + item.Buffer.LongLength;
        ////        if (end > computedLength) computedLength = end;
        ////    }

        ////    if (computedLength < 0) computedLength = 0;
        ////    if (maxDegreeOfParallelism <= 0) maxDegreeOfParallelism = Environment.ProcessorCount;

        ////    string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        ////    Directory.CreateDirectory(directory);

        ////    // Preallocate
        ////    using (var pre = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.None))
        ////    {
        ////        pre.SetLength(computedLength);
        ////    }

        ////    var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);
        ////    var tasks = new List<Task>(buffers.Count);

        ////    foreach (var item in buffers)
        ////    {
        ////        cancellationToken.ThrowIfCancellationRequested();

        ////        if (item.Buffer.Length == 0) continue;

        ////        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        ////        var localItem = item;
        ////        var t = Task.Run(async () =>
        ////        {
        ////            try
        ////            {
        ////                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.Asynchronous))
        ////                {
        ////                    fs.Seek(localItem.Offset, SeekOrigin.Begin);
        ////                    await fs.WriteAsync(localItem.Buffer, 0, localItem.Buffer.Length, cancellationToken).ConfigureAwait(false);
        ////                }
        ////            }
        ////            finally
        ////            {
        ////                semaphore.Release();
        ////            }
        ////        }, cancellationToken);

        ////        tasks.Add(t);
        ////    }

        ////    await Task.WhenAll(tasks).ConfigureAwait(false);
        ////}
    }
}