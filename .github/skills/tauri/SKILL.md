---
name: tauri
description: 'Tauri 桌面端开发经验速记：多语言、标题栏/菜单、窗口按钮、状态栏、连接态可视化，以及 Rust 与前端职责边界。'
argument-hint: '描述你的 Tauri 目标（菜单/窗口控件/状态同步/UI状态可视化/前后端边界）'
user-invocable: true
---

# Tauri Desktop 实战速记（MiniDb）

## 适用场景
- Tauri + Angular/React/Vue 的桌面壳层开发
- 需要“原生感”标题栏、窗口按钮、状态栏
- 业务状态（连接/断开/加载/失败）必须在 UI 上有明确反馈

## 累积经验（只写高价值）

### 1) 多语言：优先清理“隐形硬编码”
- 标题栏菜单、弹层按钮、tooltip、空态文案、状态栏文案最容易漏 i18n。
- 规则：**任何用户可见字符串都走翻译键**，包括确认弹窗和错误提示。
- 建议保留 `status.*` 分组，用于状态栏统一消费（如 connecting/connected/disconnected/failed）。

### 1.1) 反复踩坑：i18n 资源“双来源”会导致局部翻译失效
- 症状：语言切换看起来生效，但只有少数菜单/文案更新，其他区域显示 key 或旧文案。
- 典型根因：`public/assets/i18n` 与 `src/assets/i18n` 同时存在，构建后被不同来源覆盖，运行时读到的是“旧词典”。
- 解决方案：
	- 只保留一个词典来源（推荐 `src/assets/i18n`）。
	- 删除另一份目录中的重复 locale 文件，避免阴影覆盖。
	- 在 loader 开启缓存规避（如 `enforceLoading: true`）。
	- Zoneless 场景下语言切换后触发一次显式 rerender（如信号 epoch 递增）。

### 2) Windows 菜单与标题栏：先做架构决策
- 如果要求“菜单和最小化/最大化/关闭在同一行”：
	- 用**自定义标题栏**（`decorations: false`）。
	- 菜单在前端实现（如 Material Menu）。
- 如果要真正原生菜单行为（系统菜单栏语义）：
	- 用 Tauri 原生菜单，但在 Windows 上通常是独立菜单行，不等同单行融合标题栏。

### 3) 窗口按钮不能做摆设：三件套必须同时满足
- 前端调用窗口 API（minimize / toggleMaximize / close）。
- 能力配置放行：
	- `core:window:allow-minimize`
	- `core:window:allow-toggle-maximize`
	- `core:window:allow-close`
	- 自定义拖拽还需 `core:window:allow-start-dragging`
- 出错不能吞：保留日志，便于判断是权限问题还是上下文问题。

### 4) 业务状态必须驱动 UI，不是只改文案
- 连接成功后要同步到全局状态（connectedId/connectedName/isConnected）。
- 未连接时：
	- 表列表不展示业务数据；
	- 刷新/断开按钮禁用；
	- 主区显示“未连接”空态。
- 已连接时：
	- 显示表列表和数据；
	- 连接按钮改“已连接态”（图标/禁用/高亮）。

### 5) 固定底部状态栏：统一承接操作反馈
- 状态栏建议常驻底部，承接 info/success/error。
- 事件驱动（如 `minidb:status`）比组件间硬传值更稳定，适合跨页面反馈。
- 连接态独立胶囊显示（Connected/Disconnected），和操作消息分离，避免信息混杂。

### 6) 行内垂直对齐：桌面感细节
- 工具栏、分页区、标题栏统一 `display:flex; align-items:center`。
- 混合控件（按钮/输入框/select/chip）时，给容器统一对齐策略，避免“视觉抖动”。
- 状态图标尺寸（如 16px）与文字行高统一，避免基线漂移。

## Rust vs 前端：职责边界（精简版）
- **Rust（Tauri）负责**
	- 文件访问、数据库连接/断开、解析与分页读取
	- 系统能力（窗口特效、原生权限、插件能力）
	- 持久化（设置、连接配置）
- **前端负责**
	- 展示状态机（连接态/加载态/错误态/空态）
	- 菜单与交互编排、i18n、视觉反馈
	- 组件间状态同步与事件总线

## 落地检查单（提交前）
- 菜单/tooltip/弹窗/状态栏是否 100% i18n
- 未连接时是否还能误操作“数据相关按钮”
- 断开后是否立即清空可见数据与选择态
- 窗口按钮在实际桌面环境是否真实可用（非浏览器模式）
- 状态栏是否能区分成功/失败，并反映最新业务结果

