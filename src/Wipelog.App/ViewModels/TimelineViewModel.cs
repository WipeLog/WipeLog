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

using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Wipelog.App.Services;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Holds the per-clip timeline data (events, recording window, recording
/// player) plus the visual toggles. Subscribes to <see cref="ClipListViewModel"/>
/// so the timeline reloads whenever the user picks a different clip.
/// </summary>
public sealed partial class TimelineViewModel : ViewModelBase
{
    private readonly ClipListViewModel _list;
    private readonly IClipEventsLoader _loader;
    private readonly DisplayOptionsViewModel _display;
    private readonly ILogger<TimelineViewModel> _logger;
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private IReadOnlyList<TimelineEvent>? _events;

    /// <summary>
    /// Length of the underlying video file in milliseconds — equal to the
    /// encounter duration plus <see cref="PreRollMs"/>. The scrub bar and
    /// tombstone strip use this as their X-axis denominator so positions map
    /// to actual video time rather than encounter-relative time.
    /// </summary>
    [ObservableProperty]
    private double _clipDurationMs;

    /// <summary>
    /// Pre-encounter footage prepended to the video, in milliseconds. Zero
    /// for clips recorded without <c>--preroll-seconds</c> or for older
    /// sidecars predating the field. Consumers add this to each event's
    /// <c>OffsetMs</c> to shift it from encounter-relative to video-relative.
    /// </summary>
    [ObservableProperty]
    private double _preRollMs;

    /// <summary>
    /// Post-encounter footage appended after the encounter end, in
    /// milliseconds. Zero for clips recorded without <c>--postroll-seconds</c>
    /// or for older sidecars predating the field. Consumers subtract this
    /// from <see cref="ClipDurationMs"/> to find the encounter's right edge in
    /// video time (used by the scrub bar to stop the encounter band at the
    /// boss-death moment instead of running through the post-roll tail).
    /// </summary>
    [ObservableProperty]
    private double _postRollMs;

    [ObservableProperty]
    private DateTimeOffset _recordingStartedAt;

    [ObservableProperty]
    private string? _recordingPlayerName;

    [ObservableProperty]
    private ContentType _contentType = ContentType.MythicPlus;

    public bool ShowAllDeaths => _display.Deaths == DeathsFilter.All;
    public bool ShowBossStarts => _display.EncountersVisible;

    public TimelineViewModel(
        ClipListViewModel list,
        IClipEventsLoader loader,
        DisplayOptionsViewModel display,
        ILogger<TimelineViewModel> logger)
    {
        _list = list;
        _loader = loader;
        _display = display;
        _logger = logger;
        _list.PropertyChanged += OnListPropertyChanged;
        _display.PropertyChanged += OnDisplayOptionsPropertyChanged;
    }

    private void OnDisplayOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplayOptionsViewModel.Deaths))
        {
            OnPropertyChanged(nameof(ShowAllDeaths));
        }
        if (e.PropertyName == nameof(DisplayOptionsViewModel.EncountersVisible))
        {
            OnPropertyChanged(nameof(ShowBossStarts));
        }
    }

    public bool HasAnyEvents => Events is { Count: > 0 };

    public bool IsEmpty => !HasAnyEvents;

    /// <summary>True once a clip is loaded — irrespective of whether it has events.</summary>
    public bool HasClip => ClipDurationMs > 0;

    /// <summary>True only when a clip is loaded AND its events list is empty/missing.</summary>
    public bool ShowEmptyState => HasClip && !HasAnyEvents;

    partial void OnEventsChanged(IReadOnlyList<TimelineEvent>? value)
    {
        OnPropertyChanged(nameof(HasAnyEvents));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnClipDurationMsChanged(double value)
    {
        OnPropertyChanged(nameof(HasClip));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipListViewModel.SelectedItem))
        {
            _ = LoadForSelectedAsync();
        }
    }

    private async Task LoadForSelectedAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        var selected = _list.SelectedItem;
        if (selected is null)
        {
            ApplyOnUi(null, 0, 0, 0, default, null, ContentType.MythicPlus);
            return;
        }

        var record = selected.Record;
        var fallbackDurationMs = record.DurationSeconds * 1000.0;
        var fallbackStart = record.StartedAt;
        var fallbackPlayer = record.PlayerName;
        var contentType = record.ContentType;

        try
        {
            var doc = await _loader.LoadAsync(record.VideoPath, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
            if (doc is null)
            {
                ApplyOnUi(null, fallbackDurationMs, 0, 0, fallbackStart, fallbackPlayer, contentType);
                return;
            }
            var encounterMs = (doc.RecordingEndedAt - doc.RecordingStartedAt).TotalMilliseconds;
            if (encounterMs <= 0) encounterMs = fallbackDurationMs;
            // PreRollSeconds shifts encounter T=0 to the right inside the video
            // file; total video duration is pre-roll + encounter + post-roll.
            var preRollMs = Math.Max(0, (doc.PreRollSeconds ?? 0) * 1000);
            var postRollMs = Math.Max(0, (doc.PostRollSeconds ?? 0) * 1000);
            ApplyOnUi(doc.Events, encounterMs + preRollMs + postRollMs, preRollMs, postRollMs,
                doc.RecordingStartedAt, fallbackPlayer, contentType);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load timeline for {Path}", record.VideoPath);
            ApplyOnUi(null, fallbackDurationMs, 0, 0, fallbackStart, fallbackPlayer, contentType);
        }
    }

    private void ApplyOnUi(
        IReadOnlyList<TimelineEvent>? events,
        double durationMs,
        double preRollMs,
        double postRollMs,
        DateTimeOffset startedAt,
        string? playerName,
        ContentType contentType)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply(events, durationMs, preRollMs, postRollMs, startedAt, playerName, contentType);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Apply(events, durationMs, preRollMs, postRollMs, startedAt, playerName, contentType));
        }
    }

    private void Apply(
        IReadOnlyList<TimelineEvent>? events,
        double durationMs,
        double preRollMs,
        double postRollMs,
        DateTimeOffset startedAt,
        string? playerName,
        ContentType contentType)
    {
        Events = events;
        ClipDurationMs = durationMs;
        PreRollMs = preRollMs;
        PostRollMs = postRollMs;
        RecordingStartedAt = startedAt;
        RecordingPlayerName = playerName;
        ContentType = contentType;
    }
}
