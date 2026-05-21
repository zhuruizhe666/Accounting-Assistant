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

    public async Task<ReceiptAnalysisResult> AnalyzeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            EnsureServeProcessStarted();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(AnalyzeTimeout);

            var request = JsonSerializer.Serialize(new
            {
                command = "analyze",
                image_path = imagePath,
                mock = false
            });

            await _stdin!.WriteLineAsync(request.AsMemory(), timeoutCts.Token);
            await _stdin.FlushAsync(timeoutCts.Token);

            var responseLine = await _stdout!.ReadLineAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                RestartServeProcess();
                throw new InvalidOperationException($"Python worker returned no response.{Environment.NewLine}{GetRecentStderr()}");
            }

            var result = JsonSerializer.Deserialize<ReceiptAnalysisResult>(responseLine, JsonOptions)
                ?? throw new InvalidOperationException("Python worker returned invalid JSON.");

            if (string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Python worker error: {result.Error}");
            }

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
}
