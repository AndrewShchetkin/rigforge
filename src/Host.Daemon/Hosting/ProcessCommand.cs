using System.Diagnostics;
using System.Text;

namespace RigForge.Host.Daemon.Hosting;

internal static class ProcessCommand
{
    internal readonly record struct Result(int ExitCode, string StdOut, string StdErr);

    public static Result Run(string fileName, IReadOnlyList<string> arguments, Encoding? outputEncoding, CancellationToken cancellationToken) =>
        Task.Run(() => RunAsync(fileName, arguments, outputEncoding, cancellationToken), CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<Result> RunAsync(string fileName, IReadOnlyList<string> arguments, Encoding? outputEncoding, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // schtasks /XML writes UTF-16 to stdout and a console-code-page message to stderr.
            StandardOutputEncoding = outputEncoding ?? ConsoleEncoding(),
            StandardErrorEncoding = ConsoleEncoding(),
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {fileName}.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException($"{fileName} did not finish within 15 seconds.");
            }

            throw;
        }
        finally
        {
            // Drain both pipes even when the process is killed, so disposing it cannot deadlock.
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        return new Result(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static Encoding ConsoleEncoding()
    {
        try
        {
            return Console.OutputEncoding;
        }
        catch (IOException)
        {
            return Encoding.UTF8;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
