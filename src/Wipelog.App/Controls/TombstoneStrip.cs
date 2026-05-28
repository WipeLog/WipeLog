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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;

namespace Wipelog.App.Controls;

/// <summary>
/// Bottom half of the former <c>ScrubWithTombstones</c>: a fixed-height row
/// showing boss-name labels on top and 🪦 death glyphs (or "+N" overflow
/// badges) at the bottom. Lives directly beneath a <see cref="ScrubBar"/> so
/// its X axis lines up with the bar's. Clicks/badges/labels each raise a
/// <see cref="TombstoneMarkerClickedEventArgs"/> the parent translates into a
/// seek + (optional) pause.
/// </summary>
public sealed class TombstoneStrip : Control
{
    private const double TotalHeight = 24;
    private const double BossLabelY = 0;
    private const double BossLabelFontSize = 9;
    private const double BossLabelHeight = 11;
    private const double BossCharWidthEstimate = 5.0;

    private const double TombstoneCenterY = 17;
    private const double TombstoneFontSize = 13;
    private const double TombstoneGlyphHalfWidth = 6;

    private const double BadgeWidth = 22;
    private const double BadgeHeight = 12;
    private const double BadgeFontSize = 9;
    private const double BadgeCornerRadius = 6;

    private static readonly IBrush BossLabelBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0xD9, 0xF0, 0xB2, 0x32));

    private static readonly Typeface BodyTypeface = new("Inter");
    private static readonly Typeface SemiBoldTypeface = new("Inter", weight: FontWeight.SemiBold);

    private IBrush? _dangerBrush;
    private IBrush? _badgeBackgroundBrush;

    static TombstoneStrip()
    {
        AffectsRender<TombstoneStrip>(
            EventsProperty,
            ClipDurationMsProperty,
            PreRollMsProperty,
            RecordingPlayerNameProperty,
            DeathsFilterProperty,
            ShowBossStartsProperty,
            RecordingStartedAtProperty);
    }

    public TombstoneStrip()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        _dangerBrush = null;
        _badgeBackgroundBrush = null;
        InvalidateVisual();
    }

    [MemberNotNull(nameof(_dangerBrush), nameof(_badgeBackgroundBrush))]
    private void EnsureBrushes()
    {
        _dangerBrush ??= ResolveBrush("DangerBrush");
        _badgeBackgroundBrush ??= ResolveBrush("DangerBadgeBackgroundBrush");
    }

    private IBrush ResolveBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b
            ? b : Brushes.Magenta;

    /// <summary>
    /// Bakes the alpha factor into the color so FormattedText respects it —
    /// rendering text with an Opacity'd <see cref="SolidColorBrush"/> doesn't.
    /// </summary>
    private static IBrush DimmedBrush(IBrush source, double alpha)
    {
        if (source is ISolidColorBrush solid)
        {
            var c = solid.Color;
            var a = (byte)System.Math.Clamp(c.A * alpha, 0, 255);
            return new ImmutableSolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }
        return source;
    }

    // ----- Bindable properties -----

    public static readonly StyledProperty<IReadOnlyList<TimelineEvent>?> EventsProperty =
        AvaloniaProperty.Register<TombstoneStrip, IReadOnlyList<TimelineEvent>?>(nameof(Events));

    public static readonly StyledProperty<double> ClipDurationMsProperty =
        AvaloniaProperty.Register<TombstoneStrip, double>(nameof(ClipDurationMs));

    /// <summary>
    /// Pre-roll prefix in ms. Death and boss-start markers are encounter-relative;
    /// adding this shifts them rightward to their position in the underlying
    /// video file. Zero for clips recorded without pre-roll.
    /// </summary>
    public static readonly StyledProperty<double> PreRollMsProperty =
        AvaloniaProperty.Register<TombstoneStrip, double>(nameof(PreRollMs));

    public static readonly StyledProperty<string?> RecordingPlayerNameProperty =
        AvaloniaProperty.Register<TombstoneStrip, string?>(nameof(RecordingPlayerName));

    public static readonly StyledProperty<DeathsFilter> DeathsFilterProperty =
        AvaloniaProperty.Register<TombstoneStrip, DeathsFilter>(nameof(DeathsFilter), DeathsFilter.All);

    public static readonly StyledProperty<bool> ShowBossStartsProperty =
        AvaloniaProperty.Register<TombstoneStrip, bool>(nameof(ShowBossStarts), defaultValue: true);

    public static readonly StyledProperty<DateTimeOffset> RecordingStartedAtProperty =
        AvaloniaProperty.Register<TombstoneStrip, DateTimeOffset>(nameof(RecordingStartedAt));

    public IReadOnlyList<TimelineEvent>? Events
    {
        get => GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public double ClipDurationMs
    {
        get => GetValue(ClipDurationMsProperty);
        set => SetValue(ClipDurationMsProperty, value);
    }

    public double PreRollMs
    {
        get => GetValue(PreRollMsProperty);
        set => SetValue(PreRollMsProperty, value);
    }

    public string? RecordingPlayerName
    {
        get => GetValue(RecordingPlayerNameProperty);
        set => SetValue(RecordingPlayerNameProperty, value);
    }

    public DeathsFilter DeathsFilter
    {
        get => GetValue(DeathsFilterProperty);
        set => SetValue(DeathsFilterProperty, value);
    }

    public bool ShowBossStarts
    {
        get => GetValue(ShowBossStartsProperty);
        set => SetValue(ShowBossStartsProperty, value);
    }

    public DateTimeOffset RecordingStartedAt
    {
        get => GetValue(RecordingStartedAtProperty);
        set => SetValue(RecordingStartedAtProperty, value);
    }

    public event EventHandler<TombstoneMarkerClickedEventArgs>? MarkerClicked;

    // ----- Measure -----

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : 600;
        return new Size(width, TotalHeight);
    }

    // ----- Render -----

    private RenderLayout? _layout;

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        EnsureBrushes();

        var layout = BuildLayout(width);
        _layout = layout;

        // Boss labels along the top edge.
        foreach (var b in layout.Bosses)
        {
            if (string.IsNullOrEmpty(b.VisibleLabel)) continue;
            var ft = new FormattedText(
                b.VisibleLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                SemiBoldTypeface, BossLabelFontSize, BossLabelBrush);
            var labelX = b.X + 2;
            if (labelX + ft.Width > width) labelX = System.Math.Max(0, width - ft.Width);
            context.DrawText(ft, new Point(labelX, BossLabelY));
        }

        // Tombstone glyphs.
        foreach (var v in layout.Tombstones.Visibles)
        {
            var d = layout.Deaths[v.Index];
            var brush = d.IsOwn ? _dangerBrush! : DimmedBrush(_dangerBrush!, 0.55);
            var ft = new FormattedText("🪦", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, BodyTypeface, TombstoneFontSize, brush);
            var x = v.X - ft.Width / 2;
            x = System.Math.Clamp(x, 0, System.Math.Max(0, width - ft.Width));
            var y = TombstoneCenterY - ft.Height / 2;
            context.DrawText(ft, new Point(x, y));
        }

        // "+N" overflow badges.
        foreach (var badge in layout.Tombstones.Badges)
        {
            var badgeX = System.Math.Clamp(
                badge.X - BadgeWidth / 2, 0, System.Math.Max(0, width - BadgeWidth));
            var badgeY = TombstoneCenterY - BadgeHeight / 2;
            var rect = new Rect(badgeX, badgeY, BadgeWidth, BadgeHeight);
            context.DrawRectangle(_badgeBackgroundBrush, null, rect,
                BadgeCornerRadius, BadgeCornerRadius);

            var ft = new FormattedText($"+{badge.Count}", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, SemiBoldTypeface, BadgeFontSize, _dangerBrush!);
            context.DrawText(ft, new Point(
                badgeX + (BadgeWidth - ft.Width) / 2,
                badgeY + (BadgeHeight - ft.Height) / 2));
        }
    }

    private RenderLayout BuildLayout(double width)
    {
        var duration = ClipDurationMs;
        var preRollMs = System.Math.Max(0, PreRollMs);
        var folded = StringNormalization.FoldForCompare(RecordingPlayerName);
        var deaths = new List<DeathPos>();
        var bosses = new List<BossPos>();

        if (Events is { Count: > 0 } evts && duration > 0)
        {
            var deathEvents = new List<PlayerDeathEvent>();
            var bossEvents = new List<BossStartEvent>();
            foreach (var ev in evts)
            {
                switch (ev)
                {
                    case PlayerDeathEvent d:
                        if (DeathsFilter == DeathsFilter.None) break;
                        if (DeathsFilter == DeathsFilter.All ||
                            (DeathsFilter == DeathsFilter.Own && IsRecordingPlayer(d.PlayerName, folded)))
                        {
                            deathEvents.Add(d);
                        }
                        break;
                    case BossStartEvent b:
                        bossEvents.Add(b);
                        break;
                }
            }
            deathEvents.Sort((a, b) => a.OffsetMs.CompareTo(b.OffsetMs));
            bossEvents.Sort((a, b) => a.OffsetMs.CompareTo(b.OffsetMs));

            foreach (var d in deathEvents)
            {
                var x = (d.OffsetMs + preRollMs) / duration * width;
                deaths.Add(new DeathPos(d, x, IsRecordingPlayer(d.PlayerName, folded)));
            }

            if (ShowBossStarts)
            {
                // Project event offsets into video-time before handing them
                // to BossLabelLayout — the label-assignment algorithm picks
                // visible labels based on their X positions, and those need
                // to match where we ultimately draw them.
                var labels = BossLabelLayout.AssignVisibleLabels(
                    bossEvents.Select(b => ((int)(b.OffsetMs + preRollMs),
                                            Name: b.EncounterName ?? string.Empty)).ToArray(),
                    width, duration, BossCharWidthEstimate);
                for (int i = 0; i < bossEvents.Count; i++)
                {
                    var b = bossEvents[i];
                    var x = (b.OffsetMs + preRollMs) / duration * width;
                    bosses.Add(new BossPos(b, x, labels[i]));
                }
            }
        }

        var tombstones = TombstoneRowLayout.Layout(
            deaths.Select(d => d.X).ToList(), width);
        return new RenderLayout(width, deaths, bosses, tombstones);
    }

    private static bool IsRecordingPlayer(string eventPlayerName, string foldedRecordingName)
    {
        if (string.IsNullOrEmpty(foldedRecordingName)) return false;
        if (string.IsNullOrEmpty(eventPlayerName)) return false;
        var full = StringNormalization.FoldForCompare(eventPlayerName);
        if (full == foldedRecordingName) return true;
        var dash = eventPlayerName.IndexOf('-');
        if (dash > 0)
        {
            var bare = StringNormalization.FoldForCompare(eventPlayerName[..dash]);
            if (bare == foldedRecordingName) return true;
        }
        return false;
    }

    // ----- Input -----

    private object? _hoveredTooltipKey;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width <= 0 || _layout is not { } layout) return;
        var point = e.GetPosition(this);

        foreach (var badge in layout.Tombstones.Badges)
        {
            if (BadgeRect(badge, layout.Width).Contains(point))
            {
                var first = layout.Deaths[badge.CollapsedIndices[0]].Event;
                RaiseMarkerClicked(first);
                e.Handled = true;
                return;
            }
        }
        foreach (var v in layout.Tombstones.Visibles)
        {
            if (GlyphRect(v.X).Contains(point))
            {
                RaiseMarkerClicked(layout.Deaths[v.Index].Event);
                e.Handled = true;
                return;
            }
        }
        foreach (var b in layout.Bosses)
        {
            if (BossLabelRect(b, layout.Width).Contains(point))
            {
                RaiseMarkerClicked(b.Event);
                e.Handled = true;
                return;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Bounds.Width <= 0 || _layout is not { } layout)
        {
            ClearTooltip();
            return;
        }
        var point = e.GetPosition(this);

        foreach (var badge in layout.Tombstones.Badges)
        {
            if (BadgeRect(badge, layout.Width).Contains(point))
            {
                SetTooltipText(badge,
                    FormatClusterTooltip(badge, layout.Deaths, RecordingStartedAt));
                Cursor = new Cursor(StandardCursorType.Hand);
                return;
            }
        }
        foreach (var v in layout.Tombstones.Visibles)
        {
            if (GlyphRect(v.X).Contains(point))
            {
                var ev = layout.Deaths[v.Index].Event;
                SetTooltipText(ev, TimelineTooltipFormatter.Format(ev, RecordingStartedAt));
                Cursor = new Cursor(StandardCursorType.Hand);
                return;
            }
        }
        foreach (var b in layout.Bosses)
        {
            if (BossLabelRect(b, layout.Width).Contains(point))
            {
                SetTooltipText(b.Event,
                    TimelineTooltipFormatter.Format(b.Event, RecordingStartedAt));
                Cursor = new Cursor(StandardCursorType.Hand);
                return;
            }
        }

        ClearTooltip();
        Cursor = Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearTooltip();
        Cursor = Cursor.Default;
    }

    private void RaiseMarkerClicked(TimelineEvent ev)
    {
        // OffsetMs is encounter-relative; the player VM speaks video-time, so
        // shift by PreRollMs before emitting. The 5s tombstone rewind is
        // applied to the video-time target (clamped at 0 to avoid seeking into
        // negative space for events near the start of the pre-roll prefix).
        var preRollMs = System.Math.Max(0, PreRollMs);
        var videoMs = ev.OffsetMs + preRollMs;
        var seekMs = ev switch
        {
            BossStartEvent => videoMs,                          // jump to encounter start, no pause
            _ => System.Math.Max(0, videoMs - 5000),            // tombstone: rewind 5s
        };
        MarkerClicked?.Invoke(this,
            new TombstoneMarkerClickedEventArgs(ev, TimeSpan.FromMilliseconds(seekMs)));
    }

    private void SetTooltipText(object key, string text)
    {
        if (ReferenceEquals(_hoveredTooltipKey, key)) return;
        _hoveredTooltipKey = key;
        ToolTip.SetTip(this, text);
    }

    private void ClearTooltip()
    {
        if (_hoveredTooltipKey is null) return;
        _hoveredTooltipKey = null;
        ToolTip.SetTip(this, null);
    }

    private static Rect GlyphRect(double x) => new(
        x - TombstoneGlyphHalfWidth, 0, TombstoneGlyphHalfWidth * 2, TotalHeight);

    private static Rect BadgeRect(TombstoneRowLayout.Badge badge, double width)
    {
        var x = System.Math.Clamp(
            badge.X - BadgeWidth / 2, 0, System.Math.Max(0, width - BadgeWidth));
        var y = TombstoneCenterY - BadgeHeight / 2;
        return new Rect(x, y, BadgeWidth, BadgeHeight);
    }

    private static Rect BossLabelRect(BossPos b, double width)
    {
        // Empty-label boss still needs a clickable hit region around the bar X.
        var labelWidth = string.IsNullOrEmpty(b.VisibleLabel)
            ? 16
            : System.Math.Min(b.VisibleLabel.Length * BossCharWidthEstimate + 4, width - b.X);
        var x = System.Math.Min(b.X, System.Math.Max(0, width - labelWidth));
        return new Rect(x, BossLabelY - 1, labelWidth, BossLabelHeight + 2);
    }

    private static string FormatClusterTooltip(
        TombstoneRowLayout.Badge badge,
        IReadOnlyList<DeathPos> deaths,
        DateTimeOffset recordingStartedAt)
    {
        var lines = badge.CollapsedIndices
            .Select(i => TimelineTooltipFormatter.Format(deaths[i].Event, recordingStartedAt));
        return string.Join("\n\n", lines);
    }

    // ----- Layout records -----

    private sealed record RenderLayout(
        double Width,
        IReadOnlyList<DeathPos> Deaths,
        IReadOnlyList<BossPos> Bosses,
        TombstoneRowLayout.Result Tombstones);

    private sealed record DeathPos(PlayerDeathEvent Event, double X, bool IsOwn);

    private sealed record BossPos(BossStartEvent Event, double X, string VisibleLabel);
}

/// <summary>Marker click raised by <see cref="TombstoneStrip"/> for a death,
/// overflow badge, or boss label.</summary>
public sealed record TombstoneMarkerClickedEventArgs(TimelineEvent Event, TimeSpan SeekTarget);
