using System.Collections;

namespace Perigon.MiniDb;

/// <summary>
/// Entity collection with LINQ support
/// </summary>
public class DbSet<TEntity> : IEnumerable<TEntity> where TEntity : class, new()
{
    private readonly List<TEntity> _entities;
    private readonly ChangeTracker _changeTracker;
    private readonly string _tableName;
    private readonly FileDataCache _sharedCache;

    internal DbSet(List<TEntity> entities, ChangeTracker changeTracker, string tableName, FileDataCache sharedCache)
    {
        _entities = entities;
        _changeTracker = changeTracker;
        _tableName = tableName;
        _sharedCache = sharedCache;
    }

    public void Add(TEntity entity)
    {
        _sharedCache.EnterWriteLock();
        try
        {
            // Assign next Id
            var idProperty = typeof(TEntity).GetProperty("Id");
            if (idProperty != null && idProperty.PropertyType == typeof(int))
            {
                var currentId = (int)idProperty.GetValue(entity)!;
                if (currentId == 0)
                {
                    var maxId = _entities.Count > 0 
                        ? _entities.Max(e => (int)idProperty.GetValue(e)!) 
                        : 0;
                    idProperty.SetValue(entity, maxId + 1);
                }
            }

            _entities.Add(entity);
            _changeTracker.TrackAdded(entity);
        }
        finally
        {
            _sharedCache.ExitWriteLock();
        }
    }

    public void Update(TEntity entity)
    {
        _changeTracker.TrackModified(entity);
    }

    public void Remove(TEntity entity)
    {
        _sharedCache.EnterWriteLock();
        try
        {
            _entities.Remove(entity);
            _changeTracker.TrackDeleted(entity);
        }
        finally
        {
            _sharedCache.ExitWriteLock();
        }
    }

    public IEnumerator<TEntity> GetEnumerator()
    {
        _sharedCache.EnterReadLock();
        try
        {
            // Create a snapshot to avoid holding the lock during iteration
            return _entities.ToList().GetEnumerator();
        }
        finally
        {
            _sharedCache.ExitReadLock();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count 
    { 
        get
        {
            _sharedCache.EnterReadLock();
            try
            {
                return _entities.Count;
            }
            finally
            {
                _sharedCache.ExitReadLock();
            }
        }
    }
}
