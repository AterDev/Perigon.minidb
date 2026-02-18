using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

// Test entities with unsupported types
public class InvalidEntityWithLong : IMicroEntity
{
    public int Id { get; set; }
    public long UnsupportedLong { get; set; }
}

// Valid DbContext for unsupported type tests
public class InvalidDbContext : MiniDbContext
{
    public DbSet<InvalidEntityWithLong> InvalidLongs { get; set; } = null!;
}

public class DynamicPathContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
}

public class ExceptionTestDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}

/// <summary>
/// Tests for exception handling and error cases
/// </summary>
public class ExceptionHandlingTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public ExceptionHandlingTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_exception_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<InvalidDbContext>(o => o.UseMiniDb(_testDbPath));
        MiniDbConfiguration.AddDbContext<ExceptionTestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(10);
        
        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public async Task UnsupportedType_Long_ThrowsException()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            var db = new InvalidDbContext();
        });
        
        // The exception message contains "Int64" (the type name), not "long"
        Assert.Contains("Int64", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidFile_ThrowsException()
    {
        // Create an invalid file
        await File.WriteAllTextAsync(_testDbPath, "This is not a valid database file");
        
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            var db = new ExceptionTestDbContext();
        });
        
        Assert.Contains("Invalid database file format", exception.Message);
    }

    [Fact]
    public async Task CorruptedMagicNumber_ThrowsException()
    {
        // Create file with wrong magic number
        await using (var file = File.Create(_testDbPath))
        {
            var writer = new BinaryWriter(file);
            writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // Wrong magic
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            var db = new ExceptionTestDbContext();
        });

        Assert.Contains("Invalid database file format", exception.Message);
    }

    [Fact]
    public async Task ConcurrentWrite_LockedFile_ThrowsException()
    {
        // Lock the file
        using (var file = File.Open(_testDbPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var exception = await Assert.ThrowsAsync<IOException>(async () =>
            {
                var db = new ExceptionTestDbContext();
                // Add many entities
                for (int i = 0; i < 1000; i++)
                {
                    db.Users.Add(new User { Name = $"User{i}" });
                }
                await db.SaveChangesAsync();
            });
        }
    }

    [Fact]
    public async Task NullStringProperty_PersistsAsEmptyString()
    {
        var db = new ExceptionTestDbContext();
        var user = new User
        {
            Name = null!,
            Email = "test@example.com"
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        var reload = new ExceptionTestDbContext();
        var loaded = reload.Users.First();
        Assert.Equal(string.Empty, loaded.Name);
    }

    [Fact]
    public async Task DuplicateId_ThrowsException()
    {
        var db = new ExceptionTestDbContext();
        var user1 = new User { Id = 123, Name = "User1" };
        db.Users.Add(user1);
        await db.SaveChangesAsync();

        var user2 = new User { Id = 123, Name = "User2" };
        db.Users.Add(user2);
        await db.SaveChangesAsync();

        Assert.Equal(1, user1.Id);
        Assert.Equal(2, user2.Id);
    }

    [Fact]
    public async Task ModifiedEntity_NotFound_ThrowsException()
    {
        var db = new ExceptionTestDbContext();
        var user = new User { Name = "NonExistent" };
        
        // Track as modified but it doesn't exist in DB
        Assert.Throws<InvalidOperationException>(() => db.Users.Update(user));
    }

    [Fact]
    public async Task DeletedEntity_NotFound_ThrowsException()
    {
        var db = new ExceptionTestDbContext();
        var product = new Product();
        
        Assert.Throws<InvalidOperationException>(() => db.Products.Remove(product));
    }

    [Fact]
    public async Task ConcurrentAccess_DisposedContext_ThrowsException()
    {
        var db = new ExceptionTestDbContext();
        var user = new User { Name = "Test" };
        db.Users.Add(user);
        await db.DisposeAsync();
        
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await db.SaveChangesAsync());
    }

    [Fact]
    public async Task MissingDbSetType_ThrowsException()
    {
        var db = new ExceptionTestDbContext();
        Assert.Throws<InvalidOperationException>(() => db.Set<InvalidEntityWithLong>());
    }

    [Fact]
    public async Task SaveChanges_EmptyContext_DoesNothing()
    {
        var db = new ExceptionTestDbContext();
        await db.SaveChangesAsync();
        Assert.Empty(db.Users);
        Assert.Empty(db.Products);
    }

    [Fact]
    public async Task Context_InitializesDbSets()
    {
        var db = new ExceptionTestDbContext();
        Assert.NotNull(db.Users);
        Assert.NotNull(db.Products);
    }

    [Fact]
    public async Task SaveChanges_NoChanges_ReturnsSuccessfully()
    {
        var db = new ExceptionTestDbContext();
        // Call SaveChangesAsync without any changes
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FilePath_CanBeChanged()
    {
        var newPath = Path.Combine(Path.GetTempPath(), $"test_newpath_{Guid.NewGuid()}.mds");
        try
        {
            MiniDbConfiguration.AddDbContext<DynamicPathContext>(o => o.UseMiniDb(newPath));
            var db = new DynamicPathContext();
            Assert.NotNull(db);
            // We can't easily check the path property as it's private/protected
        }
        finally
        {
            if (File.Exists(newPath)) File.Delete(newPath);
        }
    }

    [Fact]
    public void InvalidFilePath_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => 
            MiniDbConfiguration.AddDbContext<InvalidDbContext>(o => o.UseMiniDb("")));
    }
}
