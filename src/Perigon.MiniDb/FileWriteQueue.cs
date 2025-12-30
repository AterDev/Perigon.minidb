using System.Threading.Channels;

namespace Perigon.MiniDb;

/// <summary>
/// Manages a single-threaded write queue for database file operations.
/// Ensures that only one thread at a time writes to the database file to prevent data corruption.
/// </summary>
internal class FileWriteQueue : IDisposable
{
    private const int DefaultDisposeTimeoutSeconds = 10;
    
    private readonly string _filePath;
    private readonly Channel<WriteOperation> _writeChannel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _shutdownCts;
    private bool _disposed = false;

    public FileWriteQueue(string filePath)
    {
        _filePath = filePath;
        _writeChannel = Channel.CreateUnbounded<WriteOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _shutdownCts = new CancellationTokenSource();
        _writerTask = Task.Run(ProcessWriteQueueAsync);
    }

    /// <summary>
    /// Queues a write operation to be executed by the single writer thread
    /// </summary>
    public async Task QueueWriteAsync(Func<Task> writeAction, CancellationToken cancellationToken = default)
    {
        var operation = new WriteOperation(writeAction);
        await _writeChannel.Writer.WriteAsync(operation, cancellationToken);
        await operation.CompletionSource.Task;
    }

    /// <summary>
    /// Queues a synchronous write operation to be executed by the single writer thread
    /// </summary>
    public async Task QueueWriteAsync(Action writeAction, CancellationToken cancellationToken = default)
    {
        await QueueWriteAsync(() =>
        {
            writeAction();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    private async Task ProcessWriteQueueAsync()
    {
        try
        {
            await foreach (var operation in _writeChannel.Reader.ReadAllAsync(_shutdownCts.Token))
            {
                try
                {
                    await operation.WriteAction();
                    operation.CompletionSource.SetResult();
                }
                catch (Exception ex)
                {
                    operation.CompletionSource.SetException(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    public async Task FlushAsync()
    {
        // Queue a no-op operation to ensure all previous operations are complete
        await QueueWriteAsync(() => Task.CompletedTask);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Ensure all writes are flushed before disposing
        try
        {
            // Use Task.Run to avoid potential deadlocks in synchronization contexts
            Task.Run(async () => await FlushAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(DefaultDisposeTimeoutSeconds));
        }
        catch
        {
            // Ignore exceptions during flush
        }

        // Signal shutdown and complete the channel
        _writeChannel.Writer.Complete();
        _shutdownCts.Cancel();

        // Wait for the writer task to complete (with timeout)
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(DefaultDisposeTimeoutSeconds));
        }
        catch
        {
            // Ignore exceptions during disposal
        }

        _shutdownCts.Dispose();
    }

    private class WriteOperation
    {
        public Func<Task> WriteAction { get; }
        public TaskCompletionSource CompletionSource { get; }

        public WriteOperation(Func<Task> writeAction)
        {
            WriteAction = writeAction;
            CompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
