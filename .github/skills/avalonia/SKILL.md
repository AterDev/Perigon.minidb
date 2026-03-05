---
name: avalonia
description: 'Avalonia UI AI workflow for project setup, migration prompts, MCP-assisted debugging, and safe code generation. Use for Avalonia XAML/MVVM tasks with Copilot/AI assistants.'
argument-hint: 'Provide your Avalonia goal (new app, migration, UI refactor, packaging, or debugging)'
user-invocable: true
---

# Avalonia AI Playbook

## When to use
- 新建 Avalonia 项目并建立 AI 协作规范
- 从 MAUI/WPF/WinForms 迁移到 Avalonia
- 生成复杂 XAML + MVVM 代码时降低幻觉概率
- 借助 MCP 做 UI 树诊断、样式定位、打包自动化

## Procedure
1. **先规划后编码**
   - 明确目标（新建/迁移/重构/打包/调试）
   - 列出实施步骤（5-8 步），每步聚焦一个小目标
   - 代码全部完成后，通过构建验证代码正确性

2. **先复用现有模式，后扩展新模式**
	- 优先沿用项目已有命名、目录结构、DI 注册方式。
	- 若需要新模式（如 `ReactiveUI`、`CommunityToolkit.Mvvm`），先说明引入理由与影响范围。

3. **遇到不确定 API：先查证再生成**
	- 优先官方文档 + 当前仓库已有示例。
	- 避免杜撰 Avalonia 控件属性、附加属性或样式键。

## Best Practices（最佳实践）

### 1) 架构与分层
- 保持 MVVM 纯度：业务逻辑放 ViewModel/Service，不放 code-behind。
- ViewModel 不直接依赖具体 UI 控件类型，避免 UI 反向侵入。
- 将状态与副作用分离：状态可测试，副作用（弹窗/文件系统/网络）通过服务抽象。

### 2) XAML 与绑定
- 优先使用强类型/编译期友好的绑定写法（可用时启用 compiled bindings）。
- 控件模板和 DataTemplate 复用到 `Styles`/资源字典，减少重复 XAML。
- 对列表项与复杂布局优先定义清晰的数据模型，而不是在 XAML 内堆复杂表达式。

### 3) 命令与异步
- 用户动作统一走 `ICommand`（或异步命令）并处理禁用态/忙碌态。
- 异步命令必须处理异常与取消（`CancellationToken`），避免吞异常。
- 避免在 UI 线程执行重 I/O 或长计算；必要时下沉到后台服务。

### 4) 样式与主题
- 统一主题变量与资源命名，避免页面级“魔法色值”散落。
- 同步覆盖亮色/暗色与高对比度场景。
- 样式覆盖优先小范围、可追踪，避免全局样式误伤。

### 5) 可维护性与可测试性
- ViewModel 层新增行为时同步补充测试（至少 happy path + error path）。
- 对关键交互输出可回归清单：空状态、加载态、错误态、长文本、窄窗口。
- 大改优先拆成多个小 PR/小提交，保证每步可编译可回滚。

## Coding Conventions（常见规范）
- 文件组织建议：
  - `Views/`：`.axaml` + 极薄 code-behind
  - `ViewModels/`：状态与命令
  - `Services/`：I/O、平台能力、外部依赖
  - `Resources/`：主题、样式、字典、本地化
- 命名建议：
  - View: `XxxView.axaml`
  - ViewModel: `XxxViewModel.cs`
  - Command: `SaveCommand` / `LoadCommand`
- 绑定建议：
  - 避免深层级链式绑定（可读性差且难排错）
  - 避免在 Binding Converter 里塞业务规则

## Common Mistakes to Avoid（常见错误与规避）

### 错误 1：把业务逻辑写进 code-behind
- 症状：页面事件处理函数越来越重，难测且难复用。
- 规避：事件只做转发，逻辑迁移至 ViewModel 命令。

### 错误 2：AI 生成了不存在的 Avalonia API
- 症状：编译报“属性/类型不存在”。
- 规避：先查官方文档或项目现有写法，再落地代码；对新 API 做最小编译验证。

### 错误 3：一次性重写整页导致回归不可控
- 症状：UI 看似完成但行为缺失、样式错乱。
- 规避：拆批次迁移（布局 → 绑定 → 命令 → 样式），每批构建+手测。

### 错误 4：异步命令未处理并发与取消
- 症状：重复点击导致重入、状态错乱。
- 规避：加入 Busy 状态与并发保护；提供取消路径。

### 错误 5：资源字典混乱、样式命名冲突
- 症状：跨页面样式互相污染，难以定位来源。
- 规避：建立资源命名前缀与分层策略（全局/模块/页面）。

### 错误 6：只在一个主题下验收
- 症状：暗色主题或高 DPI 下 UI 崩坏。
- 规避：至少验证亮/暗主题、不同缩放、不同窗口宽度。

## Debug Checklist（调试检查单）
- 绑定异常：先看输出日志与 DataContext 链路，再看属性名与可见性。
- 样式异常：检查选择器命中、资源覆盖顺序、主题字典加载顺序。
- 交互异常：检查命令 CanExecute、异步重入、导航/状态同步。
- 性能异常：检查大列表虚拟化、频繁 PropertyChanged、重绘热点。
