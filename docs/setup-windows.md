# Windows Setup

Install these before Phase 0 verification:

1. Git for Windows.
2. Visual Studio with `.NET desktop development`.
3. .NET 10 SDK.
4. Python 3.11 or 3.12.

After installing dependencies, open PowerShell in the repository root and run:

```powershell
.\scripts\check-environment.ps1
```

Expected minimum results:

- `dotnet --list-sdks` shows one installed SDK.
- `python --version` or `py --version` shows Python 3.11 or 3.12.
- `git --version` succeeds.

Then verify the C# app:

```powershell
dotnet build .\AccountingAssistant.sln
dotnet run --project .\app\AccountingAssistant.App\AccountingAssistant.App.csproj
```

Verify the Python worker:

```powershell
python .\worker\accounting_worker\main.py analyze .\samples\sample.jpg --mock
```

If `python` is not found but `py` works, either add Python to PATH or configure the C# app to use `py` in a later config step.
