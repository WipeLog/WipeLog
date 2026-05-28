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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;

namespace Wipelog.App.Controls;

/// <summary>
/// Top half of the former <c>ScrubWithTombstones</c>: a fixed-height scrub
/// track with yellow encounter regions, a playhead line, and a draggable thumb.
/// Click or drag inside the bar to scrub — pointer events are constrained to
/// the control's own height so the sibling <see cref="TombstoneStrip"/> below
/// can own its own clicks without ambiguity.
/// </summary>
public sealed class ScrubBar : Control
{
    private const double BarHeight = 18;
    private const double TrackHeight = 14;
    private const double TrackTop = (BarHeight - TrackHeight) / 2;
    private const double TrackCornerRadius = 7;
    private const double ThumbRadius = 6;
    private const double ThumbOutlineWidth = 1;

    private const double ThumbHoverRadius = 8;

    private static readonly IBrush EncounterFillBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0x52, 0xF0, 0xB2, 0x32));
    private static readonly IBrush EncounterBorderBrush =
        new ImmutableSolidColorBrush(Color.FromArgb(0xB3, 0xF0, 0xB2, 0x32));
    // Faint vertical guide drawn at the pointer X while hovering.
    private static readonly IPen HoverGuidePen =
        new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0xF2, 0xF3, 0xF5)), 1);

    private IBrush? _baseBrush;
    private IBrush? _trackBrush;
    private IBrush? _accentBrush;

    static ScrubBar()
    {
        AffectsRender<ScrubBar>(
            EventsProperty,
            ClipDurationMsProperty,
            PreRollMsProperty,
            PostRollMsProperty,
            CurrentPositionMsProperty,
            ShowBossStartsProperty,
            ContentTypeProperty);
    }

    public ScrubBar()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        _baseBrush = _trackBrush = _accentBrush = null;
        InvalidateVisual();
    }

    [MemberNotNull(nameof(_baseBrush), nameof(_trackBrush), nameof(_accentBrush))]
    private void EnsureBrushes()
    {
        _baseBrush ??= ResolveBrush("BackgroundBaseBrush");
        _trackBrush ??= ResolveBrush("BackgroundElevatedBrush");
        _accentBrush ??= ResolveBrush("AccentPrimaryBrush");
    }

    private IBrush ResolveBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b
            ? b : Brushes.Magenta;

    // ----- Bindable properties (mirror TombstoneStrip's surface) -----

    public static readonly StyledProperty<IReadOnlyList<TimelineEvent>?> EventsProperty =
        AvaloniaProperty.Register<ScrubBar, IReadOnlyList<TimelineEvent>?>(nameof(Events));

    public static readonly StyledProperty<double> ClipDurationMsProperty =
        AvaloniaProperty.Register<ScrubBar, double>(nameof(ClipDurationMs));

    /// <summary>
    /// Pre-roll prefix in ms. Event <c>OffsetMs</c> values are encounter-relative;
    /// rendering and seek targets add this so encounter T=0 sits at the correct
    /// X in the video timebase. Zero for clips recorded without pre-roll.
    /// </summary>
    public static readonly StyledProperty<double> PreRollMsProperty =
        AvaloniaProperty.Register<ScrubBar, double>(nameof(PreRollMs));

    /// <summary>
    /// Post-encounter tail in ms. Encounter regions clip to
    /// <c>ClipDurationMs - PostRollMs</c> so the yellow band ends at the
    /// boss-death moment instead of running through the post-roll seconds.
    /// Zero for clips recorded without post-roll.
    /// </summary>
    public static readonly StyledProperty<double> PostRollMsProperty =
        AvaloniaProperty.Register<ScrubBar, double>(nameof(PostRollMs));

    public static readonly StyledProperty<double> CurrentPositionMsProperty =
        AvaloniaProperty.Register<ScrubBar, double>(nameof(CurrentPositionMs));

    public static readonly StyledProperty<bool> ShowBossStartsProperty =
        AvaloniaProperty.Register<ScrubBar, bool>(nameof(ShowBossStarts), defaultValue: true);

    public static readonly StyledProperty<ContentType> ContentTypeProperty =
        AvaloniaProperty.Register<ScrubBar, ContentType>(nameof(ContentType), ContentType.MythicPlus);

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

    public double PostRollMs
    {
        get => GetValue(PostRollMsProperty);
        set => SetValue(PostRollMsProperty, value);
    }

    public double CurrentPositionMs
    {
        get => GetValue(CurrentPositionMsProperty);
        set => SetValue(CurrentPositionMsProperty, value);
    }

    public bool ShowBossStarts
    {
        get => GetValue(ShowBossStartsProperty);
        set => SetValue(ShowBossStartsProperty, value);
    }

    public ContentType ContentType
    {
        get => GetValue(ContentTypeProperty);
        set => SetValue(ContentTypeProperty, value);
    }

    public event EventHandler<TombstoneSeekRequestedEventArgs>? SeekRequested;

    // ----- Measure -----

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : 600;
        return new Size(width, BarHeight);
    }

    // ----- Render -----

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        EnsureBrushes();

        // Track background.
        context.DrawRectangle(_trackBrush, null,
            new Rect(0, TrackTop, width, TrackHeight),
            TrackCornerRadius, TrackCornerRadius);

        // Encounter regions, derived on-render from the boss-start events
        // so the bar stays in sync with whatever clip is loaded.
        foreach (var (x, w) in BuildEncounterRegions(width))
        {
            var clamped = System.Math.Min(w, width - x);
            if (clamped <= 0) continue;
            var rect = new Rect(x, TrackTop, clamped, TrackHeight);
            context.FillRectangle(EncounterFillBrush, rect);
            var pen = new Pen(EncounterBorderBrush, 1);
            context.DrawLine(pen, new Point(x + 0.5, TrackTop),
                new Point(x + 0.5, TrackTop + TrackHeight));
            context.DrawLine(pen, new Point(x + clamped - 0.5, TrackTop),
                new Point(x + clamped - 0.5, TrackTop + TrackHeight));
        }

        var duration = ClipDurationMs;
        if (duration <= 0) return;

        var pos = System.Math.Clamp(CurrentPositionMs, 0, duration);
        var thumbX = pos / duration * width;
        var centerY = TrackTop + TrackHeight / 2;

        // Faint hover guide at the pointer, behind the playhead. Suppressed
        // while scrubbing so it doesn't compete with the active thumb.
        if (_isHovering && !_isScrubbing)
        {
            var hx = System.Math.Clamp(_hoverX, 0, width);
            context.DrawLine(HoverGuidePen, new Point(hx, 0), new Point(hx, BarHeight));
        }

        var playheadPen = new Pen(_accentBrush, 2) { LineCap = PenLineCap.Round };
        context.DrawLine(playheadPen, new Point(0, centerY), new Point(thumbX, centerY));

        var thumbRadius = _isHovering ? ThumbHoverRadius : ThumbRadius;
        context.DrawEllipse(_accentBrush, new Pen(_baseBrush, ThumbOutlineWidth),
            new Point(thumbX, centerY), thumbRadius, thumbRadius);
    }

    private IEnumerable<(double X, double Width)> BuildEncounterRegions(double width)
    {
        if (!ShowBossStarts) yield break;
        var duration = ClipDurationMs;
        if (duration <= 0) yield break;
        var preRollMs = System.Math.Max(0, PreRollMs);
        var postRollMs = System.Math.Max(0, PostRollMs);
        // The encounter window lives at [preRollMs, duration - postRollMs] in
        // video time. Event offsets are encounter-relative so we add preRollMs
        // before projecting to X. Clipping at duration-postRoll is what stops
        // the yellow band from running through the post-encounter tail.
        var encounterEndMs = System.Math.Max(preRollMs, duration - postRollMs);

        var starts = new List<BossStartEvent>();
        var ends = new List<BossEndEvent>();
        if (Events is { Count: > 0 } evts)
        {
            foreach (var e in evts)
            {
                if (e is BossStartEvent b) starts.Add(b);
                else if (e is BossEndEvent be) ends.Add(be);
            }
            starts.Sort((a, b) => a.OffsetMs.CompareTo(b.OffsetMs));
            ends.Sort((a, b) => a.OffsetMs.CompareTo(b.OffsetMs));
        }

        if (starts.Count > 0)
        {
            var endUsed = new bool[ends.Count];
            for (int i = 0; i < starts.Count; i++)
            {
                var s = starts[i];
                // Prefer the first unclaimed BossEnd with the same encounter
                // ID at or after this start. Falls back to next start, then
                // recording end — covers a missing end (key abandoned, or the
                // recording-stop boss whose end we strip in the buffer).
                double endOffsetMs = -1;
                for (int j = 0; j < ends.Count; j++)
                {
                    if (endUsed[j]) continue;
                    var e = ends[j];
                    if (e.OffsetMs < s.OffsetMs) continue;
                    if (e.EncounterId != s.EncounterId) continue;
                    endUsed[j] = true;
                    endOffsetMs = e.OffsetMs;
                    break;
                }
                if (endOffsetMs < 0)
                {
                    endOffsetMs = i + 1 < starts.Count
                        ? starts[i + 1].OffsetMs
                        : encounterEndMs - preRollMs;
                }
                var startX = (s.OffsetMs + preRollMs) / duration * width;
                var endX = (endOffsetMs + preRollMs) / duration * width;
                yield return (startX, System.Math.Max(0, endX - startX));
            }
        }
        else if (ContentType == ContentType.RaidEncounter)
        {
            // Raid clips with no explicit boss starts: the encounter spans
            // [preRoll, duration - postRoll] in video time. The triggering
            // EncounterStart/End sit at the encounter boundaries and are
            // filtered out by the buffer; post-roll is appended after them.
            var startX = preRollMs / duration * width;
            var endX = encounterEndMs / duration * width;
            yield return (startX, System.Math.Max(0, endX - startX));
        }
    }

    // ----- Input -----

    private bool _isScrubbing;
    private bool _isHovering;
    private double _hoverX;  // control-local px; only meaningful while hovering

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isHovering = true;
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bounds.Width <= 0 || ClipDurationMs <= 0) return;
        _isScrubbing = true;
        e.Pointer.Capture(this);
        RaiseSeekTo(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Bounds.Width <= 0 || ClipDurationMs <= 0) return;
        var x = e.GetPosition(this).X;
        if (_isScrubbing)
        {
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
            RaiseSeekTo(x);
        }
        else
        {
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
            // Repaint the hover guide as the pointer moves (≥1px to avoid churn).
            if (_isHovering && System.Math.Abs(x - _hoverX) >= 1)
            {
                _hoverX = x;
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isScrubbing)
        {
            _isScrubbing = false;
            e.Pointer.Capture(null);
            Cursor = Cursor.Default;
            e.Handled = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isScrubbing) Cursor = Cursor.Default;
        _isHovering = false;
        InvalidateVisual();
    }

    private void RaiseSeekTo(double x)
    {
        var width = Bounds.Width;
        var duration = ClipDurationMs;
        if (width <= 0 || duration <= 0) return;
        var clamped = System.Math.Clamp(x, 0, width);
        var targetMs = clamped / width * duration;
        SeekRequested?.Invoke(this,
            new TombstoneSeekRequestedEventArgs(TimeSpan.FromMilliseconds(targetMs)));
    }
}

/// <summary>Seek target raised by <see cref="ScrubBar"/> on click / drag.</summary>
public sealed record TombstoneSeekRequestedEventArgs(TimeSpan Target);
