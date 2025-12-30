using System.Collections;
using System.Runtime.InteropServices;

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
            if (idProperty is { PropertyType: var propType } && propType == typeof(int))
            {
                var currentId = (int)idProperty.GetValue(entity)!;
                if (currentId == 0)
                {
                    int maxId = 0;
                    var span = CollectionsMarshal.AsSpan(_entities);
                    foreach (var e in span)
                    {
                        var id = (int)idProperty.GetValue(e)!;
                        if (id > maxId) maxId = id;
                    }
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
            // Use CollectionsMarshal to create efficient snapshot
            var span = CollectionsMarshal.AsSpan(_entities);
            var snapshot = new TEntity[span.Length];
            span.CopyTo(snapshot);
            return ((IEnumerable<TEntity>)snapshot).GetEnumerator();
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
