using System.Collections;
using System.Runtime.InteropServices;

namespace Perigon.MiniDb;

/// <summary>
/// Entity collection with LINQ support.
/// Uses direct property access via IMicroEntity interface for optimal performance.
/// </summary>
public class DbSet<TEntity> : IEnumerable<TEntity> where TEntity : IMicroEntity
{
    private readonly List<TEntity> _entities;
    private readonly ChangeTracker _changeTracker;
    private readonly string _tableName;
    private readonly FileDataCache _sharedCache;

    // Track maximum ID for O(1) ID assignment
    private int _maxId;

    internal DbSet(List<TEntity> entities, ChangeTracker changeTracker, string tableName, FileDataCache sharedCache)
    {
        _entities = entities;
        _changeTracker = changeTracker;
        _tableName = tableName;
        _sharedCache = sharedCache;

        // Calculate max ID once during initialization using direct property access.
        // Acquire lock because the shared entity list may be modified concurrently.
        _sharedCache.EnterReadLock();
        try
        {
            _maxId = entities.Count > 0 ? entities.Max(e => e.Id) : 0;
        }
        finally
        {
            _sharedCache.ExitReadLock();
        }
    }

    public void Add(TEntity entity)
    {
        _sharedCache.EnterWriteLock();
        try
        {
            // Id is always assigned internally to guarantee contiguous IDs.
            _maxId++;
            entity.Id = _maxId;

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
        _sharedCache.EnterReadLock();
        try
        {
            if (entity.Id <= 0)
            {
                throw new InvalidOperationException(
                    $"Cannot update entity in table '{_tableName}' because it has no valid Id. Add the entity first.");
            }

            // Ensure the entity instance is tracked by this DbSet.
            if (!_entities.Contains(entity))
            {
                throw new InvalidOperationException(
                    $"Cannot update entity in table '{_tableName}' because it is not tracked by this DbSet. Query it from the context first.");
            }

            _changeTracker.TrackModified(entity);
        }
        finally
        {
            _sharedCache.ExitReadLock();
        }
    }

    public void Remove(TEntity entity)
    {
        _sharedCache.EnterWriteLock();
        try
        {
            if (entity.Id <= 0)
            {
                throw new InvalidOperationException(
                    $"Cannot remove entity from table '{_tableName}' because it has no valid Id.");
            }

            if (!_entities.Remove(entity))
            {
                throw new InvalidOperationException(
                    $"Cannot remove entity from table '{_tableName}' because it is not tracked by this DbSet. Query it from the context first.");
            }

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
