using System.Diagnostics;
using System.Text;

namespace MiningGame.ErrorChecker;

/// <summary>
/// Windows-only process monitor smoke test. It exercises the process-exit signal used by
/// the GUI monitor without requiring a graphics device or starting the full game.
/// </summary>
internal static class CrashMonitorSelfTest
{
    public static int Run()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiningGame", "Logs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"checker-self-test-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var report = new StringBuilder();

        try
        {
            var normalExit = RunCommand(0);
            report.AppendLine($"Normal child process exit code: {normalExit}");
            if (normalExit != 0)
                throw new InvalidOperationException($"A normal child process returned {normalExit} instead of 0.");

            var crashExit = RunCommand(42);
            report.AppendLine($"Simulated crash child process exit code: {crashExit}");
            if (crashExit != 42)
                throw new InvalidOperationException($"The self-test expected exit code 42 but received {crashExit}.");

            report.AppendLine("PASS: normal exit and unexpected non-zero exit were both detected.");
            File.WriteAllText(logPath, report.ToString(), Encoding.UTF8);
            return 0;
        }
        catch (Exception exception)
        {
            report.AppendLine(exception.ToString());
            try { File.WriteAllText(logPath, report.ToString(), Encoding.UTF8); }
            catch { /* Preserve the failing exit code if disk logging is unavailable. */ }
            return 1;
        }
    }

    private static int RunCommand(int expectedExitCode)
    {
        var commandProcessor = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(commandProcessor))
            commandProcessor = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = commandProcessor,
            Arguments = $"/d /c exit /b {expectedExitCode}",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Windows did not start the self-test child process.");

        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* Best-effort cleanup of a hung test child. */ }
            throw new TimeoutException("The self-test child process did not exit within 10 seconds.");
        }
        return process.ExitCode;
    }
}
