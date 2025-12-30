using System.Collections.Concurrent;
using System.Linq.Expressions;
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

    // Cache for table loading delegates to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, List<Func<MicroDbContext, CancellationToken, Task>>> _loadingDelegatesCache = new();
    // Cache for table type initialization delegates
    private static readonly ConcurrentDictionary<Type, Action<MicroDbContext>> _initializationDelegatesCache = new();

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
    }

    private void InitializeDbSets()
    {
        var type = GetType();
        var initializer = _initializationDelegatesCache.GetOrAdd(type, t =>
        {
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsGenericType &&
                            p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

            var actions = new List<Action<MicroDbContext>>();
            foreach (var prop in properties)
            {
                var entityType = prop.PropertyType.GetGenericArguments()[0];
                var name = prop.Name;
                actions.Add(ctx => ctx._tableTypes[name] = entityType);
            }

            return ctx => { foreach (var action in actions) action(ctx); };
        });

        initializer(this);
    }

    private async Task LoadAllTablesAsync(CancellationToken cancellationToken = default)
    {
        var type = GetType();
        var loaders = _loadingDelegatesCache.GetOrAdd(type, t =>
        {
            var list = new List<Func<MicroDbContext, CancellationToken, Task>>();
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsGenericType &&
                            p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

            foreach (var prop in properties)
            {
                var entityType = prop.PropertyType.GetGenericArguments()[0];
                var name = prop.Name;

                // MethodInfo for LoadAndSetPropertyAsync<T>
                var method = typeof(MicroDbContext).GetMethod(nameof(LoadAndSetPropertyAsync),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType);

                // Create delegate using Expression tree to avoid Invoke overhead
                var ctxParam = Expression.Parameter(typeof(MicroDbContext), "ctx");
                var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");
                var propConst = Expression.Constant(prop);
                var nameConst = Expression.Constant(name);

                var call = Expression.Call(ctxParam, method, propConst, nameConst, ctParam);
                var lambda = Expression.Lambda<Func<MicroDbContext, CancellationToken, Task>>(call, ctxParam, ctParam);

                list.Add(lambda.Compile());
            }
            return list;
        });

        foreach (var loader in loaders)
        {
            await loader(this, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadAndSetPropertyAsync<T>(PropertyInfo property, string tableName, CancellationToken cancellationToken) where T : class, IMicroEntity, new()
    {
        var dbSet = await LoadTableHelperAsync<T>(tableName, cancellationToken);
        property.SetValue(this, dbSet);
        _dbSets[tableName] = dbSet;
    }

    private async Task<DbSet<T>> LoadTableHelperAsync<T>(string tableName, CancellationToken cancellationToken = default) where T : class, IMicroEntity, new()
    {
        // Load entities from shared cache (or from storage if not cached)
        var entities = await _sharedCache.GetOrLoadTableDataAsync<T>(tableName,
            async () => await _storageManager.LoadTableAsync<T>(tableName, cancellationToken),
            cancellationToken);

        // Create and return DbSet instance with shared cache for synchronization
        return new DbSet<T>(entities, _changeTracker, tableName, _sharedCache);
    }

    /// <summary>
    /// Returns a DbSet instance for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity for which a set should be returned.</typeparam>
    /// <returns>The DbSet for the given entity type.</returns>
    public DbSet<TEntity> Set<TEntity>() where TEntity : IMicroEntity
    {
        foreach (var dbSet in _dbSets.Values)
        {
            if (dbSet is DbSet<TEntity> typedDbSet)
            {
                return typedDbSet;
            }
        }

        throw new InvalidOperationException($"Cannot find DbSet for type {typeof(TEntity).Name}. Ensure it is declared as a public property on the context.");
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
        await cache.WriteQueue.FlushAsync().ConfigureAwait(false);

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
