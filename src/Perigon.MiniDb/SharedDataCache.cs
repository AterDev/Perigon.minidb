namespace Perigon.MiniDb;

/// <summary>
/// Manages shared in-memory data cache across multiple DbContext instances for the same file.
/// Ensures that all contexts working with the same database file share a single copy of data.
/// </summary>
internal static class SharedDataCache
{
    private static readonly Dictionary<string, FileDataCache> _caches = new();
    private static readonly Lock _cacheLock = new();
    private static int _isDisposed;

    static SharedDataCache()
    {
        // Ensure cleanup of shared resources at process/AppDomain shutdown
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        // Ensure Cleanup is only executed once
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        lock (_cacheLock)
        {
            foreach (var cache in _caches.Values)
            {
                cache.Dispose();
            }

            _caches.Clear();
        }
    }

    /// <summary>
    /// Gets or creates a shared cache for the specified file path
    /// </summary>
    public static FileDataCache GetOrCreateCache(string filePath)
    {
        // Normalize the path to ensure consistency
        var normalizedPath = Path.GetFullPath(filePath);

        lock (_cacheLock)
        {
            if (_caches.TryGetValue(normalizedPath, out var cache))
            {
                cache.IncrementRefCount();
                return cache;
            }

            cache = new FileDataCache(normalizedPath);
            _caches[normalizedPath] = cache;
            cache.IncrementRefCount();
            return cache;
        }
    }

    /// <summary>
    /// Releases a reference to the cache. If no more references exist, removes the cache.
    /// </summary>
    public static void ReleaseCache(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);

        FileDataCache? cacheToDispose = null;
        lock (_cacheLock)
        {
            if (_caches.TryGetValue(normalizedPath, out var cache) &&
                cache.DecrementRefCount() == 0)
            {
                _caches.Remove(normalizedPath);
                cacheToDispose = cache;
            }
        }

        // Dispose outside the lock to avoid holding it during disposal
        cacheToDispose?.Dispose();
    }
}

/// <summary>
/// Holds the actual data cache for a single database file
/// </summary>
internal class FileDataCache(string filePath) : IDisposable
{
    private readonly string _filePath = filePath;
    private readonly Dictionary<string, object> _tableData = new();
    private readonly Lock _dataLock = new();
    private int _refCount = 0;
    private int _disposed = 0;

    public int IncrementRefCount()
    {
        return Interlocked.Increment(ref _refCount);
    }

    public int DecrementRefCount()
    {
        return Interlocked.Decrement(ref _refCount);
    }

    /// <summary>
    /// Gets the data for a table. If not cached, loads it using the provided loader function.
    /// </summary>
    public List<T> GetOrLoadTableData<T>(string tableName, Func<List<T>> loader) where T : new()
    {
        lock (_dataLock)
        {
            if (_tableData.TryGetValue(tableName, out var cachedData))
            {
                return (List<T>)cachedData;
            }

            var data = loader();
            _tableData[tableName] = data;
            return data;
        }
    }

    /// <summary>
    /// Acquires a lock for thread-safe read operations
    /// </summary>
    public void EnterReadLock()
    {
        Monitor.Enter(_dataLock);
    }

    /// <summary>
    /// Releases a read lock
    /// </summary>
    public void ExitReadLock()
    {
        Monitor.Exit(_dataLock);
    }

    /// <summary>
    /// Acquires a lock for thread-safe write operations
    /// </summary>
    public void EnterWriteLock()
    {
        Monitor.Enter(_dataLock);
    }

    /// <summary>
    /// Releases a write lock
    /// </summary>
    public void ExitWriteLock()
    {
        Monitor.Exit(_dataLock);
    }

    public void Dispose()
    {
        // Use CompareExchange for thread-safe disposal check
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
    }
}
