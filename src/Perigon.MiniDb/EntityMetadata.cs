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
    public PropertyInfo Property { get; set; } = null!;
    public int Offset { get; set; }
    public int Size { get; set; }
}

/// <summary>
/// Entity metadata containing field mapping and record size information
/// </summary>
public class EntityMetadata
{
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
            fields[i] = new FieldMetadata
            {
                Property = prop,
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

        if (!_typeSizes.TryGetValue(underlyingType, out int baseSize))
        {
            throw new NotSupportedException($"Type {type.Name} is not supported");
        }

        // Nullable types need extra 1 byte for null marker
        return isNullable ? baseSize + 1 : baseSize;
    }
}
