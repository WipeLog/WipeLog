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

namespace Wipelog.Core.Recording;

/// <summary>
/// Turns a gpu-screen-recorder <c>-a</c> source string (e.g.
/// <c>app:World of Warcraft</c>, <c>default_output</c>) into a human-readable
/// audio-track title that vanilla players like VLC, mpv, and Quick Look will
/// show in their track-picker. gsr itself doesn't expose track titles, so we
/// stamp them on via <c>ffmpeg -metadata:s:a:N title=</c> during the pre-roll
/// concat (the only ffmpeg pass that runs in the normal recording lifecycle).
/// </summary>
public static class AudioTrackNaming
{
    /// <summary>
    /// Map a single source string to a track title. Unknown forms pass
    /// through unchanged so a user with a custom <c>device:...</c> or raw
    /// PulseAudio sink name still gets a meaningful label.
    /// </summary>
    public static string FriendlyTitle(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Audio";
        var s = source.Trim();
        if (string.Equals(s, "default_output", StringComparison.OrdinalIgnoreCase))
        {
            return "System mix";
        }
        if (string.Equals(s, "default_input", StringComparison.OrdinalIgnoreCase))
        {
            return "Microphone";
        }
        if (TryStripPrefix(s, "app-inverse:", out var inv))
        {
            return $"Not {inv}";
        }
        if (TryStripPrefix(s, "app:", out var app))
        {
            return app;
        }
        if (TryStripPrefix(s, "device:", out var dev))
        {
            return dev;
        }
        return s;
    }

    /// <summary>
    /// Project a list of <c>-a</c> sources to titles in the same order. Empty
    /// input returns an empty list so callers can skip the metadata pass.
    /// </summary>
    public static IReadOnlyList<string> FriendlyTitles(IReadOnlyList<string>? sources)
    {
        if (sources is null || sources.Count == 0) return Array.Empty<string>();
        var titles = new string[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            titles[i] = FriendlyTitle(sources[i]);
        }
        return titles;
    }

    private static bool TryStripPrefix(string value, string prefix, out string remainder)
    {
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            remainder = value.Substring(prefix.Length).Trim();
            if (remainder.Length > 0) return true;
        }
        remainder = string.Empty;
        return false;
    }
}
