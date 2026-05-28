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

namespace Wipelog.Core.Preferences;

/// <summary>
/// The canonical set of <see cref="DaemonConfig.VideoResolution"/> values.
/// "source" (or null/empty) means "no scaling — record at the capture
/// monitor's native resolution". The other choices are target heights; the
/// recorder pairs them with the monitor's aspect ratio to derive the width
/// at runtime so ultrawide displays stay un-letterboxed.
/// </summary>
public static class VideoResolutions
{
    public const string Source = "source";

    /// <summary>Values shown to the user, in dropdown order.</summary>
    public static IReadOnlyList<string> UserChoices { get; } =
        new[] { Source, "1440p", "1080p", "720p" };

    /// <summary>
    /// True when <paramref name="value"/> is one of the recognised choices,
    /// or null/empty (which deserialise to <see cref="Source"/>).
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        foreach (var c in UserChoices)
        {
            if (string.Equals(value, c, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Target encoded height for <paramref name="value"/>, or <c>null</c> when
    /// the user picked "source" / null / empty (don't scale).
    /// </summary>
    public static int? TargetHeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "720p" => 720,
            "1080p" => 1080,
            "1440p" => 1440,
            _ => null,
        };
    }

    /// <summary>
    /// Derive the <c>-s WxH</c> string for gpu-screen-recorder given the
    /// capture monitor's native dimensions and the chosen target height.
    /// Width is rounded to the nearest even number (most encoders reject odd
    /// dimensions). Returns <c>null</c> when no downscale is needed —
    /// either because the user picked "source" or because the source
    /// monitor is already at or below the target height.
    /// </summary>
    public static string? ResolveCaptureSize(int sourceWidth, int sourceHeight, int? targetHeight)
    {
        if (targetHeight is not int th || th <= 0) return null;
        if (sourceWidth <= 0 || sourceHeight <= 0) return null;
        if (th >= sourceHeight) return null;
        var width = (int)Math.Round((double)sourceWidth * th / sourceHeight);
        if (width % 2 != 0) width += 1;
        return $"{width}x{th}";
    }
}
