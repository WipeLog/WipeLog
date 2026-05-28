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

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Wipelog.Core.Recording;

/// <summary>
/// Tiny wrapper around <c>ffprobe</c> for the one piece of media metadata
/// we actually need: the duration of a saved pre-roll snippet, so the
/// sidecar can record how many seconds of pre-encounter footage are in the
/// final clip.
/// </summary>
public static class FfmpegProbe
{
    /// <summary>
    /// Returns the duration of <paramref name="path"/> in seconds, or
    /// <c>null</c> if ffprobe is missing, the file can't be opened, or the
    /// output can't be parsed.
    /// </summary>
    public static async Task<double?> GetDurationSecondsAsync(
        string path,
        ILogger logger,
        CancellationToken ct,
        string ffprobePath = "ffprobe")
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format=duration");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        psi.ArgumentList.Add(path);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ffprobe launch failed ({Path}); pre-roll duration unknown.", ffprobePath);
            return null;
        }
        if (proc is null) return null;

        using (proc)
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                return null;
            }
            var output = (await stdoutTask.ConfigureAwait(false)).Trim();
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
            {
                return seconds;
            }
            return null;
        }
    }

    /// <summary>
    /// Returns the height of the first video stream in <paramref name="path"/>
    /// in pixels, or <c>null</c> if ffprobe is missing, the file can't be
    /// opened, or has no video stream. Used by the chat-overlay scaler to
    /// match mask coordinates against the actual encoded height.
    /// </summary>
    public static async Task<int?> GetVideoHeightAsync(
        string path,
        ILogger logger,
        CancellationToken ct,
        string ffprobePath = "ffprobe")
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams"); psi.ArgumentList.Add("v:0");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("stream=height");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        psi.ArgumentList.Add(path);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ffprobe launch failed ({Path}); video height unknown.", ffprobePath);
            return null;
        }
        if (proc is null) return null;

        using (proc)
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                return null;
            }
            var output = (await stdoutTask.ConfigureAwait(false)).Trim();
            if (int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h > 0)
            {
                return h;
            }
            return null;
        }
    }

    /// <summary>
    /// Returns the codec name of the first video stream in <paramref name="path"/>
    /// (e.g. <c>"h264"</c>, <c>"hevc"</c>, <c>"av1"</c>), or <c>null</c> if
    /// ffprobe is missing, the file can't be opened, or has no video stream.
    /// Used to pick a matching encoder when re-encoding a clip.
    /// </summary>
    public static async Task<string?> GetVideoCodecNameAsync(
        string path,
        ILogger logger,
        CancellationToken ct,
        string ffprobePath = "ffprobe")
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams"); psi.ArgumentList.Add("v:0");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("stream=codec_name");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
        psi.ArgumentList.Add(path);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ffprobe launch failed ({Path}); video codec unknown.", ffprobePath);
            return null;
        }
        if (proc is null) return null;

        using (proc)
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                return null;
            }
            var output = (await stdoutTask.ConfigureAwait(false)).Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
    }
}
