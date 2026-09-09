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
6. 在 `OCR` 页检查识别文本；双击某一行可以修正 OCR 文字。
7. 点击图片上的 OCR 框会选中右侧对应文字；点击右侧文字也会高亮图片上的框。
8. `Confirm OCR` 或 `Shift+Enter`：确认 OCR 审核，随后调用 Ollama/Qwen 解析字段。
9. 在 `Fields` 页审核字段。字段可以为空，也可以手动输入或从下拉建议选择。
10. `Approve Fields` 或 `Ctrl+Enter`：确认字段审核完成。
11. `Next Receipt` 或 `Shift+Down`：切换到下一张票据。

`Confirm OCR` 和 `Approve Fields` 两次触发之间有 3 秒锁定，避免误操作。

## 队列状态

- `Pending`：等待 OCR。
- `Processing`：正在处理。
- `OCR Review`：OCR 完成，等待人工确认文字。
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

当前版本只读取这些常用值，不提供 UI 管理页面。可以直接编辑 JSON 文件来增加选项。
