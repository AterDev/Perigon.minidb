using System.Collections.Concurrent;

namespace Perigon.MiniDb.Tests;

/// <summary>
/// Tests for concurrent operations and thread safety
/// </summary>
public class ConcurrencyTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public ConcurrencyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_concurrency_{Guid.NewGuid()}.mdb");
    }

    public async ValueTask DisposeAsync()
    {
        await TestDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task ParallelReads_NoDataCorruption()
    {
        // Setup: Add 100 users
        var db = new TestDbContext(_testDbPath);        for (int i = 0; i < 100; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + i,
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
        await db.DisposeAsync();

        // Test: 10 threads reading in parallel
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var readDb = new TestDbContext(_testDbPath);
            // Context automatically initialized in constructor

            try
            {
                for (int i = 0; i < 100; i++)
                {
                    var users = readDb.Users.Where(u => u.Age >= 20).ToList();
                    Assert.Equal(100, users.Count);
                }

                return true;
            }
            finally
            {
                await readDb.DisposeAsync();
            }
        });

        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.True(result));
    }

    [Fact]
    public async Task SequentialWrites_MaintainConsistency()
    {
        var db = new TestDbContext(_testDbPath);        // 10 sequential writes
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var user = new User
            {
                Name = $"SeqUser{i}",
                Email = $"seq{i}@example.com",
                Age = 20 + i,
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return user.Id;
        });

        var ids = await Task.WhenAll(tasks);

        // All IDs should be unique and sequential
        Assert.Equal(10, ids.Distinct().Count());

        await db.DisposeAsync();
    }

    [Fact]
    public async Task MultipleContexts_ConcurrentWrites_Serialized()
    {
        // Create file first
        var initCtx = new TestDbContext(_testDbPath);        await initCtx.DisposeAsync();

        // Create 5 contexts
        var contexts = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(async _ =>
            {
                var ctx = new TestDbContext(_testDbPath);                return ctx;
            })
        );

        try
        {
            // Each context writes 20 users concurrently
            var writeTasks = contexts.Select(async (ctx, ctxIndex) =>
            {
                var tasks = Enumerable.Range(0, 20).Select(async i =>
                {
                    var user = new User
                    {
                        Name = $"Ctx{ctxIndex}User{i}",
                        Email = $"ctx{ctxIndex}user{i}@example.com",
                        Age = 20 + i,
                        Balance = 100m * i,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    };

                    ctx.Users.Add(user);
                    await ctx.SaveChangesAsync();
                });

                await Task.WhenAll(tasks);
            });

            await Task.WhenAll(writeTasks);

            // Verify total count
            Assert.Equal(100, contexts[0].Users.Count);
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                await ctx.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ConcurrentReadWrite_NoDeadlock()
    {
        var db = new TestDbContext(_testDbPath);        // Start with 50 users
        for (int i = 0; i < 50; i++)
        {
            db.Users.Add(new User
            {
                Name = $"Initial{i}",
                Email = $"init{i}@example.com",
                Age = 20,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // 5 readers + 2 writers running concurrently
        var readerTasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            for (int i = 0; i < 50; i++)
            {
                var users = db.Users.Where(u => u.IsActive).ToList();
                Assert.NotEmpty(users);
                await Task.Delay(1);
            }
        });

        var writerTasks = Enumerable.Range(0, 2).Select(async writerIndex =>
        {
            for (int i = 0; i < 10; i++)
            {
                db.Users.Add(new User
                {
                    Name = $"Writer{writerIndex}User{i}",
                    Email = $"w{writerIndex}u{i}@example.com",
                    Age = 25,
                    Balance = 500m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await db.SaveChangesAsync();
                await Task.Delay(5);
            }
        });

        await Task.WhenAll(readerTasks.Concat(writerTasks));

        // Should have 50 initial + 20 written = 70 users
        Assert.Equal(70, db.Users.Count);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task StressTest_1000Writes_NoDataLoss()
    {
        var db = new TestDbContext(_testDbPath);        // Write 1000 users with batched saves (every 10 users)
        // Note: This is a stress test of the write queue serialization.
        // In real applications, you should batch all changes and call SaveChangesAsync once.
        var tasks = Enumerable.Range(0, 1000).Select(async i =>
        {
            db.Users.Add(new User
            {
                Name = $"StressUser{i}",
                Email = $"stress{i}@example.com",
                Age = 20 + (i % 50),
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = i % 2 == 0
            });

            // Batch saves every 10 users
            // This results in 100 SaveChanges calls (~30ms each = ~3s total)
            if ((i + 1) % 10 == 0)
            {
                await db.SaveChangesAsync();
            }
        });

        await Task.WhenAll(tasks);
        await db.SaveChangesAsync(); // Final save

        Assert.Equal(1000, db.Users.Count);

        await db.DisposeAsync();

        // Reload and verify
        var db2 = new TestDbContext(_testDbPath);        Assert.Equal(1000, db2.Users.Count);
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task BatchInsert_1000Records_FastPerformance()
    {
        var db = new TestDbContext(_testDbPath);        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Recommended pattern: Batch all changes, save once
        for (int i = 0; i < 1000; i++)
        {
            db.Users.Add(new User
            {
                Name = $"BatchUser{i}",
                Email = $"batch{i}@example.com",
                Age = 20 + (i % 50),
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = i % 2 == 0
            });
        }

        await db.SaveChangesAsync(); // Single save for all 1000 records
        stopwatch.Stop();

        Assert.Equal(1000, db.Users.Count);

        // Batch insert should be much faster than repeated small saves
        // Expected: < 200ms (vs ~3s for 100 individual saves)
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"Batch insert took {stopwatch.ElapsedMilliseconds}ms, expected < 200ms");

        await db.DisposeAsync();
    }

    [Fact(Skip = "File access conflicts in parallel initialization - known issue with FileShare.None during database creation")]
    public async Task ParallelContextInitialization_NoRaceCondition()
    {
        // Initialize file first
        var firstCtx = new TestDbContext(_testDbPath);        await firstCtx.DisposeAsync();

        // Small delay to ensure file is fully released
        await Task.Delay(50);

        // Now 10 contexts initialized in parallel (file already exists)
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            // Small stagger to reduce contention
            await Task.Delay(i * 5);

            var ctx = new TestDbContext(_testDbPath);            // Immediately add data
            ctx.Users.Add(new User
            {
                Name = $"ParallelInit{i}",
                Email = $"parallel{i}@example.com",
                Age = 20,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await ctx.SaveChangesAsync();

            return ctx;
        });

        var contexts = await Task.WhenAll(tasks);

        try
        {
            // All should see 10 users
            foreach (var ctx in contexts)
            {
                Assert.Equal(10, ctx.Users.Count);
            }
        }
        finally
        {
            foreach (var ctx in contexts)
            {
                await ctx.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task ConcurrentUpdates_LastWriteWins()
    {
        var db = new TestDbContext(_testDbPath);        // Add initial user
        db.Users.Add(new User
        {
            Id = 1,
            Name = "Original",
            Email = "original@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        // 10 concurrent updates
        var updates = Enumerable.Range(0, 10).Select(async i =>
        {
            var user = db.Users.First();
            user.Balance = 1000m + i;
            db.Users.Update(user);
            await db.SaveChangesAsync();
            await Task.Delay(1); // Small delay to increase concurrency
        });

        await Task.WhenAll(updates);

        // Should have one of the updated values
        var finalUser = db.Users.First();
        Assert.InRange(finalUser.Balance, 1000m, 1009m);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentDeletes_AllSucceed()
    {
        var db = new TestDbContext(_testDbPath);        // Add 20 users
        for (int i = 0; i < 20; i++)
        {
            db.Users.Add(new User
            {
                Name = $"DeleteMe{i}",
                Email = $"delete{i}@example.com",
                Age = 20,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // Delete all concurrently
        var users = db.Users.ToList();
        var deleteTasks = users.Select(async user =>
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        });

        await Task.WhenAll(deleteTasks);

        Assert.Equal(0, db.Users.Count);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task MixedOperations_Concurrent_MaintainIntegrity()
    {
        var db = new TestDbContext(_testDbPath);        // Initial data
        for (int i = 0; i < 20; i++)
        {
            db.Users.Add(new User
            {
                Name = $"Initial{i}",
                Email = $"init{i}@example.com",
                Age = 20,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        var errors = new ConcurrentBag<Exception>();

        // Mix of operations
        var addTasks = Enumerable.Range(0, 10).Select(async i =>
        {
            try
            {
                db.Users.Add(new User
                {
                    Name = $"Added{i}",
                    Email = $"add{i}@example.com",
                    Age = 25,
                    Balance = 500m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        var updateTasks = Enumerable.Range(0, 5).Select(async i =>
        {
            try
            {
                await Task.Delay(5);
                var user = db.Users.Skip(i).FirstOrDefault();
                if (user != null)
                {
                    user.Balance += 100m;
                    db.Users.Update(user);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        var deleteTasks = Enumerable.Range(0, 3).Select(async i =>
        {
            try
            {
                await Task.Delay(10);
                var user = db.Users.Skip(15 + i).FirstOrDefault();
                if (user != null)
                {
                    db.Users.Remove(user);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        await Task.WhenAll(addTasks.Concat(updateTasks).Concat(deleteTasks));

        // Should have: 20 initial + 10 added - 3 deleted = 27 users
        Assert.Equal(27, db.Users.Count);
        Assert.Empty(errors);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task LongRunningOperations_WithCancellation()
    {
        var db = new TestDbContext(_testDbPath);        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        try
        {
            // Try to add many users with cancellation
            for (int i = 0; i < 10000; i++)
            {
                db.Users.Add(new User
                {
                    Name = $"LongRun{i}",
                    Email = $"long{i}@example.com",
                    Age = 20,
                    Balance = 1000m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });

                if (i % 100 == 0)
                {
                    await db.SaveChangesAsync(cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Should have partial data
        Assert.True(db.Users.Count > 0);
        Assert.True(db.Users.Count < 10000);

        await db.DisposeAsync();
    }
}
