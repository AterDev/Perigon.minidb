namespace Perigon.MiniDb;

/// <summary>
/// Thread-safe read/write lock manager
/// </summary>
public class ThreadSafetyManager : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    public void EnterReadLock()
    {
        _lock.EnterReadLock();
    }

    public void ExitReadLock()
    {
        _lock.ExitReadLock();
    }

    public void EnterWriteLock()
    {
        _lock.EnterWriteLock();
    }

    public void ExitWriteLock()
    {
        _lock.ExitWriteLock();
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}
