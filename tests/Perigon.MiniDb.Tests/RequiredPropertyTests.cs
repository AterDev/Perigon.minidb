using System.ComponentModel.DataAnnotations;

namespace Perigon.MiniDb.Tests;

// Test enums
public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum PaymentMethod
{
    CreditCard = 1,
    DebitCard = 2,
    PayPal = 3,
    BankTransfer = 4
}

// 测试实体 - 使用 required 修饰符
public class EntityWithRequired : IMicroEntity
{
    public int Id { get; set; }

    // 使用 required 关键字（C# 11+ 特性）
    [MaxLength(50)]
    public required string Name { get; set; }

    [MaxLength(100)]
    public required string Email { get; set; }

    public int Age { get; set; }
}

// 测试实体 - 包含枚举属性
public class OrderEntity : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string OrderNumber { get; set; } = string.Empty;

    public OrderStatus Status { get; set; }

    public Priority Priority { get; set; }

    public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; }

    public OrderStatus? LastStatus { get; set; }

    public PaymentMethod? PaymentMethod { get; set; }
}

// 测试实体 - 多枚举组合
public class TaskEntity : IMicroEntity
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    public Priority TaskPriority { get; set; }

    public OrderStatus CurrentStatus { get; set; }

    public PaymentMethod? PreferredPayment { get; set; }

    public bool IsCompleted { get; set; }
}

// 测试 DbContext
public class RequiredTestContext : MiniDbContext
{
    public DbSet<EntityWithRequired> Entities { get; set; } = null!;
}

// 测试 DbContext with enums
public class EnumTestContext : MiniDbContext
{
    public DbSet<OrderEntity> Orders { get; set; } = null!;
    public DbSet<TaskEntity> Tasks { get; set; } = null!;
}

/// <summary>
/// 测试 DbSet 与 required 属性的兼容性
/// </summary>
public class RequiredPropertyTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public RequiredPropertyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_required_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<RequiredTestContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await RequiredTestContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task RequiredProperties_CanBeUsedWithDbSet()
    {
        var db = new RequiredTestContext();
        await using (db)
        {
            // 使用对象初始化器设置 required 属性
            var entity = new EntityWithRequired
            {
                Name = "Alice",
                Email = "alice@example.com",
                Age = 25
            };

            db.Entities.Add(entity);
            await db.SaveChangesAsync();

            Assert.Equal(1, entity.Id);
        }
    }

    [Fact]
    public async Task RequiredProperties_SaveAndLoadCorrectly()
    {
        var db = new RequiredTestContext();
        await using (db)
        {
            var entity = new EntityWithRequired
            {
                Name = "Bob",
                Email = "bob@example.com",
                Age = 30
            };

            db.Entities.Add(entity);
            await db.SaveChangesAsync();
        }

        // 重新加载
        await RequiredTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new RequiredTestContext();
        await using (db2)
        {
            var loaded = db2.Entities.First();

            // 验证 required 属性正确加载
            Assert.Equal("Bob", loaded.Name);
            Assert.Equal("bob@example.com", loaded.Email);
            Assert.Equal(30, loaded.Age);
        }
    }

    [Fact]
    public async Task RequiredProperties_UpdateWorks()
    {
        var db = new RequiredTestContext();
        await using (db)
        {
            var entity = new EntityWithRequired
            {
                Name = "Charlie",
                Email = "charlie@example.com",
                Age = 35
            };

            db.Entities.Add(entity);
            await db.SaveChangesAsync();

            // 更新 required 属性
            entity.Name = "Charlie Updated";
            entity.Email = "charlie.updated@example.com";
            db.Entities.Update(entity);
            await db.SaveChangesAsync();
        }

        // 重新加载并验证
        await RequiredTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new RequiredTestContext();
        await using (db2)
        {
            var loaded = db2.Entities.First();

            Assert.Equal("Charlie Updated", loaded.Name);
            Assert.Equal("charlie.updated@example.com", loaded.Email);
        }
    }
}

/// <summary>
/// 测试枚举类型支持
/// </summary>
public class EnumTypeTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public EnumTypeTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_enum_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<EnumTestContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task Enum_BasicSaveAndLoad()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            var order = new OrderEntity
            {
                OrderNumber = "ORD-001",
                Status = OrderStatus.Processing,
                Priority = Priority.High,
                Amount = 99.99m,
                CreatedAt = DateTime.Now
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            Assert.Equal(1, order.Id);
        }

        // 重新加载并验证
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            var loaded = db2.Orders.First();

            Assert.Equal("ORD-001", loaded.OrderNumber);
            Assert.Equal(OrderStatus.Processing, loaded.Status);
            Assert.Equal(Priority.High, loaded.Priority);
            Assert.Equal(99.99m, loaded.Amount);
        }
    }

    [Fact]
    public async Task Enum_AllEnumValues()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            // Test all OrderStatus values
            foreach (OrderStatus status in Enum.GetValues(typeof(OrderStatus)))
            {
                var order = new OrderEntity
                {
                    OrderNumber = $"ORD-{status}",
                    Status = status,
                    Priority = Priority.Medium,
                    Amount = 50.0m,
                    CreatedAt = DateTime.Now
                };
                db.Orders.Add(order);
            }

            await db.SaveChangesAsync();

            // Verify all statuses were saved
            Assert.Equal(5, db.Orders.Count);
        }

        // Reload and verify all values
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            var orders = db2.Orders.ToList();
            Assert.Equal(5, orders.Count);

            var statuses = orders.Select(o => o.Status).OrderBy(s => (int)s).ToList();
            Assert.Equal(OrderStatus.Pending, statuses[0]);
            Assert.Equal(OrderStatus.Processing, statuses[1]);
            Assert.Equal(OrderStatus.Shipped, statuses[2]);
            Assert.Equal(OrderStatus.Delivered, statuses[3]);
            Assert.Equal(OrderStatus.Cancelled, statuses[4]);
        }
    }

    [Fact]
    public async Task Enum_NullableEnumProperty()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            var order = new OrderEntity
            {
                OrderNumber = "ORD-002",
                Status = OrderStatus.Pending,
                Priority = Priority.Low,
                Amount = 25.50m,
                CreatedAt = DateTime.Now,
                LastStatus = OrderStatus.Cancelled,
                PaymentMethod = PaymentMethod.CreditCard
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        // Reload and verify nullable enum values
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            var loaded = db2.Orders.First();

            Assert.NotNull(loaded.LastStatus);
            Assert.Equal(OrderStatus.Cancelled, loaded.LastStatus);
            Assert.NotNull(loaded.PaymentMethod);
            Assert.Equal(PaymentMethod.CreditCard, loaded.PaymentMethod);
        }
    }

    [Fact]
    public async Task Enum_NullableEnumPropertyAsNull()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            var order = new OrderEntity
            {
                OrderNumber = "ORD-003",
                Status = OrderStatus.Shipped,
                Priority = Priority.Medium,
                Amount = 75.00m,
                CreatedAt = DateTime.Now,
                LastStatus = null,
                PaymentMethod = null
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        // Reload and verify nullable enum null values
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            var loaded = db2.Orders.First();

            Assert.Null(loaded.LastStatus);
            Assert.Null(loaded.PaymentMethod);
        }
    }

    [Fact]
    public async Task Enum_UpdateEnumValue()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            var order = new OrderEntity
            {
                OrderNumber = "ORD-004",
                Status = OrderStatus.Pending,
                Priority = Priority.Low,
                Amount = 50.00m,
                CreatedAt = DateTime.Now
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // Update enum value
            order.Status = OrderStatus.Delivered;
            order.Priority = Priority.Critical;
            db.Orders.Update(order);
            await db.SaveChangesAsync();
        }

        // Reload and verify updated enum values
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            var loaded = db2.Orders.First();

            Assert.Equal(OrderStatus.Delivered, loaded.Status);
            Assert.Equal(Priority.Critical, loaded.Priority);
        }
    }

    [Fact]
    public async Task Enum_DeleteEntityWithEnum()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            var order = new OrderEntity
            {
                OrderNumber = "ORD-005",
                Status = OrderStatus.Processing,
                Priority = Priority.High,
                Amount = 100.00m,
                CreatedAt = DateTime.Now
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // Delete the order
            db.Orders.Remove(order);
            await db.SaveChangesAsync();

            Assert.Empty(db.Orders);
        }

        // Reload and verify deletion
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            Assert.Empty(db2.Orders);
        }
    }

    [Fact]
    public async Task Enum_MultipleEnumTypesInEntity()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            var task = new TaskEntity
            {
                Title = "Important Task",
                TaskPriority = Priority.High,
                CurrentStatus = OrderStatus.Processing,
                PreferredPayment = PaymentMethod.BankTransfer,
                IsCompleted = false
            };

            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            Assert.Equal(1, task.Id);
        }

        // Reload and verify all enum types
        await EnumTestContext.ReleaseSharedCacheAsync(_testDbPath);
        var db2 = new EnumTestContext();
        await using (db2)
        {
            var loaded = db2.Tasks.First();

            Assert.Equal("Important Task", loaded.Title);
            Assert.Equal(Priority.High, loaded.TaskPriority);
            Assert.Equal(OrderStatus.Processing, loaded.CurrentStatus);
            Assert.Equal(PaymentMethod.BankTransfer, loaded.PreferredPayment);
            Assert.False(loaded.IsCompleted);
        }
    }

    [Fact]
    public async Task Enum_QueryByEnumValue()
    {
        var db = new EnumTestContext();
        await using (db)
        {
            db.Orders.Add(new OrderEntity
            {
                OrderNumber = "ORD-HIGH-1",
                Status = OrderStatus.Pending,
                Priority = Priority.High,
                Amount = 100.00m,
                CreatedAt = DateTime.Now
            });

            db.Orders.Add(new OrderEntity
            {
                OrderNumber = "ORD-LOW-1",
                Status = OrderStatus.Pending,
                Priority = Priority.Low,
                Amount = 25.00m,
                CreatedAt = DateTime.Now
            });

            db.Orders.Add(new OrderEntity
            {
                OrderNumber = "ORD-HIGH-2",
                Status = OrderStatus.Shipped,
                Priority = Priority.High,
                Amount = 150.00m,
                CreatedAt = DateTime.Now
            });

            await db.SaveChangesAsync();

            // Query by enum value
            var highPriorityOrders = db.Orders.Where(o => o.Priority == Priority.High).ToList();
            Assert.Equal(2, highPriorityOrders.Count);

            var pendingOrders = db.Orders.Where(o => o.Status == OrderStatus.Pending).ToList();
            Assert.Equal(2, pendingOrders.Count);

            var highAndPending = db.Orders.Where(o => o.Priority == Priority.High && o.Status == OrderStatus.Pending).ToList();
            Assert.Single(highAndPending);
        }
    }
}
