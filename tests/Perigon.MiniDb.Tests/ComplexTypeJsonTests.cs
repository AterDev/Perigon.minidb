using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// 复杂类型 - 用户定义的类
public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

// 测试实体 - 使用 JSON 存储复杂类型
public class UserWithComplexType : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    // 复杂类型属性 - 不会直接保存到数据库
    [NotMapped]
    public Address? Address
    {
        get
        {
            // 从 JSON 字符串反序列化
            if (string.IsNullOrEmpty(AddressJsonString))
                return null;

            try
            {
                return JsonSerializer.Deserialize<Address>(AddressJsonString);
            }
            catch
            {
                return null;
            }
        }
        set
        {
            // 序列化为 JSON 字符串
            if (value == null)
            {
                AddressJsonString = string.Empty;
            }
            else
            {
                AddressJsonString = JsonSerializer.Serialize(value);
            }
        }
    }

    // 实际存储的 JSON 字符串
    [MaxLength(500)]  // 足够存储地址 JSON
    public string AddressJsonString { get; set; } = string.Empty;
}

// 测试 DbContext
public class ComplexTypeTestContext : MiniDbContext
{
    public DbSet<UserWithComplexType> Users { get; set; } = null!;
}

/// <summary>
/// 测试通过 JSON 序列化支持复杂类型存储
/// </summary>
public class ComplexTypeJsonTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public ComplexTypeJsonTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_complextype_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<ComplexTypeTestContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task ComplexType_SerializedAsJson_CanBeStored()
    {
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            var user = new UserWithComplexType
            {
                Name = "Alice",
                Email = "alice@example.com",
                Address = new Address
                {
                    Street = "123 Main St",
                    City = "New York",
                    Country = "USA",
                    ZipCode = "10001"
                }
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // 验证 JSON 字符串已生成
            Assert.NotEmpty(user.AddressJsonString);
            Assert.Contains("123 Main St", user.AddressJsonString);
            Assert.Contains("New York", user.AddressJsonString);
        }

        // Reload and verify deserialization
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // 验证基本属性
            Assert.Equal("Alice", loaded.Name);
            Assert.Equal("alice@example.com", loaded.Email);

            // 验证复杂类型通过 getter 正确反序列化
            Assert.NotNull(loaded.Address);
            Assert.Equal("123 Main St", loaded.Address!.Street);
            Assert.Equal("New York", loaded.Address.City);
            Assert.Equal("USA", loaded.Address.Country);
            Assert.Equal("10001", loaded.Address.ZipCode);
        }
    }

    [Fact]
    public async Task ComplexType_LoadedFromJson_WorksCorrectly()
    {
        // 1. 保存带有复杂类型的实体
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            var user = new UserWithComplexType
            {
                Name = "Bob",
                Email = "bob@example.com",
                Address = new Address
                {
                    Street = "456 Oak Ave",
                    City = "Los Angeles",
                    Country = "USA",
                    ZipCode = "90001"
                }
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // 2. 重新加载并验证复杂类型
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // 验证基本属性
            Assert.Equal("Bob", loaded.Name);
            Assert.Equal("bob@example.com", loaded.Email);

            // 验证复杂类型通过 getter 正确反序列化
            Assert.NotNull(loaded.Address);
            Assert.Equal("456 Oak Ave", loaded.Address!.Street);
            Assert.Equal("Los Angeles", loaded.Address.City);
            Assert.Equal("USA", loaded.Address.Country);
            Assert.Equal("90001", loaded.Address.ZipCode);
        }
    }

    [Fact]
    public async Task ComplexType_NullValue_HandledCorrectly()
    {
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            var user = new UserWithComplexType
            {
                Name = "Charlie",
                Email = "charlie@example.com",
                Address = null  // 空地址
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // 验证 JSON 字符串为空
            Assert.Equal(string.Empty, user.AddressJsonString);
        }

        // Reload
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // 验证空值处理正确
            Assert.Null(loaded.Address);
            Assert.Equal(string.Empty, loaded.AddressJsonString);
        }
    }

    [Fact]
    public async Task ComplexType_UpdateAddress_UpdatesJsonString()
    {
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            var user = new UserWithComplexType
            {
                Name = "David",
                Email = "david@example.com",
                Address = new Address
                {
                    Street = "789 Pine Rd",
                    City = "Chicago",
                    Country = "USA",
                    ZipCode = "60601"
                }
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            // 更新地址
            user.Address = new Address
            {
                Street = "321 Elm St",
                City = "Boston",
                Country = "USA",
                ZipCode = "02101"
            };

            db.Users.Update(user);
            await db.SaveChangesAsync();
        }

        // 重新加载并验证更新
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // 验证地址已更新
            Assert.NotNull(loaded.Address);
            Assert.Equal("321 Elm St", loaded.Address!.Street);
            Assert.Equal("Boston", loaded.Address.City);
            Assert.Equal("02101", loaded.Address.ZipCode);
        }
    }

    [Fact]
    public async Task ComplexType_ChineseCharacters_SerializedCorrectly()
    {
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            var user = new UserWithComplexType
            {
                Name = "张三",
                Email = "zhangsan@example.com",
                Address = new Address
                {
                    Street = "北京市朝阳区建国路",
                    City = "北京",
                    Country = "中国",
                    ZipCode = "100000"
                }
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // 重新加载并验证中文
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // 验证中文字符正确处理
            Assert.Equal("张三", loaded.Name);
            Assert.NotNull(loaded.Address);
            Assert.Equal("北京市朝阳区建国路", loaded.Address!.Street);
            Assert.Equal("北京", loaded.Address.City);
            Assert.Equal("中国", loaded.Address.Country);
        }
    }

    [Fact]
    public async Task ComplexType_MultipleUsers_AllAddressesPreserved()
    {
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            // 添加多个用户，每个有不同的地址
            var users = new[]
            {
                new UserWithComplexType
                {
                    Name = "User1",
                    Email = "user1@example.com",
                    Address = new Address { Street = "Street 1", City = "City 1", Country = "Country 1", ZipCode = "11111" }
                },
                new UserWithComplexType
                {
                    Name = "User2",
                    Email = "user2@example.com",
                    Address = new Address { Street = "Street 2", City = "City 2", Country = "Country 2", ZipCode = "22222" }
                },
                new UserWithComplexType
                {
                    Name = "User3",
                    Email = "user3@example.com",
                    Address = new Address { Street = "Street 3", City = "City 3", Country = "Country 3", ZipCode = "33333" }
                }
            };

            foreach (var user in users)
            {
                db.Users.Add(user);
            }
            await db.SaveChangesAsync();
        }

        // 重新加载并验证所有地址
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loadedUsers = db2.Users.OrderBy(u => u.Name).ToList();

            Assert.Equal(3, loadedUsers.Count);

            for (int i = 0; i < 3; i++)
            {
                var address = loadedUsers[i].Address;
                Assert.NotNull(address);
                Assert.Equal($"Street {i + 1}", address.Street);
                Assert.Equal($"City {i + 1}", address.City);
                Assert.Equal($"Country {i + 1}", address.Country);
                Assert.Equal($"{(i + 1)}{(i + 1)}{(i + 1)}{(i + 1)}{(i + 1)}", address.ZipCode);
            }
        }
    }

    [Fact]
    public async Task ComplexType_InvalidJson_ReturnsNull()
    {
        var db = new ComplexTypeTestContext();
        await using (db)
        {
            var user = new UserWithComplexType
            {
                Name = "InvalidUser",
                Email = "invalid@example.com",
                // 直接设置无效的 JSON 字符串
                AddressJsonString = "{ invalid json }"
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        // 重新加载
        await ComplexTypeTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new ComplexTypeTestContext();
        await using (db2)
        {
            var loaded = db2.Users.First();

            // 无效的 JSON 应该返回 null，而不是抛出异常
            Assert.Null(loaded.Address);
        }
    }
}
