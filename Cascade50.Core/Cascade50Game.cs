using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Capture;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;
using Cascade50.Core.Platform;
using Cascade50.Core.Scenes;
using Cascade50.Core.Storage;
using Cascade50.Core.UI;

namespace Cascade50.Core;

/// <summary>
/// Cascade 50: fifty Oric-1 classics, remade by PFJ, based on the Cascade Games originals.
/// Runs at 60 ticks per second and draws a resolution-independent 640x360 playfield.
/// </summary>
public class Cascade50Game : Game
{
    public const int TicksPerSecond = 60;
    public const string Name = "CASCADE 50";
    public const string Credit = "BY PFJ";
    public const string BasedOn = "BASED ON THE CASCADE GAMES ORIGINALS";

    private readonly GraphicsDeviceManager _graphics;
    private Scene _scene;
    private Scene _pending;
    private float _fade = 1f;
    private bool _fadingOut;
    private KeyboardState _prevKeys;

    public Cascade50Game() : this(null)
    {
    }

    public Cascade50Game(IPlatformServices platform)
    {
        Platform = platform ?? new DesktopPlatformServices();
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferMultiSampling = false,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / TicksPerSecond);

        if (Platform.IsMobile)
        {
            _graphics.IsFullScreen = true;
            _graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
        }
        else
        {
            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;
            IsMouseVisible = true;
        }
    }

    public IPlatformServices Platform { get; }
    public GpuGfx Gfx { get; private set; }
    public InputManager Input { get; private set; }
    public Ui Ui { get; private set; }
    public SoundBank Sound { get; private set; }
    public Profile Profile { get; private set; }

    /// <summary>The sound the games play through (the sound bank, or a recorder while capturing video).</summary>
    public ISound GameSound { get; private set; }

    internal void UseRecordingSound(CaptureDirector director) => GameSound = new RecordingSound(director);

    /// <summary>Store-asset capture in progress (see <see cref="CaptureDirector"/>).</summary>
    public CaptureDirector Capture { get; set; }

    /// <summary>Unattended soak test in progress (see <see cref="SoakDirector"/>).</summary>
    public SoakDirector Soak { get; set; }

    public bool IsMobile => Platform.IsMobile || (Capture?.Mobile ?? false) || (Soak?.Mobile ?? false);

    /// <summary>Resizes the desktop window (soak test).</summary>
    internal void SetWindowSize(int width, int height)
    {
        if (Platform.IsMobile)
            return;
        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.ApplyChanges();
    }

    public Scene Scene => _scene;

    protected override void Initialize()
    {
        Window.Title = "Cascade 50";
        if (Capture != null || Soak != null)
        {
            IsFixedTimeStep = false;
            _graphics.SynchronizeWithVerticalRetrace = false;
            _graphics.ApplyChanges();
        }
        if (!Platform.IsMobile && Capture == null)
        {
            Window.AllowUserResizing = true;
            FitWindowToDisplay();
        }
        base.Initialize();
    }

    private void FitWindowToDisplay()
    {
        var mode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        int w = 1280, h = 720;
        if (w > mode.Width * 0.9 || h > mode.Height * 0.85)
        {
            float s = MathF.Min(mode.Width * 0.9f / w, mode.Height * 0.85f / h);
            w = (int)(w * s);
            h = (int)(h * s);
        }
        _graphics.PreferredBackBufferWidth = w;
        _graphics.PreferredBackBufferHeight = h;
        _graphics.ApplyChanges();
    }

    protected override void LoadContent()
    {
        Gfx = new GpuGfx(GraphicsDevice) { AnchorTop = IsMobile };
        Input = new InputManager(IsMobile);
        Sound = new SoundBank();
        Profile = Profile.Load(Soak?.DataDirectory ?? Platform.DataDirectory);
        Sound.SoundOn = Profile.SoundOn && Capture == null;
        Sound.MusicOn = Profile.MusicOn && Capture == null;
        Ui = new Ui(Sound);
        GameSound = Sound;
        if (Soak != null)
        {
            // Exercise the real sound engine, silently.
            Sound.SoundOn = Sound.MusicOn = true;
            Microsoft.Xna.Framework.Audio.SoundEffect.MasterVolume = 0f;
        }
        if (Capture != null)
        {
            Capture.Start(this);
            return;
        }
        _scene = new SplashScene(this);
        _scene.Enter();
        Soak?.Start(this);
    }

    protected override void UnloadContent()
    {
        Sound?.Dispose();
        Gfx?.Dispose();
        base.UnloadContent();
    }

    protected override void OnDeactivated(object sender, EventArgs args)
    {
        _scene?.Deactivated();
        Sound?.Pause();
        base.OnDeactivated(sender, args);
    }

    protected override void OnActivated(object sender, EventArgs args)
    {
        if (_scene is MenuScene || _scene is SplashScene || _scene is InfoScene)
            Sound?.SetMusic(true);
        base.OnActivated(sender, args);
    }

    protected override void Update(GameTime gameTime)
    {
        if (Capture != null)
        {
            Capture.Update(this);
            base.Update(gameTime);
            return;
        }

        var keys = Keyboard.GetState();
        bool altEnter = keys.IsKeyDown(Keys.Enter) && !_prevKeys.IsKeyDown(Keys.Enter) &&
                        (keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt));
        if (!IsMobile && (altEnter || (keys.IsKeyDown(Keys.F11) && !_prevKeys.IsKeyDown(Keys.F11))))
            _graphics.ToggleFullScreen();
        _prevKeys = keys;

        if (_fadingOut)
        {
            _fade = MathF.Min(1, _fade + 1f / 8);
            if (_fade >= 1 && _pending != null)
            {
                _scene?.Leave();
                _scene = _pending;
                _pending = null;
                _fadingOut = false;
                Input.Reset();
                _scene.Enter();
            }
        }
        else if (_fade > 0)
        {
            _fade = MathF.Max(0, _fade - 1f / 10);
        }

        var pp = GraphicsDevice.PresentationParameters;
        Gfx.SideReserve = IsMobile && _scene.TouchPad != null ? 100 : 0;
        Gfx.Layout(pp.BackBufferWidth, pp.BackBufferHeight);
        Input.Update(Gfx.ToVirtual, Gfx.Visible, IsMobile ? _scene.TouchPad : null, _scene.TextMode, IsActive);
        if (_fadingOut)
            Input.Controls.Clear();
        else
            Soak?.Inject(this);
        Ui.BeginFrame(Input.Controls);
        _scene.Tick();
        Soak?.AfterTick(this);
        Sound.EndFrame();
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (Capture != null)
        {
            Capture.Draw(this);
            base.Draw(gameTime);
            return;
        }
        var pp = GraphicsDevice.PresentationParameters;
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderScene(Gfx, pp.BackBufferWidth, pp.BackBufferHeight, _fade);
        Soak?.FrameTime(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        base.Draw(gameTime);
    }

    /// <summary>Draws the current scene onto the bound surface (also used by capture).</summary>
    internal void RenderScene(GpuGfx g, int width, int height, float fade)
    {
        g.SideReserve = IsMobile && _scene?.TouchPad != null ? 100 : 0;
        g.Begin(width, height);
        _scene?.Draw(g);
        if (IsMobile && _scene?.TouchPad != null)
        {
            g.Offset = Vector2.Zero;
            g.SetClip(null);
            Input.TouchPad.Layout(g.Visible, _scene.TouchPad);
            Input.TouchPad.Draw(g);
        }
        if (fade > 0)
        {
            g.Offset = Vector2.Zero;
            g.SetClip(null);
            g.Rect(g.Visible, Color.Black * fade);
        }
        g.End();
    }

    internal void SetSceneImmediately(Scene scene)
    {
        _scene?.Leave();
        _scene = scene;
        _scene.Enter();
        _fade = 0;
    }

    // ---------------- flow ----------------

    public void SwitchTo(Scene scene)
    {
        _pending = scene;
        _fadingOut = true;
    }

    public void ShowMenu(int selectGame = -1)
    {
        if (selectGame >= 0)
            Profile.LastGame = selectGame;
        SwitchTo(new MenuScene(this));
    }

    public void ShowInfo(int gameIndex)
    {
        Profile.LastGame = gameIndex;
        SwitchTo(new InfoScene(this, gameIndex));
    }

    public void Play(int gameIndex)
    {
        Profile.LastGame = gameIndex;
        Profile.Save();
        SwitchTo(new PlayScene(this, gameIndex));
    }

    public void SaveSettings()
    {
        Profile.SoundOn = Sound.SoundOn;
        Profile.MusicOn = Sound.MusicOn;
        Profile.Save();
    }

    public void Quit() => Platform.Quit(this);
}
