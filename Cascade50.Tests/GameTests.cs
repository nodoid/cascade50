using System.Collections.Generic;
using System.Linq;
using Cascade50.Core.Games;

namespace Cascade50.Tests;

public class GameTests
{
    public static IEnumerable<object[]> AllGames() => Enumerable.Range(0, GameCatalog.Count).Select(i => new object[] { i });

    [Fact]
    public void CatalogHasFiftyNumberedGames()
    {
        Assert.Equal(50, GameCatalog.Count);
        for (int i = 0; i < GameCatalog.Count; i++)
            Assert.Equal(i + 1, GameCatalog.Info[i].Number);
        Assert.Equal(50, GameCatalog.Info.Select(g => g.Title).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllGames))]
    public void DescriptionIsComplete(int index)
    {
        var g = GameCatalog.Info[index];
        Assert.False(string.IsNullOrWhiteSpace(g.Title));
        Assert.False(string.IsNullOrWhiteSpace(g.Tagline));
        Assert.True(g.Tagline.Length <= 80, $"{g.Title}: tagline is {g.Tagline.Length} characters");
        Assert.NotEmpty(g.HowToPlay);
        Assert.NotEmpty(g.DesktopControls);
        Assert.NotEmpty(g.TouchControls);
        Assert.DoesNotContain("Coming soon", g.Tagline);
    }

    [Theory]
    [MemberData(nameof(AllGames))]
    public void AutoPlaySurvivesTwoMinutes(int index)
    {
        foreach (bool touch in new[] { false, true })
        {
            var h = new GameHarness(index, 1000 + index, touch);
            for (int t = 0; t < 60 * 120; t++)
            {
                h.AutoTick();
                if (t % 7 == 0)
                    h.Draw();
                h.RestartIfOver(t);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllGames))]
    public void RandomInputNeverCrashes(int index)
    {
        for (int seed = 0; seed < 3; seed++)
        {
            var h = new GameHarness(index, seed * 31 + index, seed == 1);
            for (int t = 0; t < 60 * 60; t++)
            {
                h.RandomTick();
                if (t % 5 == 0)
                    h.Draw();
                h.RestartIfOver(t + seed);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllGames))]
    public void ScoreIsNeverNegative(int index)
    {
        var h = new GameHarness(index, 77);
        for (int t = 0; t < 60 * 90; t++)
        {
            h.AutoTick();
            Assert.True(h.Game.Score >= 0, $"{h.Game.Title}: score {h.Game.Score}");
            h.RestartIfOver(t);
        }
    }
}
