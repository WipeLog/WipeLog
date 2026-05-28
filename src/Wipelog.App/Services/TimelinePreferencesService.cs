// Wipelog — Linux-native World of Warcraft gameplay recorder.
// Copyright (C) 2026 Patrik - WipeLog-dev
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wipelog.App.Services;

/// <summary>
/// User-tweakable behaviour of the timeline strip: how far to rewind when a
/// marker is clicked, and whether to also pause. Persisted next to the
/// window-state file so the user's preferred values survive restarts.
/// </summary>
public sealed record TimelinePreferences(int SeekBackSeconds, bool PauseOnClick)
{
    public const int DefaultSeekBackSeconds = 5;
    public const bool DefaultPauseOnClick = true;
    public const int MinSeekBackSeconds = 0;
    public const int MaxSeekBackSeconds = 30;

    public static TimelinePreferences Defaults =>
        new(DefaultSeekBackSeconds, DefaultPauseOnClick);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TimelinePreferences))]
internal partial class TimelinePreferencesJsonContext : JsonSerializerContext
{
}

public sealed class TimelinePreferencesService
{
    private readonly string _path;

    public TimelinePreferencesService()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        var baseDir = string.IsNullOrEmpty(xdg) ? System.IO.Path.Combine(home, ".config") : xdg;
        _path = System.IO.Path.Combine(baseDir, "wipelog", "timeline.json");
    }

    public string Path => _path;

    public TimelinePreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return TimelinePreferences.Defaults;
            using var stream = File.OpenRead(_path);
            var loaded = JsonSerializer.Deserialize(
                stream, TimelinePreferencesJsonContext.Default.TimelinePreferences);
            if (loaded is null) return TimelinePreferences.Defaults;
            // Clamp on read so a hand-edited file with an out-of-range value
            // can't put the UI in a bad state.
            return loaded with
            {
                SeekBackSeconds = Math.Clamp(
                    loaded.SeekBackSeconds,
                    TimelinePreferences.MinSeekBackSeconds,
                    TimelinePreferences.MaxSeekBackSeconds),
            };
        }
        catch
        {
            return TimelinePreferences.Defaults;
        }
    }

    public void Save(TimelinePreferences prefs)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(
                stream, prefs, TimelinePreferencesJsonContext.Default.TimelinePreferences);
        }
        catch
        {
            // Best-effort — never crash on save.
        }
    }
}
