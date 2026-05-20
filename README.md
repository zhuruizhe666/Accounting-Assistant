# Accounting Assistant

Accounting Assistant is a Windows-first receipt review tool. The first milestone is a thin vertical slice:

1. Select receipt images.
2. Send one image path to a Python worker.
3. Receive normalized JSON.
4. Display fields and review status in a C# WPF app.

The current scaffold intentionally uses mock worker data. Real OCR, candidate extraction, confidence scoring, Excel export, and LLM ranking are added in later phases.

## Repository Layout

```text
Accounting-Assistant/
  app/
    AccountingAssistant.App/       # C# WPF desktop UI
  worker/
    accounting_worker/             # Python logic layer
    tests/                         # Python contract tests
  shared/
    schemas/                       # JSON examples and contracts
  docs/                            # Development flow and decisions
  samples/                         # Local receipt images, ignored by git
  exports/                         # Local Excel exports, ignored by git
  config/                          # Example app config
```

## Environment Status

At scaffold time, this machine did not expose `python` on PATH and `dotnet --list-sdks` did not report installed SDKs. Before running the app, install:

- .NET 10 SDK or a compatible Windows desktop SDK.
- Visual Studio with `.NET desktop development`.
- Python 3.11 or 3.12.

## First Run Target

After dependencies are installed:

```powershell
dotnet build .\AccountingAssistant.sln
dotnet run --project .\app\AccountingAssistant.App\AccountingAssistant.App.csproj
```

Python worker mock:

```powershell
python .\worker\accounting_worker\main.py analyze .\samples\sample.jpg --mock
```
