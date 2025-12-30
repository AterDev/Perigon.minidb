using System.ComponentModel.DataAnnotations;
using Perigon.MiniDb;

// Define entity models
public class User
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public int Age { get; set; }

    public decimal Balance { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public int? CategoryId { get; set; }

    public DateTime? PublishedAt { get; set; }
}

public class Product
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsAvailable { get; set; }

    public DateTime CreatedAt { get; set; }
}

// Define database context
public class SampleDbContext(string filePath) : MicroDbContext(filePath)
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Perigon.MiniDb Sample Application ===\n");

        var dbPath = "sample.mdb";

        // Clean up existing database for demo
        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
            Console.WriteLine($"Cleaned up existing database: {dbPath}\n");
        }

        using var db = new SampleDbContext(dbPath);

        // === Demo 1: Add entities ===
        Console.WriteLine("--- Demo 1: Adding Users ---");
        var users = new[]
        {
            new User
            {
                Name = "Alice",
                Email = "alice@example.com",
                Age = 25,
                Balance = 1000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                CategoryId = 1
            },
            new User
            {
                Name = "Bob",
                Email = "bob@example.com",
                Age = 30,
                Balance = 2500m,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                CategoryId = 2
            },
            new User
            {
                Name = "张三",
                Email = "zhangsan@example.com",
                Age = 28,
                Balance = 5000m,
                CreatedAt = DateTime.UtcNow,
                IsActive = false,
                CategoryId = 1
            }
        };

        foreach (var user in users)
        {
            db.Users.Add(user);
            Console.WriteLine($"Added: {user.Name} (Age: {user.Age}, Balance: {user.Balance:C})");
        }

        db.SaveChanges();
        Console.WriteLine($"✓ Saved {users.Length} users to database\n");

        // === Demo 2: Add products ===
        Console.WriteLine("--- Demo 2: Adding Products ---");
        var products = new[]
        {
            new Product
            {
                Name = "Laptop",
                Description = "High-performance laptop for developers",
                Price = 1299.99m,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow
            },
            new Product
            {
                Name = "Wireless Mouse",
                Description = "Ergonomic wireless mouse",
                Price = 29.99m,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow
            },
            new Product
            {
                Name = "Mechanical Keyboard",
                Description = "RGB mechanical keyboard with blue switches",
                Price = 149.99m,
                IsAvailable = false,
                CreatedAt = DateTime.UtcNow
            }
        };

        foreach (var product in products)
        {
            db.Products.Add(product);
            Console.WriteLine($"Added: {product.Name} (Price: {product.Price:C}, Available: {product.IsAvailable})");
        }

        db.SaveChanges();
        Console.WriteLine($"✓ Saved {products.Length} products to database\n");

        // === Demo 3: Query with LINQ ===
        Console.WriteLine("--- Demo 3: Querying with LINQ ---");

        Console.WriteLine("\n3.1. Active users:");
        var activeUsers = db.Users.Where(u => u.IsActive).ToList();
        foreach (var user in activeUsers)
        {
            Console.WriteLine($"  - {user.Name} (Age: {user.Age}, Balance: {user.Balance:C})");
        }

        Console.WriteLine("\n3.2. Users with balance >= $2000:");
        var richUsers = db.Users.Where(u => u.Balance >= 2000).OrderByDescending(u => u.Balance).ToList();
        foreach (var user in richUsers)
        {
            Console.WriteLine($"  - {user.Name}: {user.Balance:C}");
        }

        Console.WriteLine("\n3.3. Available products:");
        var availableProducts = db.Products.Where(p => p.IsAvailable).ToList();
        foreach (var product in availableProducts)
        {
            Console.WriteLine($"  - {product.Name}: {product.Price:C}");
        }

        Console.WriteLine("\n3.4. Users in Category 1:");
        var category1Users = db.Users.Where(u => u.CategoryId == 1).ToList();
        foreach (var user in category1Users)
        {
            Console.WriteLine($"  - {user.Name}");
        }

        // === Demo 4: Update entities ===
        Console.WriteLine("\n--- Demo 4: Updating Entities ---");
        var alice = db.Users.First(u => u.Name == "Alice");
        Console.WriteLine($"Before: Alice's balance = {alice.Balance:C}");

        alice.Balance += 500m;
        alice.CategoryId = 3;
        db.Users.Update(alice);
        db.SaveChanges();

        Console.WriteLine($"After: Alice's balance = {alice.Balance:C}, Category = {alice.CategoryId}");
        Console.WriteLine("✓ Updated Alice's information\n");

        // Update product availability
        var keyboard = db.Products.First(p => p.Name == "Mechanical Keyboard");
        Console.WriteLine($"Before: {keyboard.Name} availability = {keyboard.IsAvailable}");

        keyboard.IsAvailable = true;
        keyboard.Price = 129.99m; // Price reduction
        db.Products.Update(keyboard);
        db.SaveChanges();

        Console.WriteLine($"After: {keyboard.Name} availability = {keyboard.IsAvailable}, Price = {keyboard.Price:C}");
        Console.WriteLine("✓ Updated product information\n");

        // === Demo 5: Delete entities ===
        Console.WriteLine("--- Demo 5: Deleting Entities ---");
        var bob = db.Users.First(u => u.Name == "Bob");
        Console.WriteLine($"Deleting user: {bob.Name}");

        db.Users.Remove(bob);
        db.SaveChanges();

        Console.WriteLine($"✓ User deleted. Remaining users: {db.Users.Count}\n");

        // === Demo 6: Complex queries ===
        Console.WriteLine("--- Demo 6: Complex Queries ---");

        Console.WriteLine("\n6.1. Average balance of active users:");
        var avgBalance = db.Users.Where(u => u.IsActive).Average(u => u.Balance);
        Console.WriteLine($"  Average: {avgBalance:C}");

        Console.WriteLine("\n6.2. Total value of available products:");
        var totalValue = db.Products.Where(p => p.IsAvailable).Sum(p => p.Price);
        Console.WriteLine($"  Total: {totalValue:C}");

        Console.WriteLine("\n6.3. Youngest user:");
        var youngest = db.Users.OrderBy(u => u.Age).First();
        Console.WriteLine($"  {youngest.Name} (Age: {youngest.Age})");

        Console.WriteLine("\n6.4. Most expensive product:");
        var mostExpensive = db.Products.OrderByDescending(p => p.Price).First();
        Console.WriteLine($"  {mostExpensive.Name}: {mostExpensive.Price:C}");

        // === Demo 7: Database persistence ===
        Console.WriteLine("\n--- Demo 7: Database Persistence ---");
        Console.WriteLine("Closing and reopening database...");

        // Get current counts
        var userCount = db.Users.Count;
        var productCount = db.Products.Count;

        db.Dispose();

        // Reopen database
        using var db2 = new SampleDbContext(dbPath);
        Console.WriteLine($"✓ Database reopened successfully");
        Console.WriteLine($"  Users loaded: {db2.Users.Count} (expected: {userCount})");
        Console.WriteLine($"  Products loaded: {db2.Products.Count} (expected: {productCount})");

        // Verify data integrity
        var aliceReloaded = db2.Users.First(u => u.Name == "Alice");
        Console.WriteLine($"  Alice's balance after reload: {aliceReloaded.Balance:C}");
        Console.WriteLine($"  Alice's category after reload: {aliceReloaded.CategoryId}");

        Console.WriteLine("\n=== Sample Application Completed Successfully! ===");
        Console.WriteLine($"\nDatabase file: {Path.GetFullPath(dbPath)}");
        Console.WriteLine($"File size: {new FileInfo(dbPath).Length} bytes");
    }
}

