# Accounting Assistant 界面升级：阶段 3 进度

日期：2026-09-11

## 原生提示窗口审计

全项目共找到两处 `System.Windows.MessageBox.Show`：

1. `MainWindow.xaml.cs` 中，已完成 OCR confirmation 的票据再次点击 Confirm OCR 时，询问是否重置。
2. `MainWindow.xaml.cs` 中，Excel 导出目标未通过 Perfect Format 校验时，显示格式说明。

第一处是会改变票据审核进度的决策确认；第二处是单按钮警告说明。其余 `ShowDialog()` 分别属于文件／文件夹选择器或已有业务窗口，不是系统确认框。

## 已完成：统一确认弹窗

新增 `ConfirmationDialog.xaml` 与 `ConfirmationDialog.xaml.cs`：

- 使用应用设计 token、圆角表面和统一字体；
- 使用产品内警告图形，不再显示系统黄色三角图标；
- 支持双按钮确认和单按钮说明两种模式；
- 支持主操作、取消、右上关闭和 Esc；
- 重置 OCR 使用明确的 `Reset OCR` 与 `Keep current`；
- 重置动作使用 Danger 层级；
- Excel 格式警告使用 `Got it` 单按钮；
- Excel 格式说明正文为只读可选择文本，支持鼠标选择、右键复制和 `Ctrl+C`；
- Excel 格式警告提供 `Copy instructions`，可一次复制完整 Perfect Format 说明，方便粘贴给其他 AI 进行 normalize；
- 复制成功后按钮显示 `Copied`，剪贴板不可用时提示手动选择并按 `Ctrl+C`；
- 长格式说明可在弹窗内滚动。

替换完成后，全项目不再存在 `MessageBox.Show`、`MessageBoxButton`、`MessageBoxImage` 或 `MessageBoxResult` 引用。

## 已完成：Batch Fill 顶部控件

- `Selected` 与 `All analyzed` 改为两个并列选择卡，选中状态使用蓝色背景、边框和单选指示。
- 每个范围选项增加一行简短说明，减少含义猜测。
- `Overwrite existing values` 改为完整设置行和开关样式。
- 增加“替换已有数据”的说明，降低误操作风险。
- Apply 使用 Primary 样式，Cancel 使用 Ghost 样式。
- Manage suggestions 降为紧凑辅助操作。
- 保留原 `SelectedReceiptsRadioButton`、`AllReviewableRadioButton` 和 `OverwriteCheckBox`，业务属性与应用逻辑未改变。

## 验证

- `dotnet build app/AccountingAssistant.App/AccountingAssistant.App.csproj`：成功。
- 编译结果：0 个警告，0 个错误。
- 已通过 `.venv` 启动，应用持续运行，无 XAML 或资源加载错误。

## 已完成：Excel Export Review

- 将导出窗口重构为导出前决策界面，明确说明只有勾选票据会写入工作簿。
- 目标 Excel 路径改为可选择文本，并增加 `Copy path` 快捷复制。
- 增加 Approved、New Documents、Duplicates 与 Selected 四项实时统计。
- 重复单号使用琥珀色状态徽标，并在存在重复项时显示风险说明。
- 重复项继续默认不勾选，未改变原有防重复逻辑。
- 表格复选框、表头、行悬停与选中状态统一到应用设计系统。
- 底部提供 New only、Select all 与 Clear 三种明确选择方式。
- 主按钮实时显示 `Export N selected`；选择数为 0 时禁用，避免空导出。
- 保留原有导出结果与队列清理逻辑：只追加勾选项，且只在成功导出后移除对应票据。

## 阶段 3 范围结论

- 阶段 3 按用户确认后的范围完成。
- Edit OCR Text 与 Suggestion Manager 暂不处理。

## Excel Export Review 前的流程调整

- Receipt Queue 标题区增加红色垃圾桶按钮，可移除一张或多张选中票据。
- 移除只影响当前队列，不删除磁盘上的原始图片。
- 移除后自动选择原位置附近的下一张票据，并正确清理属性事件订阅。
- 正在 OCR 或 Fields 解析中的票据暂时禁止移除，避免后台结果回写到已移除项目。
- Approved 票据的 `Approve Fields` 自动切换为琥珀色 `Roll Back`。
- Roll Back 将状态恢复为 Field Review，保留现有字段内容，恢复 Fields 编辑和 Confirm OCR。
- Approved 状态下 Confirm OCR 与 Fields 编辑保持锁定，必须先 Roll Back。
