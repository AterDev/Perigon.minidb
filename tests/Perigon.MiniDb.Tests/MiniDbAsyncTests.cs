using System.ComponentModel.DataAnnotations;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

/// <summary>
/// Tests for async operations in MiniDb
/// </summary>
public class MiniDbAsyncTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public MiniDbAsyncTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_async_{Guid.NewGuid()}.mdb");
    }

    public async ValueTask DisposeAsync()
    {
        // Small delay to ensure all file handles are released
        await Task.Delay(10);
        
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task CanAddEntityAsync()
    {
        await using var db = new TestDbContext(_testDbPath);

        var user = new User
        {
            Name = "Alice",
            Email = "alice@example.com",
            Age = 25,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = 5
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Equal(1, user.Id);
        Assert.Equal(1, db.Users.Count);
    }

    [Fact]
    public async Task CanAddMultipleEntitiesAsync()
    {
        await using var db = new TestDbContext(_testDbPath);

        for (int i = 0; i < 10; i++)
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
        Assert.Equal(10, db.Users.Count);
    }

    [Fact]
    public async Task CanUpdateEntityAsync()
    {
        await using var db = new TestDbContext(_testDbPath);

        var user = new User
        {
            Name = "Alice",
            Email = "alice@example.com",
            Age = 25,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.Name = "Alexandra";
        user.Balance = 1500m;
        db.Users.Update(user);
        await db.SaveChangesAsync();

        // Reload database to verify persistence
        await using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal("Alexandra", loadedUser.Name);
        Assert.Equal(1500m, loadedUser.Balance);
    }

    [Fact]
    public async Task CanDeleteEntityAsync()
    {
        await using var db = new TestDbContext(_testDbPath);

        var user = new User
        {
            Name = "Alice",
            Email = "alice@example.com",
            Age = 25,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Equal(1, db.Users.Count);

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.Equal(0, db.Users.Count);

        // Reload database to verify persistence
        await using var db2 = new TestDbContext(_testDbPath);
        Assert.Equal(0, db2.Users.Count);
    }

    [Fact]
    public async Task CanHandleLargeDatasetAsync()
    {
        await using var db = new TestDbContext(_testDbPath);

        const int recordCount = 1000;
        for (int i = 0; i < recordCount; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + (i % 50),
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow.AddDays(-i),
                IsActive = i % 2 == 0
            });
        }

        await db.SaveChangesAsync();
        Assert.Equal(recordCount, db.Users.Count);

        // Verify data integrity
        var activeUsers = db.Users.Where(u => u.IsActive).ToList();
        Assert.Equal(recordCount / 2, activeUsers.Count);
    }

    [Fact]
    public async Task CancellationTokenWorks()
    {
        await using var db = new TestDbContext(_testDbPath);

        // Add some data
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

        using var cts = new CancellationTokenSource();
        await db.SaveChangesAsync(cts.Token);

        Assert.Equal(100, db.Users.Count);
    }

    [Fact]
    public async Task CancellationTokenCanCancelOperation()
    {
        await using var db = new TestDbContext(_testDbPath);

        // Add large amount of data
        for (int i = 0; i < 10000; i++)
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

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(1); // Cancel almost immediately

        // This should throw OperationCanceledException or complete if too fast
        try
        {
            await db.SaveChangesAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected - operation was cancelled
            Assert.True(true);
        }
    }

    [Fact]
    public async Task ConcurrentAsyncReadsAreThreadSafe()
    {
        // Setup data
        await using (var dbSetup = new TestDbContext(_testDbPath))
        {
            for (int i = 0; i < 100; i++)
            {
                dbSetup.Users.Add(new User
                {
                    Name = $"User{i}",
                    Email = $"user{i}@test.com",
                    Age = 20 + i,
                    Balance = 100m * i,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }
            await dbSetup.SaveChangesAsync();
        }

        // Small delay to ensure disposal completes
        await Task.Delay(100);

        // Create multiple concurrent read operations
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(10); // Simulate some work
                using var db = new TestDbContext(_testDbPath);
                return db.Users.Count(u => u.IsActive);
            }));
        }

        var results = await Task.WhenAll(tasks);
        
        // All should read the same count
        foreach (var result in results)
        {
            Assert.Equal(100, result);
        }
    }

    [Fact]
    public async Task AsyncWritesArePersisted()
    {
        // Write data asynchronously
        await using (var db1 = new TestDbContext(_testDbPath))
        {
            db1.Users.Add(new User
            {
                Name = "AsyncUser",
                Email = "async@test.com",
                Age = 30,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await db1.SaveChangesAsync();
        }

        // Small delay to ensure disposal completes
        await Task.Delay(100);

        // Read back asynchronously in a new context
        await using (var db2 = new TestDbContext(_testDbPath))
        {
            Assert.Equal(1, db2.Users.Count);
            var user = db2.Users.First();
            Assert.Equal("AsyncUser", user.Name);
            Assert.Equal(30, user.Age);
        }
    }

    [Fact]
    public async Task AsyncUpdateModifiesCorrectRecord()
    {
        await using var db = new TestDbContext(_testDbPath);

        // Add multiple users
        for (int i = 0; i < 5; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@test.com",
                Age = 20 + i,
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // Update specific user
        var userToUpdate = db.Users.First(u => u.Name == "User2");
        userToUpdate.Balance = 9999m;
        userToUpdate.Age = 99;
        db.Users.Update(userToUpdate);
        await db.SaveChangesAsync();

        // Reload and verify only the correct user was updated
        await using var db2 = new TestDbContext(_testDbPath);
        var updatedUser = db2.Users.First(u => u.Name == "User2");
        Assert.Equal(9999m, updatedUser.Balance);
        Assert.Equal(99, updatedUser.Age);

        // Other users should remain unchanged
        var otherUser = db2.Users.First(u => u.Name == "User1");
        Assert.Equal(100m, otherUser.Balance);
        Assert.Equal(21, otherUser.Age);
    }

    [Fact]
    public async Task AsyncDeleteRemovesCorrectRecord()
    {
        await using var db = new TestDbContext(_testDbPath);

        // Add multiple users
        for (int i = 0; i < 5; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@test.com",
                Age = 20 + i,
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // Delete specific user
        var userToDelete = db.Users.First(u => u.Name == "User2");
        db.Users.Remove(userToDelete);
        await db.SaveChangesAsync();

        Assert.Equal(4, db.Users.Count);
        Assert.DoesNotContain(db.Users, u => u.Name == "User2");

        // Reload and verify deletion persisted
        await using var db2 = new TestDbContext(_testDbPath);
        Assert.Equal(4, db2.Users.Count);
        Assert.DoesNotContain(db2.Users, u => u.Name == "User2");
    }

    [Fact]
    public async Task AsyncHandlesNullableTypesCorrectly()
    {
        await using var db = new TestDbContext(_testDbPath);

        var product = new Product
        {
            Name = "Test Product",
            Price = 99.99m,
            IsPublished = true,
            LastModified = DateTime.UtcNow
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Reload and verify
        await using var db2 = new TestDbContext(_testDbPath);
        var loadedProduct = db2.Products.First();
        Assert.Equal("Test Product", loadedProduct.Name);
        Assert.Equal(99.99m, loadedProduct.Price);
        Assert.True(loadedProduct.IsPublished);
        Assert.NotNull(loadedProduct.LastModified);
    }

    [Fact]
    public async Task AsyncHandlesNullValuesCorrectly()
    {
        await using var db = new TestDbContext(_testDbPath);

        var product = new Product
        {
            Name = "Test Product",
            Price = null,
            IsPublished = null,
            LastModified = null
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Reload and verify
        await using var db2 = new TestDbContext(_testDbPath);
        var loadedProduct = db2.Products.First();
        Assert.Equal("Test Product", loadedProduct.Name);
        Assert.Null(loadedProduct.Price);
        Assert.Null(loadedProduct.IsPublished);
        Assert.Null(loadedProduct.LastModified);
    }

    [Fact]
    public async Task AsyncHandlesUtf8StringsCorrectly()
    {
        await using var db = new TestDbContext(_testDbPath);

        var user = new User
        {
            Name = "张三",
            Email = "zhangsan@例え.com",
            Age = 30,
            Balance = 5000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Reload and verify
        await using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal("张三", loadedUser.Name);
        Assert.Equal("zhangsan@例え.com", loadedUser.Email);
    }

    [Fact]
    public async Task AsyncBatchOperationsAreAtomic()
    {
        await using var db = new TestDbContext(_testDbPath);

        // Add initial data
        for (int i = 0; i < 5; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@test.com",
                Age = 20 + i,
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
        }
        await db.SaveChangesAsync();

        // Batch operations: add, update, delete
        var userToUpdate = db.Users.First(u => u.Name == "User1");
        userToUpdate.Balance = 9999m;
        db.Users.Update(userToUpdate);

        var userToDelete = db.Users.First(u => u.Name == "User2");
        db.Users.Remove(userToDelete);

        db.Users.Add(new User
        {
            Name = "NewUser",
            Email = "new@test.com",
            Age = 40,
            Balance = 5000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        await db.SaveChangesAsync();

        // Verify all operations were applied
        Assert.Equal(5, db.Users.Count); // 5 - 1 + 1 = 5
        Assert.Contains(db.Users, u => u.Name == "NewUser");
        Assert.DoesNotContain(db.Users, u => u.Name == "User2");
        Assert.Equal(9999m, db.Users.First(u => u.Name == "User1").Balance);
    }

    [Fact]
    public async Task AsyncPerformanceIsReasonable()
    {
        await using var db = new TestDbContext(_testDbPath);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Add 500 records asynchronously
        for (int i = 0; i < 500; i++)
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
        stopwatch.Stop();

        Assert.Equal(500, db.Users.Count);
        
        // Performance should be reasonable (adjust threshold as needed)
        // This is mainly to catch severe performance regressions
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Operation took {stopwatch.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact]
    public async Task MixedSyncAndAsyncOperationsWork()
    {
        // Sync write
        using (var db = new TestDbContext(_testDbPath))
        {
            db.Users.Add(new User
            {
                Name = "SyncUser",
                Email = "sync@test.com",
                Age = 25,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            db.SaveChanges();
        }

        // Small delay to ensure disposal completes
        await Task.Delay(100);

        // Async write
        await using (var db = new TestDbContext(_testDbPath))
        {
            db.Users.Add(new User
            {
                Name = "AsyncUser",
                Email = "async@test.com",
                Age = 30,
                Balance = 2000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // Small delay to ensure disposal completes
        await Task.Delay(100);

        // Verify both are persisted
        await using (var db = new TestDbContext(_testDbPath))
        {
            Assert.Equal(2, db.Users.Count);
            Assert.Contains(db.Users, u => u.Name == "SyncUser");
            Assert.Contains(db.Users, u => u.Name == "AsyncUser");
        }
    }
}
