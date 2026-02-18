# Perigon.MiniDb - GitHub Copilot Instructions

## 项目概览

**Perigon.MiniDb** 是一个轻量级、高性能的单文件内存数据库引擎，使用 .NET 10 和 C# 14 开发。

- **项目类型**：.NET C# 类库（带 WPF 客户端）
- **目标框架**：.NET 10.0
- **C# 版本**：14.0
- **主要特性**：全异步API、单文件存储、内存操作、增量更新、线程安全、LINQ查询
- **适用场景**：≤50MB 文件，单用户或离线应用，桌面应用本地存储
- **不支持**：多用户Web应用、复杂外键、SQL查询、事务隔离

---

## 项目结构

```
Perigon.MiniDb/
├── src/
│   ├── Perigon.MiniDb/                  # 核心库
│   │   ├── MiniDbContext.cs             # 数据库上下文（继承基类）
│   │   ├── DbSet.cs                     # 泛型表集合
│   │   ├── IMicroEntity.cs              # 实体接口（必须实现）
│   │   ├── ChangeTracker.cs             # 变更追踪（Added/Modified/Deleted）
│   │   ├── StorageManager.cs            # 二进制文件操作
│   │   ├── FileDataCache.cs             # DbContext 私有内存缓存
│   │   ├── EntityMetadata.cs            # 运行时元数据（启动缓存）
│   │   ├── MiniDbConfiguration.cs       # 全局配置和注册
│   │   ├── MiniDbOptions.cs             # 配置选项
│   │   └── (线程安全由 FileDataCache / SaveChanges 写锁协同实现)
│   │
│   ├── Perigon.MiniDb.Client/           # Avalonia 桌面客户端
│   │   ├── ViewModels/                  # MVVM 视图模型
│   │   ├── Views/                       # XAML 视图和对话框
│   │   ├── Services/                    # 业务服务层
│   │   ├── Models/                      # 数据模型
│   │   └── Helpers/                     # 辅助类（RelayCommand）
│   │
│   └── Perigon.MiniDb.Client.csproj
│
├── tests/
│   └── Perigon.MiniDb.Tests/
│       ├── RequiredPropertyTests.cs     # 必需属性和枚举类型测试
│       ├── MiniDbAsyncTests.cs          # 异步操作测试
│       ├── ExceptionHandlingTests.cs    # 异常处理测试
│       ├── ConcurrencyTests.cs          # 并发测试
│       └── Perigon.MiniDb.Tests.csproj
│
├── samples/
│   └── Perigon.MiniDb.Sample/           # 控制台示例应用
│
└── docs/
    ├── 项目开发文档.md                   # 完整开发文档
    ├── 架构演进总结.md                   # 架构说明
    └── 技术设计文档.md                   # 技术细节
```

---

## 核心架构

### 数据流

```
DbContext (MiniDbContext)
    ↓ 创建/打开
DbSet<T> (内存集合 + 变更追踪)
    ↓ Add/Update/Remove
ChangeTracker (HashSet 追踪)
    ↓ SaveChangesAsync
FileDataCache (上下文内存存储)
    ↓ 获取写入锁
StorageManager (二进制格式)
    ↓ 
磁盘文件 (.mds)
```

### 关键组件

#### 1. MiniDbContext (抽象基类)
- **职责**：数据库上下文管理、DbSet 初始化、SaveChanges 协调
- **特点**：
  - 自动扫描 public DbSet<T> 属性
  - 自动加载上下文内存数据
  - 通过文件头版本检测与外部更新保持同步
  - 支持 IDisposable 和 IAsyncDisposable

#### 2. DbSet<T> (泛型表集合)
- **职责**：表级操作、LINQ 查询、实体管理
- **支持**：Add、Update、Remove、First、Where、Select 等标准 LINQ
- **特点**：
  - 实现 IEnumerable<T>，支持 foreach
  - Count 属性
  - 自动调用 ChangeTracker 追踪变更

#### 3. ChangeTracker
- **职责**：跟踪实体状态（新增、修改、删除）
- **存储**：三个 HashSet<object>（Added、Modified、Deleted）
- **性能**：O(1) 查询和添加
- **线程安全**：使用 Lock 保护

#### 4. FileDataCache
- **职责**：DbContext 实例内存数据管理与并发同步
- **生命周期**：DbContext 创建 → 持续存在 → DbContext 释放
- **API**：
  - GetOrLoadTableDataAsync() - 延迟加载表数据
  - EnterReadLock/EnterWriteLock - 同步访问控制
  - EnterWriteLockAsync - 异步访问控制

#### 5. StorageManager
- **职责**：二进制文件格式读写
- **格式**：固定长度记录（O(1) 寻址）
- **特性**：
  - 软删除（IsDeleted 字节标记）
  - 类型大小预计算（EntityMetadata）
  - Span<T> 零分配读写
  - ArrayPool 缓冲区复用

#### 6. EntityMetadata
- **职责**：运行时实体元数据管理
- **缓存**：启动时反射，FrozenDictionary 缓存
- **内容**：字段顺序、类型大小、可空标记
- **性能**：运行时零反射

## 支持的数据类型

### 基础类型（✅ 完整支持）

| 类型 | 大小 | 说明 | 示例 |
|------|------|------|------|
| `int` | 4 字节 | 32位整数 | `public int Age { get; set; }` |
| `bool` | 1 字节 | 布尔值 | `public bool IsActive { get; set; }` |
| `decimal` | 16 字节 | 128位十进制 | `public decimal Price { get; set; }` |
| `DateTime` | 8 字节 | UTC 时间 | `public DateTime CreatedAt { get; set; }` |
| `string` | 可变 | UTF-8 编码 | `[MaxLength(50)] public string Name { get; set; }` |
| **枚举** | 4/8 字节 | 整数枚举 | `public OrderStatus Status { get; set; }` |

### 可空类型（✅ 完整支持）

在上述基础类型前加 `?`，额外需要 1 字节标记 null：

```csharp
public int? CategoryId { get; set; }           // 5 字节（4 + 1）
public DateTime? PublishedAt { get; set; }     // 9 字节（8 + 1）
public bool? HasConfirmed { get; set; }        // 2 字节（1 + 1）
public OrderStatus? PreviousStatus { get; set; } // 5 字节（4 + 1）
```

## 开发规范

**现代 C# 特性**（.NET 10 / C# 14）：
- ✅ File-scoped namespaces：`namespace Perigon.MiniDb;`
- ✅ Primary constructors：`public class Foo(string bar) { }`
- ✅ Collection expressions：`[.. items]`
- ✅ Switch expressions：`x switch { 1 => "one", _ => "other" }`
- ✅ Range 和 Index：`data[1..]`, `data[^1]`
- ✅ Lock 类型：`lock (obj) { }`（替代 lock 语句）
- ✅ FrozenDictionary / FrozenSet：缓存元数据
- ✅ 总是接受 `CancellationToken` 参数（默认值 = `default`）
- ✅ 总是 `await` 异步调用（不要 fire-and-forget）
- ✅ 使用 `ConfigureAwait(false)` 在库代码中
- ✅ 使用 `await using` 处理异步资源
- ✅ 不要有多余的 `async/await`：


## 资源链接

- 📖 **完整开发文档**：`docs/项目开发文档.md`
- 🔧 **技术细节**：`docs/技术设计文档.md`
- 📦 **GitHub**：https://github.com/AterDev/Perigon.minidb
- 📝 **README**：根目录 `README.md`