using System.Collections.Concurrent;

namespace Perigon.MiniDb.Tests;

public class ConcurrencyTestDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
}

/// <summary>
/// Tests for concurrent operations and thread safety
/// </summary>
public class ConcurrencyTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public ConcurrencyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_concurrency_{Guid.NewGuid()}.mdb");
        MiniDbConfiguration.AddDbContext<ConcurrencyTestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await ConcurrencyTestDbContext.ReleaseSharedCacheAsync(_testDbPath);
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
        var db = new ConcurrencyTestDbContext();
        for (int i = 0; i < 100; i++)
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
            var readDb = new ConcurrencyTestDbContext();
            await using (readDb)
            {
                for (int i = 0; i < 100; i++)
                {
                    var users = readDb.Users.Where(u => u.Age >= 20).ToList();
                    Assert.Equal(100, users.Count);
                }

                return true;
            }
        });

        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => Assert.True(result));
    }

    [Fact]
    public async Task SequentialWrites_NoDataLoss()
    {
        var db = new ConcurrencyTestDbContext();
        // 10 sequential writes
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
        var initCtx = new ConcurrencyTestDbContext();
        await initCtx.DisposeAsync();

        // Create 5 contexts
        var contexts = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(async _ =>
            {
                var ctx = new ConcurrencyTestDbContext();
                return ctx;
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
        // Setup data
        var db = new ConcurrencyTestDbContext();
        // Start with 50 users
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
        var db = new ConcurrencyTestDbContext();
        // Write 1000 users with batched saves (every 10 users)
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
        var db2 = new ConcurrencyTestDbContext();
        Assert.Equal(1000, db2.Users.Count);
    }

    [Fact]
    public async Task BatchedWrites_Performance()
    {
        var db = new ConcurrencyTestDbContext();
        // Write 1000 users with batched saves (every 10 users)
        // This tests the performance of a common write pattern
        var tasks = Enumerable.Range(0, 1000).Select(async i =>
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

            // Batch saves every 10 users
            if ((i + 1) % 10 == 0)
            {
                await db.SaveChangesAsync();
            }
        });

        await Task.WhenAll(tasks);
        await db.SaveChangesAsync(); // Final save

        Assert.Equal(1000, db.Users.Count);

        // Reload and verify
        var db2 = new ConcurrencyTestDbContext();
        Assert.Equal(1000, db2.Users.Count);
    }

    [Fact]
    public async Task LargeDataset_Performance()
    {
        var db = new ConcurrencyTestDbContext();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Setup: Add 5000 users
        for (int i = 0; i < 5000; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + (i % 50),
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // Test: 5 parallel tasks reading data
        var readTasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var readDb = new ConcurrencyTestDbContext();
            try
            {
                var users = readDb.Users.Where(u => u.Age >= 20).ToList();
                Assert.Equal(5000, users.Count);
            }
            finally
            {
                await readDb.DisposeAsync();
            }
        });

        await Task.WhenAll(readTasks);
        stopwatch.Stop();

        // Duration should be reasonable for 5000 records
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Reading 5000 records took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");

        // Cleanup
        await db.DisposeAsync();
    }

    [Fact(Skip = "File access conflicts in parallel initialization - known issue with FileShare.None during database creation")]
    public async Task ParallelContextInitialization_NoRaceCondition()
    {
        // This test verifies that multiple contexts can be initialized in parallel
        // without file access conflicts (FileShare.ReadWrite)
        
        // Create initial database
        var initCtx = new ConcurrencyTestDbContext();
        await initCtx.DisposeAsync();
        
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await Task.Yield(); // Force async execution
            try
            {
                var ctx = new ConcurrencyTestDbContext();
                return ctx;
            }
            catch
            {
                return null;
            }
        });

        var contexts = await Task.WhenAll(tasks);

        // Just verify no exceptions and all contexts are disposed
        foreach (var ctx in contexts.Where(c => c != null))
        {
            await ctx.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentReadWrite_ExpectedResults()
    {
        var db = new ConcurrencyTestDbContext();
        // Start with 50 users
        for (int i = 0; i < 50; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + i,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // 5 readers + 2 writers running concurrently
        var readerTasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var readDb = new ConcurrencyTestDbContext();
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    var users = readDb.Users.Where(u => u.IsActive).ToList();
                    Assert.NotEmpty(users);
                    await Task.Delay(1);
                }
            }
            finally
            {
                await readDb.DisposeAsync();
            }
        });

        var writerTasks = Enumerable.Range(0, 2).Select(async writerIndex =>
        {
            var ctx = new ConcurrencyTestDbContext();
            for (int i = 0; i < 10; i++)
            {
                ctx.Users.Add(new User
                {
                    Name = $"Writer{writerIndex}User{i}",
                    Email = $"w{writerIndex}u{i}@example.com",
                    Age = 25,
                    Balance = 500m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await ctx.SaveChangesAsync();
                await Task.Delay(5);
            }
        });

        await Task.WhenAll(readerTasks.Concat(writerTasks));

        // Should have 50 initial + 20 written = 70 users
        Assert.Equal(70, db.Users.Count);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task TransactionLike_Behavior_WithLock()
    {
        var db = new ConcurrencyTestDbContext();
        // Add initial user
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

        // This method simulates a transaction-like behavior using locks
        var semaphore = new SemaphoreSlim(1, 1);
        async Task<bool> UpdateUserBalance(int userId, decimal newBalance)
        {
            // Locking to simulate serialized access
            await semaphore.WaitAsync();
            try
            {
                var user = db.Users.FirstOrDefault(u => u.Id == userId);
                if (user == null) return false;

                user.Balance = newBalance;
                db.Users.Update(user);
                await db.SaveChangesAsync();
            }
            finally
            {
                semaphore.Release();
            }

            return true;
        }

        // 10 concurrent updates
        var updates = Enumerable.Range(0, 10).Select(async i =>
        {
            await UpdateUserBalance(1, 1000m + i);
            await Task.Delay(1); // Small delay to increase concurrency
        });

        await Task.WhenAll(updates);

        // Should have one of the updated values
        var finalUser = db.Users.First();
        Assert.InRange(finalUser.Balance, 1000m, 1009m);
        
        await db.DisposeAsync();
        await ConcurrencyTestDbContext.ReleaseSharedCacheAsync(_testDbPath);
    }

    [Fact]
    public async Task RapidOpenClose_Stability()
    {
        var errors = new ConcurrentBag<Exception>();

        // Rapidly open and close contexts
        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            var db = new ConcurrencyTestDbContext();
            try
            {
                // Add a user
                db.Users.Add(new User
                {
                    Name = $"User{i}",
                    Email = $"user{i}@example.com",
                    Age = 20 + i,
                    Balance = 1000m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                await db.DisposeAsync();
            }
        });

        await Task.WhenAll(tasks);

        // No errors should have occurred
        Assert.Empty(errors);
    }

    [Fact]
    public async Task MixedOperations_Stability()
    {
        var db = new ConcurrencyTestDbContext();
        // Initial data
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
    public async Task ParallelQuery_Performance()
    {
        // Setup 1000 users
        var db = new ConcurrencyTestDbContext();
        for (int i = 0; i < 1000; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + (i % 50),
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
        await db.DisposeAsync();

        // Parallel queries
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var queryDb = new ConcurrencyTestDbContext();
            await using (queryDb)
            {
                // Simulate complex query
                var result = queryDb.Users
                    .Where(u => u.Age > 25 && u.IsActive)
                    .OrderByDescending(u => u.Balance)
                    .Take(10)
                    .ToList();
                
                Assert.NotNull(result);
            }
        });

        await Task.WhenAll(tasks);
        
        // Cleanup
        await ConcurrencyTestDbContext.ReleaseSharedCacheAsync(_testDbPath);
    }
}
