namespace Perigon.MiniDb;

/// <summary>
/// Manages shared in-memory data cache across multiple DbContext instances for the same file.
/// Ensures that all contexts working with the same database file share a single copy of data.
/// </summary>
internal static class SharedDataCache
{
    private static readonly Dictionary<string, FileDataCache> _caches = new();
    private static readonly ReaderWriterLockSlim _cacheLock = new(LockRecursionPolicy.NoRecursion);
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

        _cacheLock.EnterWriteLock();
        try
        {
            foreach (var cache in _caches.Values)
            {
                cache.Dispose();
            }

            _caches.Clear();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
            _cacheLock.Dispose();
        }
    }

    /// <summary>
    /// Gets or creates a shared cache for the specified file path
    /// </summary>
    public static FileDataCache GetOrCreateCache(string filePath)
    {
        // Normalize the path to ensure consistency
        var normalizedPath = Path.GetFullPath(filePath);

        _cacheLock.EnterUpgradeableReadLock();
        try
        {
            if (_caches.TryGetValue(normalizedPath, out var cache))
            {
                cache.IncrementRefCount();
                return cache;
            }

            _cacheLock.EnterWriteLock();
            try
            {
                // Double-check in case another thread created it
                if (_caches.TryGetValue(normalizedPath, out cache))
                {
                    cache.IncrementRefCount();
                    return cache;
                }

                cache = new FileDataCache(normalizedPath);
                _caches[normalizedPath] = cache;
                cache.IncrementRefCount();
                return cache;
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }
        finally
        {
            _cacheLock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Releases a reference to the cache. If no more references exist, removes the cache.
    /// </summary>
    public static void ReleaseCache(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);

        FileDataCache? cacheToDispose = null;
        _cacheLock.EnterWriteLock();
        try
        {
            if (_caches.TryGetValue(normalizedPath, out var cache) &&
                cache.DecrementRefCount() == 0)
            {
                _caches.Remove(normalizedPath);
                cacheToDispose = cache;
            }
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        // Dispose outside the lock to avoid holding it during disposal
        cacheToDispose?.Dispose();
    }
}

/// <summary>
/// Holds the actual data cache for a single database file
/// </summary>
internal class FileDataCache : IDisposable
{
    private readonly string _filePath;
    private readonly Dictionary<string, object> _tableData = new();
    private readonly ReaderWriterLockSlim _dataLock = new(LockRecursionPolicy.NoRecursion);
    private int _refCount = 0;
    private int _disposed = 0;

    public FileDataCache(string filePath)
    {
        _filePath = filePath;
    }

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
        _dataLock.EnterUpgradeableReadLock();
        try
        {
            if (_tableData.TryGetValue(tableName, out var cachedData))
            {
                return (List<T>)cachedData;
            }

            _dataLock.EnterWriteLock();
            try
            {
                // Double-check in case another thread loaded it
                if (_tableData.TryGetValue(tableName, out cachedData))
                {
                    return (List<T>)cachedData;
                }

                var data = loader();
                _tableData[tableName] = data;
                return data;
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }
        }
        finally
        {
            _dataLock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Acquires a read lock for thread-safe read operations
    /// </summary>
    public void EnterReadLock()
    {
        _dataLock.EnterReadLock();
    }

    /// <summary>
    /// Releases a read lock
    /// </summary>
    public void ExitReadLock()
    {
        _dataLock.ExitReadLock();
    }

    /// <summary>
    /// Acquires a write lock for thread-safe write operations
    /// </summary>
    public void EnterWriteLock()
    {
        _dataLock.EnterWriteLock();
    }

    /// <summary>
    /// Releases a write lock
    /// </summary>
    public void ExitWriteLock()
    {
        _dataLock.ExitWriteLock();
    }

    public void Dispose()
    {
        // Use CompareExchange for thread-safe disposal check
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        _dataLock.Dispose();
    }
}
