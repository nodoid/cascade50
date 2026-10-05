using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cascade50.Core.Games.All;

namespace Cascade50.Core.Games;

/// <summary>The fifty games, in the order of the original Oric-1 tape list.</summary>
public static class GameCatalog
{
    private static readonly Func<MiniGame>[] Factories =
    [
        () => new Attacker(),
        () => new BarrelJump(),
        () => new BlackHole(),
        () => new Boggles(),
        () => new CannonballBattle(),
        () => new DerbyDash(),
        () => new DoYourSums(),
        () => new Dynamite(),
        () => new Exchange(),
        () => new ForceField(),
        () => new GalacticAttack(),
        () => new GalacticDogFight(),
        () => new Ghosts(),
        () => new Hangman(),
        () => new HighRise(),
        () => new Inferno(),
        () => new Intruder(),
        () => new InvasiveAction(),
        () => new JetFlight(),
        () => new JetMobile(),
        () => new LunarLanding(),
        () => new MazeEater(),
        () => new Motorway(),
        () => new Nim(),
        () => new NoughtsAndCrosses(),
        () => new OldBones(),
        () => new Orbitter(),
        () => new Overtake(),
        () => new Parachute(),
        () => new Phaser(),
        () => new Planets(),
        () => new PlasmaBolt(),
        () => new Pontoon(),
        () => new PsionAttack(),
        () => new RadarLanding(),
        () => new Rats(),
        () => new RocketLaunch(),
        () => new SittingTarget(),
        () => new SkiJump(),
        () => new SmashTheWindows(),
        () => new SpaceMission(),
        () => new SpaceSearch(),
        () => new SpaceShip(),
        () => new StarTrek(),
        () => new Submarines(),
        () => new Tanker(),
        () => new TheForce(),
        () => new ThinIce(),
        () => new TunnelEscape(),
        () => new Universe(),
    ];

    private static MiniGame[] _info;

    public static int Count => Factories.Length;

    /// <summary>One instance of each game, for reading titles and instructions (never played).</summary>
    public static IReadOnlyList<MiniGame> Info => _info ??= Array.ConvertAll(Factories, f => f());

    /// <summary>A fresh instance of game <paramref name="index"/> (0-based) to play.</summary>
    public static MiniGame Create(int index) => Factories[index]();

    public static int IndexOf(int number)
    {
        for (int i = 0; i < Info.Count; i++)
            if (Info[i].Number == number)
                return i;
        return 0;
    }

    /// <summary>Writes every game's description as JSON (for the store copy and support page).</summary>
    public static void ExportJson(string path)
    {
        var list = new List<object>();
        foreach (var g in Info)
            list.Add(new
            {
                number = g.Number,
                title = g.Title,
                category = g.Category.ToString(),
                tagline = g.Tagline,
                howToPlay = g.HowToPlay,
                desktopControls = g.DesktopControls,
                touchControls = g.TouchControls,
                accent = $"#{g.Accent.R:X2}{g.Accent.G:X2}{g.Accent.B:X2}",
            });
        File.WriteAllText(path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
