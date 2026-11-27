using System.Runtime.InteropServices;

namespace BlockCraft.Client;

/// <summary>BlockCraft Windows desktop entry point with visible startup diagnostics.</summary>
internal static class Program
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockCraft", "Logs");

    [STAThread]
    private static void Main()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Log("Starting BlockCraft.");
            Log($"OS: {Environment.OSVersion}; Runtime: {Environment.Version}; 64-bit: {Environment.Is64BitProcess}.");
            using var game = new BlockCraftGame();
            Log("Game object created; entering MonoGame loop.");
            game.Run();
            Log("Game loop exited normally.");
        }
        catch (Exception exception)
        {
            // The external launch monitor uses this exit code to detect caught startup failures.
            Environment.ExitCode = 1;
            var report = WriteCrashReport(exception);
            var message = "BlockCraft could not start.\n\n" +
                          exception.GetType().FullName + ": " + exception.Message +
                          "\n\nA diagnostic report was saved here:\n" + report +
                          "\n\nPlease send that file to the developer.";
            try { _ = MessageBoxW(IntPtr.Zero, message, "BlockCraft startup error", 0x10 | 0x1000); }
            catch { /* Keep the crash report even if Windows cannot display the dialog. */ }
        }
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, "startup.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch { /* Diagnostics must never prevent the game from launching. */ }
    }

    private static string WriteCrashReport(Exception exception)
    {
        var path = Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var report = "BlockCraft startup crash report" + Environment.NewLine +
                     $"Time: {DateTimeOffset.Now:O}" + Environment.NewLine +
                     $"OS: {Environment.OSVersion}" + Environment.NewLine +
                     $"Process architecture: {RuntimeInformation.ProcessArchitecture}" + Environment.NewLine +
                     $"Framework: {RuntimeInformation.FrameworkDescription}" + Environment.NewLine +
                     $"Base directory: {AppContext.BaseDirectory}" + Environment.NewLine +
                     $"Current directory: {Environment.CurrentDirectory}" + Environment.NewLine +
                     $"Command line: {Environment.CommandLine}" + Environment.NewLine +
                     Environment.NewLine + exception;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(path, report);
            Log($"Startup failed. Crash report: {path}");
        }
        catch
        {
            return Path.Combine(LogDirectory, "crash-report-could-not-be-written.txt");
        }
        return path;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
