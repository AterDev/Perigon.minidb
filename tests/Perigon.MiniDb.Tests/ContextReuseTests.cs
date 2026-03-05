namespace Perigon.MiniDb.Tests;

/// <summary>
/// Isolated DbContext for ContextReuseTests to avoid parallel test config conflicts.
/// </summary>
public class ReuseTestDbContext : MiniDbContext
{
    public DbSet<Solution> Solutions { get; set; } = null!;
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<ApiDocumentation> ApiDocumentations { get; set; } = null!;
    public DbSet<AppConfiguration> Configurations { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
}

public class ContextReuseTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public ContextReuseTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_reuse_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<ReuseTestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(10);
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task CanReopenContextAfterDispose()
    {
        await using (var db = new ReuseTestDbContext())
        {
            var customer = new Customer
            {
                Name = "Contoso",
                Email = "contact@contoso.com",
                IsActive = true,
                RegisteredAt = DateTime.UtcNow,
                LoyaltyLevel = 3
            };

            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            var order = new Order
            {
                CustomerId = customer.Id,
                Status = ReuseOrderStatus.Paid,
                Total = 199.99m,
                PaidAt = DateTime.UtcNow
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync();

            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                Sku = "SKU-001",
                Quantity = 2,
                UnitPrice = 49.99m
            });
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                Sku = "SKU-002",
                Quantity = 1,
                UnitPrice = 99.99m
            });

            await db.SaveChangesAsync();
        }

        await using (var reopened = new ReuseTestDbContext())
        {
            Assert.Equal(1, reopened.Customers.Count);
            Assert.Equal(1, reopened.Orders.Count);
            Assert.Equal(2, reopened.OrderItems.Count);

            var loadedOrder = reopened.Orders.First();
            Assert.Equal(ReuseOrderStatus.Paid, loadedOrder.Status);
            Assert.Equal(199.99m, loadedOrder.Total);
        }
    }
}
