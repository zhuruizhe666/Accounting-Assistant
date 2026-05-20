$ErrorActionPreference = "Continue"

Write-Host "Accounting Assistant environment check"
Write-Host ""

Write-Host ".NET SDKs:"
dotnet --list-sdks
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet command failed or is not installed."
}

Write-Host ""
Write-Host ".NET info:"
dotnet --info

Write-Host ""
Write-Host "Python:"
python --version
if ($LASTEXITCODE -ne 0) {
    py --version
}

Write-Host ""
Write-Host "Git:"
git --version

Write-Host ""
Write-Host "Repository files:"
git status --short
