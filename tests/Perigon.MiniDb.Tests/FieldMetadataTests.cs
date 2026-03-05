using System.Text;

namespace Perigon.MiniDb.Tests;

/// <summary>
/// Isolated DbContext for FieldMetadataTests to avoid parallel test config conflicts.
/// </summary>
public class FieldMetaTestDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}

/// <summary>
/// Isolated Complex DbContext for FieldMetadataTests enum/nullable enum tests.
/// </summary>
public class FieldMetaComplexDbContext : MiniDbContext
{
    public DbSet<Solution> Solutions { get; set; } = null!;
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<ApiDocumentation> ApiDocumentations { get; set; } = null!;
    public DbSet<AppConfiguration> Configurations { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
}

/// <summary>
/// Tests that verify field metadata is correctly stored in the binary file format.
/// Uses binary reading to validate StorageManager's field metadata write path.
/// </summary>
public class FieldMetadataTests : IAsyncDisposable
{
    private const int FileHeaderSize = 256;
    private const int TableMetaSize = 128;
    private const int TableNameBytes = 64;
    private const int FieldMetaEntrySize = 80;
    private const int FieldNameBytes = 64;

    private readonly string _testDbPath;

    public FieldMetadataTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_fieldmeta_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<FieldMetaTestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(10);
        if (File.Exists(_testDbPath))
            File.Delete(_testDbPath);
    }

    [Fact]
    public async Task CreateDatabase_WritesFieldMetadataForAllTables()
    {
        // Arrange & Act: Creating the context triggers CreateDatabase
        await using var db = new FieldMetaTestDbContext();

        // Assert: Read binary file and verify field metadata is present
        using var file = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8);

        // Skip to table count
        reader.ReadBytes(4); // magic
        reader.ReadInt16();  // version
        var tableCount = reader.ReadInt16();
        Assert.True(tableCount >= 2, "Expected at least 2 tables (Users, Products)");

        // Skip rest of header
        file.Seek(FileHeaderSize, SeekOrigin.Begin);

        // Read each table's metadata and verify field metadata offset/count
        for (int i = 0; i < tableCount; i++)
        {
            reader.ReadBytes(TableNameBytes); // name
            reader.ReadInt32(); // recordCount
            reader.ReadInt32(); // recordSize
            reader.ReadInt64(); // dataStartOffset
            reader.ReadInt32(); // reservedRecordCount
            reader.ReadInt32(); // tableIndex
            reader.ReadInt64(); // extentDirectoryOffset
            reader.ReadInt32(); // extentCount

            var fieldMetadataOffset = reader.ReadInt64();
            var fieldCount = reader.ReadInt32();

            Assert.True(fieldMetadataOffset > 0, $"Table {i}: FieldMetadataOffset should be > 0");
            Assert.True(fieldCount > 0, $"Table {i}: FieldCount should be > 0");

            reader.ReadBytes(16); // remaining reserved
        }
    }

    [Fact]
    public async Task FieldMetadata_UserTable_HasCorrectFieldNamesAndTypes()
    {
        await using var db = new FieldMetaTestDbContext();

        var (offset, count) = ReadTableFieldMetaInfo("Users");
        Assert.True(offset > 0);
        Assert.True(count > 0);

        var fields = ReadFieldMetadataEntries(offset, count);

        // User entity has these properties (sorted alphabetically, excluding Id):
        // Age (int), Balance (decimal), CategoryId (int?), CreatedAt (DateTime),
        // Email (string), IsActive (bool), Name (string), PublishedAt (DateTime?)
        Assert.Equal(8, fields.Count);

        var fieldDict = fields.ToDictionary(f => f.Name, f => f);

        Assert.Equal(FieldTypeCode.Int32, fieldDict["Age"].TypeCode);
        Assert.False(fieldDict["Age"].IsNullable);

        Assert.Equal(FieldTypeCode.Decimal, fieldDict["Balance"].TypeCode);
        Assert.False(fieldDict["Balance"].IsNullable);

        Assert.Equal(FieldTypeCode.Int32, fieldDict["CategoryId"].TypeCode);
        Assert.True(fieldDict["CategoryId"].IsNullable);

        Assert.Equal(FieldTypeCode.DateTime, fieldDict["CreatedAt"].TypeCode);
        Assert.False(fieldDict["CreatedAt"].IsNullable);

        Assert.Equal(FieldTypeCode.String, fieldDict["Email"].TypeCode);
        Assert.False(fieldDict["Email"].IsNullable);
        Assert.Equal(100, fieldDict["Email"].Size);

        Assert.Equal(FieldTypeCode.Boolean, fieldDict["IsActive"].TypeCode);
        Assert.False(fieldDict["IsActive"].IsNullable);

        Assert.Equal(FieldTypeCode.String, fieldDict["Name"].TypeCode);
        Assert.False(fieldDict["Name"].IsNullable);
        Assert.Equal(50, fieldDict["Name"].Size);

        Assert.Equal(FieldTypeCode.DateTime, fieldDict["PublishedAt"].TypeCode);
        Assert.True(fieldDict["PublishedAt"].IsNullable);
    }

    [Fact]
    public async Task FieldMetadata_ProductTable_HasCorrectFieldNamesAndTypes()
    {
        await using var db = new FieldMetaTestDbContext();

        var (offset, count) = ReadTableFieldMetaInfo("Products");
        Assert.True(offset > 0);
        Assert.True(count > 0);

        var fields = ReadFieldMetadataEntries(offset, count);

        // Product: IsPublished (bool?), LastModified (DateTime?), Name (string), Price (decimal?)
        Assert.Equal(4, fields.Count);

        var fieldDict = fields.ToDictionary(f => f.Name, f => f);

        Assert.Equal(FieldTypeCode.Boolean, fieldDict["IsPublished"].TypeCode);
        Assert.True(fieldDict["IsPublished"].IsNullable);

        Assert.Equal(FieldTypeCode.DateTime, fieldDict["LastModified"].TypeCode);
        Assert.True(fieldDict["LastModified"].IsNullable);

        Assert.Equal(FieldTypeCode.String, fieldDict["Name"].TypeCode);
        Assert.Equal(100, fieldDict["Name"].Size);

        Assert.Equal(FieldTypeCode.Decimal, fieldDict["Price"].TypeCode);
        Assert.True(fieldDict["Price"].IsNullable);
    }

    [Fact]
    public async Task FieldMetadata_EnumFields_StoredAsEnumTypeCode()
    {
        var enumDbPath = Path.Combine(Path.GetTempPath(), $"test_enum_meta_{Guid.NewGuid()}.mds");

        try
        {
            MiniDbConfiguration.AddDbContext<FieldMetaComplexDbContext>(o => o.UseMiniDb(enumDbPath));
            await using var db = new FieldMetaComplexDbContext();

            var (offset, count) = ReadTableFieldMetaInfo("Orders", enumDbPath);
            var fields = ReadFieldMetadataEntries(offset, count, enumDbPath);

            var statusField = fields.First(f => f.Name == "Status");
            Assert.Equal(FieldTypeCode.Enum, statusField.TypeCode);
            Assert.False(statusField.IsNullable);
            Assert.Equal(4, statusField.Size); // enum stored as int32
        }
        finally
        {
            await Task.Delay(10);
            if (File.Exists(enumDbPath))
                File.Delete(enumDbPath);
        }
    }

    [Fact]
    public async Task FieldMetadata_NullableEnum_StoredCorrectly()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_nullable_enum_{Guid.NewGuid()}.mds");

        try
        {
            MiniDbConfiguration.AddDbContext<FieldMetaComplexDbContext>(o => o.UseMiniDb(dbPath));
            await using var db = new FieldMetaComplexDbContext();

            var (offset, count) = ReadTableFieldMetaInfo("Solutions", dbPath);
            var fields = ReadFieldMetadataEntries(offset, count, dbPath);

            var solutionTypeField = fields.First(f => f.Name == "SolutionType");
            Assert.Equal(FieldTypeCode.Enum, solutionTypeField.TypeCode);
            Assert.True(solutionTypeField.IsNullable);
            Assert.Equal(5, solutionTypeField.Size); // 4 bytes enum + 1 nullable byte
        }
        finally
        {
            await Task.Delay(10);
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task FieldMetadata_SurvivesReload()
    {
        // Create DB and insert data
        await using (var db = new FieldMetaTestDbContext())
        {
            db.Users.Add(new User
            {
                Name = "Alice",
                Email = "alice@example.com",
                Age = 30,
                Balance = 99.99m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // Re-open
        MiniDbConfiguration.AddDbContext<FieldMetaTestDbContext>(o => o.UseMiniDb(_testDbPath));

        await using (var db = new FieldMetaTestDbContext())
        {
            // Verify data is still accessible
            var users = db.Users.ToList();
            Assert.Single(users);
            Assert.Equal("Alice", users[0].Name);
        }

        // Verify field metadata is still readable after reload
        var (offset, count) = ReadTableFieldMetaInfo("Users");
        Assert.True(offset > 0);
        Assert.Equal(8, count);

        var fields = ReadFieldMetadataEntries(offset, count);
        Assert.Contains(fields, f => f.Name == "Name" && f.TypeCode == FieldTypeCode.String);
        Assert.Contains(fields, f => f.Name == "Age" && f.TypeCode == FieldTypeCode.Int32);
    }

    [Fact]
    public async Task FieldMetadata_DataCanBeReadBackCorrectlyAfterInsert()
    {
        await using var db = new FieldMetaTestDbContext();

        var now = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User
        {
            Name = "Bob",
            Email = "bob@test.com",
            Age = 25,
            Balance = 100.50m,
            CreatedAt = now,
            IsActive = true,
            CategoryId = 42,
            PublishedAt = now
        });
        await db.SaveChangesAsync();

        // Read back via DbContext to verify data integrity
        var user = db.Users.First(u => u.Name == "Bob");
        Assert.Equal("bob@test.com", user.Email);
        Assert.Equal(25, user.Age);
        Assert.Equal(100.50m, user.Balance);
        Assert.Equal(now, user.CreatedAt);
        Assert.True(user.IsActive);
        Assert.Equal(42, user.CategoryId);
        Assert.Equal(now, user.PublishedAt);
    }

    [Fact]
    public async Task FieldMetadata_OffsetPlacementIsBeforeDataArea()
    {
        await using var db = new FieldMetaTestDbContext();

        // Read table metadata to verify field metadata comes before data areas
        using var file = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8);

        file.Seek(4, SeekOrigin.Begin); // skip magic
        reader.ReadInt16(); // version
        var tableCount = reader.ReadInt16();
        file.Seek(FileHeaderSize, SeekOrigin.Begin);

        long minDataStartOffset = long.MaxValue;
        long maxFieldMetaEnd = 0;

        for (int i = 0; i < tableCount; i++)
        {
            reader.ReadBytes(TableNameBytes);
            reader.ReadInt32(); // recordCount
            reader.ReadInt32(); // recordSize
            var dataStartOffset = reader.ReadInt64();
            reader.ReadInt32(); // reservedRecordCount
            reader.ReadInt32(); // tableIndex
            reader.ReadInt64(); // extentDirectoryOffset
            reader.ReadInt32(); // extentCount
            var fieldMetadataOffset = reader.ReadInt64();
            var fieldCount = reader.ReadInt32();
            reader.ReadBytes(16); // reserved

            if (dataStartOffset < minDataStartOffset)
                minDataStartOffset = dataStartOffset;

            var fieldMetaEnd = fieldMetadataOffset + (fieldCount * FieldMetaEntrySize);
            if (fieldMetaEnd > maxFieldMetaEnd)
                maxFieldMetaEnd = fieldMetaEnd;
        }

        // Field metadata sections must end before or at the start of the first data area
        Assert.True(maxFieldMetaEnd <= minDataStartOffset,
            $"Field metadata end ({maxFieldMetaEnd}) should be <= first data area start ({minDataStartOffset})");
    }

    #region Binary reading helpers

    private record FieldMetaEntry(string Name, FieldTypeCode TypeCode, int Size, bool IsNullable);

    private (long Offset, int Count) ReadTableFieldMetaInfo(string tableName, string? filePath = null)
    {
        var path = filePath ?? _testDbPath;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8);

        file.Seek(4, SeekOrigin.Begin);
        reader.ReadInt16(); // version
        var tableCount = reader.ReadInt16();
        file.Seek(FileHeaderSize, SeekOrigin.Begin);

        for (int i = 0; i < tableCount; i++)
        {
            var nameBytes = reader.ReadBytes(TableNameBytes);
            var name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            reader.ReadInt32(); // recordCount
            reader.ReadInt32(); // recordSize
            reader.ReadInt64(); // dataStartOffset
            reader.ReadInt32(); // reservedRecordCount
            reader.ReadInt32(); // tableIndex
            reader.ReadInt64(); // extentDirectoryOffset
            reader.ReadInt32(); // extentCount
            var fieldMetadataOffset = reader.ReadInt64();
            var fieldCount = reader.ReadInt32();
            reader.ReadBytes(16); // reserved

            if (string.Equals(name, tableName, StringComparison.Ordinal))
                return (fieldMetadataOffset, fieldCount);
        }

        throw new InvalidOperationException($"Table '{tableName}' not found in file.");
    }

    private List<FieldMetaEntry> ReadFieldMetadataEntries(long offset, int count, string? filePath = null)
    {
        var path = filePath ?? _testDbPath;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8);

        file.Seek(offset, SeekOrigin.Begin);
        var result = new List<FieldMetaEntry>(count);

        for (int i = 0; i < count; i++)
        {
            var nameBytes = reader.ReadBytes(FieldNameBytes);
            var name = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            var typeCode = (FieldTypeCode)reader.ReadInt32();
            var size = reader.ReadInt32();
            var isNullable = reader.ReadByte() == 1;
            reader.ReadBytes(7); // reserved

            result.Add(new FieldMetaEntry(name, typeCode, size, isNullable));
        }

        return result;
    }

    #endregion
}
