using System;
using System.Collections.Generic;
using System.IO;

namespace Cascade50.Core.Storage;

/// <summary>High scores, play counts and settings, saved as a small text file in the app's own data folder.</summary>
public sealed class Profile
{
    private const string FileName = "cascade50.txt";
    private readonly string _directory;
    private readonly Dictionary<int, int> _best = new();
    private readonly Dictionary<int, int> _plays = new();

    public Profile(string directory)
    {
        _directory = directory;
    }

    public bool SoundOn { get; set; } = true;
    public bool MusicOn { get; set; } = true;

    /// <summary>The last game picked in the menu (so the menu reopens there).</summary>
    public int LastGame { get; set; }

    public int Best(int number) => _best.TryGetValue(number, out var v) ? v : 0;
    public int Plays(int number) => _plays.TryGetValue(number, out var v) ? v : 0;

    /// <summary>Records a finished game. Returns true for a new best score.</summary>
    public bool Record(int number, int score)
    {
        _plays[number] = Plays(number) + 1;
        bool best = score > Best(number);
        if (best)
            _best[number] = score;
        Save();
        return best;
    }

    public int GamesPlayed()
    {
        int n = 0;
        foreach (var v in _plays.Values)
            if (v > 0)
                n++;
        return n;
    }

    public static Profile Load(string directory)
    {
        var p = new Profile(directory);
        try
        {
            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
                return p;
            foreach (var line in File.ReadAllLines(path))
            {
                var kv = line.Split('=');
                if (kv.Length != 2)
                    continue;
                string k = kv[0].Trim();
                if (!int.TryParse(kv[1].Trim(), out int v))
                    continue;
                if (k == "sound") p.SoundOn = v != 0;
                else if (k == "music") p.MusicOn = v != 0;
                else if (k == "last") p.LastGame = v;
                else if (k.StartsWith("best.") && int.TryParse(k[5..], out int b)) p._best[b] = v;
                else if (k.StartsWith("plays.") && int.TryParse(k[6..], out int n)) p._plays[n] = v;
            }
        }
        catch (Exception)
        {
            // A damaged file just means starting afresh.
        }
        return p;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var lines = new List<string>
            {
                $"sound={(SoundOn ? 1 : 0)}",
                $"music={(MusicOn ? 1 : 0)}",
                $"last={LastGame}",
            };
            foreach (var (k, v) in _best)
                lines.Add($"best.{k}={v}");
            foreach (var (k, v) in _plays)
                lines.Add($"plays.{k}={v}");
            string path = Path.Combine(_directory, FileName);
            File.WriteAllLines(path + ".tmp", lines);
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception)
        {
        }
    }
}
