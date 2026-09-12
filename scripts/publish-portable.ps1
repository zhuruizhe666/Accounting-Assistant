$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ProjectPath = Join-Path $RepoRoot "app\AccountingAssistant.App\AccountingAssistant.App.csproj"
$PublishRoot = Join-Path $RepoRoot "release"
$PublishDir = Join-Path $PublishRoot "AccountingAssistant"

Write-Host "Publishing Accounting Assistant portable package..."
Write-Host "Output: $PublishDir"

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

dotnet publish $ProjectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $PublishDir

Write-Host ""
Write-Host "Portable package created."
Write-Host "Next step: zip this folder and send it to the user:"
Write-Host $PublishDir
