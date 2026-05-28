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
using System.Text;
using Microsoft.Extensions.Logging;

namespace Wipelog.Core.Recording;

/// <summary>
/// Thin wrapper around <c>ffmpeg -f concat -c copy</c> for stitching the
/// pre-roll snippet onto the front of a finalized clip without re-encoding.
/// Stream copy requires both inputs to share codec parameters; both come from
/// the same gpu-screen-recorder config so that condition holds in normal use.
/// We pass <c>-map 0</c> so that multi-track audio (one mp4 audio track per
/// <c>-a</c> source) survives the concat — ffmpeg's default stream selection
/// keeps only one audio track, which silently drops Spotify/Discord/etc.
/// when the user has the split-by-app preset configured.
/// </summary>
public static class FfmpegConcat
{
    /// <summary>
    /// Concatenates <paramref name="inputs"/> in order into
    /// <paramref name="outputPath"/>. Returns <c>true</c> on success.
    /// Returns <c>false</c> (and logs) if ffmpeg is missing, returns a
    /// non-zero exit code, or fewer than two inputs are supplied — the
    /// caller is expected to keep the un-concatenated main file in that case.
    /// When <paramref name="audioTrackTitles"/> is supplied, ffmpeg stamps
    /// <c>title=</c> metadata onto each audio stream in matching order so
    /// vanilla players like VLC show meaningful names instead of "Track N".
    /// </summary>
    public static async Task<bool> ConcatAsync(
        IReadOnlyList<string> inputs,
        string outputPath,
        ILogger logger,
        CancellationToken ct,
        string ffmpegPath = "ffmpeg",
        IReadOnlyList<string>? audioTrackTitles = null)
    {
        if (inputs is null || inputs.Count < 2)
        {
            logger.LogDebug("FfmpegConcat called with {Count} inputs; skipping.", inputs?.Count ?? 0);
            return false;
        }
        foreach (var input in inputs)
        {
            if (!File.Exists(input))
            {
                logger.LogWarning("FfmpegConcat input missing: {Path}", input);
                return false;
            }
        }

        var listPath = outputPath + ".concat.txt";
        try
        {
            var sb = new StringBuilder();
            foreach (var input in inputs)
            {
                // ffmpeg's concat demuxer reads `file '<path>'` lines; escape
                // any embedded single quotes by closing/reopening the quoted
                // string ('\'' style).
                sb.Append("file '").Append(input.Replace("'", "'\\''")).AppendLine("'");
            }
            await File.WriteAllTextAsync(listPath, sb.ToString(), ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("concat");
            psi.ArgumentList.Add("-safe"); psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(listPath);
            psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("copy");
            if (audioTrackTitles is { Count: > 0 })
            {
                // mp4 stores stream titles in the `udta.name` atom (VLC, mpv,
                // and Quick Look all read this as the track label). ffmpeg's
                // mp4 muxer only emits non-standard per-stream metadata when
                // `-movflags use_metadata_tags` is set; without it the
                // -metadata:s:a:N entries are silently dropped during write.
                psi.ArgumentList.Add("-movflags"); psi.ArgumentList.Add("use_metadata_tags");
                for (int i = 0; i < audioTrackTitles.Count; i++)
                {
                    var title = audioTrackTitles[i];
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    psi.ArgumentList.Add($"-metadata:s:a:{i}");
                    psi.ArgumentList.Add($"title={title}");
                }
            }
            psi.ArgumentList.Add(outputPath);

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to launch ffmpeg ({Path}); leaving clip without pre-roll. " +
                    "Install ffmpeg via your package manager to enable pre-roll concat.",
                    ffmpegPath);
                return false;
            }
            if (proc is null)
            {
                logger.LogWarning("Process.Start returned null for ffmpeg path '{Path}'.", ffmpegPath);
                return false;
            }

            using (proc)
            {
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                if (proc.ExitCode == 0)
                {
                    return true;
                }
                var stderr = await stderrTask.ConfigureAwait(false);
                logger.LogWarning(
                    "ffmpeg concat exited with code {Code}. stderr:\n{Stderr}",
                    proc.ExitCode, stderr);
                return false;
            }
        }
        finally
        {
            try { File.Delete(listPath); } catch { /* best-effort */ }
        }
    }
}
