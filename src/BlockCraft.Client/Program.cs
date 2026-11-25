namespace BlockCraft.Client;

/// <summary>BlockCraft Windows desktop entry point.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var game = new BlockCraftGame();
        game.Run();
    }
}
