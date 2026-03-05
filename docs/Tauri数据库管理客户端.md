# Tauri 数据库管理客户端开发计划

## 一、项目概述

基于 Tauri 2 + Angular + Fluent UI 开发的 MiniDb 数据库管理客户端，替代现有 Avalonia 客户端。  
项目目录：`src/MiniDb.Client`

### 技术栈
| 层次 | 技术 |
|------|------|
| 桌面框架 | Tauri 2 |
| 后端语言 | Rust |
| 前端框架 | Angular 19 (最新版) |
| UI 组件库 | Fluent UI Web Components for Angular |
| 样式 | CSS 变量 + Fluent Design Token |
| 平台效果 | Windows Mica / Acrylic（通过 Tauri window-vibrancy 插件）|
| 国际化 | @ngx-translate/core（支持中/英文）|
| 主题 | Fluent UI Light/Dark + 自定义 CSS 变量 |
| 构建工具 | Vite (前端) + Cargo (Rust) |

---

## 二、功能清单

### 2.1 连接管理
- 添加新连接（名称 + 文件路径）
- 编辑现有连接
- 删除连接
- 连接列表（持久化到 `%APPDATA%\minidb-client\connections.json`）
- 最近使用记录

### 2.2 数据库操作
- 连接/断开数据库文件（.mds）
- 刷新表列表
- 显示连接状态（已连接/断开/错误）

### 2.3 表数据展示
- 左侧面板：表名列表
- 右侧面板：选中表的数据展示
  - 动态列（根据字段元数据生成）
  - 分页（每页 50 条）
  - 单字段筛选（contains/equals/gt/lt/range 等操作符）
  - 总记录数 / 已筛选数 / 软删除记录标记

### 2.4 多语言支持
- 切换语言（中文 / 英文）
- 使用 JSON 翻译文件
- 语言偏好持久化

### 2.5 主题支持
- Light / Dark 主题切换
- Windows Mica 效果（可选，Windows 11）
- 毛玻璃效果（Acrylic）
- 主题偏好持久化

---

## 三、项目结构

```
src/MiniDb.Client/
├── src-tauri/                   # Rust 后端
│   ├── src/
│   │   ├── main.rs              # 程序入口
│   │   ├── lib.rs               # Tauri app + 命令注册
│   │   ├── mds/                 # MDS 文件解析模块
│   │   │   ├── mod.rs
│   │   │   ├── reader.rs        # 二进制文件读取
│   │   │   ├── types.rs         # 类型定义（TableMeta, FieldMeta, Record）
│   │   │   └── decoder.rs       # 字段值解码
│   │   ├── commands/            # Tauri 命令
│   │   │   ├── mod.rs
│   │   │   ├── file_commands.rs # 文件操作命令
│   │   │   └── settings_commands.rs # 设置命令
│   │   └── state.rs             # AppState（连接管理）
│   ├── Cargo.toml
│   └── tauri.conf.json
│
└── src/                         # Angular 前端
    ├── app/
    │   ├── core/                # 核心模块
    │   │   ├── services/
    │   │   │   ├── tauri.service.ts     # Tauri invoke 封装
    │   │   │   ├── connection.service.ts # 连接管理
    │   │   │   ├── settings.service.ts   # 设置服务
    │   │   │   └── theme.service.ts      # 主题切换
    │   │   └── models/
    │   │       ├── connection.model.ts
    │   │       ├── table-data.model.ts
    │   │       └── settings.model.ts
    │   ├── features/
    │   │   ├── connections/     # 连接管理页面
    │   │   │   ├── connection-list/
    │   │   │   ├── connection-dialog/
    │   │   │   └── connections.module.ts
    │   │   └── database/        # 数据库浏览页面
    │   │       ├── table-list/
    │   │       ├── table-viewer/
    │   │       ├── filter-panel/
    │   │       └── database.module.ts
    │   ├── shared/              # 共享组件
    │   │   ├── layout/
    │   │   └── components/
    │   ├── i18n/                # 翻译文件
    │   │   ├── en.json
    │   │   └── zh.json
    │   ├── app.component.ts
    │   ├── app.routes.ts
    │   └── app.config.ts
    ├── styles/
    │   ├── fluent-theme.css
    │   ├── mica.css
    │   └── variables.css
    └── index.html
```

---

## 四、MDS 文件格式（Rust解析依据）

```
文件头（256字节）
  [0..3]   Magic "MDB1"
  [4..5]   版本 (int16 = 1)
  [6..7]   TableCount (int16)
  [8..15]  GlobalWriteVersion (int64)
  [16..255] Reserved

表元数据（128字节 × TableCount）
  [0..63]   TableName (UTF-8, 空填充)
  [64..67]  RecordCount (int32)
  [68..71]  RecordSize (int32)
  [72..79]  DataStartOffset (int64)
  [80..83]  ReservedRecordCount (int32)
  [84..87]  TableIndex (int32)
  [88..95]  ExtentDirectoryOffset (int64)
  [96..99]  ExtentCount (int32)
  [100..107] FieldMetadataOffset (int64)
  [108..111] FieldCount (int32)
  [112..127] Reserved

字段元数据（80字节 × FieldCount）
  [0..63]  FieldName (UTF-8)
  [64..67] FieldTypeCode (int32): Unknown=0 Int32=1 Boolean=2 Decimal=3 DateTime=4 String=5 Enum=6
  [68..71] Size (int32)
  [72]     IsNullable (byte 0/1)
  [73..79] Reserved

记录格式（RecordSize字节）
  [0]      IsDeleted (byte)
  [1..4]   Id (int32 LE)
  [5..]    字段值（按字母序，nullable字段前置1字节null标记）
```

---

## 五、Tauri 命令 API

### 文件相关命令
```typescript
// 获取表名列表
invoke<string[]>('get_table_names', { filePath: string })

// 获取表的字段元数据
invoke<FieldMeta[]>('get_field_metadata', { filePath: string, tableName: string })

// 加载表数据（分页）
invoke<TableDataResponse>('load_table_data', {
  filePath: string,
  tableName: string,
  page: number,
  pageSize: number,
  filter?: FilterRequest
})

// 选择文件（打开文件对话框）
invoke<string | null>('select_db_file')
```

### 设置相关命令
```typescript
// 获取所有连接
invoke<DatabaseConnection[]>('get_connections')

// 保存连接
invoke<void>('save_connection', { connection: DatabaseConnection })

// 删除连接
invoke<void>('delete_connection', { id: string })

// 获取应用设置
invoke<AppSettings>('get_settings')

// 保存应用设置
invoke<void>('save_settings', { settings: AppSettings })
```

---

## 六、开发任务分解

### 阶段一：项目初始化
- [x] 检查开发环境（Rust / Node.js）
- [ ] 安装缺失工具（Rust + Tauri CLI + Angular CLI）
- [ ] 创建 Tauri 2 + Angular 项目
- [ ] 配置 Fluent UI Web Components
- [ ] 配置 Windows Mica 插件

### 阶段二：Rust 后端
- [ ] 实现 MDS 文件格式解析器
- [ ] 实现字段值解码（Int32/Bool/Decimal/DateTime/String/Enum）
- [ ] 实现 Tauri 命令（文件 + 设置）
- [ ] 连接状态持久化（serde_json）

### 阶段三：Angular 前端
- [ ] 核心服务（Tauri 调用、连接、设置、主题）
- [ ] 连接管理 UI（列表 + 对话框）
- [ ] 数据库浏览 UI（表列表 + 数据表格）
- [ ] 筛选面板
- [ ] 分页组件

### 阶段四：功能完善
- [ ] i18n（中/英文 JSON 翻译）
- [ ] 主题切换（Light/Dark）
- [ ] Windows Mica/Acrylic 效果
- [ ] 构建与测试

---

## 七、关键设计决策

1. **Rust 解析器无依赖**：直接使用 `std::fs` + `byteorder` crate 读取二进制文件，避免引入 C# 依赖。
2. **前端状态管理**：使用 Angular Signals（Angular 19 内置），轻量高效。
3. **动态列**：使用 Angular CDK Table 的动态列定义，根据字段元数据运行时生成列。
4. **Fluent UI**：使用 `@fluentui/web-components` + Angular 集成，保持 Windows 风格。
5. **持久化**：连接和设置存储在 Tauri 应用数据目录（`app_data_dir()`）。
6. **错误处理**：所有 Tauri 命令返回 `Result<T, String>`，前端统一处理错误状态。
