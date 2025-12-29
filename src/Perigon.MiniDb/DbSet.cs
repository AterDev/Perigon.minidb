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

    internal DbSet(List<TEntity> entities, ChangeTracker changeTracker, string tableName)
    {
        _entities = entities;
        _changeTracker = changeTracker;
        _tableName = tableName;
    }

    public void Add(TEntity entity)
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

    public void Update(TEntity entity)
    {
        _changeTracker.TrackModified(entity);
    }

    public void Remove(TEntity entity)
    {
        _entities.Remove(entity);
        _changeTracker.TrackDeleted(entity);
    }

    public IEnumerator<TEntity> GetEnumerator()
    {
        return _entities.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _entities.Count;
}
