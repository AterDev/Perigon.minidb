using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// Test entity with [NotMapped] properties
public class UserWithNotMapped : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public int Age { get; set; }
    public decimal Balance { get; set; }

    // Computed property - not stored in database
    [NotMapped]
    public string FullInfo => $"{Name} ({Email}) - Balance: {Balance}";

    // Temporary property - not stored
    [NotMapped]
    public bool IsProcessed { get; set; }

    // Computed property
    [NotMapped]
    public bool IsAdult => Age >= 18;
}

// Test DbContext
public class NotMappedTestContext : MiniDbContext
{
    public DbSet<UserWithNotMapped> Users { get; set; } = null!;
}

/// <summary>
/// Tests for [NotMapped] attribute support
/// </summary>
public class NotMappedTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public NotMappedTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_notmapped_{Guid.NewGuid()}.mdb");
        MiniDbConfiguration.AddDbContext<NotMappedTestContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await NotMappedTestContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task NotMappedProperties_ShouldNotBePersisted()
    {
        var db = new NotMappedTestContext();
        await using (db)
        {
            var user = new UserWithNotMapped
            {
                Name = "Alice",
                Email = "alice@example.com",
                Age = 25,
                Balance = 1000m,
                IsProcessed = true  // [NotMapped] property
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            Assert.Equal(1, user.Id);
        }

        // Reload and verify [NotMapped] properties are not loaded
        await NotMappedTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new NotMappedTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // Mapped properties should be loaded
            Assert.Equal("Alice", loaded.Name);
            Assert.Equal("alice@example.com", loaded.Email);
            Assert.Equal(25, loaded.Age);
            Assert.Equal(1000m, loaded.Balance);

            // [NotMapped] properties should have default values
            Assert.False(loaded.IsProcessed);  // Default bool value
        }
    }

    [Fact]
    public async Task ComputedProperties_ShouldWorkCorrectly()
    {
        var db = new NotMappedTestContext();
        await using (db)
        {
            var user = new UserWithNotMapped
            {
                Name = "Bob",
                Email = "bob@example.com",
                Age = 17,
                Balance = 500m
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Computed properties should work
            Assert.Equal("Bob (bob@example.com) - Balance: 500", user.FullInfo);
            Assert.False(user.IsAdult);  // Age = 17
        }

        // Reload and verify computed properties work
        await NotMappedTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new NotMappedTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // Computed properties should work after reload
            Assert.Equal("Bob (bob@example.com) - Balance: 500", loaded.FullInfo);
            Assert.False(loaded.IsAdult);
        }
    }

    [Fact]
    public async Task UpdateWithNotMappedProperties_ShouldOnlyUpdateMappedFields()
    {
        var db = new NotMappedTestContext();
        await using (db)
        {
            var user = new UserWithNotMapped
            {
                Name = "Charlie",
                Email = "charlie@example.com",
                Age = 30,
                Balance = 2000m,
                IsProcessed = false
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Modify both mapped and [NotMapped] properties
            user.Balance = 2500m;  // Mapped
            user.IsProcessed = true;  // [NotMapped]
            db.Users.Update(user);
            await db.SaveChangesAsync();
        }

        // Reload and verify only mapped property was updated
        await NotMappedTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new NotMappedTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            Assert.Equal(2500m, loaded.Balance);  // Updated
            Assert.False(loaded.IsProcessed);  // Not persisted
        }
    }

    [Fact]
    public async Task MultipleNotMappedProperties_ShouldAllBeIgnored()
    {
        var db = new NotMappedTestContext();
        await using (db)
        {
            var user = new UserWithNotMapped
            {
                Name = "David",
                Email = "david@example.com",
                Age = 40,
                Balance = 5000m,
                IsProcessed = true
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // Reload and verify all [NotMapped] properties are ignored
        await NotMappedTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new NotMappedTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // All computed properties should work
            Assert.NotEmpty(loaded.FullInfo);
            Assert.True(loaded.IsAdult);  // Age = 40
            Assert.False(loaded.IsProcessed);  // Default value
        }
    }
}
