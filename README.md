# Perigon.MiniDb

A lightweight, single-file, in-memory database engine for small data volumes (≤50MB).

## Features

- **Single-file storage**: All data stored in one binary file with fixed-length records
- **In-memory operations**: Fast queries using LINQ on fully loaded data
- **Incremental updates**: Only modified records are written to disk
- **Thread-safe**: Multi-threaded read support with single-threaded write safety
- **Simple API**: EF Core-like API without the complexity
- **No dependencies**: Self-contained implementation with no external libraries
- **Type-safe**: Strongly-typed entity models with compile-time checking

## Supported Data Types

| Type | Size | Description |
|------|------|-------------|
| `int` | 4 bytes | 32-bit signed integer |
| `int?` | 5 bytes | Nullable int (1 byte marker + 4 bytes value) |
| `bool` | 1 byte | Boolean value |
| `bool?` | 2 bytes | Nullable bool (1 byte marker + 1 byte value) |
| `string` | Variable | UTF-8 encoded, requires `[MaxLength]` attribute |
| `decimal` | 16 bytes | .NET decimal for financial calculations |
| `decimal?` | 17 bytes | Nullable decimal (1 byte marker + 16 bytes value) |
| `DateTime` | 8 bytes | UTC timestamp stored as ticks |
| `DateTime?` | 9 bytes | Nullable DateTime (1 byte marker + 8 bytes value) |

## Installation

```bash
dotnet add package Perigon.MiniDb
```

## Quick Start

### 1. Define Your Entities

```csharp
using System.ComponentModel.DataAnnotations;

public class User
{
    public int Id { get; set; }
    
    [MaxLength(50)]
    public string Name { get; set; }
    
    [MaxLength(100)]
    public string Email { get; set; }
    
    public int Age { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public int? CategoryId { get; set; }
}
```

### 2. Create Your DbContext

```csharp
using Perigon.MiniDb;

public class MyDbContext : MicroDbContext
{
    public DbSet<User> Users { get; set; }
    
    public MyDbContext(string filePath) : base(filePath)
    {
    }
}
```

### 3. Use the Database

```csharp
using var db = new MyDbContext("data.mdb");

// Add entities
db.Users.Add(new User 
{ 
    Name = "Alice",
    Email = "alice@example.com",
    Age = 25,
    Balance = 1000m,
    CreatedAt = DateTime.UtcNow,
    IsActive = true
});
db.SaveChanges();

// Query with LINQ
var activeUsers = db.Users
    .Where(u => u.IsActive && u.Balance >= 500)
    .OrderBy(u => u.Name)
    .ToList();

// Update entities
var user = db.Users.First(u => u.Name == "Alice");
user.Balance += 500m;
db.Users.Update(user);
db.SaveChanges();

// Delete entities
db.Users.Remove(user);
db.SaveChanges();
```

## File Format

The database uses a fixed-length binary format for efficient incremental updates:

```
┌────────────────────────────────────────┐
│ File Header (256 bytes)                │
│ - Magic Number: "MDB1"                 │
│ - Version: 1                           │
│ - Table Count                          │
├────────────────────────────────────────┤
│ Table Metadata (128 bytes per table)   │
│ - Table Name                           │
│ - Record Count                         │
│ - Record Size                          │
│ - Data Start Offset                    │
├────────────────────────────────────────┤
│ Table Data (fixed-length records)      │
│ [IsDeleted(1B)][Field1][Field2]...     │
└────────────────────────────────────────┘
```

## Performance Characteristics

- **O(1) Record Updates**: Direct offset calculation for instant record location
- **Soft Deletes**: Deletion marks records as deleted without rewriting the file
- **Full Memory Load**: Entire database loaded into memory on startup
- **Incremental Writes**: Only changed records written to disk
- **Thread-Safe Reads**: Multiple threads can read simultaneously
- **Serialized Writes**: File writes happen on a single thread to avoid conflicts

## Limitations

- Maximum file size: 50MB recommended
- Maximum records: ~100,000 recommended
- No foreign keys or constraints
- No indexes (full table scans for queries)
- No SQL parser (LINQ only)
- No transactions (use SaveChanges for atomicity)

## Use Cases

✅ **Good for:**
- Small desktop applications
- Local configuration storage
- Embedded application data
- Development/testing databases
- Single-user applications

❌ **Not suitable for:**
- Multi-user web applications
- Large datasets (>50MB)
- High-concurrency scenarios
- Complex relational queries
- Production enterprise systems

## Project Structure

```
Perigon.minidb/
├── src/
│   └── Perigon.MiniDb/          # Core library
│       ├── MicroDbContext.cs    # Main context class
│       ├── DbSet.cs             # Entity collection
│       ├── ChangeTracker.cs     # Change tracking
│       ├── StorageManager.cs    # File I/O operations
│       ├── EntityMetadata.cs    # Metadata & serialization
│       └── ThreadSafetyManager.cs
├── tests/
│   └── Perigon.MiniDb.Tests/    # Unit tests
└── samples/
    └── Perigon.MiniDb.Sample/   # Sample console app
```

## Building from Source

```bash
# Clone the repository
git clone https://github.com/AterDev/Perigon.minidb.git
cd Perigon.minidb

# Build the solution
dotnet build

# Run tests
dotnet test

# Run sample application
cd samples/Perigon.MiniDb.Sample
dotnet run
```

## License

See LICENSE file for details.

## Author

**NilTor**

## Version

0.0.1 (Initial Release)
