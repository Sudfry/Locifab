using System.Diagnostics;

namespace LocalCraft.Conversion.Engines;

internal static class ProcessRunner
{
    public static async Task RunAsync(
        string executable,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("转换引擎进程启动失败。");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 进程可能已经退出。
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException($"转换引擎超过 {timeout.TotalSeconds:0} 秒未完成，已终止。");
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            detail = detail.Trim();
            if (detail.Length > 800) detail = detail[^800..];
            throw new InvalidOperationException($"转换引擎返回错误（退出码 {process.ExitCode}）：{detail}");
        }
    }
}
