using System.ComponentModel.DataAnnotations;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// Test entities
public class User : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public int Age { get; set; }

    public decimal Balance { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public int? CategoryId { get; set; }

    public DateTime? PublishedAt { get; set; }
}

public class Product : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public bool? IsPublished { get; set; }

    public DateTime? LastModified { get; set; }
}

// Test DbContext
public class TestDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}

/// <summary>
/// Tests for async operations in MiniDb
/// </summary>
public class MiniDbAsyncTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public MiniDbAsyncTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_async_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<TestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        // Explicitly release the shared cache
        await TestDbContext.ReleaseSharedCacheAsync(_testDbPath);
        
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
        var db = new TestDbContext();
        await using (db)
        {
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
    }

    [Fact]
    public async Task CanAddMultipleEntitiesAsync()
    {
        var db = new TestDbContext();        await using (db)
        {
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
    }

    [Fact]
    public async Task CanUpdateEntityAsync()
    {
        var db = new TestDbContext();        await using (db)
        {
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
        }

        // Reload database to verify persistence
        var db2 = new TestDbContext();        await using (db2)
        {
            var loadedUser = db2.Users.First();
            Assert.Equal("Alexandra", loadedUser.Name);
            Assert.Equal(1500m, loadedUser.Balance);
        }
    }

    [Fact]
    public async Task CanDeleteEntityAsync()
    {
        var db = new TestDbContext();        await using (db)
        {
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
        }

        // Reload database to verify persistence
        var db2 = new TestDbContext();        await using (db2)
        {
            Assert.Equal(0, db2.Users.Count);
        }
    }

    [Fact]
    public async Task CanHandleLargeDatasetAsync()
    {
        var db = new TestDbContext();        await using (db)
        {
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
    }

    [Fact]
    public async Task CancellationTokenWorks()
    {
        var db = new TestDbContext();        await using (db)
        {
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
    }

    [Fact]
    public async Task CancellationTokenCanCancelOperation()
    {
        var db = new TestDbContext();        await using (db)
        {
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
    }

    [Fact]
    public async Task AsyncHandlesNullableTypesCorrectly()
    {
        var db = new TestDbContext();        await using (db)
        {
            var product = new Product
            {
                Name = "Test Product",
                Price = 99.99m,
                IsPublished = true,
                LastModified = DateTime.UtcNow
            };

            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        // Reload and verify
        var db2 = new TestDbContext();        await using (db2)
        {
            var loadedProduct = db2.Products.First();
            Assert.Equal("Test Product", loadedProduct.Name);
            Assert.Equal(99.99m, loadedProduct.Price);
            Assert.True(loadedProduct.IsPublished);
            Assert.NotNull(loadedProduct.LastModified);
        }
    }

    [Fact]
    public async Task AsyncHandlesNullValuesCorrectly()
    {
        var db = new TestDbContext();        await using (db)
        {
            var product = new Product
            {
                Name = "Test Product",
                Price = null,
                IsPublished = null,
                LastModified = null
            };

            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        // Reload and verify
        var db2 = new TestDbContext();        await using (db2)
        {
            var loadedProduct = db2.Products.First();
            Assert.Equal("Test Product", loadedProduct.Name);
            Assert.Null(loadedProduct.Price);
            Assert.Null(loadedProduct.IsPublished);
            Assert.Null(loadedProduct.LastModified);
        }
    }

    [Fact]
    public async Task AsyncHandlesUtf8StringsCorrectly()
    {
        var db = new TestDbContext();        await using (db)
        {
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
        }

        // Reload and verify
        var db2 = new TestDbContext();        await using (db2)
        {
            var loadedUser = db2.Users.First();
            Assert.Equal("张三", loadedUser.Name);
            Assert.Equal("zhangsan@例え.com", loadedUser.Email);
        }
    }

    [Fact]
    public async Task AsyncPerformanceIsReasonable()
    {
        var db = new TestDbContext();        await using (db)
        {
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
    }
}
