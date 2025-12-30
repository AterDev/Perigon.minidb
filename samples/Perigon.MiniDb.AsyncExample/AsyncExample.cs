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
}

// Define database context
public class SampleDbContext(string filePath) : MicroDbContext(filePath)
{
    public DbSet<User> Users { get; set; } = null!;
}

// Example usage - Async version
class AsyncExample
{
    static async Task Main(string[] args)
    {
        const string dbPath = "async_example.mdb";
        
        // Clean up any existing database
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        // Create and use database asynchronously
        await using var db = new SampleDbContext(dbPath);

        // Add users
        db.Users.Add(new User { Name = "Alice", Email = "alice@example.com", Age = 30 });
        db.Users.Add(new User { Name = "Bob", Email = "bob@example.com", Age = 25 });
        db.Users.Add(new User { Name = "Charlie", Email = "charlie@example.com", Age = 35 });

        // Save changes asynchronously with cancellation support
        using var cts = new CancellationTokenSource();
        await db.SaveChangesAsync(cts.Token);

        Console.WriteLine($"Added {db.Users.Count} users");

        // Query users
        var adults = db.Users.Where(u => u.Age >= 30).ToList();
        Console.WriteLine($"Found {adults.Count} adults:");
        foreach (var user in adults)
        {
            Console.WriteLine($"  - {user.Name} ({user.Age})");
        }
    }
}
