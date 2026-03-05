using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Perigon.MiniDb;

/// <summary>
/// Main database context with DbSet management and SaveChanges.
/// DbContext instances operate on in-memory data, never directly on files.
/// SaveChanges writes directly to the database file.
/// </summary>
public abstract class MiniDbContext : IDisposable, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly string _normalizedFilePath;
    private readonly StorageManager _storageManager;
    private readonly ChangeTracker _changeTracker;
    private readonly FileDataCache _sharedCache;
    private readonly Dictionary<string, object> _dbSets = [];
    private readonly Dictionary<string, Type> _tableTypes = [];
    private bool _disposed = false;

    // Cache for table loading delegates to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, List<Func<MiniDbContext, CancellationToken, Task>>> _loadingDelegatesCache = new();
    // Cache for table type initialization delegates
    private static readonly ConcurrentDictionary<Type, Action<MiniDbContext>> _initializationDelegatesCache = new();
    // Cache for typed save delegates to avoid reflection in SaveChanges hot path
    private static readonly ConcurrentDictionary<Type,
        Func<MiniDbContext, string, IReadOnlyList<object>, IReadOnlyList<object>, IReadOnlyList<object>, CancellationToken, Task>> _saveDelegatesCache = new();
    // Cache for typed table reload delegates to avoid reflection in refresh path
    private static readonly ConcurrentDictionary<Type, Action<MiniDbContext, string>> _reloadDelegatesCache = new();
    // In-process per-file write gate to avoid concurrent write handle conflicts.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileWriteGates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MiniDbContext"/> class.
    /// The configuration must be registered via <see cref="MiniDbConfiguration.AddDbContext{TContext}"/> beforehand.
    /// </summary>
    protected MiniDbContext()
    {
        var options = MiniDbConfiguration.GetOptions(GetType());
        _filePath = options.FilePath!;
        _normalizedFilePath = Path.GetFullPath(_filePath);
        MiniDbDiagnostics.Info($"Context initializing: {GetType().Name}, file='{_filePath}'");
        
        _sharedCache = new FileDataCache();
        _storageManager = new StorageManager(_filePath);
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

            var actions = new List<Action<MiniDbContext>>();
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
            var list = new List<Func<MiniDbContext, CancellationToken, Task>>();
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsGenericType &&
                            p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

            foreach (var prop in properties)
            {
                var entityType = prop.PropertyType.GetGenericArguments()[0];
                var name = prop.Name;

                // MethodInfo for LoadAndSetPropertyAsync<T>
                var method = typeof(MiniDbContext).GetMethod(nameof(LoadAndSetPropertyAsync),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType);

                // Create delegate using Expression tree to avoid Invoke overhead
                var ctxParam = Expression.Parameter(typeof(MiniDbContext), "ctx");
                var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");
                var propConst = Expression.Constant(prop);
                var nameConst = Expression.Constant(name);

                var call = Expression.Call(ctxParam, method, propConst, nameConst, ctParam);
                var lambda = Expression.Lambda<Func<MiniDbContext, CancellationToken, Task>>(call, ctxParam, ctParam);

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
        var dbSet = await LoadTableHelperAsync<T>(tableName, cancellationToken).ConfigureAwait(false);
        property.SetValue(this, dbSet);
        _dbSets[tableName] = dbSet;
    }

    private async Task<DbSet<T>> LoadTableHelperAsync<T>(string tableName, CancellationToken cancellationToken = default) where T : class, IMicroEntity, new()
    {
        // Load entities from this context cache (or from storage if not cached)
        var entities = await _sharedCache.GetOrLoadTableDataAsync<T>(tableName,
            () => _storageManager.LoadTableAsync<T>(tableName, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // Create and return DbSet instance with context-local cache for synchronization
        return new DbSet<T>(entities, _changeTracker, tableName, _sharedCache, EnsureDataFreshForQuery);
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
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        EnsureDataFreshForSave();

        var writeGate = _fileWriteGates.GetOrAdd(_normalizedFilePath, static _ => new SemaphoreSlim(1, 1));
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        await _sharedCache.EnterWriteLockAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = _changeTracker.CreateSnapshot();
            if (snapshot.Added.Count == 0 && snapshot.Modified.Count == 0 && snapshot.Deleted.Count == 0)
            {
                return;
            }

            var addedByType = snapshot.Added.GroupBy(e => e.GetType()).ToDictionary(g => g.Key, g => g.ToList());
            var modifiedByType = snapshot.Modified.GroupBy(e => e.GetType()).ToDictionary(g => g.Key, g => g.ToList());
            var deletedByType = snapshot.Deleted.GroupBy(e => e.GetType()).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in _dbSets)
            {
                var tableName = kvp.Key;
                var entityType = _tableTypes[tableName];

                // Get added, modified, deleted entities for this table
                var added = addedByType.TryGetValue(entityType, out var addedListByType)
                    ? addedListByType
                    : [];
                var modified = modifiedByType.TryGetValue(entityType, out var modifiedListByType)
                    ? modifiedListByType
                    : [];
                var deleted = deletedByType.TryGetValue(entityType, out var deletedListByType)
                    ? deletedListByType
                    : [];

                if (added.Count > 0 || modified.Count > 0 || deleted.Count > 0)
                {
                    var saveDelegate = _saveDelegatesCache.GetOrAdd(entityType, static t =>
                    {
                        var method = typeof(MiniDbContext)
                            .GetMethod(nameof(SaveTableChangesTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                            .MakeGenericMethod(t);

                        var ctxParam = Expression.Parameter(typeof(MiniDbContext), "ctx");
                        var tableNameParam = Expression.Parameter(typeof(string), "tableName");
                        var addedParam = Expression.Parameter(typeof(IReadOnlyList<object>), "added");
                        var modifiedParam = Expression.Parameter(typeof(IReadOnlyList<object>), "modified");
                        var deletedParam = Expression.Parameter(typeof(IReadOnlyList<object>), "deleted");
                        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

                        var body = Expression.Call(ctxParam, method,
                            tableNameParam, addedParam, modifiedParam, deletedParam, ctParam);

                        return Expression.Lambda<Func<MiniDbContext, string, IReadOnlyList<object>, IReadOnlyList<object>, IReadOnlyList<object>, CancellationToken, Task>>(
                            body,
                            ctxParam,
                            tableNameParam,
                            addedParam,
                            modifiedParam,
                            deletedParam,
                            ctParam).Compile();
                    });

                    await saveDelegate(this, tableName, added, modified, deleted, cancellationToken).ConfigureAwait(false);

                    // Only clear successfully persisted changes for this table.
                    // If a later table fails, unpersisted changes remain tracked for retry.
                    _changeTracker.RemovePersisted(added, modified, deleted);
                }
            }
        }
        finally
        {
            _sharedCache.ExitWriteLockAsync();
            writeGate.Release();
        }
    }

    private void EnsureDataFreshForQuery()
    {
        EnsureDataFreshCore(isSaveOperation: false);
    }

    private void EnsureDataFreshForSave()
    {
        EnsureDataFreshCore(isSaveOperation: true);
    }

    private void EnsureDataFreshCore(bool isSaveOperation)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        if (!_storageManager.HasExternalUpdates())
        {
            return;
        }

        _sharedCache.EnterWriteLock();
        try
        {
            if (!_storageManager.HasExternalUpdates())
            {
                return;
            }

            if (_changeTracker.HasChanges)
            {
                if (isSaveOperation)
                {
                    // Keep save path compatible with concurrent writers:
                    // StorageManager.SaveChangesAsync will re-read file header metadata
                    // and assign append IDs at write time.
                    return;
                }

                var operation = isSaveOperation ? "save" : "query";
                throw new InvalidOperationException(
                    $"External updates were detected before {operation}. Current context has pending local changes. " +
                    "Please create a new context or clear/reapply local changes to avoid stale-write conflicts.");
            }

            ReloadAllTablesInPlace();
        }
        finally
        {
            _sharedCache.ExitWriteLock();
        }
    }

    private void ReloadAllTablesInPlace()
    {
        foreach (var (tableName, entityType) in _tableTypes)
        {
            var reloader = _reloadDelegatesCache.GetOrAdd(entityType, static t =>
            {
                var method = typeof(MiniDbContext)
                    .GetMethod(nameof(ReloadTableInPlace), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(t);

                var ctxParam = Expression.Parameter(typeof(MiniDbContext), "ctx");
                var tableNameParam = Expression.Parameter(typeof(string), "tableName");
                var body = Expression.Call(ctxParam, method, tableNameParam);

                return Expression.Lambda<Action<MiniDbContext, string>>(body, ctxParam, tableNameParam).Compile();
            });

            reloader(this, tableName);
        }
    }

    private void ReloadTableInPlace<TEntity>(string tableName) where TEntity : class, IMicroEntity, new()
    {
        var latest = _storageManager.LoadTable<TEntity>(tableName);

        if (_dbSets.TryGetValue(tableName, out var dbSet) && dbSet is DbSet<TEntity> typedDbSet)
        {
            typedDbSet.ReplaceAllFromStore(latest, assumeWriteLockHeld: true);
            return;
        }

        var replacement = new DbSet<TEntity>(latest, _changeTracker, tableName, _sharedCache, EnsureDataFreshForQuery);
        var property = GetType().GetProperty(tableName, BindingFlags.Public | BindingFlags.Instance);
        property?.SetValue(this, replacement);
        _dbSets[tableName] = replacement;
    }

    private async Task SaveTableChangesTypedAsync<TEntity>(
        string tableName,
        IReadOnlyList<object> added,
        IReadOnlyList<object> modified,
        IReadOnlyList<object> deleted,
        CancellationToken cancellationToken) where TEntity : class, IMicroEntity
    {
        var typedAdded = added.Cast<TEntity>().ToList();
        var typedModified = modified.Cast<TEntity>().ToList();
        var typedDeleted = deleted.Cast<TEntity>().ToList();

        await _storageManager
            .SaveChangesAsync(tableName, typedAdded, typedModified, typedDeleted, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sharedCache.Dispose();

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sharedCache.Dispose();
        await Task.CompletedTask;
        GC.SuppressFinalize(this);
    }

    private void LoadAllTablesSynchronously()
    {
        // Use GetAwaiter().GetResult() to synchronously wait for async operation
        // This is acceptable for small databases (≤50MB) as per design goals
        var sw = Stopwatch.StartNew();
        try
        {
            var task = LoadAllTablesAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();
            sw.Stop();
            MiniDbDiagnostics.Info($"LoadAllTables completed for {GetType().Name} in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            MiniDbDiagnostics.Error($"LoadAllTables failed for {GetType().Name} after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
