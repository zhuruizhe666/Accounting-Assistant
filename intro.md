# Accounting Assistant Intro

## 项目构成

这是一个 Windows-first 的本地票据审核工具。

- `app/AccountingAssistant.App/`：C# WPF 桌面 UI。
- `worker/accounting_worker/`：Python worker，负责 OCR 和字段语义解析。
- `data/project_profile.json`：项目级常用值库，用于字段填写下拉提示。
- `docs/`：安装、开发流程和数据契约文档。
- `samples/`：本地测试图片目录。

当前主流程是两段式：

1. PaddleOCR 先把图片转成文字和位置框。
2. 人工确认 OCR 后，再把修正后的文字交给 Ollama/Qwen 解析字段。

## 基本操作

1. 打开程序后会自动初始化 OCR。初始化期间 `Analyze` 和 `Analyze All` 会禁用。
2. `Select Images`：选择一个或多个图片文件，支持 `.jpg/.jpeg/.png`。
3. `Select Folder`：选择文件夹，将其中支持的图片追加进队列。
4. `Analyze`：分析当前选中的 Pending 图片，只执行 OCR。
5. `Analyze All`：顺序分析所有 Pending 图片。
6. `Batch Fill`：批量填写已完成 Analyze 的票据；空输入不修改，默认不覆盖已有值。弹窗里的 `管理建议` 可以维护常用下拉建议；在新增建议输入框里按 Enter 会直接新增。
7. `Export Excel`：把 Approved 票据追加到 `.xlsx`。如果目标文件不存在，会创建 Perfect Format；如果目标文件存在但第一行表头不是 Perfect Format，会拒绝写入并弹出 normalize 说明。导出前会打开审核窗口，逐条显示将要 append 的 receipt，并标出目标 Excel 中已有相同 `单号` 的项目；用户可以逐条决定是否 append。成功 append 的 receipt 会从当前 queue 移除。
8. `Sort Queue`：手动按处理阶段重排 Receipt Queue，越早的阶段越靠上，越接近完成越靠下。
9. 在 `OCR` 页检查识别文本；双击某一行可以修正 OCR 文字。
10. 点击图片上的 OCR 框会选中右侧对应文字；点击右侧文字也会高亮图片上的框。
11. `Confirm OCR` 或 `Shift+Enter`：确认 OCR 审核，随后在后台调用 Ollama/Qwen 解析字段；界面不会等待在当前票据上。
12. 在 `Fields` 页审核字段。字段可以为空，也可以手动输入或从下拉建议选择。
13. 如果字段值与它的 OCR/LLM 原始判断明显冲突，字段输入框会显示红框；多次 Batch Fill 后仍会保留并提示原始判断值。
14. `Approve Fields` 或 `Ctrl+Enter`：确认字段审核完成。
15. `Next Receipt` 或 `Shift+Down`：切换到下一张票据。
16. `Shift+O`：切到 Review Panel 的 OCR 页；`Shift+F`：切到 Fields 页。

按钮可用性由当前选中票据的状态决定：Pending 才能 `Analyze`，OCR Review 可以首次 `Confirm OCR`，Field Review 或 Approved 再点 `Confirm OCR` 会提示是否重置并重新解析字段，Field Review 才能 `Approve Fields`。只要队列中存在已完成 Analyze 的票据，就可以使用 `Batch Fill`；只要存在 Approved 票据，就可以 `Export Excel`。`Confirm OCR` 前批量填写的字段会传给 Ollama/Qwen 作为上下文，但 Qwen 仍会基于 OCR 独立判断；如果两者冲突，字段会红框提示。`Confirm OCR` 和 `Approve Fields` 两次触发之间有 3 秒锁定，避免误操作。

队列不会在状态变化时自动重排；需要时点击 `Sort Queue`。手动重排会保留同阶段内部顺序。

## 队列状态

- `Pending`：等待 OCR。
- `Processing`：正在处理。
- `OCR Review`：OCR 完成，等待人工确认文字。
- `Parsing Fields`：OCR 已确认，正在后台解析字段。
- `Field Review`：字段解析完成，等待人工审核字段。
- `Approved`：最终审核完成。
- `Error`：处理失败。

## 字段 Schema

第一版字段保持精简，允许空值：

- `document_type`：票据类型
- `document_number`：单据号
- `issue_date`：日期
- `counterparty_name`：交易方
- `total_amount`：总金额
- `tax_amount`：税额
- `expense_category`：费用类别
- `project_name`：项目名
- `department`：部门
- `handler`：经办人
- `summary`：摘要
- `notes`：备注

## Excel Perfect Format

Excel 导出只支持 Perfect Format。目标文件不存在时会自动创建；目标文件存在时，第一行表头必须完全等于以下顺序：

```text
日期
单号
票据类型
交易方
总金额
税额
费用类别
项目名
部门
经办人
摘要
备注
图片路径
OCR状态
字段状态
导出时间
```

如果表头不匹配，程序会拒绝写入。已有 Excel 的列迁移、删减、合并、语义解释都不由本软件自动处理。

重复导出保护使用 `单号` 列作为业务 key：如果目标 Excel 已存在相同单号，导出审核窗口会把该 receipt 标为 `重复单号`，并默认不勾选。用户仍然可以手动勾选并 append。空单号不会触发重复判断。

## `data/project_profile.json`

这个文件保存当前项目的常用值，主要用于 `Fields` 页的 ComboBox 下拉建议。

```json
{
  "document_types": [],
  "counterparties": [],
  "expense_categories": [],
  "project_names": [],
  "departments": [],
  "handlers": []
}
```

可以在 `Batch Fill` 弹窗中点击 `管理建议` 来新增或删除常用值；保存后会写回 `data/project_profile.json`。
