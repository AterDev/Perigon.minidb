---
description: "Plan and implement Avalonia UI tasks with strict MVVM, migration safety, and validation checklist"
name: "Avalonia Project Task"
argument-hint: "Describe feature/migration goal + target view + constraints"
agent: "Avalonia Architect"
---
你是 Avalonia 工程顾问。请基于当前仓库完成以下任务：

`{{task}}`

必须遵循：
1. 先输出实施计划（5-8 步），再改代码。
2. 严格使用 Avalonia + MVVM 约束，避免把业务逻辑塞进 code-behind。
3. 不确定 API 时，先查项目现有实现和官方文档再下结论。
4. 输出内容需包含：
   - 变更文件清单
   - 关键绑定/样式说明
   - 验证步骤（构建 + 关键交互回归）
   - 风险与后续建议
