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
using System.Text.RegularExpressions;

namespace Wipelog.Core.Recording;

/// <summary>
/// Helpers around <c>xrandr --listmonitors</c>. Unlike gpu-screen-recorder's
/// <c>--list-monitors</c> (name + size only), xrandr also reports each monitor's
/// pixel <i>offset</i> within the X11 root window — which <c>x11grab</c> needs to
/// crop a single monitor out of a multi-head desktop for the "Test mask" preview.
///
/// On X11 the names match gsr's: both use the RandR connector name (e.g.
/// <c>DisplayPort-0</c>), so a <c>captureTarget</c> picked from the gsr list can
/// be matched here by name. Each entry line has the shape
/// <c>" 0: +*DisplayPort-0 2560/598x1440/336+0+0  DisplayPort-0"</c>: an index,
/// flags+primary-name, a <c>W/mmWxH/mmH+Xoff+Yoff</c> geometry, then the
/// connector name (which we use as the canonical name).
/// </summary>
public static class XrandrMonitorList
{
    public sealed record Monitor(string Name, int Width, int Height, int X, int Y);

    // index: flags+name  W/mmWxH/mmH+Xoff+Yoff  connector
    private static readonly Regex LineRegex = new(
        @"^\s*\d+:\s+\S+\s+(\d+)/\d+x(\d+)/\d+([+-]\d+)([+-]\d+)\s+(\S+)\s*$",
        RegexOptions.Compiled);

    public static async Task<IReadOnlyList<Monitor>> QueryAsync(
        CancellationToken ct, TimeSpan? timeout = null, string executablePath = "xrandr")
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--listmonitors");

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Process.Start returned null for '{executablePath} --listmonitors'.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(
                $"'{executablePath} --listmonitors' did not exit within the timeout.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        return Parse(stdout);
    }

    public static IReadOnlyList<Monitor> Parse(string output)
    {
        var monitors = new List<Monitor>();
        foreach (var raw in output.Split('\n'))
        {
            var m = LineRegex.Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)
                || w <= 0) continue;
            if (!int.TryParse(m.Groups[2].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
                || h <= 0) continue;
            // +Xoff/+Yoff carry an explicit sign; AllowLeadingSign handles it.
            if (!int.TryParse(m.Groups[3].ValueSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!int.TryParse(m.Groups[4].ValueSpan, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y))
                continue;
            monitors.Add(new Monitor(m.Groups[5].Value, w, h, x, y));
        }
        return monitors;
    }

    /// <summary>
    /// Pick the monitor with the largest pixel area, ties broken by the
    /// lexicographically-first name (so the choice is deterministic). Returns
    /// null when the list is empty. Mirrors gsr's <c>focused</c> default, which
    /// captures the largest monitor when none is named.
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
