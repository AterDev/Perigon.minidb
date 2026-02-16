# Perigon MiniDB Client

基于 **.NET MAUI + Blazor Hybrid + Fluent UI** 的 MiniDB 桌面管理工具（Windows / macOS）。

## 当前能力

- 连接管理（新增/更新/删除），本地持久化
- 连接后自动加载表列表与数据
- 分页浏览（首页/上一页/下一页/末页）
- 快速关键字筛选
- 连接与表搜索
- 主题切换（浅色/深色/跟随系统）
- 中英文切换（持久化）
- 原生菜单动作联动页面（连接/断开/刷新）

## 技术栈

- **Framework**: .NET 10
- **Shell/UI**: MAUI + BlazorWebView
- **Components**: Microsoft Fluent UI for Blazor
- **Pattern**: MVVM + Service 分层

## 运行方式

```bash
dotnet build src/Perigon.MiniDb.Client/Perigon.MiniDb.Client.csproj
dotnet run --project src/Perigon.MiniDb.Client/Perigon.MiniDb.Client.csproj
```

## 关键目录

- `MainPage.xaml` / `MainPage.xaml.cs`：MAUI 页面壳与原生菜单
- `AppHost.razor` / `AppHost.razor.cs`：Blazor 主页面与交互逻辑
- `ViewModels/MainViewModel.cs`：状态编排与命令
- `Services/`：连接会话、筛选分页、本地化、状态语义等服务
- `Services/MiniDbFileDriver.cs`：客户端驱动层（直接读取文件元数据与记录）

## Schema 解析说明

客户端仅使用 `.mds` 文件内嵌的 schema（由 MiniDb v2+ 在创建时写入）进行结构化解析。

- v2+ 且 schema 完整：按内嵌字段定义展示列数据
- v1 或 schema 缺失/损坏：连接阶段直接判定为无效数据库文件并提示，不进入 `RawText` 回退展示
