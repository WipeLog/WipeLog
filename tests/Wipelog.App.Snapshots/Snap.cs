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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Wipelog.App.Snapshots;

/// <summary>
/// Writes what a headless window actually rendered to
/// <c>&lt;repo&gt;/snapshots/</c>: a PNG of the frame and a text dump of the
/// visual tree (bounds, visibility, text, and any system/fallback font resolution).
/// </summary>
public static class Snap
{
    // ~0.5 s of 60 Hz frames: enough for SukiUI's entrance transitions to
    // settle, since headless timers only advance when ticked.
    private const int SettleFrames = 30;

    public static string Directory { get; } = ResolveSnapshotDirectory();

    public static void All(Window window, string name)
    {
        Save(window, name);
        Dump(window, name);
    }

    public static string Save(Window window, string name)
    {
        Settle();
        var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                "CaptureRenderedFrame returned null — is UseHeadlessDrawing=false with UseSkia()?");
        var path = Path.Combine(Directory, name + ".png");
        frame.Save(path);
        return path;
    }

    public static string Dump(Window window, string name)
    {
        Settle();
        var sb = new StringBuilder();
        DumpVisual(window, 0, sb);
        var path = Path.Combine(Directory, name + ".txt");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    /// <summary>Flush layout, bindings, posted jobs and animation frames.</summary>
    public static void Settle()
    {
        for (int i = 0; i < SettleFrames; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Let async VM work finish (SQLite queries and event-sidecar loads
    /// complete on thread-pool continuations that post back to the UI
    /// thread), settling frames between rounds.
    /// </summary>
    public static async Task WaitAsync(int rounds = 6)
    {
        for (int i = 0; i < rounds; i++)
        {
            await Task.Delay(40);
            Settle();
        }
    }

    private static void DumpVisual(Visual visual, int depth, StringBuilder sb)
    {
        var b = visual.Bounds;
        sb.Append(' ', depth * 2)
          .Append(visual.GetType().Name);
        if (visual is StyledElement { Name: { Length: > 0 } name }) sb.Append(" #").Append(name);
        sb.Append(CultureInfo.InvariantCulture,
            $" [{Math.Round(b.X)},{Math.Round(b.Y)} {Math.Round(b.Width)}×{Math.Round(b.Height)}]");
        if (!visual.IsVisible) sb.Append(" hidden");

        var text = TextOf(visual);
        if (text is not null) sb.Append(" \"").Append(Escape(text)).Append('"');

        var font = FontNote(visual);
        if (font is not null) sb.Append(' ').Append(font);

        sb.Append('\n');
        foreach (var child in visual.GetVisualChildren())
            DumpVisual(child, depth + 1, sb);
    }

    private static string? TextOf(Visual visual) => visual switch
    {
        TextBlock tb when !string.IsNullOrEmpty(tb.Text) => tb.Text,
        TextBox tbx => tbx.Text ?? (tbx.Watermark is { } w ? $"(watermark) {w}" : null),
        ContentPresenter { Content: string s } => s,
        ContentControl { Content: string s } => s,
        ContentControl { Content: { } c } when c is not Visual => c.ToString(),
        _ => null,
    };

    // Only for text controls, and only when the font is NOT one of the
    // embedded avares:// families (SukiUI's Quicksand, Avalonia.Fonts.Inter)
    // or resolves to a different family than declared — makes system-font
    // dependence (e.g. FontFamily="monospace") and silent fallback visible.
    private static string? FontNote(Visual visual)
    {
        FontFamily family;
        Typeface typeface;
        switch (visual)
        {
            case TextBlock tb:
                family = tb.FontFamily;
                typeface = new Typeface(family, tb.FontStyle, tb.FontWeight, tb.FontStretch);
                break;
            case TextBox tbx:
                family = tbx.FontFamily;
                typeface = new Typeface(family, tbx.FontStyle, tbx.FontWeight, tbx.FontStretch);
                break;
            default:
                return null;
        }

        var resolved = FontManager.Current.TryGetGlyphTypeface(typeface, out var glyph)
            ? glyph.FamilyName
            : "(unresolved)";
        bool embedded = family.Key is not null || family.ToString().StartsWith("avares://", StringComparison.Ordinal);
        bool matches = family.FamilyNames.Any(n => resolved.StartsWith(n, StringComparison.OrdinalIgnoreCase));
        return embedded && matches ? null : $"font={family}→{resolved}";
    }

    private static string Escape(string s)
    {
        s = s.Replace("\r", "").Replace("\n", "⏎");
        return s.Length > 120 ? s[..117] + "..." : s;
    }

    private static string ResolveSnapshotDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wipelog.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate repo root (Wipelog.sln) above " + AppContext.BaseDirectory);
        var path = Path.Combine(dir.FullName, "snapshots");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
}
