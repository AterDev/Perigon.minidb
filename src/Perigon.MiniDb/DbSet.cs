using System.Collections;
using System.Runtime.InteropServices;
using System.Linq;

namespace Perigon.MiniDb;

/// <summary>
/// Entity collection with LINQ support.
/// Uses direct property access via IMicroEntity interface for optimal performance.
/// </summary>
public class DbSet<TEntity> : IEnumerable<TEntity> where TEntity : class, IMicroEntity, new()
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
        
        // Calculate max ID once during initialization using direct property access
        _maxId = entities.Count > 0 ? entities.Max(e => e.Id) : 0;
    }

    public void Add(TEntity entity)
    {
        _sharedCache.EnterWriteLock();
        try
        {
            // Direct property access - no reflection needed
            if (entity.Id == 0)
            {
                // Auto-assign next ID
                _maxId++;
                entity.Id = _maxId;
            }
            else if (entity.Id > _maxId)
            {
                // Update max ID if manually specified ID is larger
                _maxId = entity.Id;
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
