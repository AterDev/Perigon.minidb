# Perigon MiniDB 客户端工具

一个基于 WPF 的 Windows 桌面应用程序，用于管理和查看 Perigon MiniDB 数据库文件。

## 功能特性

### 数据库连接管理
- ✅ 添加数据库连接（名称 + 路径）
- ✅ 编辑现有连接
- ✅ 删除连接
- ✅ 连接配置自动保存到本地

### 数据库操作
- ✅ 连接到 MiniDB 数据库文件
- ✅ 浏览数据库中的所有表
- ✅ 查看表数据（以数据网格形式展示）
- ✅ 编辑数据并保存更改
- ✅ 断开数据库连接

### 用户界面
- ✅ 深色主题设计（Dark Mode）
- ✅ 适配 Windows 11 风格
- ✅ 响应式布局
- ✅ 状态栏显示操作状态

### 文件锁定处理
- ✅ 检测数据库文件是否被其他进程锁定
- ✅ 提供友好的错误提示
- ✅ 支持文件共享读取

## 技术规格

- **框架**: .NET 10.0
- **UI框架**: WPF (Windows Presentation Foundation)
- **目标平台**: Windows 10/11 (x64)
- **设计模式**: MVVM (Model-View-ViewModel)

## 系统要求

- Windows 10 版本 1809 或更高版本
- Windows 11（推荐）
- .NET 10.0 Runtime

## 项目结构

```
Perigon.MiniDb.Client/
├── Models/              # 数据模型
│   ├── DatabaseConnection.cs
│   └── TableInfo.cs
├── ViewModels/          # 视图模型（MVVM）
│   └── MainViewModel.cs
├── Views/               # 视图和对话框
│   └── ConnectionDialog.xaml
├── Services/            # 业务服务
│   ├── DatabaseConnectionService.cs
│   └── DatabaseReaderService.cs
├── Helpers/             # 辅助类
│   └── RelayCommand.cs
├── Sample/              # 示例数据库
│   └── SampleDbContext.cs
├── MainWindow.xaml      # 主窗口
└── App.xaml            # 应用程序入口
```

## 使用指南

### 1. 创建示例数据库

首次启动时，应用程序会在 `Documents\Perigon.MiniDb.Sample` 目录下自动创建一个示例数据库。

您也可以通过菜单 `File -> Create Sample Database` 手动创建示例数据库。

### 2. 添加数据库连接

1. 点击工具栏的 `➕ Add Connection` 按钮
2. 输入连接名称
3. 浏览并选择数据库文件（.mdb）
4. 点击 OK 保存

### 3. 连接到数据库

1. 在左侧面板的"Connections"列表中选择一个连接
2. 点击工具栏的 `🔌 Connect` 按钮
3. 连接成功后，表列表会显示在左下方

### 4. 查看和编辑数据

1. 在"Tables"列表中选择要查看的表
2. 表数据将在右侧的数据网格中显示
3. 双击单元格即可编辑数据
4. 编辑完成后，点击 `💾 Save Changes` 保存修改

### 5. 断开连接

点击工具栏的 `⛔ Disconnect` 按钮断开当前连接。

## 技术说明

### 内存操作模式

Perigon MiniDB 采用全内存操作模式：
- 连接数据库时，所有数据会加载到内存
- 后续的查询和操作都在内存中进行（性能极快）
- 修改和删除通过类库方法同步到文件

### 文件锁定处理

- 当程序和管理客户端同时访问数据库时，文件可能被锁定
- 系统会检测文件锁定状态
- 如果文件被锁定，会提示用户稍后重试
- 支持文件共享读取模式

### 数据保存机制

- 所有修改先在内存中进行
- 点击 `Save Changes` 后才会写入文件
- 使用增量写入机制，只更新修改的记录

## 限制说明

### 当前版本限制

1. **数据库类型识别**: 
   - 当前版本使用预定义的 DbContext（SampleDbContext）
   - 只能查看包含 Products 和 Categories 表的数据库
   - 后续版本将支持动态加载任意数据库结构

2. **数据验证**:
   - 基本的数据类型验证
   - 不支持复杂的业务规则验证

3. **并发控制**:
   - 不支持多用户同时编辑
   - 文件锁定时需要等待其他进程释放

## 开发说明

### 构建项目

```bash
# 构建项目
dotnet build src/Perigon.MiniDb.Client/Perigon.MiniDb.Client.csproj

# 运行项目
dotnet run --project src/Perigon.MiniDb.Client/Perigon.MiniDb.Client.csproj
```

### 添加新功能

1. **添加新的数据库实体**:
   - 在 `Sample/` 目录下定义实体类
   - 实现 `IMicroEntity` 接口
   - 在 `SampleDbContext` 中添加 `DbSet<T>` 属性

2. **扩展 UI 功能**:
   - 在 `ViewModels/` 中添加新的 ViewModel
   - 在 `Views/` 中创建对应的 XAML 视图
   - 使用 `RelayCommand` 处理用户交互

## 后续计划

- [ ] 支持动态加载任意数据库结构
- [ ] 添加数据导入/导出功能
- [ ] 支持数据搜索和过滤
- [ ] 添加数据库压缩工具
- [ ] 支持主题切换（亮色主题）
- [ ] 添加更多数据编辑工具（批量编辑、复制粘贴等）

## 许可证

MIT License - 参见根目录的 LICENSE 文件

## 联系方式

- 项目主页: https://github.com/AterDev/Perigon.minidb
- 问题反馈: https://github.com/AterDev/Perigon.minidb/issues
