# Ollama + Qwen 本地语义模型配置说明

本文档用于配置本项目的本地语义理解层。目标是让 OCR 结果经过本地 Qwen 模型判断字段含义，例如日期、收据号、客户名、金额、合计等。

本项目当前决定：

- 不使用 DeepSeek 作为默认语义层。
- 不使用 regex 作为字段语义判断方案。
- 使用 Ollama + Qwen 本地模型。
- 后续 Python worker 通过本机 `localhost` 调用 Ollama。

## 1. 安装 Ollama

打开 Ollama Windows 官方文档：

```text
https://docs.ollama.com/windows
```

下载并安装 Ollama for Windows。

安装后，Ollama 通常会在后台运行，并提供本地 API：

```text
http://localhost:11434
```

## 2. 验证 Ollama 是否可用

打开新的 PowerShell，运行：

```powershell
ollama --version
```

如果能看到版本号，说明命令行可用。

再检查本地服务：

```powershell
Invoke-RestMethod http://localhost:11434/api/tags
```

如果返回模型列表或空列表，说明本地 Ollama server 正常。

## 3. 下载推荐模型

推荐先使用：

```powershell
ollama pull qwen2.5:7b-instruct
```

原因：

- 中文理解能力较好。
- 本地运行成本为零。
- 对 OCR 文本字段分类任务足够作为 MVP 起点。

如果电脑性能一般，使用更小模型：

```powershell
ollama pull qwen2.5:3b-instruct
```

如果电脑性能较强，并且后续需要更高准确度，可测试：

```powershell
ollama pull qwen2.5:14b-instruct
```

当前默认建议：

```text
qwen2.5:7b-instruct
```

## 4. 手动测试模型

运行：

```powershell
ollama run qwen2.5:7b-instruct
```

输入：

```text
请只输出 JSON：从“收款收据 NO00000003 2017-04-12 合计 16.08”中提取日期、单号和总金额。
```

期望模型能输出类似：

```json
{
  "date": "2017-04-12",
  "receipt_no": "NO00000003",
  "total": "16.08"
}
```

输入 `/bye` 退出交互。

## 5. 使用本地 API 测试

PowerShell 测试：

```powershell
$body = @{
  model = "qwen2.5:7b-instruct"
  stream = $false
  prompt = "请只输出 JSON：从“收款收据 NO00000003 2017-04-12 合计 16.08”中提取日期、单号和总金额。"
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "http://localhost:11434/api/generate" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

如果成功，返回对象中会包含：

```text
response
```

`response` 字段里是模型输出文本。

## 6. 后续项目集成方式

后续 Python worker 会新增语义层：

```text
ocr_items -> Ollama Qwen -> semantic_fields
```

worker 输出会新增：

```json
{
  "semantic_fields": {
    "date": {
      "value": "2017-04-12",
      "confidence": 0.8,
      "ocr_refs": [3],
      "reason": "模型判断该文本为日期字段"
    }
  }
}
```

重要约束：

- Qwen 只能基于 OCR 输出判断字段含义。
- 不允许凭空生成 OCR 中不存在的值。
- 每个字段必须尽量返回 `ocr_refs`，指向原始 `ocr_items` 的索引。
- 如果模型不确定，应返回低 confidence 或空字段。
- UI 后续根据 `semantic_fields` 显示可审核字段，而不是显示原始 OCR dump。

## 7. 配置项建议

后续项目可以使用这些配置：

```text
OLLAMA_BASE_URL=http://localhost:11434
OLLAMA_MODEL=qwen2.5:7b-instruct
```

PowerShell 临时配置：

```powershell
$env:OLLAMA_BASE_URL="http://localhost:11434"
$env:OLLAMA_MODEL="qwen2.5:7b-instruct"
```

如果未配置，程序默认使用：

```text
http://localhost:11434
qwen2.5:7b-instruct
```

## 8. 当前不做的事

当前阶段不做：

- DeepSeek API 调用。
- 按 token 计费模型。
- regex 字段判断。
- Excel 写入。
- UI 字段审核面板。

下一步只做：

```text
Python worker 调用 Ollama，把 OCR items 转成 semantic_fields。
```
