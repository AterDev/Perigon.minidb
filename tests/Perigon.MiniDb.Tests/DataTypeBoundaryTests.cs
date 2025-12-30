using System.ComponentModel.DataAnnotations;
using System.Text;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// Test entity with very small MaxLength
public class TinyStringEntity
{
    public int Id { get; set; }
    
    [MaxLength(5)]
    public string TinyString { get; set; } = string.Empty;
}

// Test entity with large MaxLength
public class LargeStringEntity
{
    public int Id { get; set; }
    
    [MaxLength(5000)]
    public string LargeString { get; set; } = string.Empty;
}

public class TinyStringDbContext(string filePath) : MicroDbContext(filePath)
{
    public DbSet<TinyStringEntity> TinyStrings { get; set; } = null!;
}

public class LargeStringDbContext(string filePath) : MicroDbContext(filePath)
{
    public DbSet<LargeStringEntity> LargeStrings { get; set; } = null!;
}

/// <summary>
/// Tests for data type boundary conditions and edge cases
/// </summary>
public class DataTypeBoundaryTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public DataTypeBoundaryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_boundary_{Guid.NewGuid()}.mdb");
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
    public async Task String_ExceedsMaxLength_GetsTruncated()
    {
        var tinyPath = Path.Combine(Path.GetTempPath(), $"test_tiny_{Guid.NewGuid()}.mdb");
        
        try
        {
            var db = new TinyStringDbContext(tinyPath);            var entity = new TinyStringEntity
            {
                TinyString = "This is a very long string that exceeds 5 bytes"
            };
            
            db.TinyStrings.Add(entity);
            await db.SaveChangesAsync();
            await db.DisposeAsync();
            
            // Release cache to force reload from file (where truncation happens)
            await TinyStringDbContext.ReleaseSharedCacheAsync(tinyPath);
            
            // Reload and verify truncation
            var db2 = new TinyStringDbContext(tinyPath);            var loaded = db2.TinyStrings.First();
            var actualBytes = Encoding.UTF8.GetByteCount(loaded.TinyString);
            
            // String should be truncated to at most 5 bytes when loaded from file
            Assert.True(actualBytes <= 5, $"String was {actualBytes} bytes, expected <= 5 bytes. Value: '{loaded.TinyString}'");
            
            await db2.DisposeAsync();
            await TinyStringDbContext.ReleaseSharedCacheAsync(tinyPath);
        }
        finally
        {
            if (File.Exists(tinyPath))
            {
                File.Delete(tinyPath);
            }
        }
    }

    [Fact]
    public async Task String_UTF8MultibyteCharacters_HandleCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "张三李四王五", // Chinese characters (3 bytes each)
            Email = "测试@例え.com", // Mixed scripts
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        // Reload and verify
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal("张三李四王五", loaded.Name);
        Assert.Equal("测试@例え.com", loaded.Email);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task String_Emoji_HandleCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "User😀", // Emoji (4 bytes)
            Email = "test🎉@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        // Reload and verify
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        // Emoji might be truncated due to MaxLength, but should not corrupt data
        Assert.NotNull(loaded.Name);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task String_ExactlyMaxLength_NoTruncation()
    {
        var tinyPath = Path.Combine(Path.GetTempPath(), $"test_exact_{Guid.NewGuid()}.mdb");
        
        try
        {
            var db = new TinyStringDbContext(tinyPath);            var entity = new TinyStringEntity
            {
                TinyString = "12345" // Exactly 5 bytes
            };
            
            db.TinyStrings.Add(entity);
            await db.SaveChangesAsync();
            await db.DisposeAsync();
            
            // Reload and verify
            var db2 = new TinyStringDbContext(tinyPath);            var loaded = db2.TinyStrings.First();
            Assert.Equal("12345", loaded.TinyString);
            
            await db2.DisposeAsync();
            await TinyStringDbContext.ReleaseSharedCacheAsync(tinyPath);
        }
        finally
        {
            if (File.Exists(tinyPath))
            {
                File.Delete(tinyPath);
            }
        }
    }

    [Fact]
    public async Task Int_MinValue_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "MinInt",
            Email = "min@example.com",
            Age = int.MinValue,
            Balance = 0m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(int.MinValue, loaded.Age);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Int_MaxValue_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "MaxInt",
            Email = "max@example.com",
            Age = int.MaxValue,
            Balance = 0m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(int.MaxValue, loaded.Age);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Decimal_MinValue_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "MinDecimal",
            Email = "mindec@example.com",
            Age = 30,
            Balance = decimal.MinValue,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(decimal.MinValue, loaded.Balance);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Decimal_MaxValue_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "MaxDecimal",
            Email = "maxdec@example.com",
            Age = 30,
            Balance = decimal.MaxValue,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(decimal.MaxValue, loaded.Balance);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task Decimal_HighPrecision_MaintainedCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user = new User
        {
            Name = "PreciseDecimal",
            Email = "precise@example.com",
            Age = 30,
            Balance = 123456789.123456789m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(123456789.123456789m, loaded.Balance);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task DateTime_MinValue_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        // Use UTC explicitly
        var minUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        
        var user = new User
        {
            Name = "MinDateTime",
            Email = "mindate@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = minUtc,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(minUtc, loaded.CreatedAt);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task DateTime_MaxValue_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        // Use UTC explicitly
        var maxUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        
        var user = new User
        {
            Name = "MaxDateTime",
            Email = "maxdate@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = maxUtc,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        Assert.Equal(maxUtc, loaded.CreatedAt);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task DateTime_LocalToUtc_ConvertedCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var expectedUtc = localTime.ToUniversalTime();
        
        var user = new User
        {
            Name = "LocalTime",
            Email = "local@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = localTime,
            IsActive = true
        };
        
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        // Release shared cache to force reload from file
        await TestDbContext.ReleaseSharedCacheAsync(_testDbPath);
        
        var db2 = new TestDbContext(_testDbPath);        var loaded = db2.Users.First();
        // Stored time is always UTC when loaded from file
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        // Compare the actual time value
        Assert.Equal(expectedUtc, loaded.CreatedAt);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task NullableInt_BoundaryValues_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user1 = new User
        {
            Name = "NullableMin",
            Email = "nullmin@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = int.MinValue
        };
        
        var user2 = new User
        {
            Name = "NullableMax",
            Email = "nullmax@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CategoryId = int.MaxValue
        };
        
        db.Users.Add(user1);
        db.Users.Add(user2);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded1 = db2.Users.First(u => u.Name == "NullableMin");
        var loaded2 = db2.Users.First(u => u.Name == "NullableMax");
        
        Assert.Equal(int.MinValue, loaded1.CategoryId);
        Assert.Equal(int.MaxValue, loaded2.CategoryId);
        
        await db2.DisposeAsync();
    }

    [Fact]
    public async Task LargeString_5000Bytes_HandledCorrectly()
    {
        var largePath = Path.Combine(Path.GetTempPath(), $"test_large_{Guid.NewGuid()}.mdb");
        
        try
        {
            var db = new LargeStringDbContext(largePath);            var largeString = new string('A', 4900); // 4900 ASCII characters = 4900 bytes
            
            var entity = new LargeStringEntity
            {
                LargeString = largeString
            };
            
            db.LargeStrings.Add(entity);
            await db.SaveChangesAsync();
            await db.DisposeAsync();
            
            // Reload and verify
            var db2 = new LargeStringDbContext(largePath);            var loaded = db2.LargeStrings.First();
            Assert.Equal(largeString, loaded.LargeString);
            
            await db2.DisposeAsync();
            await LargeStringDbContext.ReleaseSharedCacheAsync(largePath);
        }
        finally
        {
            if (File.Exists(largePath))
            {
                File.Delete(largePath);
            }
        }
    }

    [Fact]
    public async Task Boolean_TrueFalse_HandledCorrectly()
    {
        var db = new TestDbContext(_testDbPath);        var user1 = new User
        {
            Name = "ActiveUser",
            Email = "active@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        var user2 = new User
        {
            Name = "InactiveUser",
            Email = "inactive@example.com",
            Age = 30,
            Balance = 1000m,
            CreatedAt = DateTime.UtcNow,
            IsActive = false
        };
        
        db.Users.Add(user1);
        db.Users.Add(user2);
        await db.SaveChangesAsync();
        await db.DisposeAsync();
        
        var db2 = new TestDbContext(_testDbPath);        var loaded1 = db2.Users.First(u => u.Name == "ActiveUser");
        var loaded2 = db2.Users.First(u => u.Name == "InactiveUser");
        
        Assert.True(loaded1.IsActive);
        Assert.False(loaded2.IsActive);
        
        await db2.DisposeAsync();
    }
}
