using System.Reflection;

namespace Perigon.MiniDb;

/// <summary>
/// Main database context with DbSet management and SaveChanges.
/// DbContext instances only operate on shared in-memory data, never directly on files.
/// File operations are handled by the shared write queue.
/// </summary>
public abstract class MicroDbContext : IDisposable, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly StorageManager _storageManager;
    private readonly ChangeTracker _changeTracker;
    private readonly FileDataCache _sharedCache;
    private readonly Dictionary<string, object> _dbSets = [];
    private readonly Dictionary<string, Type> _tableTypes = [];
    private bool _disposed = false;
    private bool _initialized = false;

    protected MicroDbContext(string filePath)
    {
        _filePath = filePath;
        _sharedCache = SharedDataCache.GetOrCreateCache(filePath);
        _storageManager = new StorageManager(filePath, _sharedCache.WriteQueue);
        _changeTracker = new ChangeTracker();

        InitializeDbSets();
        _storageManager.Initialize(_tableTypes);

        // Immediately load all tables synchronously
        // This ensures DbSet properties are initialized and ready to use
        // Data is loaded from shared cache (or from file if first time)
        LoadAllTablesSynchronously();
        _initialized = true;
    }

    private void InitializeDbSets()
    {
        var dbSetProperties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToList();

        foreach (var property in dbSetProperties)
        {
            var entityType = property.PropertyType.GetGenericArguments()[0];
            var tableName = property.Name;
            _tableTypes[tableName] = entityType;
        }
    }

    private async Task LoadAllTablesAsync(CancellationToken cancellationToken = default)
    {
        var dbSetProperties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .ToList();

        foreach (var property in dbSetProperties)
        {
            var entityType = property.PropertyType.GetGenericArguments()[0];
            var tableName = property.Name;

            // Use helper method to load table data with proper generic type handling
            var helperMethod = typeof(MicroDbContext).GetMethod(nameof(LoadTableHelperAsync),
                BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType);

            var dbSetTask = (Task)helperMethod.Invoke(this, [tableName, cancellationToken])!;
            await dbSetTask.ConfigureAwait(false);

            var dbSet = dbSetTask.GetType().GetProperty("Result")!.GetValue(dbSetTask);

            property.SetValue(this, dbSet);
            _dbSets[tableName] = dbSet!;
        }
    }

    private async Task<DbSet<T>> LoadTableHelperAsync<T>(string tableName, CancellationToken cancellationToken = default) where T : class, new()
    {
        // Load entities from shared cache (or from storage if not cached)
        var entities = await _sharedCache.GetOrLoadTableDataAsync<T>(tableName,
            async () => await _storageManager.LoadTableAsync<T>(tableName, cancellationToken),
            cancellationToken);

        // Create and return DbSet instance with shared cache for synchronization
        return new DbSet<T>(entities, _changeTracker, tableName, _sharedCache);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _sharedCache.EnterWriteLockAsync(cancellationToken);
        try
        {
            foreach (var kvp in _dbSets)
            {
                var tableName = kvp.Key;
                var entityType = _tableTypes[tableName];

                // Get added, modified, deleted entities for this table
                var added = _changeTracker.Added
                    .Where(e => e.GetType() == entityType)
                    .ToList();
                var modified = _changeTracker.Modified
                    .Where(e => e.GetType() == entityType)
                    .ToList();
                var deleted = _changeTracker.Deleted
                    .Where(e => e.GetType() == entityType)
                    .ToList();

                if (added.Count > 0 || modified.Count > 0 || deleted.Count > 0)
                {
                    // Convert List<object> to List<TEntity> using reflection
                    var listType = typeof(List<>).MakeGenericType(entityType);
                    var addedList = Activator.CreateInstance(listType)!;
                    var modifiedList = Activator.CreateInstance(listType)!;
                    var deletedList = Activator.CreateInstance(listType)!;

                    var addMethod = listType.GetMethod("Add")!;
                    foreach (var item in added)
                        addMethod.Invoke(addedList, [item]);
                    foreach (var item in modified)
                        addMethod.Invoke(modifiedList, [item]);
                    foreach (var item in deleted)
                        addMethod.Invoke(deletedList, [item]);

                    var saveMethod = _storageManager.GetType().GetMethod(nameof(StorageManager.SaveChangesAsync))!
                        .MakeGenericMethod(entityType);
                    var task = (Task)saveMethod.Invoke(_storageManager, [tableName, addedList, modifiedList, deletedList, cancellationToken])!;
                    await task.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _changeTracker.Clear();
            _sharedCache.ExitWriteLockAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Note: We do NOT release the shared cache here.
        // The cache persists across DbContext instances.
        // Call SharedDataCache.ReleaseCache() explicitly when you want to free memory.

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Note: We do NOT release the shared cache here.
        // The cache persists across DbContext instances.
        // Call SharedDataCache.ReleaseCache() explicitly when you want to free memory.

        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Explicitly releases the shared memory cache for the database file.
    /// Call this when you want to free memory resources.
    /// All pending writes will be flushed before releasing.
    /// </summary>
    public static void ReleaseSharedCache(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var cache = SharedDataCache.GetOrCreateCache(normalizedPath);

        // Flush any pending writes before releasing
        // Use Task.Run to avoid potential deadlocks in synchronization contexts
        Task.Run(async () => await cache.WriteQueue.FlushAsync().ConfigureAwait(false)).GetAwaiter().GetResult();

        SharedDataCache.ReleaseCache(normalizedPath);
    }

    /// <summary>
    /// Explicitly releases the shared memory cache for the database file (async version).
    /// Call this when you want to free memory resources.
    /// All pending writes will be flushed before releasing.
    /// </summary>
    public static async Task ReleaseSharedCacheAsync(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var cache = SharedDataCache.GetOrCreateCache(normalizedPath);

        // Flush any pending writes before releasing
        await cache.WriteQueue.FlushAsync();

        SharedDataCache.ReleaseCache(normalizedPath);
    }

    private void LoadAllTablesSynchronously()
    {
        // Use GetAwaiter().GetResult() to synchronously wait for async operation
        // This is acceptable for small databases (≤50MB) as per design goals
        var task = LoadAllTablesAsync(CancellationToken.None);
        task.GetAwaiter().GetResult();
    }
}
