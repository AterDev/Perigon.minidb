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
public class TestDbContext : MicroDbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;

    public TestDbContext(string filePath) : base(filePath)
    {
    }
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
}
