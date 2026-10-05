using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;
using Cascade50.Core;

namespace Cascade50.Android;

/// <summary>Android entry point: landscape, full screen, played with the on-screen touch controls.</summary>
[Activity(
    Label = "Cascade 50",
    MainLauncher = true,
    Icon = "@mipmap/icon",
    RoundIcon = "@mipmap/icon_round",
    Theme = "@style/Theme.Splash",
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode
)]
public class MainActivity : AndroidGameActivity
{
    private Cascade50Game _game;
    private View _view;

    protected override void OnCreate(Bundle bundle)
    {
        base.OnCreate(bundle);

        _game = new Cascade50Game(new AndroidPlatformServices(FilesDir?.AbsolutePath));
        _view = _game.Services.GetService(typeof(View)) as View;

        SetContentView(_view);
        HideSystemUi();
        _game.Run();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            HideSystemUi();
    }

    private void HideSystemUi()
    {
        if (Window == null)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var controller = Window.InsetsController;
            if (controller != null)
            {
                controller.Hide(WindowInsets.Type.SystemBars());
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
        }
        else
        {
#pragma warning disable CA1422
            Window.DecorView.SystemUiFlags =
                SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation |
                SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable;
#pragma warning restore CA1422
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(28) && Window.Attributes != null)
        {
            var attrs = Window.Attributes;
            attrs.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            Window.Attributes = attrs;
        }
    }
}

internal sealed class AndroidPlatformServices : Core.Platform.DesktopPlatformServices
{
    private readonly string _dataDirectory;

    public AndroidPlatformServices(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public override bool IsMobile => true;
    public override string DataDirectory => _dataDirectory ?? base.DataDirectory;

    // Android apps leave via the Back button / home; finishing the activity is enough.
    public override void Quit(Game game) => game.Exit();
}
