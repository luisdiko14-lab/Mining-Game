using System.Windows.Forms;

namespace MiningGame.ErrorChecker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new CheckerForm(args));
    }
}
