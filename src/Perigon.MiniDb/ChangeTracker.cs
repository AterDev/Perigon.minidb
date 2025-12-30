namespace Perigon.MiniDb;

/// <summary>
/// Track entity changes for incremental file updates
/// </summary>
public class ChangeTracker
{
    private readonly HashSet<object> _added = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _modified = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _deleted = new(ReferenceEqualityComparer.Instance);
    private readonly Lock _syncRoot = new();

    public IReadOnlyList<object> Added
    {
        get
        {
            lock (_syncRoot)
            {
                return [.. _added];
            }
        }
    }

    public IReadOnlyList<object> Modified
    {
        get
        {
            lock (_syncRoot)
            {
                return [.. _modified];
            }
        }
    }

    public IReadOnlyList<object> Deleted
    {
        get
        {
            lock (_syncRoot)
            {
                return [.. _deleted];
            }
        }
    }

    public void TrackAdded(object entity)
    {
        lock (_syncRoot)
        {
            _added.Add(entity);
        }
    }

    public void TrackModified(object entity)
    {
        lock (_syncRoot)
        {
            if (!_added.Contains(entity))
            {
                _modified.Add(entity);
            }
        }
    }

    public void TrackDeleted(object entity)
    {
        lock (_syncRoot)
        {
            if (_added.Remove(entity))
            {
                // Entity was added in this session, just remove it
            }
            else
            {
                _modified.Remove(entity);
                _deleted.Add(entity);
            }
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _added.Clear();
            _modified.Clear();
            _deleted.Clear();
        }
    }

    public bool HasChanges
    {
        get
        {
            lock (_syncRoot)
            {
                return _added.Count > 0 || _modified.Count > 0 || _deleted.Count > 0;
            }
        }
    }
}
