using System.Reflection;

namespace Perigon.MiniDb;

/// <summary>
/// Main database context with DbSet management and SaveChanges
/// </summary>
public abstract class MicroDbContext : IDisposable
{
    private readonly string _filePath;
    private readonly StorageManager _storageManager;
    private readonly ChangeTracker _changeTracker;
    private readonly FileDataCache _sharedCache;
    private readonly Dictionary<string, object> _dbSets = new();
    private readonly Dictionary<string, Type> _tableTypes = new();
    private bool _disposed = false;

    protected MicroDbContext(string filePath)
    {
        _filePath = filePath;
        _sharedCache = SharedDataCache.GetOrCreateCache(filePath);
        _storageManager = new StorageManager(filePath);
        _changeTracker = new ChangeTracker();

        InitializeDbSets();
        _storageManager.Initialize(_tableTypes);
        LoadAllTables();
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

    private void LoadAllTables()
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
            var helperMethod = typeof(MicroDbContext).GetMethod(nameof(LoadTableHelper), 
                BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType);
            
            var dbSet = helperMethod.Invoke(this, new object[] { tableName });
            
            property.SetValue(this, dbSet);
            _dbSets[tableName] = dbSet!;
        }
    }

    private DbSet<T> LoadTableHelper<T>(string tableName) where T : class, new()
    {
        // Load entities from shared cache (or from storage if not cached)
        var entities = _sharedCache.GetOrLoadTableData<T>(tableName, () => _storageManager.LoadTable<T>(tableName));
        
        // Create and return DbSet instance
        return new DbSet<T>(entities, _changeTracker, tableName);
    }

    public void SaveChanges()
    {
        _sharedCache.EnterWriteLock();
        try
        {
            foreach (var kvp in _dbSets)
            {
                var tableName = kvp.Key;
                var dbSet = kvp.Value;
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
                        addMethod.Invoke(addedList, new[] { item });
                    foreach (var item in modified)
                        addMethod.Invoke(modifiedList, new[] { item });
                    foreach (var item in deleted)
                        addMethod.Invoke(deletedList, new[] { item });

                    var saveMethod = _storageManager.GetType().GetMethod(nameof(StorageManager.SaveChanges))!
                        .MakeGenericMethod(entityType);
                    saveMethod.Invoke(_storageManager, new object[] { tableName, addedList, modifiedList, deletedList });
                }
            }
        }
        finally
        {
            _changeTracker.Clear();
            _sharedCache.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SharedDataCache.ReleaseCache(_filePath);
    }
}
