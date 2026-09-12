$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$RuntimeRoot = Join-Path $RepoRoot "runtime"
$PythonRuntimeRoot = Join-Path $RuntimeRoot "python"
$VenvPath = Join-Path $PythonRuntimeRoot ".venv"
$PythonExe = Join-Path $VenvPath "Scripts\python.exe"
$RequirementsPath = Join-Path $RepoRoot "requirements.txt"
$ModelName = "qwen2.5:7b-instruct"

function Write-Step($Message) {
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Find-PythonLauncher {
    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) {
        return @{
            Command = "py"
            Arguments = @("-3.12")
        }
    }

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        return @{
            Command = "python"
            Arguments = @()
        }
    }

    return $null
}

function Test-OllamaApi {
    try {
        Invoke-RestMethod "http://localhost:11434/api/tags" -TimeoutSec 5 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

Write-Host "Accounting Assistant first-time setup"
Write-Host "Project folder: $RepoRoot"

Write-Step "Preparing Python OCR environment"
New-Item -ItemType Directory -Force -Path $PythonRuntimeRoot | Out-Null

if (!(Test-Path $PythonExe)) {
    $pythonLauncher = Find-PythonLauncher
    if ($null -eq $pythonLauncher) {
        Write-Host "Python was not found on this computer." -ForegroundColor Red
        Write-Host "Please install Python 3.12 from https://www.python.org/downloads/windows/ and run this setup again."
        exit 1
    }

    Write-Host "Creating virtual environment at $VenvPath"
    & $pythonLauncher.Command @($pythonLauncher.Arguments) -m venv $VenvPath
}
else {
    Write-Host "Virtual environment already exists."
}

Write-Host "Upgrading pip."
& $PythonExe -m pip install --upgrade pip

Write-Host "Installing Python dependencies. This can take a long time on first setup."
& $PythonExe -m pip install -r $RequirementsPath

Write-Step "Checking Ollama"
$ollama = Get-Command ollama -ErrorAction SilentlyContinue
if (!$ollama) {
    Write-Host "Ollama was not found. Trying to install Ollama with winget."
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (!$winget) {
        Write-Host "winget was not found. Please install Ollama from https://ollama.com/download/windows and run this setup again." -ForegroundColor Red
        exit 1
    }

    winget install Ollama.Ollama --accept-package-agreements --accept-source-agreements
}

if (!(Test-OllamaApi)) {
    Write-Host "Starting Ollama local service."
    Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
    Start-Sleep -Seconds 5
}

if (!(Test-OllamaApi)) {
    Write-Host "Ollama is installed but the local service is not responding." -ForegroundColor Red
    Write-Host "Please restart the computer, then run this setup again."
    exit 1
}

Write-Step "Checking local Qwen model"
$tags = Invoke-RestMethod "http://localhost:11434/api/tags" -TimeoutSec 10
$hasModel = $false
foreach ($model in $tags.models) {
    if ($model.name -eq $ModelName) {
        $hasModel = $true
    }
}

if (!$hasModel) {
    Write-Host "Downloading $ModelName. This is large and may take a long time."
    ollama pull $ModelName
}
else {
    Write-Host "$ModelName is already installed."
}

Write-Step "Verifying worker imports"
& $PythonExe -c "from paddleocr import PaddleOCR; print('PaddleOCR import OK')"

Write-Step "Setup complete"
Write-Host "You can now start Accounting Assistant by double-clicking the app executable."
