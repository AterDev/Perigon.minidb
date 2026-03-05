namespace Perigon.MiniDb.Tests;

/// <summary>
/// Regression tests for deadlock risk when constructor synchronously waits on async table loading.
/// </summary>
public class DeadlockCtorTestDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
}

public class MiniDbConstructorDeadlockRegressionTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public MiniDbConstructorDeadlockRegressionTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_ctor_deadlock_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<DeadlockCtorTestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task Constructor_SyncWait_OnAsyncLoad_DoesNotDeadlock_UnderNonPumpingSyncContext()
    {
        // Arrange: create persistent data first so ctor must load from file path.
        await using (var seed = new DeadlockCtorTestDbContext())
        {
            seed.Users.Add(new User
            {
                Name = "seed",
                Email = "seed@example.com",
                Age = 18,
                Balance = 1m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await seed.SaveChangesAsync();
        }

        // Force next context to read from file.

        var run = RunCtorUnderNonPumpingContext(() =>
        {
            using var db = new DeadlockCtorTestDbContext();
            Assert.Equal(1, db.Users.Count);
        });

        Assert.True(run.Finished, "Potential deadlock detected: constructor did not complete within timeout.");
        Assert.Null(run.Exception);
    }

    [Fact]
    public async Task Constructor_SyncWait_RepeatedReloads_DoNotDeadlock_UnderNonPumpingSyncContext()
    {
        // Arrange: seed some data first
        await using (var seed = new DeadlockCtorTestDbContext())
        {
            for (var i = 0; i < 3; i++)
            {
                seed.Users.Add(new User
                {
                    Name = $"seed-{i}",
                    Email = $"seed-{i}@example.com",
                    Age = 20 + i,
                    Balance = i,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await seed.SaveChangesAsync();
        }

        // Act + Assert: force reload from file repeatedly, each time constructing under non-pumping context.
        for (var i = 0; i < 15; i++)
        {
            var run = RunCtorUnderNonPumpingContext(() =>
            {
                using var db = new DeadlockCtorTestDbContext();
                Assert.Equal(3, db.Users.Count);
            });

            Assert.True(run.Finished, $"Potential deadlock detected at iteration {i}: constructor did not complete within timeout.");
            Assert.Null(run.Exception);
        }
    }

    private static (bool Finished, Exception? Exception) RunCtorUnderNonPumpingContext(Action action)
    {
        Exception? ctorException = null;
        using var completed = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());

            try
            {
                action();
            }
            catch (Exception ex)
            {
                ctorException = ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                completed.Set();
            }
        })
        {
            IsBackground = true
        };

        thread.Start();
        var finished = completed.Wait(TimeSpan.FromSeconds(5));
        return (finished, ctorException);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Intentionally do nothing: simulates UI-style context with no message pumping on this thread.
            // If async code captures this context and caller blocks synchronously, deadlock can occur.
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }
}
