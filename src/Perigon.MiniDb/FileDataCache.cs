namespace Perigon.MiniDb;

/// <summary>
/// Holds the in-memory table cache and synchronization primitives for a single DbContext instance.
/// DbContext only operates on this in-memory data, never directly on files.
/// </summary>
internal class FileDataCache : IDisposable
{
    private readonly Dictionary<string, object> _tableData = new();
    private readonly Lock _dataLock = new();
    private readonly SemaphoreSlim _asyncLock = new(1, 1);
    private int _disposed = 0;

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
    /// Gets the data for a table. If not cached, loads it using the provided loader function.
    /// </summary>
    public async Task<List<T>> GetOrLoadTableDataAsync<T>(string tableName, Func<Task<List<T>>> loader, CancellationToken cancellationToken = default) where T : new()
    {
        await _asyncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tableData.TryGetValue(tableName, out var cachedData))
            {
                return (List<T>)cachedData;
            }

            var data = await loader().ConfigureAwait(false);
            _tableData[tableName] = data;
            return data;
        }
        finally
        {
            _asyncLock.Release();
        }
    }

    /// <summary>
    /// Acquires a lock for thread-safe read operations
    /// </summary>
    public void EnterReadLock()
    {
        _dataLock.Enter();
    }

    /// <summary>
    /// Releases a read lock
    /// </summary>
    public void ExitReadLock()
    {
        _dataLock.Exit();
    }

    /// <summary>
    /// Acquires a lock for thread-safe write operations
    /// </summary>
    public void EnterWriteLock()
    {
        _dataLock.Enter();
    }

    /// <summary>
    /// Releases a write lock
    /// </summary>
    public void ExitWriteLock()
    {
        _dataLock.Exit();
    }

    /// <summary>
    /// Acquires an async lock for thread-safe write operations
    /// </summary>
    public async Task EnterWriteLockAsync(CancellationToken cancellationToken = default)
    {
        await _asyncLock.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Releases an async write lock
    /// </summary>
    public void ExitWriteLockAsync()
    {
        _asyncLock.Release();
    }

    public void Dispose()
    {
        // Use CompareExchange for thread-safe disposal check
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        _asyncLock.Dispose();
    }
}
