using System.ComponentModel.DataAnnotations;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// Test entities
public class User
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

public class Product
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public bool? IsPublished { get; set; }

    public DateTime? LastModified { get; set; }
}

// Test DbContext
public class TestDbContext(string filePath) : MicroDbContext(filePath)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}

public class MiniDbTests : IDisposable
{
    private readonly string _testDbPath;

    public MiniDbTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.mdb");
    }

    public void Dispose()
    {
        // Explicitly release the shared cache
        TestDbContext.ReleaseSharedCache(_testDbPath);
        
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public void CanCreateNewDatabase()
    {
        using var db = new TestDbContext(_testDbPath);
        
        Assert.True(File.Exists(_testDbPath));
        Assert.NotNull(db.Users);
        Assert.NotNull(db.Products);
    }

    [Fact]
    public void CanAddEntity()
    {
        using var db = new TestDbContext(_testDbPath);

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
        db.SaveChanges();

        Assert.Equal(1, user.Id);
        Assert.Equal(1, db.Users.Count);
    }

    [Fact]
    public void CanAddMultipleEntities()
    {
        using var db = new TestDbContext(_testDbPath);

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

        db.SaveChanges();
        Assert.Equal(10, db.Users.Count);
    }

    [Fact]
    public void CanUpdateEntity()
    {
        using var db = new TestDbContext(_testDbPath);

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
        db.SaveChanges();

        user.Name = "Alexandra";
        user.Balance = 1500m;
        db.Users.Update(user);
        db.SaveChanges();

        // Reload database to verify persistence
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal("Alexandra", loadedUser.Name);
        Assert.Equal(1500m, loadedUser.Balance);
    }

    [Fact]
    public void CanDeleteEntity()
    {
        using var db = new TestDbContext(_testDbPath);

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
        db.SaveChanges();

        Assert.Equal(1, db.Users.Count);

        db.Users.Remove(user);
        db.SaveChanges();

        Assert.Equal(0, db.Users.Count);

        // Reload database to verify persistence
        using var db2 = new TestDbContext(_testDbPath);
        Assert.Equal(0, db2.Users.Count);
    }

    [Fact]
    public void CanQueryWithLinq()
    {
        using var db = new TestDbContext(_testDbPath);

        for (int i = 0; i < 10; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@example.com",
                Age = 20 + i,
                Balance = 100m * i,
                CreatedAt = DateTime.UtcNow,
                IsActive = i % 2 == 0
            });
        }

        db.SaveChanges();

        var activeUsers = db.Users.Where(u => u.IsActive).ToList();
        Assert.Equal(5, activeUsers.Count);

        var richUsers = db.Users.Where(u => u.Balance >= 500).ToList();
        Assert.Equal(5, richUsers.Count);

        var youngUsers = db.Users.Where(u => u.Age < 25).OrderBy(u => u.Age).ToList();
        Assert.Equal(5, youngUsers.Count);
        Assert.Equal(20, youngUsers.First().Age);
    }

    [Fact]
    public void CanPersistNullableTypes()
    {
        using var db = new TestDbContext(_testDbPath);

        var product = new Product
        {
            Name = "Test Product",
            Price = 99.99m,
            IsPublished = true,
            LastModified = DateTime.UtcNow
        };

        db.Products.Add(product);
        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        var loadedProduct = db2.Products.First();
        Assert.Equal("Test Product", loadedProduct.Name);
        Assert.Equal(99.99m, loadedProduct.Price);
        Assert.True(loadedProduct.IsPublished);
        Assert.NotNull(loadedProduct.LastModified);
    }

    [Fact]
    public void CanPersistNullValues()
    {
        using var db = new TestDbContext(_testDbPath);

        var product = new Product
        {
            Name = "Test Product",
            Price = null,
            IsPublished = null,
            LastModified = null
        };

        db.Products.Add(product);
        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        var loadedProduct = db2.Products.First();
        Assert.Equal("Test Product", loadedProduct.Name);
        Assert.Null(loadedProduct.Price);
        Assert.Null(loadedProduct.IsPublished);
        Assert.Null(loadedProduct.LastModified);
    }

    [Fact]
    public void CanHandleUtf8Strings()
    {
        using var db = new TestDbContext(_testDbPath);

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
        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal("张三", loadedUser.Name);
        Assert.Equal("zhangsan@例え.com", loadedUser.Email);
    }

    [Fact]
    public void CanHandleDateTimePersistence()
    {
        using var db = new TestDbContext(_testDbPath);

        var now = DateTime.UtcNow;
        var user = new User
        {
            Name = "Test",
            Email = "test@test.com",
            Age = 25,
            Balance = 100m,
            CreatedAt = now,
            IsActive = true,
            PublishedAt = now.AddDays(-5)
        };

        db.Users.Add(user);
        db.SaveChanges();

        // Reload and verify (allow small time difference due to tick precision)
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal(now.Ticks, loadedUser.CreatedAt.Ticks);
        Assert.NotNull(loadedUser.PublishedAt);
        Assert.Equal(now.AddDays(-5).Ticks, loadedUser.PublishedAt.Value.Ticks);
    }

    [Fact]
    public void CanHandleDecimalPrecision()
    {
        using var db = new TestDbContext(_testDbPath);

        var user = new User
        {
            Name = "Test",
            Email = "test@test.com",
            Age = 25,
            Balance = 123456.789012m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal(123456.789012m, loadedUser.Balance);
    }

    [Fact]
    public void CanHandleBooleanValues()
    {
        using var db = new TestDbContext(_testDbPath);

        db.Users.Add(new User
        {
            Name = "Active",
            Email = "active@test.com",
            Age = 25,
            Balance = 100m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        db.Users.Add(new User
        {
            Name = "Inactive",
            Email = "inactive@test.com",
            Age = 30,
            Balance = 200m,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        });

        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        var activeUser = db2.Users.First(u => u.Name == "Active");
        var inactiveUser = db2.Users.First(u => u.Name == "Inactive");
        
        Assert.True(activeUser.IsActive);
        Assert.False(inactiveUser.IsActive);
    }

    [Fact]
    public void CanReopenExistingDatabase()
    {
        // Create and populate database
        using (var db = new TestDbContext(_testDbPath))
        {
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
            db.SaveChanges();
        }

        // Reopen database
        using (var db = new TestDbContext(_testDbPath))
        {
            Assert.Equal(5, db.Users.Count);
            var user2 = db.Users.First(u => u.Name == "User2");
            Assert.Equal(22, user2.Age);
            Assert.Equal(200m, user2.Balance);
        }
    }

    [Fact]
    public void MultipleContextsShareSameData()
    {
        // Create first context and add data
        using (var db1 = new TestDbContext(_testDbPath))
        {
            db1.Users.Add(new User
            {
                Name = "SharedUser",
                Email = "shared@test.com",
                Age = 30,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            db1.SaveChanges();
        }

        // Create second context - should share the same in-memory data
        using (var db2 = new TestDbContext(_testDbPath))
        {
            Assert.Equal(1, db2.Users.Count);
            var user = db2.Users.First();
            Assert.Equal("SharedUser", user.Name);
            Assert.Equal(30, user.Age);
        }
    }

    [Fact]
    public void ConcurrentContextsCanReadSimultaneously()
    {
        // Setup data
        using (var dbSetup = new TestDbContext(_testDbPath))
        {
            for (int i = 0; i < 10; i++)
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
            dbSetup.SaveChanges();
        }

        // Create multiple contexts and read concurrently
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                using var db = new TestDbContext(_testDbPath);
                return db.Users.Count(u => u.IsActive);
            }));
        }

        Task.WaitAll(tasks.ToArray());
        
        // All should read the same count
        foreach (var task in tasks)
        {
            Assert.Equal(10, task.Result);
        }
    }

    [Fact]
    public void WritesFromOneContextVisibleInAnother()
    {
        // Create context 1 and add initial data
        using var db1 = new TestDbContext(_testDbPath);
        db1.Users.Add(new User
        {
            Name = "User1",
            Email = "user1@test.com",
            Age = 25,
            Balance = 500m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        db1.SaveChanges();

        // Create context 2 - should see the data from context 1
        using var db2 = new TestDbContext(_testDbPath);
        Assert.Equal(1, db2.Users.Count);

        // Add data in context 2
        db2.Users.Add(new User
        {
            Name = "User2",
            Email = "user2@test.com",
            Age = 30,
            Balance = 750m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        db2.SaveChanges();

        // Context 1 should now see both users (shared memory)
        Assert.Equal(2, db1.Users.Count);
    }

    [Fact]
    public void CanHandleLargeNumberOfRecords()
    {
        using var db = new TestDbContext(_testDbPath);

        // Add 1000 records
        for (int i = 0; i < 1000; i++)
        {
            db.Users.Add(new User
            {
                Name = $"User{i}",
                Email = $"user{i}@test.com",
                Age = 20 + (i % 50),
                Balance = i * 10m,
                CreatedAt = DateTime.UtcNow,
                IsActive = i % 2 == 0
            });
        }

        db.SaveChanges();
        Assert.Equal(1000, db.Users.Count);

        // Query and verify
        var activeUsers = db.Users.Where(u => u.IsActive).ToList();
        Assert.Equal(500, activeUsers.Count);

        var richUsers = db.Users.Where(u => u.Balance >= 5000).ToList();
        Assert.Equal(500, richUsers.Count);
    }

    [Fact]
    public void StringTruncationHandledCorrectly()
    {
        using (var db = new TestDbContext(_testDbPath))
        {
            // Create a very long string that exceeds MaxLength(50)
            var longName = new string('A', 100);
            
            var user = new User
            {
                Name = longName,
                Email = "test@test.com",
                Age = 25,
                Balance = 100m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            db.Users.Add(user);
            db.SaveChanges();
        }

        // Release cache to force reload from disk
        TestDbContext.ReleaseSharedCache(_testDbPath);

        // Reload from disk to verify string was truncated at storage level
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        // String should be truncated to fit MaxLength(50) in bytes (UTF-8)
        Assert.True(loadedUser.Name.Length <= 50);
    }

    [Fact]
    public void CanHandleMultibyteCharactersTruncation()
    {
        using var db = new TestDbContext(_testDbPath);

        // Create a string with multibyte characters that exceeds MaxLength
        var multibyteString = new string('中', 30); // Chinese character, 3 bytes each in UTF-8
        
        var user = new User
        {
            Name = multibyteString,
            Email = "test@test.com",
            Age = 25,
            Balance = 100m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        db.SaveChanges();

        // Reload and verify no corruption
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        
        // Name should be truncated but still valid UTF-8
        Assert.NotEmpty(loadedUser.Name);
        // Should not throw on string operations
        Assert.DoesNotContain('\0', loadedUser.Name);
    }

    [Fact]
    public void UpdatesAreReflectedAcrossContexts()
    {
        // Create initial data
        using (var db1 = new TestDbContext(_testDbPath))
        {
            db1.Users.Add(new User
            {
                Name = "Original",
                Email = "original@test.com",
                Age = 25,
                Balance = 500m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            db1.SaveChanges();
        }

        // Update in one context
        using var db2 = new TestDbContext(_testDbPath);
        var user = db2.Users.First();
        user.Name = "Updated";
        user.Balance = 1000m;
        db2.Users.Update(user);
        db2.SaveChanges();

        // Verify in another context
        using var db3 = new TestDbContext(_testDbPath);
        var updatedUser = db3.Users.First();
        Assert.Equal("Updated", updatedUser.Name);
        Assert.Equal(1000m, updatedUser.Balance);
    }

    [Fact]
    public void DeletesAreReflectedAcrossContexts()
    {
        // Create initial data
        using (var db1 = new TestDbContext(_testDbPath))
        {
            db1.Users.Add(new User
            {
                Name = "ToDelete",
                Email = "delete@test.com",
                Age = 25,
                Balance = 500m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            db1.SaveChanges();
        }

        // Delete in one context
        using var db2 = new TestDbContext(_testDbPath);
        var user = db2.Users.First();
        db2.Users.Remove(user);
        db2.SaveChanges();

        // Verify deletion in another context
        using var db3 = new TestDbContext(_testDbPath);
        Assert.Equal(0, db3.Users.Count);
    }

    [Fact]
    public void CanHandleEmptyStringValues()
    {
        using var db = new TestDbContext(_testDbPath);

        var user = new User
        {
            Name = "",
            Email = "",
            Age = 25,
            Balance = 100m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        db.Users.Add(user);
        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        var loadedUser = db2.Users.First();
        Assert.Equal("", loadedUser.Name);
        Assert.Equal("", loadedUser.Email);
    }

    [Fact]
    public void CanHandleNullableIntEdgeCases()
    {
        using var db = new TestDbContext(_testDbPath);

        // Add with null CategoryId
        db.Users.Add(new User
        {
            Name = "NullCategory",
            Email = "null@test.com",
            Age = 25,
            Balance = 100m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = null
        });

        // Add with max int value
        db.Users.Add(new User
        {
            Name = "MaxCategory",
            Email = "max@test.com",
            Age = 30,
            Balance = 200m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = int.MaxValue
        });

        // Add with min int value
        db.Users.Add(new User
        {
            Name = "MinCategory",
            Email = "min@test.com",
            Age = 35,
            Balance = 300m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = int.MinValue
        });

        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        Assert.Equal(3, db2.Users.Count);
        
        var nullUser = db2.Users.First(u => u.Name == "NullCategory");
        Assert.Null(nullUser.CategoryId);
        
        var maxUser = db2.Users.First(u => u.Name == "MaxCategory");
        Assert.Equal(int.MaxValue, maxUser.CategoryId);
        
        var minUser = db2.Users.First(u => u.Name == "MinCategory");
        Assert.Equal(int.MinValue, minUser.CategoryId);
    }

    [Fact]
    public void CanHandleDecimalPrecisionEdgeCases()
    {
        using var db = new TestDbContext(_testDbPath);

        // Add with very small value
        db.Users.Add(new User
        {
            Name = "SmallBalance",
            Email = "small@test.com",
            Age = 25,
            Balance = 0.000001m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        // Add with very large value
        db.Users.Add(new User
        {
            Name = "LargeBalance",
            Email = "large@test.com",
            Age = 30,
            Balance = 999999999999.999999m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        // Add with negative value
        db.Users.Add(new User
        {
            Name = "NegativeBalance",
            Email = "negative@test.com",
            Age = 35,
            Balance = -12345.6789m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        
        var smallUser = db2.Users.First(u => u.Name == "SmallBalance");
        Assert.Equal(0.000001m, smallUser.Balance);
        
        var largeUser = db2.Users.First(u => u.Name == "LargeBalance");
        Assert.Equal(999999999999.999999m, largeUser.Balance);
        
        var negativeUser = db2.Users.First(u => u.Name == "NegativeBalance");
        Assert.Equal(-12345.6789m, negativeUser.Balance);
    }

    [Fact]
    public void CanHandleDateTimeEdgeCases()
    {
        using var db = new TestDbContext(_testDbPath);

        var minDate = DateTime.MinValue;
        var maxDate = DateTime.MaxValue;
        var currentDate = DateTime.UtcNow;

        db.Users.Add(new User
        {
            Name = "MinDate",
            Email = "min@test.com",
            Age = 25,
            Balance = 100m,
            CreatedAt = minDate,
            IsActive = true,
            PublishedAt = null
        });

        db.Users.Add(new User
        {
            Name = "MaxDate",
            Email = "max@test.com",
            Age = 30,
            Balance = 200m,
            CreatedAt = maxDate,
            IsActive = true,
            PublishedAt = maxDate
        });

        db.Users.Add(new User
        {
            Name = "CurrentDate",
            Email = "current@test.com",
            Age = 35,
            Balance = 300m,
            CreatedAt = currentDate,
            IsActive = true,
            PublishedAt = currentDate
        });

        db.SaveChanges();

        // Reload and verify
        using var db2 = new TestDbContext(_testDbPath);
        
        var minUser = db2.Users.First(u => u.Name == "MinDate");
        Assert.Equal(minDate.Ticks, minUser.CreatedAt.Ticks);
        Assert.Null(minUser.PublishedAt);
        
        var maxUser = db2.Users.First(u => u.Name == "MaxDate");
        Assert.Equal(maxDate.Ticks, maxUser.CreatedAt.Ticks);
        Assert.Equal(maxDate.Ticks, maxUser.PublishedAt!.Value.Ticks);
        
        var currentUser = db2.Users.First(u => u.Name == "CurrentDate");
        Assert.Equal(currentDate.Ticks, currentUser.CreatedAt.Ticks);
        Assert.Equal(currentDate.Ticks, currentUser.PublishedAt!.Value.Ticks);
    }

    [Fact]
    public void MultipleTablesWorkCorrectly()
    {
        using var db = new TestDbContext(_testDbPath);

        // Add users
        db.Users.Add(new User
        {
            Name = "User1",
            Email = "user1@test.com",
            Age = 25,
            Balance = 100m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        // Add products
        db.Products.Add(new Product
        {
            Name = "Product1",
            Price = 99.99m,
            IsPublished = true,
            LastModified = DateTime.UtcNow
        });

        db.SaveChanges();

        // Verify both tables
        Assert.Equal(1, db.Users.Count);
        Assert.Equal(1, db.Products.Count);

        // Reload and verify both tables persist
        using var db2 = new TestDbContext(_testDbPath);
        Assert.Equal(1, db2.Users.Count);
        Assert.Equal(1, db2.Products.Count);
        
        var user = db2.Users.First();
        var product = db2.Products.First();
        
        Assert.Equal("User1", user.Name);
        Assert.Equal("Product1", product.Name);
    }
}
