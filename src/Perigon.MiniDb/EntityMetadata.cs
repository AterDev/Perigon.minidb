using System.ComponentModel.DataAnnotations;
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
    public List<FieldMetadata> Fields { get; set; } = new();
    public int RecordSize { get; set; }

    public static EntityMetadata Create(Type entityType)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();
        
        var fields = new List<FieldMetadata>();
        int offset = 1; // Skip IsDeleted byte

        foreach (var prop in properties)
        {
            int size = FieldSizeCalculator.GetFixedSize(prop);
            fields.Add(new FieldMetadata
            {
                Property = prop,
                Offset = offset,
                Size = size
            });
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
    public static int GetFixedSize(PropertyInfo property)
    {
        var type = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        bool isNullable = Nullable.GetUnderlyingType(type) != null;

        int baseSize = 0;
        if (underlyingType == typeof(int))
            baseSize = 4;
        else if (underlyingType == typeof(bool))
            baseSize = 1;
        else if (underlyingType == typeof(decimal))
            baseSize = 16;
        else if (underlyingType == typeof(DateTime))
            baseSize = 8;
        else if (underlyingType == typeof(string))
        {
            // Read from [MaxLength] attribute, default to 1024
            var maxLengthAttr = property.GetCustomAttribute<MaxLengthAttribute>();
            return maxLengthAttr?.Length ?? 1024;
        }
        else
            throw new NotSupportedException($"Type {type.Name} is not supported");

        // Nullable types need extra 1 byte for null marker
        return isNullable ? baseSize + 1 : baseSize;
    }
}
