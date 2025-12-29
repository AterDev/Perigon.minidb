namespace Perigon.MiniDb;

/// <summary>
/// Track entity changes for incremental file updates
/// </summary>
public class ChangeTracker
{
    private readonly List<object> _added = new();
    private readonly List<object> _modified = new();
    private readonly List<object> _deleted = new();
    private readonly object _syncRoot = new();

    public IReadOnlyList<object> Added
    {
        get
        {
            lock (_syncRoot)
            {
                return _added.ToArray();
            }
        }
    }

    public IReadOnlyList<object> Modified
    {
        get
        {
            lock (_syncRoot)
            {
                return _modified.ToArray();
            }
        }
    }

    public IReadOnlyList<object> Deleted
    {
        get
        {
            lock (_syncRoot)
            {
                return _deleted.ToArray();
            }
        }
    }

    public void TrackAdded(object entity)
    {
        lock (_syncRoot)
        {
            if (!_added.Contains(entity))
            {
                _added.Add(entity);
            }
        }
    }

    public void TrackModified(object entity)
    {
        lock (_syncRoot)
        {
            if (!_modified.Contains(entity) && !_added.Contains(entity))
            {
                _modified.Add(entity);
            }
        }
    }

    public void TrackDeleted(object entity)
    {
        lock (_syncRoot)
        {
            if (_added.Contains(entity))
            {
                _added.Remove(entity);
            }
            else
            {
                _modified.Remove(entity);
                if (!_deleted.Contains(entity))
                {
                    _deleted.Add(entity);
                }
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
