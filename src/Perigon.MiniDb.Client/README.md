# Perigon MiniDB Client

基于 **Avalonia + .NET 10** 的轻量数据库浏览工具，用于打开 MiniDB 文件、浏览表数据、分页查看与字段筛选。

## 当前能力

- 连接管理（新增/更新/删除），本地持久化保存
- 连接选择后自动加载表列表
- 表数据分页浏览（首页/上一页/下一页/末页）
- 字段筛选（`Contains/Equals/NotEquals/>/>=/</<=/Between`）
- 多条件 `AND` 组合筛选
- 连接与表搜索
- 主题切换（浅色/深色/跟随系统）
- 中英文界面切换（可持久化）
- Windows 毛玻璃效果（支持时启用，自动降级）
- 视图偏好持久化（主题/毛玻璃）
- 帮助菜单一键打开仓库地址与问题反馈（Issues）

## Language & Help Menu

- 顶部菜单 `语言 / Language` 可切换 `中文` 与 `English`
- 语言偏好会自动保存，重启后仍生效
- 顶部菜单 `帮助 / Help` 提供：
	- `打开仓库地址 / Open repository`
	- `打开问题反馈 / Open issues`

## 技术栈

- **Framework**: .NET 10.0
- **UI**: Avalonia 11
- **Pattern**: MVVM
- **Platform**: Windows x64（当前主要目标）

## 运行方式

```bash
dotnet build src/Perigon.MiniDb.Client/Perigon.MiniDb.Client.csproj
dotnet run --project src/Perigon.MiniDb.Client/Perigon.MiniDb.Client.csproj
```

## 目录概览

- `MainWindow.axaml` / `MainWindow.xaml.cs`：主界面与交互
- `ViewModels/MainViewModelV2.cs`：连接、表加载、分页、筛选、状态管理（MVVM Toolkit）
- `Services/DatabaseConnectionService.cs`：连接持久化
- `Services/ClientSettingsService.cs`：视图偏好持久化
- `Models/DatabaseConnection.cs`：连接模型
- `Models/FilterCondition.cs`：筛选条件模型
- `Sample/SampleDbContext.cs`：示例数据上下文

## 已知限制

- 当前采用已知 `DbContext` 模式（`SampleDbContext`）读取数据
- 不支持任意未知结构 `.mds` 的动态 schema 浏览
- 主要定位为单机轻量浏览工具，不面向多用户并发编辑

## 快速自测清单（发布前）

- [ ] 添加连接并重启应用，连接仍存在
- [ ] 连接后可看到表列表，切换表可加载数据
- [ ] 分页按钮（首页/上一页/下一页/末页）行为正确
- [ ] 添加多个筛选条件（AND）后查询结果正确
- [ ] 点击已有筛选条件可回填到编辑区
- [ ] 语言切换后界面主要文案和筛选操作符显示正确
- [ ] 帮助菜单可正常打开仓库与 issues 页面
- [ ] Win11 下毛玻璃生效；不支持环境自动降级且可正常使用
