using AccountingAssistant.App.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AccountingAssistant.App.Services;

public sealed class PythonWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ReceiptAnalysisResult> AnalyzeMockAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var workerScript = Path.Combine(repoRoot, "worker", "accounting_worker", "main.py");

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(workerScript);
        startInfo.ArgumentList.Add("analyze");
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("--mock");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Python worker.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python worker exited with code {process.ExitCode}.{Environment.NewLine}{stderr}");
        }

        return JsonSerializer.Deserialize<ReceiptAnalysisResult>(stdout, JsonOptions)
            ?? throw new InvalidOperationException("Python worker returned empty or invalid JSON.");
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
