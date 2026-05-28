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

using System.Globalization;
using System.Text;
using Wipelog.Core.Preferences;

namespace Wipelog.App.Services;

/// <summary>
/// Inverse of <see cref="SystemdUnitFileBuilder"/>. Reads the <c>ExecStart=</c>
/// line out of an installed wipelog.service unit file and reconstructs the
/// <see cref="DaemonConfig"/> that was used to render it. The installed unit
/// is treated as the source-of-truth for what the daemon is actually running
/// with, so the GUI can show those values instead of a possibly-stale
/// preferences.json snapshot.
/// </summary>
internal static class SystemdUnitFileParser
{
    /// <summary>
    /// Parse the unit file content and return a populated <see cref="DaemonConfig"/>,
    /// or <c>null</c> if no <c>ExecStart=</c> line could be located. Fields not
    /// present in the ExecStart fall back to <see cref="DaemonConfig"/>'s
    /// defaults.
    /// </summary>
    public static DaemonConfig? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var execStart = ExtractExecStart(content);
        if (execStart is null) return null;

        var tokens = Tokenize(execStart);
        if (tokens.Count == 0) return null;

        var daemonExe = tokens[0];
        string logDir = "";
        string logPattern = "WoWCombatLog-*.txt";
        string output = "";
        string dbPath = "";
        string gsrExe = "gpu-screen-recorder";
        string captureTarget = "focused";
        int framerate = 60;
        string codec = "h264";
        string container = "mp4";
        var audioTracks = new List<string>();
        string videoQuality = "medium";
        string? videoResolution = null;
        int? preRollSeconds = null;
        int? postRollSeconds = null;

        for (int i = 1; i < tokens.Count - 1; i++)
        {
            var flag = tokens[i];
            var value = tokens[i + 1];
            switch (flag)
            {
                case "--log-dir": logDir = value; i++; break;
                case "--log-pattern": logPattern = value; i++; break;
                case "--output": output = value; i++; break;
                case "--db-path": dbPath = value; i++; break;
                case "--executable": gsrExe = value; i++; break;
                case "--window": captureTarget = value; i++; break;
                case "--framerate":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fr))
                    {
                        framerate = fr;
                    }
                    i++;
                    break;
                case "--codec": codec = value; i++; break;
                case "--container": container = value; i++; break;
                case "--audio": audioTracks.Add(value); i++; break;
                case "--video-quality": videoQuality = value; i++; break;
                case "--video-resolution": videoResolution = value; i++; break;
                case "--preroll-seconds":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pr))
                    {
                        preRollSeconds = pr;
                    }
                    i++;
                    break;
                case "--postroll-seconds":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var po))
                    {
                        postRollSeconds = po;
                    }
                    i++;
                    break;
            }
        }

        return new DaemonConfig
        {
            DaemonExecutablePath = daemonExe,
            LogDirectory = logDir,
            LogPattern = logPattern,
            OutputDirectory = output,
            DatabasePath = dbPath,
            GpuScreenRecorderExecutable = gsrExe,
            CaptureTarget = captureTarget,
            Framerate = framerate,
            Codec = codec,
            Container = container,
            AudioTracks = audioTracks,
            VideoQuality = videoQuality,
            VideoResolution = videoResolution,
            PreRollSeconds = preRollSeconds,
            PostRollSeconds = postRollSeconds,
        };
    }

    /// <summary>
    /// Pull the value after <c>ExecStart=</c>, joining trailing backslash
    /// continuations into a single logical line. Stops at the first non-
    /// continued newline.
    /// </summary>
    private static string? ExtractExecStart(string content)
    {
        const string marker = "ExecStart=";
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(marker, StringComparison.Ordinal)) continue;

            var sb = new StringBuilder();
            sb.Append(trimmed.Substring(marker.Length));
            while (sb.Length > 0 && sb[^1] == '\\')
            {
                sb.Length--;
                var next = reader.ReadLine();
                if (next is null) break;
                sb.Append(' ');
                sb.Append(next.TrimStart());
            }
            return sb.ToString();
        }
        return null;
    }

    /// <summary>
    /// Split an ExecStart payload into argv tokens. Handles double-quoted
    /// segments with the same <c>\\</c> / <c>\"</c> escapes that
    /// <see cref="SystemdUnitFileBuilder.Quote"/> emits.
    /// </summary>
    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool inToken = false;

        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (inQuotes)
            {
                if (c == '\\' && i + 1 < input.Length)
                {
                    current.Append(input[i + 1]);
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                inToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
                continue;
            }

            current.Append(c);
            inToken = true;
        }
        if (inToken) tokens.Add(current.ToString());
        return tokens;
    }
}
