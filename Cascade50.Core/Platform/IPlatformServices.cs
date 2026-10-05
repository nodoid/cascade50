using Microsoft.Xna.Framework;

namespace Cascade50.Core.Platform;

/// <summary>Services a platform head can supply to the shared game.</summary>
public interface IPlatformServices
{
    bool IsMobile { get; }

    /// <summary>Directory where high scores and settings are stored.</summary>
    string DataDirectory { get; }

    /// <summary>True where the platform lets an app quit itself (not iOS).</summary>
    bool CanQuit { get; }

    /// <summary>Exit the application (ignored where the platform forbids it).</summary>
    void Quit(Game game);
}

/// <summary>Defaults suitable for desktop builds.</summary>
public class DesktopPlatformServices : IPlatformServices
{
    public virtual bool IsMobile => false;
    public virtual bool CanQuit => true;

    public virtual string DataDirectory
    {
        get
        {
            string root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
                root = System.AppContext.BaseDirectory;
            return System.IO.Path.Combine(root, "Cascade50");
        }
    }

    public virtual void Quit(Game game) => game.Exit();
}
