namespace Perigon.MiniDb.Tests;

public class MultiExtentTestDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
}

public class MultiExtentTests : IAsyncDisposable
{
    private readonly string _testDbPath;

    public MultiExtentTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_multiextent_{Guid.NewGuid()}.mds");
        MiniDbConfiguration.AddDbContext<MultiExtentTestDbContext>(o => o.UseMiniDb(_testDbPath));
    }

    public async ValueTask DisposeAsync()
    {
        await MultiExtentTestDbContext.ReleaseSharedCacheAsync(_testDbPath);
        await Task.Delay(10);

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Fact]
    public async Task MultiExtent_AddUpdateDeleteAndReload_RemainsConsistent()
    {
        const int initialCount = 2505; // Triggers multiple extents with 1000-record growth chunks
        var extentBoundaryIds = new[] { 1, 1000, 1001, 2000, 2001, 2505 };

        var db = new MultiExtentTestDbContext();
        await using (db)
        {
            for (int i = 1; i <= initialCount; i++)
            {
                db.Users.Add(new User
                {
                    Name = $"User-{i}",
                    Email = $"user{i}@example.com",
                    Age = 20 + (i % 50),
                    Balance = i,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync();
            Assert.Equal(initialCount, db.Users.Count);

            foreach (var id in extentBoundaryIds)
            {
                var user = db.Users.First(u => u.Id == id);
                user.Name = $"Updated-{id}";
                user.Balance += 1000m;
                db.Users.Update(user);
            }

            foreach (var id in new[] { 2, 1002, 2002 })
            {
                var user = db.Users.First(u => u.Id == id);
                db.Users.Remove(user);
            }

            await db.SaveChangesAsync();
        }

        await MultiExtentTestDbContext.ReleaseSharedCacheAsync(_testDbPath);

        var db2 = new MultiExtentTestDbContext();
        await using (db2)
        {
            Assert.Equal(initialCount - 3, db2.Users.Count);

            foreach (var id in extentBoundaryIds)
            {
                var updated = db2.Users.First(u => u.Id == id);
                Assert.Equal($"Updated-{id}", updated.Name);
                Assert.True(updated.Balance >= 1000m);
            }

            Assert.DoesNotContain(db2.Users, u => u.Id == 2);
            Assert.DoesNotContain(db2.Users, u => u.Id == 1002);
            Assert.DoesNotContain(db2.Users, u => u.Id == 2002);

            for (int i = 0; i < 10; i++)
            {
                db2.Users.Add(new User
                {
                    Name = $"Tail-{i}",
                    Email = $"tail{i}@example.com",
                    Age = 30,
                    Balance = 10m,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await db2.SaveChangesAsync();

            var maxId = db2.Users.Max(u => u.Id);
            Assert.True(maxId >= initialCount + 10);
            Assert.Equal(initialCount - 3 + 10, db2.Users.Count);
        }
    }
}
