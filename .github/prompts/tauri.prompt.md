# Tauri数据库管理客户端开发

`src\Perigon.MiniDb.Client`是使用Avalonia框架开发的数据库管理客户端，提供了一个图形界面，用来连接和读取MiniDb数据库文件的数据。

现在需要开发一个基于`Tauri2`框架的数据库管理客户端，提供更好的性能和用户体验。

<important>
全自主完成所有工作，拥有所有命令行/editor权限；不确认的暂时以常规方式开发。最后给出开发内容和后续待办和完善的总结报告。
</important>

## 技术选型
- 基于`Tauri2`框架开发，使用Rust语言进行后端开发，前端使用`Angular`最新版框架
- UI使用fluent UI，参考[Fluent UI for Angular](https://learn.microsoft.com/en-us/fluent-ui/web-components/integrations/angular)
- 要针对Windows，通过插件实现Mica，毛玻璃等效果
- 代码结构要清晰，分工明确，遵循`Angular`和`Tauri`的最佳实践，保证可维护性和可扩展性

> 当前使用VSCode，已安装Tauri插件

## 功能说明

根据现有 Avaloina客户端功能，开发基于`Tauri2`框架的数据库管理客户端，提供：

1. 数据库连接管理（添加、编辑、删除连接）
2. 数据库操作，连接/断开/刷新
3. 表列表展示，选择；表数据展示，分页，特定字段的筛选。
4. 必须支持多语言机制;当前支持中英文
5. 必须支持主题切换

## 执行步骤

1. 安装必要的开发工具和环境，你可以自由使用pwsh，winget等命令来安装。
2. 创建项目，在目录`src\MiniDb.Client`。
3. 理解现有功能点，并分析`Perigon.MiniDb.Client`和`Perigon.MiniDb`以了解文件格式和数据结构，制定开发计划到`docs\Tauri数据库管理客户端.md`。
4. 根据开发计划文档`docs\Tauri数据库管理客户端.md`，自主的无中断的进行开发。
5. 遵循`编码/审核/构建`流程，不断迭代开发，直到完成所有功能点的开发和测试，在遇到问题时，要主动搜索官方相关信息和社区资源，解决问题后继续开发。

