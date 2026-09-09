using AccountingAssistant.App.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AccountingAssistant.App.Services;

public sealed class PythonWorkerClient : IDisposable
{
    private static readonly TimeSpan AnalyzeTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly StringBuilder _stderrBuffer = new();
    private Process? _serveProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private Task? _stderrPumpTask;

    public event Action<string>? DebugOutputReceived;

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            OnDebugOutput("C# warmup requested.");
            EnsureServeProcessStarted();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AnalyzeTimeout);

            var responseLine = await SendServeRequestAsync(new { command = "warmup" }, timeoutCts.Token);
            using var document = JsonDocument.Parse(responseLine);
            var status = document.RootElement.GetProperty("status").GetString();

            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Python worker warmup failed: {responseLine}");
            }

            OnDebugOutput("C# warmup completed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RestartServeProcess();
            throw new TimeoutException($"Python worker warmup exceeded the {AnalyzeTimeout.TotalMinutes:0}-minute timeout.");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<ReceiptAnalysisResult> AnalyzeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            OnDebugOutput($"C# analyze requested: {imagePath}");
            EnsureServeProcessStarted();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AnalyzeTimeout);

            var responseLine = await SendServeRequestAsync(new
            {
                command = "analyze",
                image_path = imagePath,
                mock = false
            }, timeoutCts.Token);

            var result = JsonSerializer.Deserialize<ReceiptAnalysisResult>(responseLine, JsonOptions)
                ?? throw new InvalidOperationException("Python worker returned invalid JSON.");

            if (string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Python worker error: {result.Error}");
            }

            OnDebugOutput($"C# analyze completed: ocr_items={result.OcrItems.Count}");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RestartServeProcess();
            throw new TimeoutException($"Python worker exceeded the {AnalyzeTimeout.TotalMinutes:0}-minute analysis timeout.");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<SemanticAnalysisResult> ParseSemanticAsync(IReadOnlyList<OcrItem> ocrItems, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            OnDebugOutput($"C# semantic parse requested: ocr_items={ocrItems.Count}");
            EnsureServeProcessStarted();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AnalyzeTimeout);

            var responseLine = await SendServeRequestAsync(new
            {
                command = "semantic",
                ocr_items = ocrItems.Select(item => new
                {
                    text = item.Text,
                    corrected_text = item.CorrectedText,
                    confidence = item.Confidence,
                    bbox = item.BBox
                }).ToList()
            }, timeoutCts.Token);

            var result = JsonSerializer.Deserialize<SemanticAnalysisResult>(responseLine, JsonOptions)
                ?? throw new InvalidOperationException("Python worker returned invalid semantic JSON.");

            if (string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Python worker semantic error: {result.Error}");
            }

            OnDebugOutput($"C# semantic parse completed: status={result.SemanticStatus.Status}");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RestartServeProcess();
            throw new TimeoutException($"Python worker exceeded the {AnalyzeTimeout.TotalMinutes:0}-minute semantic timeout.");
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task<string> SendServeRequestAsync<TRequest>(TRequest requestObject, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(requestObject);
        OnDebugOutput($"C# sending worker request: {request}");

        await _stdin!.WriteLineAsync(request.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);

        var responseLine = await _stdout!.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            RestartServeProcess();
            throw new InvalidOperationException($"Python worker returned no response.{Environment.NewLine}{GetRecentStderr()}");
        }

        OnDebugOutput($"C# received worker response: {responseLine.Length} chars");
        return responseLine;
    }

    public Task<ReceiptAnalysisResult> AnalyzeMockAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        return RunOneShotAnalyzeAsync(imagePath, useMock: true, cancellationToken);
    }

    public void Dispose()
    {
        _requestLock.Dispose();
        RestartServeProcess();
    }

    private void EnsureServeProcessStarted()
    {
        if (_serveProcess is { HasExited: false } && _stdin is not null && _stdout is not null)
        {
            return;
        }

        RestartServeProcess();

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var workerScript = Path.Combine(repoRoot, "worker", "accounting_worker", "main.py");

        var startInfo = CreateWorkerStartInfo();
        startInfo.ArgumentList.Add(workerScript);
        startInfo.ArgumentList.Add("serve");

        _serveProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Python worker.");
        OnDebugOutput($"C# started Python worker process: pid={_serveProcess.Id}");
        _stdin = _serveProcess.StandardInput;
        _stdout = _serveProcess.StandardOutput;
        _stderrPumpTask = Task.Run(async () =>
        {
            while (!_serveProcess.HasExited)
            {
                var line = await _serveProcess.StandardError.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                lock (_stderrBuffer)
                {
                    _stderrBuffer.AppendLine(line);
                    if (_stderrBuffer.Length > 12000)
                    {
                        _stderrBuffer.Remove(0, _stderrBuffer.Length - 12000);
                    }
                }

                OnDebugOutput(line);
            }
        });
    }

    private async Task<ReceiptAnalysisResult> RunOneShotAnalyzeAsync(string imagePath, bool useMock, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AnalyzeTimeout);

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var workerScript = Path.Combine(repoRoot, "worker", "accounting_worker", "main.py");

        var startInfo = CreateWorkerStartInfo();
        startInfo.ArgumentList.Add(workerScript);
        startInfo.ArgumentList.Add("analyze");
        startInfo.ArgumentList.Add(imagePath);
        if (useMock)
        {
            startInfo.ArgumentList.Add("--mock");
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Python worker.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        string stdout;
        string stderr;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw new TimeoutException($"Python worker exceeded the {AnalyzeTimeout.TotalMinutes:0}-minute analysis timeout.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python worker exited with code {process.ExitCode}.{Environment.NewLine}{stderr}");
        }

        return JsonSerializer.Deserialize<ReceiptAnalysisResult>(stdout, JsonOptions)
            ?? throw new InvalidOperationException("Python worker returned empty or invalid JSON.");
    }

    private static ProcessStartInfo CreateWorkerStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return startInfo;
    }

    private void RestartServeProcess()
    {
        var process = _serveProcess;
        _serveProcess = null;
        _stdin = null;
        _stdout = null;
        _stderrPumpTask = null;

        if (process is null)
        {
            return;
        }

        TryKillProcess(process);
        process.Dispose();
        OnDebugOutput("C# Python worker process stopped.");
    }

    private string GetRecentStderr()
    {
        lock (_stderrBuffer)
        {
            return _stderrBuffer.ToString();
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup; the caller receives the actionable error.
        }
    }

    private static string FindRepoRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private void OnDebugOutput(string message)
    {
        DebugOutputReceived?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
