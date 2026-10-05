using System;
using Cascade50.Core;

namespace Cascade50.WindowsDX;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var game = new Cascade50Game();
        // --capture / --art / --video record the store screenshots, art and previews (see CaptureDirector).
        game.Capture = Core.Capture.CaptureDirector.FromArgs(args);
        game.Run();
    }
}
