using Serilog;

namespace UpdaterMirror;

/// <summary>
/// Encapsulates a single item that can be downloaded from a remote source
/// </summary>
public class RemoteAccessItem
{
    /// <summary>
    /// The state machine for this item
    /// </summary>
    public enum AccessState
    {
        /// <summary>
        /// The item is created, nothing is known
        /// </summary>
        Created,
        /// <summary>
        /// Currently querying the remote store for info
        /// </summary>
        Querying,
        /// <summary>
        /// The item does not exist
        /// </summary>
        NotFound,
        /// <summary>
        /// The item exists
        /// </summary>
        Found,
        /// <summary>
        /// The item is currently being downloaded
        /// </summary>
        Active,
        /// <summary>
        /// The item has been fully downloaded
        /// </summary>
        Downloaded,
        /// <summary>
        /// The item has expired
        /// </summary>
        Expired
    }

    /// <summary>
    /// The assigned time this entry expires
    /// </summary>
    public DateTime ExpiresOn { get; private set; }
    /// <summary>
    /// The time the entry was last accessed
    /// </summary>
    public DateTime LastAccessed { get; private set; }
    /// <summary>
    /// The current state
    /// </summary>
    public AccessState State { get; private set; }

    /// <summary>
    /// The length of the full file, as reported by the remote store
    /// </summary>
    public long FullLength { get; private set; }
    /// <summary>
    /// The time the file was last modified, as reported by the remote store
    /// </summary>
    public DateTime LastModified { get; private set; }

    /// <summary>
    /// Gets a value indicating if this entry does not expire
    /// </summary>
    public bool NeverExpires => m_manager.KeepForeverRegex != null && m_manager.KeepForeverRegex.IsMatch(Key);

    /// <summary>
    /// Gets a value indicating if this entry should expire given the current time
    /// </summary>
    public bool ShouldExpireNow() => !NeverExpires && ExpiresOn < DateTime.UtcNow;

    /// <summary>
    /// A task that can be awaited for new bytes
    /// </summary>
    private Task<long>? m_availableLength;
    /// <summary>
    /// A task that can be awaited for download completion
    /// </summary>
    private Task<bool>? m_downloaded;

    /// <summary>
    /// The currently available bytes in the local cache file
    /// </summary>
    public long AvailableLength { get; private set; }

    /// <summary>
    /// An awaitable task that signals new bytes arrived
    /// </summary>
    public Task<long>? NextAvailable => m_availableLength;

    /// <summary>
    /// The object key on the remote storage
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The lock guarding private fields
    /// </summary>
    private readonly object m_lock = new object();

    /// <summary>
    /// The query check task
    /// </summary>
    private Task<bool>? m_exists;

    /// <summary>
    /// The local path with cached data, if available
    /// </summary>
    public string? LocalPath { get; private set; }

    /// <summary>
    /// The parent manager
    /// </summary>
    private readonly CacheManager m_manager;

    /// <summary>
    /// Constructs an instance for a remote cached item
    /// </summary>
    /// <param name="manager">The parent manager</param>
    /// <param name="key">The file key</param>
    /// <param name="expires">The time when the item expires</param>
    public RemoteAccessItem(CacheManager manager, string key, DateTime expires)
    {
        m_manager = manager;
        Key = key;
        State = AccessState.Created;
        ExpiresOn = expires;
    }

    /// <summary>
    /// Updates the last-accessed flag
    /// </summary>
    public void UpdateLastAccessed()
    {
        LastAccessed = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if the remote file exists
    /// </summary>
    /// <returns><c>true</c> if the file exists; <c>false</c> otherwise</returns>
    public Task<bool> Exists()
    {
        TaskCompletionSource<bool>? tcs = null;
        lock (m_lock)
        {
            // If we are all but brand-new, there is already a task
            if (State != AccessState.Created)
                return m_exists!;

            // This thread was first in, so make it handle the query
            State = AccessState.Querying;
            tcs = new TaskCompletionSource<bool>();
            m_exists = tcs.Task;
        }

        // Get the info
        Task.Run(async () =>
        {
            // Assume failure
            var state = AccessState.NotFound;

            try
            {
                var res = await m_manager.Store.GetInfoAsync(Key);
                // We can only work if we know the length of the remote item
                if (res != null && res.Length.HasValue)
                {
                    state = AccessState.Found;
                    FullLength = res.Length.Value;
                    LastModified = res.LastModified ?? DateTime.UnixEpoch;
                }
            }
            catch (Exception ex)
            {
                // Don't log 404s, they are expected
                if (ex is Amazon.S3.AmazonS3Exception s3ex && s3ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    state = AccessState.NotFound;
                else
                    Log.Warning(ex, $"Error when reading info for key {Key}");
            }

            // Update and signal completion
            lock (m_lock)
            {
                State = state;
                tcs.SetResult(state == AccessState.Found);
            }
        });

        return m_exists;
    }

    /// <summary>
    /// Gets the path on disk, if available
    /// </summary>
    /// <returns>The local path or null</returns>
    public string? GetLocalPathIfDownloaded()
    {
        lock (m_lock)
            return State == AccessState.Downloaded
                ? LocalPath
                : null;
    }

    /// <summary>
    /// Gets the local filestream, if possible
    /// </summary>
    /// <returns>The filestream</returns>
    public FileStream GetLocalFileStream()
    {
        lock (m_lock)
            if (State == AccessState.Active || State == AccessState.Downloaded)
                return new FileStream(LocalPath!, FileMode.Open, FileAccess.Read, FileShare.Read);

        throw new InvalidOperationException();
    }

    /// <summary>
    /// Downloads the remote item if required
    /// </summary>
    /// <returns><c>true</c> if the file was downloaded; <c>false</c> otherwise</returns>
    public Task<bool> Download()
    {
        // Ensure we have the correct state, and stop if this is an invalid request
        if (!Exists().Result)
            throw new FileNotFoundException();

        // Prepare for reporting
        TaskCompletionSource<bool>? tcs = null;
        TaskCompletionSource<long>? pgtcs = null;
        string? tempFile = null;
        FileStream? fs = null;

        lock (m_lock)
        {
            // If this is the first thread in here, set up progress tasks
            // All states prior to Found are handled by the query
            if (State == AccessState.Found)
            {
                tempFile = Path.Combine(m_manager.CachePath, $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}");
                fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read);

                State = AccessState.Active;
                tcs = new TaskCompletionSource<bool>();
                pgtcs = new TaskCompletionSource<long>();
                m_downloaded = tcs.Task;
                m_availableLength = pgtcs.Task;
                LocalPath = tempFile;

            }
        }

        // If this was not the first thread, use the first threads reporting
        if (tcs == null || pgtcs == null)
            return m_downloaded!;

        // Run this detached from the caller
        Task.Run(() =>
        {
            try
            {
                // Get stream
                using var stream = m_manager.Store.ReadAsync(Key).Result;
                if (stream == null)
                    throw new FileNotFoundException();

                // Make a reasonable sized buffer
                var buffer = new byte[1024 * 8];
                while (true)
                {
                    var r = stream.Read(buffer, 0, buffer.Length);

                    // Standard .Net completion signal
                    if (r == 0)
                        break;

                    // Write to local cache file
                    fs!.Write(buffer, 0, r);
                    fs.Flush();

                    // Update the value
                    AvailableLength = fs.Length;

                    // Prepare a new progress task, and report current progress in the previous task
                    var prevpg = pgtcs;
                    pgtcs = new TaskCompletionSource<long>();
                    Interlocked.Exchange(ref m_availableLength, pgtcs.Task);
                    prevpg.SetResult(fs.Length);
                }

                // Completed, update states
                lock (m_lock)
                {
                    State = AccessState.Downloaded;
                    tcs.SetResult(true);
                    pgtcs.SetResult(fs!.Length);
                }

                // Do not wipe the local file
                tempFile = null;

                // Update the size metric, no locks held
                m_manager.ReportCompleted(this);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Failed to download key: {Key}");
            }
            finally
            {
                try { fs?.Dispose(); }
                catch { }

                // Delete, unless all went well
                if (tempFile != null)
                    try { File.Delete(tempFile); }
                    catch { }

                lock (m_lock)
                {
                    // If we did not complete, reset everything
                    if (State != AccessState.Downloaded)
                    {
                        State = AccessState.Created;
                        tcs.TrySetResult(false);
                        pgtcs.TrySetCanceled();
                        LocalPath = null;
                    }
                }
            }
        });

        // Return the task for awaiting
        return m_downloaded!;
    }

    /// <summary>
    /// Handler for expiring item
    /// </summary>
    public void Expire()
    {
        // Grab and update
        AccessState prevState;
        lock (m_lock)
        {
            prevState = State;
            State = AccessState.Expired;
        }

        // Call manager without any locks held
        if (prevState != AccessState.Expired)
            m_manager.ReportExpired(this, prevState);

        if (LocalPath != null)
            try { File.Delete(LocalPath!); }
            catch { }
    }
}
