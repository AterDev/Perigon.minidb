namespace Perigon.MiniDb;

/// <summary>
/// Track entity changes for incremental file updates
/// </summary>
public class ChangeTracker
{
    private readonly List<object> _added = new();
    private readonly List<object> _modified = new();
    private readonly List<object> _deleted = new();

    public IReadOnlyList<object> Added => _added.AsReadOnly();
    public IReadOnlyList<object> Modified => _modified.AsReadOnly();
    public IReadOnlyList<object> Deleted => _deleted.AsReadOnly();

    public void TrackAdded(object entity)
    {
        if (!_added.Contains(entity))
        {
            _added.Add(entity);
        }
    }

    public void TrackModified(object entity)
    {
        if (!_modified.Contains(entity) && !_added.Contains(entity))
        {
            _modified.Add(entity);
        }
    }

    public void TrackDeleted(object entity)
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

    public void Clear()
    {
        _added.Clear();
        _modified.Clear();
        _deleted.Clear();
    }

    public bool HasChanges => _added.Count > 0 || _modified.Count > 0 || _deleted.Count > 0;
}
