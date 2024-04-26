using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace UpdaterMirror;

/// <summary>
/// Provider wrapping a <see cref="CacheManager"/> in a <see cref="IFileProvider"/>
/// </summary>
public class RemoteAccessFileProvider : IFileProvider
{
    /// <summary>
    /// The cache manager being wrapped
    /// </summary>
    private readonly CacheManager m_manager;

    /// <summary>
    /// Constructs a new <see cref="RemoteAccessFileProvider"/>
    /// </summary>
    /// <param name="manager">The manager to wrap</param>
    public RemoteAccessFileProvider(CacheManager manager)
    {
        m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>
    /// Default implementation of directory
    /// </summary>
    /// <param name="subpath">Unused argument</param>
    /// <returns>A <see cref="NotFoundDirectoryContents"/> instance</returns>
    public IDirectoryContents GetDirectoryContents(string subpath)
        => new NotFoundDirectoryContents();

    /// <summary>
    /// Returns a <see cref="IFileInfo"/> that wraps a <see cref="RemoteAccessItem"/>
    /// </summary>
    /// <param name="subpath">The path to use</param>
    /// <returns>The wrapping <see cref="IFileInfo"/> instance</returns>
    public IFileInfo GetFileInfo(string subpath)
        => subpath == null || subpath.EndsWith("/")
            ? new NotFoundFileInfo(subpath ?? "")
            : new WrappedRemoteAccess(m_manager.Get(subpath));

    /// <summary>
    /// Default implementation of the change token
    /// </summary>
    /// <param name="filter">Unused argument</param>
    /// <returns>The <see cref="NullChangeToken"/> singleton instance</returns>
    public IChangeToken Watch(string filter)
        => NullChangeToken.Singleton;

    /// <summary>
    /// Wrapping <see cref="RemoteAccessItem"/> in <see cref="IFileInfo"/>
    /// </summary>
    /// <param name="Item"></param>
    private record WrappedRemoteAccess(RemoteAccessItem Item) : IFileInfo
    {
        /// <inheritdoc/>
        public bool Exists => Item.Exists().Result;

        /// <inheritdoc/>
        public bool IsDirectory => false;

        /// <inheritdoc/>
        public DateTimeOffset LastModified => Item.LastModified;

        /// <inheritdoc/>
        public long Length => Item.FullLength;

        /// <inheritdoc/>
        public string Name => Item.Key;

        /// <inheritdoc/>
        public string? PhysicalPath => Item.GetLocalPathIfDownloaded();

        /// <inheritdoc/>
        public Stream CreateReadStream()
        {
            if (!Exists)
                throw new FileNotFoundException();

            var t = Item.Download();
            var fs = Item.GetLocalFileStream();

            // If the download is complete, just give access to the cached file
            if (t.IsCompleted)
                return fs;

            return new WrappedStream(Item, t, fs);
        }
    }

    /// <summary>
    /// Stream implementation for an in-progres downloaded file
    /// </summary>
    private class WrappedStream(RemoteAccessItem Item, Task Downloaded, FileStream Local) : Stream
    {
        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => Item.FullLength;

        /// <inheritdoc/>
        public override long Position { get => Local.Position; set => throw new InvalidOperationException(); }

        /// <inheritdoc/>
        public override void Flush() => throw new InvalidOperationException();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            while (Local.Position >= Local.Length && !Downloaded.IsCompleted)
            {
                // Wait for some data to be available
                var _ = Item.NextAvailable!.Result;
            }

            return Local.Read(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (Local.Position >= Local.Length && !Downloaded.IsCompleted && !cancellationToken.IsCancellationRequested)
                await Item.NextAvailable!;

            cancellationToken.ThrowIfCancellationRequested();

            return await Local.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (Local.Position >= Local.Length && !Downloaded.IsCompleted && !cancellationToken.IsCancellationRequested)
                await Item.NextAvailable!;

            cancellationToken.ThrowIfCancellationRequested();

            return await Local.ReadAsync(buffer, cancellationToken);
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
            => throw new InvalidOperationException();

        /// <inheritdoc/>
        public override void SetLength(long value)
            => throw new InvalidOperationException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException();
    }
}
