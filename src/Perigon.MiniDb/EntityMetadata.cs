using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace Perigon.MiniDb;

/// <summary>
/// Metadata for entity field mapping
/// </summary>
public class FieldMetadata
{
    public string Name { get; set; } = string.Empty;
    public PropertyInfo Property { get; set; } = null!;
    public Type FieldType { get; set; } = null!;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public int MaxLength { get; set; }
    public int Offset { get; set; }
    public int Size { get; set; }
}

public enum SchemaFieldType : byte
{
    Int32 = 1,
    Boolean = 2,
    Decimal = 3,
    DateTime = 4,
    String = 5,
    Enum = 6
}

public sealed class PersistedFieldSchema
{
    public string Name { get; set; } = string.Empty;
    public SchemaFieldType Type { get; set; }
    public int Offset { get; set; }
    public int Size { get; set; }
    public int MaxLength { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; }
}

public sealed class PersistedTableSchema
{
    public int Version { get; set; } = 1;
    public List<PersistedFieldSchema> Fields { get; set; } = [];
}

/// <summary>
/// Entity metadata containing field mapping and record size information
/// </summary>
public class EntityMetadata
{
    public const int CurrentSchemaVersion = 1;

    public Type EntityType { get; set; } = null!;
    public FieldMetadata[] Fields { get; set; } = [];
    public int RecordSize { get; set; }

    public static EntityMetadata Create(Type entityType)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.Name != "Id")  // Skip Id property (handled separately via IMicroEntity)
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null)  // Skip [NotMapped] properties
            .OrderBy(p => p.Name)  // Sort by name for consistent ordering
            .ToArray();

        var fields = new FieldMetadata[properties.Length];
        int offset = 1; // Skip IsDeleted byte

        // Add Id field first (4 bytes for int Id from IMicroEntity)
        offset += 4;

        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            int size = FieldSizeCalculator.GetFixedSize(prop);
            var effectiveType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var maxLength = effectiveType == typeof(string)
                ? prop.GetCustomAttribute<MaxLengthAttribute>()?.Length ?? 0
                : 0;

            fields[i] = new FieldMetadata
            {
                Name = prop.Name,
                Property = prop,
                FieldType = prop.PropertyType,
                IsNullable = Nullable.GetUnderlyingType(prop.PropertyType) is not null,
                IsPrimaryKey = false,
                MaxLength = maxLength,
                Offset = offset,
                Size = size
            };
            offset += size;
        }

        return new EntityMetadata
        {
            EntityType = entityType,
            Fields = fields,
            RecordSize = offset
        };
    }

    public PersistedTableSchema ToPersistedSchema()
    {
        var schema = new PersistedTableSchema
        {
            Version = CurrentSchemaVersion,
            Fields =
            [
                new PersistedFieldSchema
                {
                    Name = nameof(IMicroEntity.Id),
                    Type = SchemaFieldType.Int32,
                    Offset = 1,
                    Size = 4,
                    MaxLength = 0,
                    IsPrimaryKey = true,
                    IsNullable = false
                }
            ]
        };

        foreach (var field in Fields)
        {
            var effectiveType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            schema.Fields.Add(new PersistedFieldSchema
            {
                Name = field.Name,
                Type = FieldSizeCalculator.GetSchemaFieldType(effectiveType),
                Offset = field.Offset,
                Size = field.Size,
                MaxLength = field.MaxLength,
                IsPrimaryKey = field.IsPrimaryKey,
                IsNullable = field.IsNullable
            });
        }

        return schema;
    }

    public static EntityMetadata CreateFromSchema(Type entityType, PersistedTableSchema schema)
    {
        if (schema.Fields.Count == 0)
        {
            throw new InvalidDataException($"Persisted schema for '{entityType.Name}' contains no fields.");
        }

        var propertyMap = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        var nonIdFields = schema.Fields
            .Where(f => !f.IsPrimaryKey && !string.Equals(f.Name, nameof(IMicroEntity.Id), StringComparison.Ordinal))
            .OrderBy(f => f.Offset)
            .ToList();

        var fields = new FieldMetadata[nonIdFields.Count];
        for (var i = 0; i < nonIdFields.Count; i++)
        {
            var persisted = nonIdFields[i];
            if (!propertyMap.TryGetValue(persisted.Name, out var property))
            {
                throw new InvalidDataException($"Field '{persisted.Name}' from persisted schema not found on entity '{entityType.Name}'.");
            }

            fields[i] = new FieldMetadata
            {
                Name = persisted.Name,
                Property = property,
                FieldType = property.PropertyType,
                IsNullable = persisted.IsNullable,
                IsPrimaryKey = persisted.IsPrimaryKey,
                MaxLength = persisted.MaxLength,
                Offset = persisted.Offset,
                Size = persisted.Size
            };
        }

        var recordSize = schema.Fields.Max(f => f.Offset + f.Size);
        return new EntityMetadata
        {
            EntityType = entityType,
            Fields = fields,
            RecordSize = recordSize
        };
    }
}

/// <summary>
/// Calculate fixed size for supported data types
/// </summary>
public static class FieldSizeCalculator
{
    private static readonly FrozenDictionary<Type, int> _typeSizes = new Dictionary<Type, int>
    {
        [typeof(int)] = 4,
        [typeof(bool)] = 1,
        [typeof(decimal)] = 16,
        [typeof(DateTime)] = 8
    }.ToFrozenDictionary();

    public static int GetFixedSize(PropertyInfo property)
    {
        var type = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        bool isNullable = Nullable.GetUnderlyingType(type) != null;

        if (underlyingType == typeof(string))
        {
            // Require [MaxLength] attribute to determine fixed size for string fields
            var maxLengthAttr = property.GetCustomAttribute<MaxLengthAttribute>();
            if (maxLengthAttr is null)
                throw new InvalidOperationException(
                    $"String property '{property.DeclaringType?.Name}.{property.Name}' must be decorated with [MaxLength] to determine its fixed size.");

            return maxLengthAttr.Length;
        }

        // Handle enum types - store as their underlying integer type
        if (underlyingType.IsEnum)
        {
            var enumUnderlyingType = Enum.GetUnderlyingType(underlyingType);
            if (enumUnderlyingType == typeof(long) || enumUnderlyingType == typeof(ulong))
            {
                throw new NotSupportedException(
                    $"Enum property '{property.DeclaringType?.Name}.{property.Name}' uses underlying type '{enumUnderlyingType.Name}', which is not supported. " +
                    "MiniDb stores enums as Int32; please use enum underlying types byte/short/int (or their unsigned variants within Int32 range)."
                );
            }

            // Always store enum as Int32 for consistent serialization layout.
            const int enumSize = 4;
            return isNullable ? enumSize + 1 : enumSize;
        }

        if (!_typeSizes.TryGetValue(underlyingType, out int baseSize))
        {
            throw new NotSupportedException($"Type {type.Name} on property '{property.DeclaringType?.Name}.{property.Name}' is not supported. Supported types: int, bool, decimal, DateTime, string (with [MaxLength]), and enums.");
        }

        // Nullable types need extra 1 byte for null marker
        return isNullable ? baseSize + 1 : baseSize;
    }

    public static SchemaFieldType GetSchemaFieldType(Type effectiveType)
    {
        if (effectiveType == typeof(int))
        {
            return SchemaFieldType.Int32;
        }

        if (effectiveType == typeof(bool))
        {
            return SchemaFieldType.Boolean;
        }

        if (effectiveType == typeof(decimal))
        {
            return SchemaFieldType.Decimal;
        }

        if (effectiveType == typeof(DateTime))
        {
            return SchemaFieldType.DateTime;
        }

        if (effectiveType == typeof(string))
        {
            return SchemaFieldType.String;
        }

        if (effectiveType.IsEnum)
        {
            return SchemaFieldType.Enum;
        }

        throw new NotSupportedException($"Type '{effectiveType.Name}' is not supported for schema persistence.");
    }
}

/// <summary>
/// Type code stored in the binary file for field metadata.
/// Used by StorageManager (write) and MiniDbFileReader (read) to agree on type encoding.
/// </summary>
public enum FieldTypeCode : int
{
    Unknown = 0,
    Int32 = 1,
    Boolean = 2,
    Decimal = 3,
    DateTime = 4,
    String = 5,
    Enum = 6
}
