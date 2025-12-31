using System.Collections.Frozen;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Tests;

#region Test Entities

/// <summary>
/// Solution entity with enums, JSON, and multiple string properties
/// </summary>
public class Solution : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Version { get; set; }

    public SolutionType? SolutionType { get; set; }

    [MaxLength(2000)]
    public string ConfigJsonString { get; set; } = string.Empty;
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

#endregion

#region Enums

public enum SolutionType
{
    [Description("DotNet")]
    DotNet = 1,

    [Description("Node")]
    Node = 2,

    [Description("Python")]
    Python = 3,

    [Description("Else")]
    Else = 4,
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

#endregion

#region Test Context

public class ComplexDbContext : MiniDbContext
{
    public DbSet<Solution> Solutions { get; set; } = null!;
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<ApiDocumentation> ApiDocumentations { get; set; } = null!;
    public DbSet<AppConfiguration> Configurations { get; set; } = null!;
}

#endregion

#region Tests

/// <summary>
/// Complex data access tests covering multi-table scenarios with enums and JSON
/// </summary>
public class ComplexDataAccessTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public ComplexDataAccessTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_complex_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<ComplexDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task CanAddAndRetrieveSingleSolutionWithEnum()
    {
        var db = new ComplexDbContext();
        await using (db)
        {
            var solution = new Solution
            {
                Name = "MyDotNetSolution",
                DisplayName = "My .NET Solution",
                Path = "C:\\Projects\\MyDotNetSolution",
                Version = "1.0.0",
                SolutionType = SolutionType.DotNet,
                ConfigJsonString = JsonSerializer.Serialize(new { basePath = "src", outputPath = "dist" })
            };

            db.Solutions.Add(solution);
            await db.SaveChangesAsync();

            Assert.Equal(1, solution.Id);
            Assert.Equal(1, db.Solutions.Count);
            Assert.Equal("MyDotNetSolution", solution.Name);
        }

        // Reload and verify persistence
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            Assert.Equal(1, db2.Solutions.Count);
            var loaded = db2.Solutions.FirstOrDefault(s => s.Id == 1);
            Assert.NotNull(loaded);
            Assert.Equal("MyDotNetSolution", loaded.Name);
            Assert.Equal(SolutionType.DotNet, loaded.SolutionType);
        }
    }

    [Fact]
    public async Task CanAddMultipleSolutionsAndRetrieveThem()
    {
        var db = new ComplexDbContext();
        await using (db)
        {
            var solutions = new[]
            {
                new Solution
                {
                    Name = "DotNetSolution",
                    DisplayName = "DotNet Solution",
                    Path = "C:\\Projects\\DotNet",
                    Version = "1.0.0",
                    SolutionType = SolutionType.DotNet,
                    ConfigJsonString = "{}"
                },
                new Solution
                {
                    Name = "NodeSolution",
                    DisplayName = "Node Solution",
                    Path = "C:\\Projects\\Node",
                    Version = "2.0.0",
                    SolutionType = SolutionType.Node,
                    ConfigJsonString = JsonSerializer.Serialize(new { nodeVersion = "18.0" })
                },
                new Solution
                {
                    Name = "PythonSolution",
                    DisplayName = "Python Solution",
                    Path = "C:\\Projects\\Python",
                    SolutionType = SolutionType.Python,
                    ConfigJsonString = JsonSerializer.Serialize(new { pythonVersion = "3.11" })
                }
            };

            foreach (var solution in solutions)
            {
                db.Solutions.Add(solution);
            }

            await db.SaveChangesAsync();

            Assert.Equal(3, db.Solutions.Count);
            Assert.Equal(1, solutions[0].Id);
            Assert.Equal(2, solutions[1].Id);
            Assert.Equal(3, solutions[2].Id);
        }

        // Reload and verify all data
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            var allSolutions = db2.Solutions.ToList();
            Assert.Equal(3, allSolutions.Count);

            var dotnet = allSolutions.First(s => s.Name == "DotNetSolution");
            var node = allSolutions.First(s => s.Name == "NodeSolution");
            var python = allSolutions.First(s => s.Name == "PythonSolution");

            Assert.Equal(SolutionType.DotNet, dotnet.SolutionType);
            Assert.Equal(SolutionType.Node, node.SolutionType);
            Assert.Equal(SolutionType.Python, python.SolutionType);

            Assert.NotEmpty(node.ConfigJsonString);
            Assert.NotEmpty(python.ConfigJsonString);
        }
    }

    [Fact]
    public async Task CanAddMultipleTablesAndReleaseReload()
    {
        // Add Solution
        var db1 = new ComplexDbContext();
        await using (db1)
        {
            var solution = new Solution
            {
                Name = "TestSolution",
                DisplayName = "Test Solution",
                Path = "C:\\Test",
                Version = "1.0.0",
                SolutionType = SolutionType.DotNet,
                ConfigJsonString = "{}"
            };

            db1.Solutions.Add(solution);
            await db1.SaveChangesAsync();

            Assert.Equal(1, solution.Id);
        }

        // Add Project in new context
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            var project = new Project
            {
                ProjectName = "TestProject",
                ProjectPath = "C:\\Test\\TestProject",
                FrameworkVersion = "net8.0",
                ProjectType = ProjectType.WebApi,
                Status = ProjectStatus.Active,
                CreatedAt = DateTime.UtcNow,
                SolutionId = 1
            };

            db2.Projects.Add(project);
            await db2.SaveChangesAsync();

            Assert.Equal(1, project.Id);
        }

        // Release and reload
        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Verify both tables loaded correctly
        var db3 = new ComplexDbContext();
        await using (db3)
        {
            Assert.Equal(1, db3.Solutions.Count);
            Assert.Equal(1, db3.Projects.Count);

            var solution = db3.Solutions.First();
            var project = db3.Projects.First();

            Assert.Equal("TestSolution", solution.Name);
            Assert.Equal("TestProject", project.ProjectName);
            Assert.Equal(ProjectType.WebApi, project.ProjectType);
        }
    }

    [Fact]
    public async Task CanHandleComplexJsonSerialization()
    {
        var db = new ComplexDbContext();
        await using (db)
        {
            var complexConfig = new { basePath = "src", outputPath = "dist", features = new[] { "feature1", "feature2" } };
            var configJson = JsonSerializer.Serialize(complexConfig);

            var solution = new Solution
            {
                Name = "ComplexJsonSolution",
                DisplayName = "Complex JSON Solution",
                Path = "C:\\Complex",
                Version = "1.0.0",
                SolutionType = SolutionType.DotNet,
                ConfigJsonString = configJson
            };

            db.Solutions.Add(solution);
            await db.SaveChangesAsync();

            var loaded = db.Solutions.First(s => s.Id == solution.Id);
            Assert.NotEmpty(loaded.ConfigJsonString);

            // Deserialize and verify
            var deserializedConfig = JsonSerializer.Deserialize<dynamic>(loaded.ConfigJsonString);
            Assert.NotNull(deserializedConfig);
        }
    }

    [Fact]
    public async Task CanAddMultipleTablesWithDifferentSizes()
    {
        // Add multiple records to different tables with varying sizes
        var db = new ComplexDbContext();
        await using (db)
        {
            // Add Solutions (larger records due to long strings and enum)
            for (int i = 1; i <= 3; i++)
            {
                var solution = new Solution
                {
                    Name = $"Solution{i}",
                    DisplayName = $"Solution {i} Display Name",
                    Path = $"C:\\Projects\\Solution{i}\\DeepFolder\\AnotherFolder\\YetAnother\\FinalFolder",
                    Version = "1.0.0",
                    SolutionType = (SolutionType)(i % 4 + 1),
                    ConfigJsonString = JsonSerializer.Serialize(new { index = i, config = "complex" })
                };
                db.Solutions.Add(solution);
            }

            // Add Projects (medium-sized records)
            for (int i = 1; i <= 5; i++)
            {
                var project = new Project
                {
                    ProjectName = $"Project{i}",
                    ProjectPath = $"C:\\Projects\\Project{i}\\Src",
                    FrameworkVersion = "net8.0",
                    ProjectType = (ProjectType)(i % 4 + 1),
                    Status = ProjectStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    SolutionId = (i % 3) + 1
                };
                db.Projects.Add(project);
            }

            // Add Configurations (smaller records)
            for (int i = 1; i <= 7; i++)
            {
                var config = new AppConfiguration
                {
                    ConfigKey = $"Key{i}",
                    ConfigValue = $"Value{i}",
                    ConfigType = (ConfigType)(i % 4 + 1),
                    IsEncrypted = i % 2 == 0,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Configurations.Add(config);
            }

            await db.SaveChangesAsync();

            Assert.Equal(3, db.Solutions.Count);
            Assert.Equal(5, db.Projects.Count);
            Assert.Equal(7, db.Configurations.Count);
        }
    }

    [Fact]
    public async Task CanAddAndRetrieveAfterMultipleReleaseReloadCycles()
    {
        // Cycle 1: Add solutions
        var db1 = new ComplexDbContext();
        await using (db1)
        {
            for (int i = 1; i <= 2; i++)
            {
                db1.Solutions.Add(new Solution
                {
                    Name = $"Solution{i}",
                    DisplayName = $"Solution {i}",
                    Path = $"C:\\Solution{i}",
                    SolutionType = SolutionType.DotNet,
                    ConfigJsonString = "{}"
                });
            }
            await db1.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Cycle 2: Add projects
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            Assert.Equal(2, db2.Solutions.Count);

            for (int i = 1; i <= 3; i++)
            {
                db2.Projects.Add(new Project
                {
                    ProjectName = $"Project{i}",
                    ProjectPath = $"C:\\Project{i}",
                    ProjectType = ProjectType.WebApi,
                    Status = ProjectStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    SolutionId = (i % 2) + 1
                });
            }
            await db2.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Cycle 3: Add API docs
        var db3 = new ComplexDbContext();
        await using (db3)
        {
            Assert.Equal(2, db3.Solutions.Count);
            Assert.Equal(3, db3.Projects.Count);

            for (int i = 1; i <= 4; i++)
            {
                db3.ApiDocumentations.Add(new ApiDocumentation
                {
                    ApiName = $"API{i}",
                    Endpoint = $"/api/endpoint{i}",
                    MethodType = (ApiMethodType)(i % 5 + 1),
                    JsonSchema = JsonSerializer.Serialize(new { type = "object", properties = new { } }),
                    IsPublished = true,
                    CreatedAt = DateTime.UtcNow,
                    ProjectId = (i % 3) + 1
                });
            }
            await db3.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Final verification
        var db4 = new ComplexDbContext();
        await using (db4)
        {
            Assert.Equal(2, db4.Solutions.Count);
            Assert.Equal(3, db4.Projects.Count);
            Assert.Equal(4, db4.ApiDocumentations.Count);

            // Verify enum values are preserved
            var projects = db4.Projects.ToList();
            Assert.All(projects, p => Assert.NotEqual(default, p.ProjectType));

            var apis = db4.ApiDocumentations.ToList();
            Assert.All(apis, a => Assert.NotEqual(default, a.MethodType));
        }
    }

    [Fact]
    public async Task CanUpdateRecordsInMultipleTables()
    {
        // Setup: Add initial data
        var db1 = new ComplexDbContext();
        await using (db1)
        {
            db1.Solutions.Add(new Solution
            {
                Name = "OriginalName",
                DisplayName = "Original Display",
                Path = "C:\\Original",
                SolutionType = SolutionType.DotNet,
                ConfigJsonString = "{}"
            });

            db1.Projects.Add(new Project
            {
                ProjectName = "OriginalProject",
                ProjectPath = "C:\\OriginalProject",
                ProjectType = ProjectType.ConsoleApp,
                Status = ProjectStatus.Active,
                CreatedAt = DateTime.UtcNow
            });

            await db1.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Update: Modify records
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            var solution = db2.Solutions.First();
            solution.Name = "UpdatedName";
            solution.SolutionType = SolutionType.Node;
            db2.Solutions.Update(solution);

            var project = db2.Projects.First();
            project.ProjectName = "UpdatedProject";
            project.Status = ProjectStatus.Archived;
            db2.Projects.Update(project);

            await db2.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Verify: Check updates persisted
        var db3 = new ComplexDbContext();
        await using (db3)
        {
            var solution = db3.Solutions.First();
            Assert.Equal("UpdatedName", solution.Name);
            Assert.Equal(SolutionType.Node, solution.SolutionType);

            var project = db3.Projects.First();
            Assert.Equal("UpdatedProject", project.ProjectName);
            Assert.Equal(ProjectStatus.Archived, project.Status);
        }
    }

    [Fact]
    public async Task CanHandleNullableEnumFields()
    {
        var db = new ComplexDbContext();
        await using (db)
        {
            // Add solution with null enum
            var solution1 = new Solution
            {
                Name = "NoType",
                DisplayName = "No Type",
                Path = "C:\\NoType",
                SolutionType = null,
                ConfigJsonString = "{}"
            };

            // Add solution with specific enum
            var solution2 = new Solution
            {
                Name = "WithType",
                DisplayName = "With Type",
                Path = "C:\\WithType",
                SolutionType = SolutionType.Python,
                ConfigJsonString = "{}"
            };

            db.Solutions.Add(solution1);
            db.Solutions.Add(solution2);
            await db.SaveChangesAsync();

            Assert.Null(solution1.SolutionType);
            Assert.Equal(SolutionType.Python, solution2.SolutionType);
        }

        // Reload and verify null handling
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            var solutions = db2.Solutions.ToList();
            var nullEnum = solutions.First(s => s.Name == "NoType");
            var withEnum = solutions.First(s => s.Name == "WithType");

            Assert.Null(nullEnum.SolutionType);
            Assert.Equal(SolutionType.Python, withEnum.SolutionType);
        }
    }

    [Fact]
    public async Task CanDeleteRecordsFromMultipleTables()
    {
        // Setup: Add data across multiple tables
        var db1 = new ComplexDbContext();
        await using (db1)
        {
            db1.Solutions.Add(new Solution
            {
                Name = "ToDelete",
                DisplayName = "To Delete",
                Path = "C:\\Delete",
                SolutionType = SolutionType.DotNet,
                ConfigJsonString = "{}"
            });

            db1.Projects.Add(new Project
            {
                ProjectName = "ToDelete",
                ProjectPath = "C:\\DeleteProject",
                ProjectType = ProjectType.WebApi,
                Status = ProjectStatus.Active,
                CreatedAt = DateTime.UtcNow
            });

            db1.Configurations.Add(new AppConfiguration
            {
                ConfigKey = "ToDelete",
                ConfigValue = "DeleteMe",
                ConfigType = ConfigType.String,
                UpdatedAt = DateTime.UtcNow
            });

            await db1.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Delete records
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            var solution = db2.Solutions.First(s => s.Name == "ToDelete");
            db2.Solutions.Remove(solution);

            var project = db2.Projects.First(p => p.ProjectName == "ToDelete");
            db2.Projects.Remove(project);

            var config = db2.Configurations.First(c => c.ConfigKey == "ToDelete");
            db2.Configurations.Remove(config);

            await db2.SaveChangesAsync();
        }

        await ComplexDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        // Verify deletions
        var db3 = new ComplexDbContext();
        await using (db3)
        {
            Assert.Empty(db3.Solutions.Where(s => s.Name == "ToDelete"));
            Assert.Empty(db3.Projects.Where(p => p.ProjectName == "ToDelete"));
            Assert.Empty(db3.Configurations.Where(c => c.ConfigKey == "ToDelete"));
        }
    }

    [Fact]
    public async Task CanHandleMaxLengthStringProperties()
    {
        var db = new ComplexDbContext();
        await using (db)
        {
            var solution = new Solution
            {
                Name = new string('A', 100),
                DisplayName = new string('B', 100),
                Path = new string('C', 200),
                Version = new string('V', 20),
                SolutionType = SolutionType.DotNet,
                ConfigJsonString = new string('J', 2000)
            };

            db.Solutions.Add(solution);
            await db.SaveChangesAsync();

            Assert.Equal(100, solution.Name.Length);
            Assert.Equal(100, solution.DisplayName.Length);
            Assert.Equal(200, solution.Path.Length);
            Assert.Equal(20, solution.Version.Length);
            Assert.Equal(2000, solution.ConfigJsonString.Length);
        }

        // Verify max-length strings persisted
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            var loaded = db2.Solutions.First();
            Assert.Equal(100, loaded.Name.Length);
            Assert.Equal(100, loaded.DisplayName.Length);
            Assert.Equal(200, loaded.Path.Length);
            Assert.Equal(20, loaded.Version.Length);
            Assert.Equal(2000, loaded.ConfigJsonString.Length);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public async Task CanHandleVariousNumberOfRecords(int recordCount)
    {
        var db = new ComplexDbContext();
        await using (db)
        {
            for (int i = 0; i < recordCount; i++)
            {
                db.Solutions.Add(new Solution
                {
                    Name = $"Solution{i}",
                    DisplayName = $"Solution {i}",
                    Path = $"C:\\Solution{i}",
                    SolutionType = (SolutionType)(i % 4 + 1),
                    ConfigJsonString = JsonSerializer.Serialize(new { id = i })
                });
            }

            await db.SaveChangesAsync();
            Assert.Equal(recordCount, db.Solutions.Count);
        }

        // Reload and verify
        var db2 = new ComplexDbContext();
        await using (db2)
        {
            Assert.Equal(recordCount, db2.Solutions.Count);
            for (int i = 0; i < recordCount; i++)
            {
                var solution = db2.Solutions.First(s => s.Id == i + 1);
                Assert.Equal($"Solution{i}", solution.Name);
            }
        }
    }

}

#endregion
