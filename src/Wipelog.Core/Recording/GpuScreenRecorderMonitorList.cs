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

namespace Wipelog.Core.Recording;

/// <summary>
/// Helpers around <c>gpu-screen-recorder --list-monitors</c>. Output format
/// is one monitor per line as <c>NAME|WxH</c>, e.g. <c>DisplayPort-0|2560x1440</c>.
/// Used to fill in <c>-s WxH</c> automatically when the user picked
/// <c>-w focused</c> (which gsr rejects without an explicit capture size).
/// </summary>
public static class GpuScreenRecorderMonitorList
{
    public sealed record Monitor(string Name, int Width, int Height);

    public static async Task<IReadOnlyList<Monitor>> QueryAsync(
        string executablePath, CancellationToken ct, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--list-monitors");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Process.Start returned null for '{executablePath} --list-monitors'.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

        // ReadToEndAsync must run before WaitForExit on processes that may
        // produce more than a pipe-buffer of output, otherwise the child
        // blocks on write. gsr's monitor list is tiny so either order works,
        // but keep the safe ordering.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(
                $"'{executablePath} --list-monitors' did not exit within the timeout.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        return Parse(stdout);
    }

    public static IReadOnlyList<Monitor> Parse(string output)
    {
        var monitors = new List<Monitor>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var bar = line.IndexOf('|');
            if (bar <= 0 || bar == line.Length - 1) continue;
            var name = line[..bar];
            var size = line[(bar + 1)..];
            var x = size.IndexOf('x');
            if (x <= 0 || x == size.Length - 1) continue;
            if (!int.TryParse(size[..x], out var w) || w <= 0) continue;
            if (!int.TryParse(size[(x + 1)..], out var h) || h <= 0) continue;
            monitors.Add(new Monitor(name, w, h));
        }
        return monitors;
    }

    /// <summary>
    /// Pick the monitor with the largest pixel area, ties broken by the
    /// lexicographically-first name (so the choice is deterministic).
    /// Returns null when the list is empty.
    /// </summary>
    public static Monitor? PickLargest(IReadOnlyList<Monitor> monitors)
    {
        Monitor? best = null;
        long bestArea = -1;
        foreach (var m in monitors)
        {
            long area = (long)m.Width * m.Height;
            if (area > bestArea ||
                (area == bestArea && best is not null &&
                 string.CompareOrdinal(m.Name, best.Name) < 0))
            {
                best = m;
                bestArea = area;
            }
        }
        return best;
    }
}
