# Perigon.MiniDb

一个轻量级、高性能的单文件内存数据库引擎，专为小数据量场景设计（≤50MB）。

[![NuGet](https://img.shields.io/nuget/v/Perigon.MiniDb.svg)](https://www.nuget.org/packages/Perigon.MiniDb/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## ✨ 核心特性

- **🚀 全异步API**：完整的 async/await 支持，非阻塞I/O操作
- **💾 单文件存储**：所有数据存储在一个二进制文件中
- **⚡ 内存操作**：全量数据加载到内存，LINQ查询性能极佳
- **📝 增量更新**：只写入修改的记录，避免全量写入
- **🔒 线程安全**：上下文内读写安全、同一文件写入串行（SaveChanges 直接落盘）
- **🎯 简单API**：类似 EF Core 的使用体验，无需复杂配置
- **🔧 零依赖**：完全自包含实现，无需外部库
- **✅ 类型安全**：强类型实体模型，编译时检查

## 📦 安装

### NuGet Package Manager
```bash
Install-Package Perigon.MiniDb
```

### .NET CLI
```bash
dotnet add package Perigon.MiniDb
```

### PackageReference
```xml
<PackageReference Include="Perigon.MiniDb" Version="0.0.1" />
```

## 🎯 适用场景

### ✅ 推荐使用
- 桌面应用的本地数据存储
- 开发/测试环境的快速数据库
- 配置文件的结构化存储
- 嵌入式应用的轻量级数据库
- 单用户应用的数据持久化
- 小型工具和脚本的数据管理

### ❌ 不推荐使用
- 多用户Web应用（高并发场景）
- 大数据集（>50MB文件）
- 需要复杂查询和索引的场景
- 需要事务隔离的应用
- 企业级生产系统

## 🚀 快速开始

### 1. 定义实体模型

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Perigon.MiniDb;

public class User : IMicroEntity
{
    public int Id { get; set; }
    
    [MaxLength(50)]  // 必须指定字符串的最大字节数
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;
    
    public int Age { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    
    // 可空类型支持
    public int? CategoryId { get; set; }
    public DateTime? PublishedAt { get; set; }
    
    // 计算属性 - 不会保存到数据库
    [NotMapped]
    public bool IsAdult => Age >= 18;
    
    // 临时属性 - 不会持久化
    [NotMapped]
    public bool IsProcessing { get; set; }
}
```

#### 📌 实体模型要求

1. **必须实现 IMicroEntity 接口**：每个实体必须实现 `IMicroEntity` 接口，该接口定义了 `int Id { get; set; }` 属性
2. **字符串必须标注长度**：所有 `string` 类型属性必须使用 `[MaxLength]` 特性指定最大字节数（UTF-8编码）
3. **支持 [NotMapped] 特性**：使用 `[NotMapped]` 特性可以排除属性不保存到数据库（计算属性、临时属性等）
4. **支持的数据类型**：仅支持特定类型（见下表）

### 2. 创建 DbContext

```csharp
using Perigon.MiniDb;

public class MyDbContext : MiniDbContext
{
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
}
```

### 3. 配置和使用数据库

```csharp
// 1. 全局配置数据库路径（通常在程序启动时）
MiniDbConfiguration.AddDbContext<MyDbContext>(options => options.UseMiniDb("app.mds"));

// 2. 创建数据库上下文（无需参数）
var db = new MyDbContext();

// 3. 使用数据库
// 初始化：加载数据到内存（自动完成）
await using (db)
{
    // 添加数据
    db.Users.Add(new User 
    { 
        Name = "Alice",
        Email = "alice@example.com",
        Age = 25,
        Balance = 1000m,
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    });
    await db.SaveChangesAsync();

    // 查询数据（支持完整的 LINQ）
    var activeUsers = db.Users
        .Where(u => u.IsActive && u.Balance >= 500)
        .OrderByDescending(u => u.CreatedAt)
        .ToList();

    // 更新数据
    var user = db.Users.First(u => u.Name == "Alice");
    user.Balance += 500m;
    db.Users.Update(user);
    await db.SaveChangesAsync();

    // 删除数据
    db.Users.Remove(user);
    await db.SaveChangesAsync();
}

// 说明：不再提供 ReleaseSharedCache* API。
// 上下文销毁后其内存缓存随实例释放；持久化由 SaveChangesAsync 负责。
```

## 📊 支持的数据类型

| 类型 | 大小 | 说明 | 示例 |
|------|------|------|------|
| `int` (Id) | 4 字节 | **必需**: 实体标识符 | `public int Id { get; set; }` (来自 `IMicroEntity`) |
| `int` | 4 字节 | 32位有符号整数 | `public int Age { get; set; }` |
| `int?` | 5 字节 | 可空整数（1字节标记+4字节值） | `public int? CategoryId { get; set; }` |
| `bool` | 1 字节 | 布尔值 | `public bool IsActive { get; set; }` |
| `bool?` | 2 字节 | 可空布尔（1字节标记+1字节值） | `public bool? IsPublished { get; set; }` |
| `decimal` | 16 字节 | 高精度十进制（适合金融计算） | `public decimal Balance { get; set; }` |
| `decimal?` | 17 字节 | 可空十进制（1字节标记+16字节值） | `public decimal? Price { get; set; }` |
| `DateTime` | 8 字节 | 日期时间（UTC格式，Ticks存储） | `public DateTime CreatedAt { get; set; }` |
| `DateTime?` | 9 字节 | 可空日期时间（1字节标记+8字节值） | `public DateTime? PublishedAt { get; set; }` |
| `string` | 可变 | UTF-8编码字符串，**必须**使用 `[MaxLength]` | `[MaxLength(100)] public string Name { get; set; }` |

### 🔴 不支持的类型
- ❌ `long`, `short`, `byte`
- ❌ `double`, `float`
- ❌ `byte[]`, `Stream`
- ❌ `List<T>`, `Dictionary<K,V>`
- ❌ `object`, `dynamic`
- ❌ 自定义类/结构体

### ⚠️ 字符串使用注意事项

1. **必须标注长度**：`[MaxLength]` 特性是必需的
   ```csharp
   [MaxLength(100)]  // 指定UTF-8编码后的最大字节数
   public string Email { get; set; }
   ```

2. **UTF-8字节数**：`MaxLength` 指定的是字节数，而非字符数
   - ASCII字符：1字节
   - 中文字符：通常3字节
   - 表情符号：通常4字节
   
3. **超长自动截断**：超出 `MaxLength` 的字符串会自动截断（在UTF-8字符边界）

4. **未标注默认值**：如果忘记标注，默认使用1024字节

### 💡 [NotMapped] 特性使用

使用 `[NotMapped]` 特性可以排除属性不保存到数据库：

```csharp
using System.ComponentModel.DataAnnotations.Schema;

public class User : IMicroEntity
{
    public int Id { get; set; }
    
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    
    public int Age { get; set; }
    
    // 计算属性 - 不会保存到数据库
    [NotMapped]
    public bool IsAdult => Age >= 18;
    
    // 临时状态 - 不会持久化
    [NotMapped]
    public bool IsSelected { get; set; }
    
    // 格式化显示 - 不会存储
    [NotMapped]
    public string DisplayName => $"{Name} (ID: {Id})";
}
```

**使用场景**：
- ✅ 计算属性（只读属性，基于其他属性计算）
- ✅ 临时状态标记（UI 选中状态、处理标记等）
- ✅ 格式化显示属性
- ✅ 业务逻辑辅助属性

### 🔄 通过 JSON 支持复杂类型

对于不支持的复杂类型（嵌套对象、集合等），可以通过 JSON 序列化存储：

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

// 复杂类型定义
public class Address
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class User : IMicroEntity
{
    public int Id { get; set; }
    
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
    
    // 复杂类型属性 - 标记为 [NotMapped]
    [NotMapped]
    public Address? Address
    {
        get
        {
            if (string.IsNullOrEmpty(AddressJsonString))
                return null;
            return JsonSerializer.Deserialize<Address>(AddressJsonString);
        }
        set
        {
            AddressJsonString = value == null 
                ? string.Empty 
                : JsonSerializer.Serialize(value);
        }
    }
    
    // 实际存储的 JSON 字符串
    [MaxLength(500)]
    public string AddressJsonString { get; set; } = string.Empty;
}

// 使用示例
var user = new User
{
    Name = "Alice",
    Address = new Address
    {
        Street = "123 Main St",
        City = "New York",
        Country = "USA",
        ZipCode = "10001"
    }
};

db.Users.Add(user);
await db.SaveChangesAsync();

// 加载后自动反序列化
var loaded = db.Users.First();
Console.WriteLine(loaded.Address.City);  // "New York"
```

**适用场景**：
- ✅ 嵌套对象（地址、联系人信息等）
- ✅ 配置数据（键值对、设置项等）
- ✅ 元数据（标签、属性列表等）
- ✅ 动态结构数据

**注意事项**：
- ⚠️ 不能直接查询 JSON 中的嵌套属性
- ⚠️ 需要加载到内存后再过滤
- ⚠️ 建议控制 JSON 大小（< 10KB）

## 🔧 关键行为说明

### 自动初始化

```csharp
// ✅ 创建即可用：构造函数自动加载数据
var db = new MyDbContext("app.mds");
await using (db)
{
    // 直接使用，无需额外初始化
    db.Users.Add(new User 
    { 
        Name = "Alice",
        Email = "alice@example.com",
        Age = 25,
        Balance = 1000m,
        CreatedAt = DateTime.UtcNow,
        IsActive = true
    });
    await db.SaveChangesAsync();
}
```

**自动初始化的工作原理**：

- **构造函数**：创建上下文、打开/创建数据库文件、加载所有表数据到内存
- **即用**：DbSet 属性在构造函数中已初始化，可以直接使用

**注意事项**：
1. ✅ 构造函数会同步加载数据（对于小数据库 ≤50MB 很快）
2. ✅ 每个上下文实例拥有独立内存缓存（隔离）
3. ✅ 类似 EF Core 的使用体验，无需额外步骤

### 上下文内存架构

```csharp
// 同一文件可创建多个上下文（内存彼此独立）
var db1 = new MyDbContext("app.mds");
var db2 = new MyDbContext("app.mds");

// db1 和 db2 各自维护内存数据；对同一文件保存后可通过版本检测触发刷新
db1.Users.Add(new User 
{ 
    Name = "Alice",
    Email = "alice@example.com",
    Age = 25,
    Balance = 1000m,
    CreatedAt = DateTime.UtcNow,
    IsActive = true
});
await db1.SaveChangesAsync();

// db2 在后续查询/保存路径会检查文件头版本并自动刷新
Console.WriteLine(db2.Users.Count);
```

### 上下文生命周期

```csharp
// DbContext 销毁时会释放该实例的内存缓存
var db = new MyDbContext("app.mds");
await using (db)
{
    // 使用数据库
} // Dispose 时实例内存释放
```

### 软删除机制

```csharp
// 删除操作只标记记录为已删除
db.Users.Remove(user);
await db.SaveChangesAsync();  // 只写入1字节的删除标记

// 已删除的记录不会出现在查询结果中
var users = db.Users.ToList();  // 自动过滤已删除记录

// 定期压缩可以回收空间（未来版本）
```

### 自动ID分配

```csharp
var db = new MyDbContext("app.mds");

var user = new User 
{ 
    Name = "Alice",
    Email = "alice@example.com",
    Age = 25,
    Balance = 1000m,
    CreatedAt = DateTime.UtcNow,
    IsActive = true
};  // Id = 0（未设置）

db.Users.Add(user);
await db.SaveChangesAsync();

Console.WriteLine(user.Id);  // 自动分配: 1

// 手动指定ID也可以
var user2 = new User 
{ 
    Id = 100,
    Name = "Bob",
    Email = "bob@example.com",
    Age = 30,
    Balance = 2000m,
    CreatedAt = DateTime.UtcNow,
    IsActive = true
};
db.Users.Add(user2);
await db.SaveChangesAsync();  // 使用指定的ID
```

## 📁 文件格式

数据库使用固定长度二进制格式，支持高效的随机访问：

```
┌─────────────────────────────────────────────┐
│ 文件头 (256字节)                           │
│ - 魔法数: "MDB1"                           │
│ - 版本: 1                                  │
│ - 表数量                                   │
│ - 保留字段                                 │
├─────────────────────────────────────────────┤
│ 表元数据 (每表128字节)                     │
│ - 表名                                     │
│ - 记录数                                   │
│ - 记录大小                                 │
│ - 数据起始偏移                             │
├─────────────────────────────────────────────┤
│ 表数据（固定长度记录）                      │
│ [IsDeleted(1B)][Id(4B)][字段数据...]       │
│ [IsDeleted(1B)][Id(4B)][字段数据...]       │
│ ...                                        │
└─────────────────────────────────────────────┘
```

### 性能特性

- **O(1) 记录定位**：`offset = tableStart + (id - 1) × recordSize`
- **增量更新**：只写入变更的记录
- **软删除**：删除只需设置1字节标记
- **预知大小**：文件大小在创建时即可计算

## ⚙️ 高级用法

### 取消令牌支持

```csharp
var db = new MyDbContext("app.mds");

using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(30));

try
{
    await db.SaveChangesAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("操作已取消");
}
```

### 批量操作

```csharp
var db = new MyDbContext("app.mds");

// 批量添加
for (int i = 0; i < 1000; i++)
{
    db.Users.Add(new User { Name = $"User{i}", Email = $"user{i}@example.com", Age = 20 + (i % 50), Balance = 100m * i, CreatedAt = DateTime.UtcNow, IsActive = true });
}
await db.SaveChangesAsync();  // 一次性写入所有记录

// 批量更新
var users = db.Users.Where(u => u.IsActive).ToList();
foreach (var user in users)
{
    user.Balance += 100;
    db.Users.Update(user);
}
await db.SaveChangesAsync();  // 一次性写入所有更新
```

### 复杂查询

```csharp
var db = new MyDbContext("app.mds");

// 支持完整的 LINQ
var result = db.Users
    .Where(u => u.Age >= 18 && u.Age <= 60)
    .Where(u => u.Balance > 1000m)
    .OrderByDescending(u => u.Balance)
    .ThenBy(u => u.Name)
    .Select(u => new 
    { 
        u.Name, 
        u.Balance, 
        Category = u.CategoryId ?? 0 
    })
    .Take(10)
    .ToList();
```

## 📈 性能指标

### 操作性能
| 操作 | 时间复杂度 | 典型耗时 |
|------|-----------|---------|
| 查询 | O(n) | 微秒级（内存） |
| 插入（单条） | O(1) | ~30ms |
| 插入（批量1000条） | O(n) | < 100ms |
| 更新 | O(1) | ~30ms |
| 删除 | O(1) | ~30ms（软删除） |
| 初始化 | O(n) | 文件大小决定 |

### 性能最佳实践

#### ✅ 推荐模式：批量操作

```csharp
// 批量添加：收集所有更改，一次保存
for (int i = 0; i < 1000; i++)
{
    db.Users.Add(new User { ... });
}
await db.SaveChangesAsync();  // < 100ms for 1000 records
```

#### ❌ 避免反模式：高频保存

```csharp
// 不推荐：每次添加都保存
for (int i = 0; i < 1000; i++)
{
    db.Users.Add(new User { ... });
    await db.SaveChangesAsync();  // 1000 × 30ms = 30秒
}
```

### 性能说明

- **文件I/O是主要瓶颈**：每次 `SaveChangesAsync` 涉及磁盘写入
- **单次保存延迟**：~30ms（文件打开、写入、Flush、关闭）
- **批量操作优势**：1000条记录 < 100ms（一次文件操作）
- **SaveChanges 直接落盘**：保证每次提交后数据已持久化

### 推荐配置
- **记录数**：≤ 100,000
- **文件大小**：≤ 50MB
- **内存占用**：≈ 文件大小
- **并发读取**：支持并行
- **并发写入**：同一文件在同一进程内串行化提交（由文件级写门闩保证）
- **单次SaveChanges延迟**：~30ms
- **批量操作吞吐量**：10,000+ 记录/秒
