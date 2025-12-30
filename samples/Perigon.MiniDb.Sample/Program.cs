using System.ComponentModel.DataAnnotations;
using Perigon.MiniDb;

// Define entity models
public class User : IMicroEntity
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

class Program
{
    static async Task Main(string[] args)
    {
        const string dbPath = "sample.mdb";
        
        Console.WriteLine("=== Perigon.MiniDb Sample ===\n");

        // Clean up any existing database for demo
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        // Create database context (automatically initialized, no InitializeAsync needed)
        var db = new SampleDbContext(dbPath);
        await using (db)
        {
            Console.WriteLine("1. Adding users...");
            db.Users.Add(new User { Name = "Alice", Email = "alice@example.com", Age = 30 });
            db.Users.Add(new User { Name = "Bob", Email = "bob@example.com", Age = 25 });
            db.Users.Add(new User { Name = "Charlie", Email = "charlie@example.com", Age = 35 });
            await db.SaveChangesAsync();
            Console.WriteLine($"   Added {db.Users.Count} users\n");

            // Query users
            Console.WriteLine("2. Querying users...");
            var adults = db.Users.Where(u => u.Age >= 30).ToList();
            Console.WriteLine($"   Found {adults.Count} adults:");
            foreach (var user in adults)
            {
                Console.WriteLine($"   - {user.Name} ({user.Age})");
            }
            Console.WriteLine();

            // Update
            Console.WriteLine("3. Updating user...");
            var alice = db.Users.First(u => u.Name == "Alice");
            alice.Age = 31;
            db.Users.Update(alice);
            await db.SaveChangesAsync();
            Console.WriteLine($"   Updated Alice's age to {alice.Age}\n");

            // Delete
            Console.WriteLine("4. Deleting user...");
            var bob = db.Users.First(u => u.Name == "Bob");
            db.Users.Remove(bob);
            await db.SaveChangesAsync();
            Console.WriteLine($"   Deleted Bob. Remaining users: {db.Users.Count}\n");
        }

        // Reload database to verify persistence
        Console.WriteLine("5. Reloading database...");
        var db2 = new SampleDbContext(dbPath);
        await using (db2)
        {
            Console.WriteLine($"   Found {db2.Users.Count} users after reload:");
            foreach (var user in db2.Users)
            {
                Console.WriteLine($"   - {user.Name} ({user.Age})");
            }
        }

        Console.WriteLine("\nDone! Press any key to exit...");
        Console.ReadKey();
    }
}

