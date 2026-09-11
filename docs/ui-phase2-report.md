# Accounting Assistant 界面升级：阶段 2 报告

日期：2026-09-11

## 阶段结论

阶段 2 已完成技术实施，主窗口已从默认 WPF 三栏界面升级为结构清晰的票据处理工作区。本阶段保留全部控件名称、事件、Binding、快捷键和业务逻辑，只重构 XAML 视觉结构，并补充少量可复用样式。

## 已完成内容

### 顶部命令区

- 将操作整理为 `Add receipts` 与 `Process` 两个分组。
- `Select Images` 作为进入工作流的首要入口使用蓝色 Primary 样式。
- Analyze 与 Analyze All 延续 Pending 阶段色。
- Batch Fill 与 Export Excel 保持中性次级层级。
- Current status 独立位于右上方，与 Review 面板对齐。
- Select Images 与 Select Folder 调整为 116px 宽度，改善文字留白。

### Receipt queue

- 以现代面板替代 GroupBox。
- 标题、说明与 Sort 操作合并到面板头部。
- 列表项强化文件名层级，状态点和状态文字保持清晰。
- 在列表内容末尾增加 `Add receipt` 按钮：空队列时位于列表顶部，有票据时自然跟随到最后一项之后。
- Receipt queue 支持拖入 JPG、JPEG、PNG 文件，也支持拖入包含这些图片的文件夹。
- 拖入可接受内容时显示蓝色覆盖提示；不支持的内容显示禁止拖放效果。
- 新增空队列状态，引导用户选择文件或直接拖入。

### Receipt preview

- 新增独立工作区标题和 OCR 覆盖说明。
- 预览表面统一为冷灰背景，与白色面板形成轻量层级。
- 未选择票据时显示明确空状态。
- 保留 `ReceiptScrollViewer`、`ReceiptImageHost`、`ReceiptImage` 和 `OcrOverlayCanvas` 原结构与事件。

### Review

- 以现代面板替代右侧 GroupBox。
- OCR 与 Fields 页签保留原切换逻辑，改善间距、字体和数据对齐。
- OCR 索引与置信度改用等宽字体，提高数字扫描效率。
- Fields 输入列改为自适应宽度，警告状态使用统一 Danger token。
- OCR 和 Fields 均增加空状态说明。
- Debug dump 保留在审核面板底部。

### 底部审核动作区

- 将快捷键说明和审核动作放入独立操作栏。
- Next Receipt 降为 Ghost 层级。
- Confirm OCR 与 Approve Fields 延续 OCR Review 橙色和 Field Review 绿色。
- 原启用逻辑确保当前阶段只有对应动作获得完整视觉权重。

### 可复用样式

在 `Styles/ControlStyles.xaml` 新增：

- `WorkspacePanelStyle`
- `SectionTitleTextStyle`
- `SectionDescriptionTextStyle`
- `CommandGroupLabelStyle`
- `CompactGhostButtonStyle`

主窗口不再包含直接写入的常用十六进制颜色值。

## 验证结果

- `dotnet build app/AccountingAssistant.App/AccountingAssistant.App.csproj`：成功。
- 编译结果：0 个警告，0 个错误。
- 已通过指定 `.venv` 入口执行 `dotnet run`。
- 应用成功启动并持续运行，无资源字典或 XAML 运行时错误。
- 所有原控件名称和事件处理器均由 XAML 编译器成功解析。
- 980px 最小窗口宽度静态核算通过：顶部按钮、三栏工作区和底部动作区均保留可用宽度。
- 拖放上传使用最小范围的 `MainWindow.xaml.cs` UI 事件；文件选择、文件夹选择和拖放最终均复用现有 `LoadImages`，未改变队列或 OCR 业务规则。

## 待确认

当前运行窗口用于阶段视觉验收。重点检查：

1. 顶部命令分组是否比原双排按钮更容易扫描。
2. 248px 队列、弹性预览区与 360px Review 面板的比例是否舒适。
3. 空状态信息是否清楚但不过度抢眼。
4. 底部当前阶段操作是否能快速识别。

确认后进入阶段 3，统一 Batch Fill、OCR 编辑、Excel 导出审核和建议管理四个业务弹窗。
