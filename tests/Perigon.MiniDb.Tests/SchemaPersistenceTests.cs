using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Perigon.MiniDb.Tests;

public sealed class SchemaEntity : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool IsActive { get; set; }
}

public sealed class SchemaTestContext : MiniDbContext
{
    public DbSet<SchemaEntity> Items { get; set; } = null!;
}

public class SchemaPersistenceTests : IAsyncDisposable
{
    private const int FileHeaderSize = 256;
    private const int HeaderRemaining = 240;
    private const int TableMetaSize = 128;
    private const int TableMetaReservedV2 = 16;

    private readonly string _testDbPath;

    public SchemaPersistenceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_schema_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<SchemaTestContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await SchemaTestContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task CreateDatabase_WritesSchemaMetadataIntoMdsFile()
    {
        var db = new SchemaTestContext();
        await using (db)
        {
            db.Items.Add(new SchemaEntity
            {
                Name = "A",
                Amount = 12.34m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });

            await db.SaveChangesAsync();
        }

        await SchemaTestContext.ReleaseSharedCacheAsync(_testDbPath);

        using var file = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);

        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        Assert.Equal("MDB1", magic);

        var version = reader.ReadInt16();
        Assert.Equal(2, version);

        var tableCount = reader.ReadInt16();
        Assert.Equal(1, tableCount);

        _ = reader.ReadInt64(); // global write version
        reader.ReadBytes(HeaderRemaining);

        var tableNameBytes = reader.ReadBytes(64);
        var tableName = Encoding.UTF8.GetString(tableNameBytes).TrimEnd('\0');
        Assert.Equal("Items", tableName);

        _ = reader.ReadInt32(); // record count
        _ = reader.ReadInt32(); // record size
        _ = reader.ReadInt64(); // data start
        _ = reader.ReadInt32(); // reserved record count
        _ = reader.ReadInt32(); // table index
        _ = reader.ReadInt64(); // extent directory offset
        _ = reader.ReadInt32(); // extent count
        var schemaOffset = reader.ReadInt64();
        var schemaLength = reader.ReadInt32();
        reader.ReadBytes(TableMetaReservedV2);

        Assert.True(schemaOffset >= FileHeaderSize + (tableCount * TableMetaSize));
        Assert.True(schemaLength > 0);

        file.Seek(schemaOffset, SeekOrigin.Begin);
        var schemaVersion = reader.ReadInt32();
        Assert.Equal(EntityMetadata.CurrentSchemaVersion, schemaVersion);

        var fieldCount = reader.ReadInt32();
        Assert.True(fieldCount >= 5); // Id + 4 entity fields

        var fields = new Dictionary<string, (byte Type, int MaxLength, bool IsPrimaryKey, bool IsNullable)>(StringComparer.Ordinal);
        for (var i = 0; i < fieldCount; i++)
        {
            var nameLen = reader.ReadInt16();
            var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLen));
            var type = reader.ReadByte();
            _ = reader.ReadInt32(); // offset
            _ = reader.ReadInt32(); // size
            var maxLength = reader.ReadInt32();
            var isPrimaryKey = reader.ReadBoolean();
            var isNullable = reader.ReadBoolean();

            fields[name] = (type, maxLength, isPrimaryKey, isNullable);
        }

        Assert.True(fields.ContainsKey("Id"));
        Assert.True(fields["Id"].IsPrimaryKey);
        Assert.False(fields["Id"].IsNullable);

        Assert.True(fields.ContainsKey("Name"));
        Assert.Equal(5, fields["Name"].Type); // string
        Assert.Equal(32, fields["Name"].MaxLength);
        Assert.False(fields["Name"].IsNullable);

        Assert.True(fields.ContainsKey("CreatedAt"));
        Assert.Equal(4, fields["CreatedAt"].Type); // datetime
        Assert.True(fields["CreatedAt"].IsNullable);
    }
}
