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
    private readonly ThreadSafetyManager _threadSafety;
    private readonly Dictionary<string, object> _dbSets = new();
    private readonly Dictionary<string, Type> _tableTypes = new();

    protected MicroDbContext(string filePath)
    {
        _filePath = filePath;
        _storageManager = new StorageManager(filePath);
        _changeTracker = new ChangeTracker();
        _threadSafety = new ThreadSafetyManager();

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

            // Load entities from storage
            var loadMethod = _storageManager.GetType().GetMethod(nameof(StorageManager.LoadTable))!
                .MakeGenericMethod(entityType);
            var entities = loadMethod.Invoke(_storageManager, new object[] { tableName });

            // Create DbSet instance
            var dbSetType = typeof(DbSet<>).MakeGenericType(entityType);
            var dbSet = Activator.CreateInstance(dbSetType, 
                BindingFlags.Instance | BindingFlags.NonPublic, 
                null, 
                new object[] { entities!, _changeTracker, tableName }, 
                null);

            property.SetValue(this, dbSet);
            _dbSets[tableName] = dbSet!;
        }
    }

    public void SaveChanges()
    {
        _threadSafety.EnterWriteLock();
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

            _changeTracker.Clear();
        }
        finally
        {
            _threadSafety.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _threadSafety?.Dispose();
    }
}
