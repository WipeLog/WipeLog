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

using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using Wipelog.App.Services;
using Wipelog.Core.Preferences;
using Wipelog.Core.Recording;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

public sealed partial class ClipListViewModel : ViewModelBase
{
    private readonly IClipRepository _repo;
    private readonly ClipFiltersViewModel _filters;
    private readonly IDialogService _dialogs;
    private readonly IClipArchiver _archiver;
    private readonly UserPreferencesService _prefs;
    private readonly ISukiToastManager _toasts;
    private readonly ILogger<ClipListViewModel> _logger;

    public ObservableCollection<ClipListItemViewModel> Items { get; } = new();

    /// <summary>
    /// Multi-selection mirror. The view's code-behind keeps this in sync with
    /// the DataGrid's native SelectedItems on every SelectionChanged. Used by
    /// the bulk-action commands; <see cref="SelectedItem"/> remains the
    /// single-selection anchor for the details panel and player.
    /// </summary>
    public ObservableCollection<ClipListItemViewModel> SelectedItems { get; } = new();

    public int SelectedCount => SelectedItems.Count;
    public bool HasSelection => SelectedItems.Count > 0;

    [ObservableProperty]
    private ClipListItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "";

    public ClipListViewModel(
        IClipRepository repo,
        ClipFiltersViewModel filters,
        IDialogService dialogs,
        IClipArchiver archiver,
        UserPreferencesService prefs,
        ISukiToastManager toasts,
        ILogger<ClipListViewModel> logger)
    {
        _repo = repo;
        _filters = filters;
        _dialogs = dialogs;
        _archiver = archiver;
        _prefs = prefs;
        _toasts = toasts;
        _logger = logger;
        _filters.FiltersChanged += async (_, _) => await RefreshAsync();
        SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
        };
    }

    public Task InitializeAsync() => RefreshAsync();

    /// <summary>
    /// Set while <see cref="SelectPrevious"/> / <see cref="SelectNext"/> are
    /// changing <see cref="SelectedItem"/>, so the player can distinguish
    /// "user is cursoring through the list with the keyboard" (don't load) from
    /// a mouse click / explicit Enter (do load). PropertyChanged fires
    /// synchronously, so the flag is observable for the duration of the
    /// notification.
    /// </summary>
    public bool IsKeyboardNavigating { get; private set; }

    public void SelectPrevious()
    {
        if (Items.Count == 0) return;
        var idx = SelectedItem is null ? 0 : Items.IndexOf(SelectedItem) - 1;
        if (idx < 0) idx = 0;
        IsKeyboardNavigating = true;
        try { SelectedItem = Items[idx]; }
        finally { IsKeyboardNavigating = false; }
    }

    public void SelectNext()
    {
        if (Items.Count == 0) return;
        var idx = SelectedItem is null ? 0 : Items.IndexOf(SelectedItem) + 1;
        if (idx >= Items.Count) idx = Items.Count - 1;
        IsKeyboardNavigating = true;
        try { SelectedItem = Items[idx]; }
        finally { IsKeyboardNavigating = false; }
    }

    [RelayCommand]
    private void ClearSelection() => SelectedItems.Clear();

    [RelayCommand]
    private async Task ToggleFavoriteSelectedAsync()
    {
        var sel = SelectedItem;
        if (sel is null) return;
        var newValue = !sel.IsFavorite;
        try
        {
            await _repo.SetFavoriteAsync(sel.Id, newValue, CancellationToken.None);
            sel.Refresh(sel.Record with { Favorite = newValue }, sel.Tags);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggle favorite failed for clip {Id}", sel.Id);
        }
    }

    [RelayCommand]
    private async Task DeleteSingleAsync()
    {
        var sel = SelectedItem;
        if (sel is null) return;
        var confirmed = await _dialogs.ConfirmAsync(
            "Delete clip?",
            $"Delete \"{sel.Record.DisplayName}\" and its files? This cannot be undone.");
        if (!confirmed) return;
        try
        {
            await _repo.DeleteClipAsync(sel.Id, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete failed for clip {Id}", sel.Id);
        }
    }

    /// <summary>
    /// Produces a masked copy of the selected clip (chat region blacked out) as a
    /// new <see cref="Wipelog.Core.Detection.ContentType.Manual"/> clip in the
    /// "Clips" tab — a shareable version for e.g. submitting a pull to a raid
    /// leader. The original clip is never touched. The timeline (.events.json) is
    /// copied alongside so death/boss markers still show.
    /// </summary>
    [RelayCommand]
    private async Task CreateMaskedCopyAsync()
    {
        var sel = SelectedItem;
        if (sel is null) return;

        var overlay = _prefs.Load().ChatOverlay with { Enabled = true };
        if (overlay.Width <= 0 || overlay.Height <= 0)
        {
            await _dialogs.ConfirmAsync(
                "Set the chat region first",
                "Configure the mask region under Settings → Recording → CHAT OVERLAY " +
                "(use Test mask to line it up), then try again.");
            return;
        }

        var src = sel.Record.VideoPath;
        if (!File.Exists(src))
        {
            Toast(NotificationType.Error, "Clip missing", "The source video file no longer exists.");
            return;
        }

        // Masked copy lands next to the original (same watched output dir).
        var dir = Path.GetDirectoryName(src)!;
        var stem = Path.GetFileNameWithoutExtension(src);
        var ext = Path.GetExtension(src);
        var maskedPath = Path.Combine(dir, $"{stem}.masked{ext}");
        for (int n = 1; File.Exists(maskedPath); n++)
        {
            maskedPath = Path.Combine(dir, $"{stem}.masked-{n}{ext}");
        }

        bool ok;
        try
        {
            ok = await _dialogs.ShowProgressAsync<bool>(
                "Masking clip…",
                async (progress, ct) =>
                {
                    progress.Report(new ProgressUpdate("Re-encoding with the chat region masked…", 0));
                    var p = new Progress<double>(pct =>
                        progress.Report(new ProgressUpdate("Re-encoding with the chat region masked…", pct)));
                    return await ChatOverlayProcessor.MaskToFileAsync(
                        src, maskedPath, overlay, _logger, ct, progress: p);
                });
        }
        catch (OperationCanceledException)
        {
            ok = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Masked copy failed for clip {Id}", sel.Id);
            ok = false;
        }

        if (!ok)
        {
            TryDeleteFile(maskedPath);
            Toast(NotificationType.Warning, "Masking did not complete",
                "The masked copy was cancelled or failed — the original is untouched (see logs).");
            return;
        }

        // Carry the timeline over so the scrub-bar markers still show for review.
        string? maskedEvents = null;
        if (sel.Record.HasEvents)
        {
            var srcEvents = sel.Record.EventsSidecarPath
                ?? Path.ChangeExtension(src, null) + ".events.json";
            if (File.Exists(srcEvents))
            {
                maskedEvents = Path.ChangeExtension(maskedPath, null) + ".events.json";
                try { File.Copy(srcEvents, maskedEvents, overwrite: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not copy events sidecar for masked copy."); maskedEvents = null; }
            }
        }

        // Insert as a Manual clip (the "Clips" tab). Drop encounter/M+/arena
        // context — it's a derived share artifact — but keep character + result.
        var record = sel.Record with
        {
            Id = 0,
            VideoPath = maskedPath,
            SidecarPath = Path.ChangeExtension(maskedPath, ".json"),
            ContentType = Wipelog.Core.Detection.ContentType.Manual,
            DisplayName = $"{sel.Record.DisplayName} (masked)",
            FileSizeBytes = new FileInfo(maskedPath).Length,
            Favorite = false,
            IndexedAt = DateTimeOffset.UtcNow,
            EventsSidecarPath = maskedEvents,
            HasEvents = maskedEvents is not null,
            Notes = null,
            Difficulty = null,
            EncounterId = null,
            KeystoneLevel = null,
            InstanceName = null,
            DungeonName = null,
            ArenaBracket = null,
            BossHpPercent = null,
            KeyUpgradeLevel = null,
        };

        long newId;
        try
        {
            newId = await _repo.InsertOrUpdateClipAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index masked copy {Path}", maskedPath);
            Toast(NotificationType.Error, "Masking failed", "Could not add the masked copy to the library (see logs).");
            return;
        }

        // Show the result: jump to the Clips tab and select the new copy.
        _filters.ContentType = ContentTypeFilter.Clip;
        await RefreshAsync();
        SelectedItem = Items.FirstOrDefault(i => i.Id == newId);
        Toast(NotificationType.Success, "Masked copy created",
            $"\"{record.DisplayName}\" is ready in the Clips tab.");
    }

    private void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not delete partial file {Path}", path); }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var ids = SelectedItems.Select(x => x.Id).ToList();
        if (ids.Count == 0) return;

        var (title, message) = ids.Count == 1
            ? ("Delete 1 clip?", "Delete this clip and its files? This cannot be undone.")
            : ($"Delete {ids.Count} clips?", $"Delete {ids.Count} clips and their files? This cannot be undone.");
        if (!await _dialogs.ConfirmAsync(title, message)) return;

        SelectedItems.Clear();
        try
        {
            var result = await _repo.DeleteClipsAsync(ids, CancellationToken.None);
            await RefreshAsync();
            // RefreshAsync just set StatusText to "N clips"; overwrite with the
            // operation summary so the user gets feedback. Next filter change
            // or refresh will replace it again.
            StatusText = result.FailedFiles == 0
                ? $"Deleted {result.Succeeded} clip{(result.Succeeded == 1 ? "" : "s")}."
                : $"Deleted {result.Succeeded} clip{(result.Succeeded == 1 ? "" : "s")} ({result.FailedFiles} file{(result.FailedFiles == 1 ? "" : "s")} could not be removed).";
            Toast(result.FailedFiles == 0 ? NotificationType.Success : NotificationType.Warning,
                "Clips deleted", StatusText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk delete failed for {Count} clips", ids.Count);
            StatusText = "Bulk delete failed — see logs.";
            Toast(NotificationType.Error, "Delete failed", "Bulk delete failed — see logs.");
        }
    }

    [RelayCommand]
    private async Task ArchiveSelectedAsync()
    {
        var clipRecords = SelectedItems.Select(x => x.Record).ToList();
        if (clipRecords.Count == 0) return;

        var suggested = $"wipelog-clips-{DateTime.Now:yyyy-MM-dd}.zip";
        var outputPath = await _dialogs.SaveFilePickerAsync(
            suggested, "ZIP archive", new[] { ".zip" }, CancellationToken.None);
        if (outputPath is null) return;

        SelectedItems.Clear();
        try
        {
            var result = await _dialogs.ShowProgressAsync<ArchiveResult>(
                "Archiving clips…",
                async (progress, ct) =>
                {
                    // Project the archiver's rich progress into the dialog's
                    // (subtitle, percent) shape. Per-clip ticks keep the UI
                    // responsive without spamming the dispatcher per byte.
                    var arch = new Progress<ArchiveProgress>(p =>
                        progress.Report(new ProgressUpdate(
                            $"{p.CompletedClips} / {p.TotalClips} · {p.CurrentName}",
                            p.TotalClips == 0 ? 0 : 100.0 * p.CompletedClips / p.TotalClips)));
                    return await _archiver.ArchiveAsync(clipRecords, outputPath, arch, ct);
                });

            var basename = Path.GetFileName(outputPath);
            StatusText = result.WasCancelled
                ? $"Archive cancelled — {result.Succeeded} of {clipRecords.Count} clips written before stop."
                : result.Failed == 0
                    ? $"Archived {result.Succeeded} clip{(result.Succeeded == 1 ? "" : "s")} to {basename}."
                    : $"Archived {result.Succeeded} clip{(result.Succeeded == 1 ? "" : "s")} to {basename} ({result.Failed} failed).";
            Toast(result is { WasCancelled: false, Failed: 0 } ? NotificationType.Success : NotificationType.Warning,
                result.WasCancelled ? "Archive cancelled" : "Archive complete", StatusText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk archive failed for {Count} clips", clipRecords.Count);
            StatusText = "Archive failed — see logs.";
            Toast(NotificationType.Error, "Archive failed", "Archive failed — see logs.");
        }
    }

    // Queue a SukiUI toast on the UI thread. Auto-dismisses after a few seconds.
    private void Toast(NotificationType type, string title, string content) =>
        Dispatcher.UIThread.Post(() => _toasts.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(content)
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Dismiss().ByClicking()
            .Queue());

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var query = _filters.BuildQuery();
            var clips = await _repo.QueryClipsAsync(query, CancellationToken.None);
            var ids = clips.Select(c => c.Id).ToList();
            var tagsByClip = await _repo.GetTagsForClipsAsync(ids, CancellationToken.None);

            await OnUIThread(() =>
            {
                long? prevSelectedId = SelectedItem?.Id;
                // Multi-selection doesn't survive a refresh — the old item
                // references will be replaced wholesale. Clear before Items
                // so the DataGrid never sees a transient stale selection.
                SelectedItems.Clear();
                Items.Clear();
                foreach (var clip in clips)
                {
                    var tags = tagsByClip.TryGetValue(clip.Id, out var t) ? t : Array.Empty<string>();
                    Items.Add(new ClipListItemViewModel(clip, tags));
                }
                // Restore previous selection if still in the list; otherwise
                // leave the player empty. We deliberately do NOT auto-select
                // the first row — selection-change immediately starts playback
                // in PlayerViewModel, so any fallback here would make the app
                // start playing audio on launch.
                SelectedItem = prevSelectedId is { } id
                    ? Items.FirstOrDefault(i => i.Id == id)
                    : null;
                StatusText = clips.Count switch
                {
                    0 => "No clips match these filters.",
                    1 => "1 clip",
                    _ => $"{clips.Count} clips",
                };
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load clip list.");
            await OnUIThread(() => StatusText = "Error loading clips — see logs.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Runs an action on the Avalonia UI thread, or inline if we're already on
    /// it. Falls back to inline execution when the dispatcher has no message
    /// pump (i.e. unit tests with no Avalonia app set up), where InvokeAsync
    /// would hang forever waiting for a loop that never runs.
    /// </summary>
    private static async Task OnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
