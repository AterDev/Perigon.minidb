using System.ComponentModel.DataAnnotations;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// 测试实体 - 使用 required 修饰符
public class EntityWithRequired : IMicroEntity
{
    public int Id { get; set; }

    // 使用 required 关键字（C# 11+ 特性）
    [MaxLength(50)]
    public required string Name { get; set; }

    [MaxLength(100)]
    public required string Email { get; set; }

    public int Age { get; set; }
}

// 测试 DbContext
public class RequiredTestContext : MiniDbContext
{
    public DbSet<EntityWithRequired> Entities { get; set; } = null!;
}

/// <summary>
/// 测试 DbSet 与 required 属性的兼容性
/// </summary>
public class RequiredPropertyTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public RequiredPropertyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_required_{Guid.NewGuid()}.mdb");
        MiniDbConfiguration.AddDbContext<RequiredTestContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await RequiredTestContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task RequiredProperties_CanBeUsedWithDbSet()
    {
        var db = new RequiredTestContext();
        await using (db)
        {
            // 使用对象初始化器设置 required 属性
            var entity = new EntityWithRequired
            {
                Name = "Alice",
                Email = "alice@example.com",
                Age = 25
            };

            db.Entities.Add(entity);
            await db.SaveChangesAsync();

            Assert.Equal(1, entity.Id);
        }
    }

    [Fact]
    public async Task RequiredProperties_SaveAndLoadCorrectly()
    {
        var db = new RequiredTestContext();
        await using (db)
        {
            var entity = new EntityWithRequired
            {
                Name = "Bob",
                Email = "bob@example.com",
                Age = 30
            };

            db.Entities.Add(entity);
            await db.SaveChangesAsync();
        }

        // 重新加载
        await RequiredTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new RequiredTestContext();
        await using (db2)
        {
            var loaded = db2.Entities.First();

            // 验证 required 属性正确加载
            Assert.Equal("Bob", loaded.Name);
            Assert.Equal("bob@example.com", loaded.Email);
            Assert.Equal(30, loaded.Age);
        }
    }

    [Fact]
    public async Task RequiredProperties_UpdateWorks()
    {
        var db = new RequiredTestContext();
        await using (db)
        {
            var entity = new EntityWithRequired
            {
                Name = "Charlie",
                Email = "charlie@example.com",
                Age = 35
            };

            db.Entities.Add(entity);
            await db.SaveChangesAsync();

            // 更新 required 属性
            entity.Name = "Charlie Updated";
            entity.Email = "charlie.updated@example.com";
            db.Entities.Update(entity);
            await db.SaveChangesAsync();
        }

        // 重新加载并验证
        await RequiredTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new RequiredTestContext();
        await using (db2)
        {
            var loaded = db2.Entities.First();

            Assert.Equal("Charlie Updated", loaded.Name);
            Assert.Equal("charlie.updated@example.com", loaded.Email);
        }
    }
}
