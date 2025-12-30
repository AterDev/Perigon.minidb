using System.ComponentModel.DataAnnotations;
using System.IO;
using Perigon.MiniDb;

namespace Perigon.MiniDb.Client.Sample;

/// <summary>
/// Sample entity for demonstration
/// </summary>
public class Product : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Sample entity for demonstration
/// </summary>
public class Category : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Sample database context for testing the client
/// </summary>
public class SampleDbContext : MicroDbContext
{
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;

    public SampleDbContext(string filePath) : base(filePath)
    {
    }

    /// <summary>
    /// Create a sample database with test data
    /// </summary>
    public static async Task CreateSampleDatabaseAsync(string filePath)
    {
        // Delete existing file
        if (File.Exists(filePath))
            File.Delete(filePath);

        var db = new SampleDbContext(filePath);
        await using (db)
        {
            // Add categories
            db.Categories.Add(new Category { Name = "Electronics", Description = "Electronic devices and gadgets" });
            db.Categories.Add(new Category { Name = "Books", Description = "Books and literature" });
            db.Categories.Add(new Category { Name = "Clothing", Description = "Apparel and fashion" });
            await db.SaveChangesAsync();

            // Add products
            db.Products.Add(new Product
            {
                Name = "Laptop",
                Description = "High-performance laptop for work and gaming",
                Price = 1299.99m,
                Stock = 15,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Products.Add(new Product
            {
                Name = "Wireless Mouse",
                Description = "Ergonomic wireless mouse with precision tracking",
                Price = 29.99m,
                Stock = 50,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Products.Add(new Product
            {
                Name = "Programming Book",
                Description = "Learn C# and .NET development",
                Price = 49.99m,
                Stock = 30,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Products.Add(new Product
            {
                Name = "T-Shirt",
                Description = "Comfortable cotton t-shirt",
                Price = 19.99m,
                Stock = 100,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Products.Add(new Product
            {
                Name = "Headphones",
                Description = "Noise-canceling over-ear headphones",
                Price = 199.99m,
                Stock = 25,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
    }
}
