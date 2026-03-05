using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Perigon.MiniDb.Tests;

public enum ReuseOrderStatus
{
    Pending,
    Paid,
    Shipped,
    Cancelled
}

public class Customer : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime RegisteredAt { get; set; }

    public int? LoyaltyLevel { get; set; }
}

public class Order : IMicroEntity
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public ReuseOrderStatus Status { get; set; }

    public decimal Total { get; set; }

    public DateTime? PaidAt { get; set; }
}

public class OrderItem : IMicroEntity
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    [MaxLength(120)]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal? UnitPrice { get; set; }
}

/// <summary>
/// Solution entity with enums, JSON, and multiple string properties
/// </summary>
public class Solution : IMicroEntity
{
        [Key]
    public int Id { get; set; }
    /// <summary>
    /// 项目名称
    /// </summary>
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名
    /// </summary>
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 路径
    /// </summary>
    [MaxLength(200)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 版本
    /// </summary>
    [MaxLength(20)]
    public string? Version { get; set; }

    /// <summary>
    /// 解决方案类型
    /// </summary>
    public SolutionType? SolutionType { get; set; }

    /// <summary>
    /// project config - stored as JSON string
    /// </summary>
    [MaxLength(2000)]
    public string ConfigJsonString
    {
        get
        {
            if (_config != null)
            {
                return JsonSerializer.Serialize(_config);
            }
            return field;
        }
        set
        {
            field = value;
            _config = null;
        }
    } = string.Empty;

    private SolutionConfig? _config;

    /// <summary>
    /// project config
    /// </summary>
    [NotMapped]
    public SolutionConfig Config
    {
        get
        {
            if (_config == null)
            {
                if (string.IsNullOrEmpty(ConfigJsonString))
                    _config = new SolutionConfig();
                else
                    _config = JsonSerializer.Deserialize<SolutionConfig>(ConfigJsonString) ?? new SolutionConfig();
            }
            return _config;
        }
        set
        {
            _config = value;
        }
    }
}

/// <summary>
///  项目配置
/// </summary>
public class SolutionConfig
{
    public string IdType { get; set; } = string.Empty;
    public string CreatedTimeName { get; set; } = string.Empty;
    public string UpdatedTimeName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    [Required]
    public string SharePath { get; set; } = string.Empty;

    [Required]
    public string EntityPath { get; set; } = string.Empty;

    [Required]
    public string EntityFrameworkPath { get; set; } = string.Empty;
    public string CommonModPath { get; set; } = string.Empty;

    [Required]
    public string ModulePath { get; set; } = string.Empty;
    public string ApiPath { get; set; } = string.Empty;

    [Required]
    public string ServicePath { get; set; } = string.Empty;
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary> 
    /// 是否为租户模式
    /// </summary>
    public bool IsTenantMode { get; set; }

    /// <summary>
    /// 用来标识用户标识名称
    /// </summary>
    public ICollection<string> UserIdKeys { get; set; } = ["UserId"];

    [Required]
    public string SystemModName { get; set; } = "SystemMod";
}

public enum SolutionType
{
    [Description("DotNet")]
    DotNet,

    [Description("Node")]
    Node,

    [Description("Else")]
    Else,
}

/// <summary>
/// Project entity with multiple enum and string properties
/// </summary>
public class Project : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string ProjectName { get; set; } = string.Empty;

    [MaxLength(300)]
    public string ProjectPath { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? FrameworkVersion { get; set; }

    public ProjectType ProjectType { get; set; }

    public ProjectStatus Status { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastModified { get; set; }

    public int? SolutionId { get; set; }
}

/// <summary>
/// API Documentation entity with JSON storage
/// </summary>
public class ApiDocumentation : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string ApiName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Endpoint { get; set; } = string.Empty;

    public ApiMethodType MethodType { get; set; }

    [MaxLength(5000)]
    public string JsonSchema { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? ProjectId { get; set; }
}

/// <summary>
/// Configuration entity to test variable-length string storage
/// </summary>
public class AppConfiguration : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string ConfigKey { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ConfigValue { get; set; } = string.Empty;

    public ConfigType ConfigType { get; set; }

    public bool IsEncrypted { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public enum ProjectType
{
    [Description("ClassLibrary")]
    ClassLibrary = 1,

    [Description("ConsoleApp")]
    ConsoleApp = 2,

    [Description("WebApi")]
    WebApi = 3,

    [Description("WebApp")]
    WebApp = 4,
}

public enum ProjectStatus
{
    Active = 1,
    Archived = 2,
    Deleted = 3,
}

public enum ApiMethodType
{
    Get = 1,
    Post = 2,
    Put = 3,
    Delete = 4,
    Patch = 5,
}

public enum ConfigType
{
    String = 1,
    Number = 2,
    Boolean = 3,
    Json = 4,
}

public class ComplexDbContext : MiniDbContext
{
    public DbSet<Solution> Solutions { get; set; } = null!;
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<ApiDocumentation> ApiDocumentations { get; set; } = null!;
    public DbSet<AppConfiguration> Configurations { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
}
