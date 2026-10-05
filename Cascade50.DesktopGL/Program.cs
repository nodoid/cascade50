using Cascade50.Core;

namespace Cascade50.DesktopGL;

internal static class Program
{
    private static void Main(string[] args)
    {
        // --catalog <file.json> writes the games' descriptions and exits.
        int c = System.Array.IndexOf(args, "--catalog");
        if (c >= 0 && c + 1 < args.Length)
        {
            Core.Games.GameCatalog.ExportJson(args[c + 1]);
            return;
        }
        // --soak <log> [--mobile] [--seconds n] plays every game unattended and logs any error.
        var soak = Core.Capture.SoakDirector.FromArgs(args);
        if (soak != null)
            System.AppDomain.CurrentDomain.UnhandledException += (_, e) => soak.Error("unhandled", (System.Exception)e.ExceptionObject);

        using var game = new Cascade50Game();
        // --capture / --art / --video record the store screenshots, art and previews (see CaptureDirector).
        game.Capture = Core.Capture.CaptureDirector.FromArgs(args);
        game.Soak = soak;
        try
        {
            game.Run();
        }
        catch (System.Exception e) when (soak != null)
        {
            soak.Error("crash", e);
            soak.Log("RESULT: CRASH");
            System.Environment.ExitCode = 2;
        }
    }
}
