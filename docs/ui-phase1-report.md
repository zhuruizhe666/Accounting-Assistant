# Accounting Assistant 界面升级：阶段 1 报告

日期：2026-09-11

## 阶段结论

阶段 1 已完成并通过视觉方向确认。应用现在具备统一、可复用的 WPF 视觉基础，并且没有修改业务流程、OCR 调用或数据模型。

本阶段采用已批准的方向：冷灰中性色、单一克制蓝色强调、Segoe UI Variable 字体、小圆角控件和中等圆角面板基准。没有引入第三方 UI 框架。

## 已完成内容

### 设计 token

新增 `app/AccountingAssistant.App/Styles/DesignTokens.xaml`，集中定义：

- 字体家族与五级字号；
- 应用背景、表面、边框和三级文字颜色；
- 蓝色强调及 Hover、Pressed、Focus 状态；
- Success、Warning、Danger 语义颜色；
- Disabled 状态颜色；
- 4、8、12、16、24 间距体系；
- 小、中、大圆角；
- 常规与紧凑控件高度。

### 基础控件样式

新增 `app/AccountingAssistant.App/Styles/ControlStyles.xaml`，完成：

- 全局 Window 字体、前景色和背景色；
- Button 的基础样式和 Primary、Secondary、Ghost、Danger 四种变体；
- TextBox 的 Hover、Focus、ReadOnly、Disabled 状态；
- ComboBox 的尺寸、字体、颜色和焦点基础；
- ListBox 与 ListBoxItem 的悬停、选中、禁用状态；
- TabControl 与 TabItem 的轻量标签样式和蓝色选中指示条；
- 统一的可见键盘焦点框。

当前未显式指定样式的 Button 默认使用 Secondary 样式。Primary、Ghost 和 Danger 已作为资源建立，具体业务按钮的层级映射留到阶段 2 随主窗口信息架构一起完成。

### 应用级接入

更新 `app/AccountingAssistant.App/App.xaml`，按顺序合并设计 token 和控件样式资源字典，使主窗口及现有弹窗自动继承视觉基础。

## 验证结果

- `dotnet build app/AccountingAssistant.App/AccountingAssistant.App.csproj`：成功。
- 编译结果：0 个警告，0 个错误。
- 已按指定入口激活 `.venv` 后执行 `dotnet run`。
- 应用成功启动并持续运行，没有资源字典、XAML 解析或 OCR 环境启动错误。
- 项目当前没有独立测试工程，因此本阶段没有可执行的单元测试套件。

## 视觉校准结果

用户已确认当前控件视觉、密度与圆角表现良好，保持现有参数：

- 32 px 基础控件高度；
- 4 px 控件圆角；
- 当前冷灰与克制蓝配色。

同时根据实际使用反馈，将当前状态文本放置在顶部工具栏最右端，与下方 Review Panel 的右侧栏对齐。新位置使用紧凑状态条承载 `Selected download.png`、`OCR worker warming up` 等动态状态，填充原本空置的右上区域，同时不占用 Review Panel 的内容高度。调整保留原有 `StatusTextBlock` 及全部更新逻辑，只改变 XAML 布局和视觉承载方式。

为强化工作流阶段识别，新增三组轻量阶段按钮样式：Analyze 与 Analyze All 对应 Pending 灰色，Confirm OCR 对应 OCR Review 橙色，Approve Fields 对应 Field Review 绿色。按钮采用原状态色边框、同色系浅背景和深色文字；禁用时保留色相并降低透明度，使阶段归属始终可见，同时仍能明确区分是否可操作。

## 下一阶段建议

阶段 2 进入主窗口视觉层级重构：整理顶部命令区、队列区、票据工作区和右侧审核区，随后把关键动作映射到 Primary、Ghost 与 Danger 层级。开始前建议先确认本阶段的整体颜色、控件密度和圆角观感。
