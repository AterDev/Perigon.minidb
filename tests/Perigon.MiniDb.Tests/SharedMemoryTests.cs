using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

/// <summary>
/// Tests for shared memory architecture across multiple DbContext instances
/// </summary>
public class SharedMemoryTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public SharedMemoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_shared_{Guid.NewGuid()}.mdb");
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
    public async Task MultipleContexts_SeeTheSameData()
    {
        // Create first context and add data
        var db1 = new TestDbContext(_testDbPath);        db1.Users.Add(new User { Name = "Alice", Email = "alice@example.com", Age = 30, Balance = 1000m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        
        // Create second context - should see the same data
        var db2 = new TestDbContext(_testDbPath);        Assert.Equal(1, db1.Users.Count);
        Assert.Equal(1, db2.Users.Count);
        
        var userFromDb1 = db1.Users.First();
        var userFromDb2 = db2.Users.First();
        
        Assert.Equal(userFromDb1.Name, userFromDb2.Name);
        Assert.Equal(userFromDb1.Email, userFromDb2.Email);
        
        await db1.DisposeAsync();
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task OneContext_ChangesVisibleToOther()
    {
        var db1 = new TestDbContext(_testDbPath);        var db2 = new TestDbContext(_testDbPath);        // Add data in db1
        db1.Users.Add(new User { Name = "Bob", Email = "bob@example.com", Age = 25, Balance = 500m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        
        // db2 should immediately see the change
        Assert.Equal(1, db2.Users.Count);
        Assert.Equal("Bob", db2.Users.First().Name);
        
        await db1.DisposeAsync();
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task ThreeContexts_AllSeeTheSameData()
    {
        var db1 = new TestDbContext(_testDbPath);        var db2 = new TestDbContext(_testDbPath);        var db3 = new TestDbContext(_testDbPath);        // Add data from each context
        db1.Users.Add(new User { Name = "User1", Email = "user1@example.com", Age = 20, Balance = 100m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        
        db2.Users.Add(new User { Name = "User2", Email = "user2@example.com", Age = 21, Balance = 200m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db2.SaveChangesAsync();
        
        db3.Users.Add(new User { Name = "User3", Email = "user3@example.com", Age = 22, Balance = 300m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db3.SaveChangesAsync();
        
        // All contexts should see all 3 users
        Assert.Equal(3, db1.Users.Count);
        Assert.Equal(3, db2.Users.Count);
        Assert.Equal(3, db3.Users.Count);
        
        await db1.DisposeAsync();
        await db2.DisposeAsync();
        await db3.DisposeAsync();
    }

    [Fact]
    public async Task Update_InOneContext_VisibleInOther()
    {
        var db1 = new TestDbContext(_testDbPath);        var db2 = new TestDbContext(_testDbPath);        // Add user
        db1.Users.Add(new User { Name = "Charlie", Email = "charlie@example.com", Age = 30, Balance = 1000m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        
        // Update in db1
        var user1 = db1.Users.First();
        user1.Balance = 2000m;
        db1.Users.Update(user1);
        await db1.SaveChangesAsync();
        
        // Verify update visible in db2
        var user2 = db2.Users.First();
        Assert.Equal(2000m, user2.Balance);
        
        await db1.DisposeAsync();
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Delete_InOneContext_VisibleInOther()
    {
        var db1 = new TestDbContext(_testDbPath);        var db2 = new TestDbContext(_testDbPath);        // Add user
        db1.Users.Add(new User { Name = "Dave", Email = "dave@example.com", Age = 35, Balance = 1500m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        
        Assert.Equal(1, db2.Users.Count);
        
        // Delete in db1
        var user = db1.Users.First();
        db1.Users.Remove(user);
        await db1.SaveChangesAsync();
        
        // Verify deletion visible in db2
        Assert.Equal(0, db2.Users.Count);
        
        await db1.DisposeAsync();
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Context_DisposeDoesNotReleaseSharedMemory()
    {
        // Create and dispose first context
        var db1 = new TestDbContext(_testDbPath);        db1.Users.Add(new User { Name = "Eve", Email = "eve@example.com", Age = 28, Balance = 800m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        await db1.DisposeAsync();
        
        // Create new context - should still see data (shared memory not released)
        var db2 = new TestDbContext(_testDbPath);        Assert.Equal(1, db2.Users.Count);
        Assert.Equal("Eve", db2.Users.First().Name);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task ExplicitReleaseSharedCache_FreesMemory()
    {
        var db1 = new TestDbContext(_testDbPath);        db1.Users.Add(new User { Name = "Frank", Email = "frank@example.com", Age = 40, Balance = 2000m, CreatedAt = DateTime.UtcNow, IsActive = true });
        await db1.SaveChangesAsync();
        await db1.DisposeAsync();
        
        // Explicitly release shared cache
        await TestDbContext.ReleaseSharedCacheAsync(_testDbPath);
        
        // Create new context - should reload from file
        var db2 = new TestDbContext(_testDbPath);        Assert.Equal(1, db2.Users.Count);
        Assert.Equal("Frank", db2.Users.First().Name);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task ParallelContextCreation_AllShareSameMemory()
    {
        // Create 5 contexts in parallel
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var db = new TestDbContext(_testDbPath);            return db;
        });
        
        var contexts = await Task.WhenAll(tasks);
        
        try
        {
            // Add data from first context
            contexts[0].Users.Add(new User { Name = "Parallel", Email = "parallel@example.com", Age = 25, Balance = 500m, CreatedAt = DateTime.UtcNow, IsActive = true });
            await contexts[0].SaveChangesAsync();
            
            // All contexts should see the data
            foreach (var ctx in contexts)
            {
                Assert.Equal(1, ctx.Users.Count);
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
    public async Task MultipleTableTypes_SharedCorrectly()
    {
        var db1 = new TestDbContext(_testDbPath);        var db2 = new TestDbContext(_testDbPath);        // Add different entity types
        db1.Users.Add(new User { Name = "UserA", Email = "usera@example.com", Age = 30, Balance = 1000m, CreatedAt = DateTime.UtcNow, IsActive = true });
        db1.Products.Add(new Product { Name = "ProductA", Price = 99.99m, IsPublished = true, LastModified = DateTime.UtcNow });
        await db1.SaveChangesAsync();
        
        // Verify both contexts see both tables
        Assert.Equal(1, db1.Users.Count);
        Assert.Equal(1, db1.Products.Count);
        Assert.Equal(1, db2.Users.Count);
        Assert.Equal(1, db2.Products.Count);
        
        await db1.DisposeAsync();
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task SequentialContexts_MaintainDataIntegrity()
    {
        // Create and use 10 contexts sequentially
        for (int i = 0; i < 10; i++)
        {
            var db = new TestDbContext(_testDbPath);            db.Users.Add(new User 
            { 
                Name = $"User{i}", 
                Email = $"user{i}@example.com", 
                Age = 20 + i, 
                Balance = 100m * i, 
                CreatedAt = DateTime.UtcNow, 
                IsActive = true 
            });
            await db.SaveChangesAsync();
            
            // Verify count matches iteration
            Assert.Equal(i + 1, db.Users.Count);
            
            await db.DisposeAsync();
        }
        
        // Final verification
        var finalDb = new TestDbContext(_testDbPath);        Assert.Equal(10, finalDb.Users.Count);
        await finalDb.DisposeAsync();
    }
}
