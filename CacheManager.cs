using KVPSButter;
using Serilog;

namespace UpdaterMirror;

/// <summary>
/// Manager for handling the cache
/// </summary>
public class CacheManager : IDisposable
{
    /// <summary>
    /// The current size of the cache
    /// </summary>
    private long m_currentSize;

    /// <summary>
    /// The number of items in the cache that are not found
    /// </summary>
    private long m_notFoundCount;
    
    /// <summary>
    /// The maximum size of the cache
    /// </summary>
    private readonly long m_maxSize;

    /// <summary>
    /// The maximum number of not-found entries
    /// </summary>
    private readonly int m_maxNotFound;

    /// <summary>
    /// The time items are stored in cache
    /// </summary>
    private readonly TimeSpan m_validityPeriod;

    /// <summary>
    /// The duration to wait for additional events after being triggered
    /// </summary>
    public static readonly TimeSpan ExpireTriggerJitter = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The list of all entries managed by the cache manager
    /// </summary>
    private Dictionary<string, RemoteAccessItem> m_items = new();

    /// <summary>
    /// The lock guarding the list of items
    /// </summary>
    private readonly object m_lock = new object();

    /// <summary>
    /// The remote store
    /// </summary>
    public IKVPS Store { get; }

    /// <summary>
    /// The path where cache items are created
    /// </summary>
    public readonly string CachePath;

    /// <summary>
    /// Flag indicating if we are disposed
    /// </summary>
    private bool m_disposed = false;

    /// <summary>
    /// Signal mecahnism for triggering expiration
    /// </summary>
    private TaskCompletionSource<bool> m_limitSignal = new();

    /// <summary>
    /// Creates a new <see cref="CacheManager"/> instance
    /// </summary>
    /// <param name="storage">KVPS connection string</param>
    /// <param name="cachePath">The path to the cache</param>
    /// <param name="maxNotFound">The maximum number of not-found items to track</param>
    /// <param name="maxSize">The maximum size of cached data</param>
    /// <param name="validityPeriod">The duration a cached entry is valid for</param>
    public CacheManager(string storage, string cachePath, int maxNotFound, long maxSize, TimeSpan validityPeriod)
        : this(KVPSLoader.Default.Create(storage), cachePath, maxNotFound, maxSize, validityPeriod)
    {}

    /// <summary>
    /// Creates a new <see cref="CacheManager"/> instance
    /// </summary>
    /// <param name="store">The remote store to use</param>
    /// <param name="cachePath">The path to the cache</param>
    /// <param name="maxNotFound">The maximum number of not-found items to track</param>
    /// <param name="maxSize">The maximum size of cached data</param>
    /// <param name="validityPeriod">The duration a cached entry is valid for</param>
    public CacheManager(IKVPS store, string cachePath, int maxNotFound, long maxSize, TimeSpan validityPeriod)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        CachePath = cachePath;
        m_maxNotFound = Math.Max(10, maxNotFound);
        m_maxSize = Math.Max(1024 * 1024 * 5, maxSize);
        m_validityPeriod = TimeSpan.FromTicks(Math.Max(TimeSpan.FromHours(1).Ticks, validityPeriod.Ticks));

        if (!Directory.Exists(cachePath))
            Directory.CreateDirectory(cachePath);

        Task.Run(async () => {
            // Use the validity period to check expiration
            // Add 1 second to reduce items being just outside the timespan
            var checkInterval = (validityPeriod / 2)
                    .Add(TimeSpan.FromSeconds(1));

            while(!m_disposed) {
                if (await Task.WhenAny(Task.Delay(checkInterval), m_limitSignal.Task) == m_limitSignal.Task)
                    Interlocked.Exchange(ref m_limitSignal, new TaskCompletionSource<bool>());

                try 
                { 
                    EnforceLimits(); 
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed while enforcing limits");
                }
            }
        });
    }

    /// <summary>
    /// Gets a remote access item
    /// </summary>
    /// <param name="key">The path to the item</param>
    /// <returns>The item</returns>
    public RemoteAccessItem Get(string key)
    {
        if (key.StartsWith("/"))
            key = key[1..];
        RemoteAccessItem? res;
        lock(m_lock)
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(CacheManager));

            res = m_items.GetValueOrDefault(key);
            if (res == null)
                res = m_items[key] = new RemoteAccessItem(this, key, DateTime.UtcNow + m_validityPeriod);
        }

        res.UpdateLastAccessed();
        
        // TODO: Serving a stale entry
        if (res.ExpiresOn < DateTime.UtcNow)
            TriggerLimitCheck();

        return res;
    }

    /// <summary>
    /// Reports an item downloaded
    /// </summary>
    /// <param name="item">The item that was downloaded</param>
    public void ReportCompleted(RemoteAccessItem item)
    {
        lock(m_lock)
        {
            if (m_disposed)
                return;
            m_currentSize += item.AvailableLength;
        }

        if (m_currentSize > m_maxSize)
            TriggerLimitCheck();
    }

    /// <summary>
    /// Reports an item not found
    /// </summary>
    /// <param name="item">The item that was not found</param>
    public void ReportNotFound(RemoteAccessItem item)
    {
        lock(m_lock)
        {
            if (m_disposed)
                return;
            m_notFoundCount++;
        }

        if (m_notFoundCount > m_maxNotFound)
            TriggerLimitCheck();
    }

    /// <summary>
    /// Report an item as expired
    /// </summary>
    /// <param name="item">The item that was expired</param>
    /// <param name="prevState">The previous state</param>
    public void ReportExpired(RemoteAccessItem item, RemoteAccessItem.AccessState prevState)
    {
        lock(m_lock)
        {
            if (m_disposed)
                return;

            if (prevState == RemoteAccessItem.AccessState.NotFound)
                m_notFoundCount--;
            else if (prevState == RemoteAccessItem.AccessState.Downloaded)
                m_currentSize -= item.AvailableLength;
        }
    }

    /// <summary>
    /// Triggers the limit check
    /// </summary>
    public async void TriggerLimitCheck()
    {
        // Capture signal at this instance
        var tcs = m_limitSignal;
        // Wait for races on the signal
        await Task.Delay(ExpireTriggerJitter);
        // Signal whatever was the signal instance when we started
        tcs.TrySetResult(true);
    }

    /// <summary>
    /// Removes all items that are expired or exceeds set limits
    /// </summary>
    private void EnforceLimits()
    {        
        var toExpire = new List<RemoteAccessItem>();
        var now = DateTime.UtcNow;        
        lock(m_lock)
        {
            if (m_disposed)
            {
                toExpire.AddRange(m_items.Values);
                m_items.Clear();
            }
            else
            {
                // Do LRU-caching-style cleanup
                var sorted = m_items.OrderByDescending(x => x.Value.LastAccessed).ToList();
                
                // Get items that are not found, and in excess of allowed count
                // Use a slightly smaller value when clearing to avoid repeated over/under scenarios
                var notFoundThreshold = m_maxNotFound - Math.Max(10, m_maxNotFound / 10);
                var expired = sorted.Where(x => x.Value.State == RemoteAccessItem.AccessState.NotFound)
                    .Skip(notFoundThreshold)
                    .Select(x => x.Key)
                    .ToHashSet();
                
                // Remove items after threshold has been exceeded
                // Use a slightly smaller value when clearing to avoid repeated over/under scenarios
                var size = 0L;
                var sizeThreshold = m_maxSize - (m_maxSize / 10);
                foreach(var p in sorted.Where(x => x.Value.State == RemoteAccessItem.AccessState.Downloaded))
                {
                    size += p.Value.AvailableLength;
                    if (size > sizeThreshold)
                        expired.Add(p.Key);
                }

                // Add everything that has expired
                expired.UnionWith(
                    m_items.Where(x => x.Value.State == RemoteAccessItem.AccessState.Expired || x.Value.ExpiresOn < now)
                    .Select(x => x.Key)
                );

                // Remove and record unique items
                foreach(var x in expired)
                {
                    toExpire.Add(m_items[x]);
                    m_items.Remove(x);
                }
            }
        }

        // Without a lock, expire everything that is removed from the lookup table
        foreach(var x in toExpire)
            x.Expire();
    }

    /// <summary>
    /// Force expire select items
    /// </summary>
    /// <param name="keys">The keys to expire</param>
    public void ForceExpire(HashSet<string> keys)
    {
        var toExpire = new List<RemoteAccessItem>();
        lock(m_lock)
        {
            foreach(var k in keys)
            {
                var e = m_items.GetValueOrDefault(k);
                if (e != null)
                {
                    m_items.Remove(k);
                    toExpire.Add(e);
                }
            }
        }

        // Expire all items, without a lock
        foreach(var x in toExpire)
            x.Expire();

    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock(m_lock)
        {
            if (m_disposed)
                return;

            m_disposed = true;
        }

        m_limitSignal.TrySetResult(true);
    }
}
