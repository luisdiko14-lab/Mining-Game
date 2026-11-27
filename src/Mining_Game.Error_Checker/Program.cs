using System.Windows.Forms;

namespace MiningGame.ErrorChecker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--self-test-monitor", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = CrashMonitorSelfTest.Run();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new CheckerForm(args));
    }
}
