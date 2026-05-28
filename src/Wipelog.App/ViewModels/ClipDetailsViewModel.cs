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
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wipelog.App.Services;
using Wipelog.Core.Data;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Drives the right-hand details panel that adapts to the selected clip's
/// content type (M+, Raid, Arena, Solo Shuffle, Battleground). One VM with
/// per-type Is* flags and per-type text projections; the view toggles row
/// visibility from the flags instead of swapping templates by data type.
/// </summary>
public sealed partial class ClipDetailsViewModel : ViewModelBase
{
    private readonly ClipListViewModel _list;
    private readonly IClipRepository _repo;
    private readonly IDialogService _dialogs;
    private readonly IClipEventsLoader _events;
    private readonly ILogger<ClipDetailsViewModel> _logger;

    private CancellationTokenSource? _loadCts;
    private TimelineEventDocument? _document;

    public TagsEditorViewModel TagsEditor { get; }

    /// <summary>
    /// Task that completes when the events sidecar for the current selection
    /// has been loaded (or the load was cancelled / there is no selection).
    /// Exposed for tests and so the UI can show a loading state if it ever
    /// needs one. Always non-null after the first selection change.
    /// </summary>
    public Task EventsLoadTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    private ClipListItemViewModel? _selected;

    /// <summary>
    /// Free-form notes about the selected clip. Loaded from the clip row on
    /// selection change; the view commits edits via
    /// <see cref="CommitNotesAsync"/> on TextBox LostFocus.
    /// </summary>
    [ObservableProperty]
    private string _notesText = string.Empty;

    /// <summary>
    /// MM:SS or M:SS tokens detected in <see cref="NotesText"/>. The view
    /// renders one chip per entry below the textbox; clicking a chip seeks
    /// the player to <see cref="NoteTimestamp.Offset"/>. Recomputed on
    /// every NotesText change.
    /// </summary>
    public IReadOnlyList<NoteTimestamp> NoteTimestamps { get; private set; } =
        Array.Empty<NoteTimestamp>();

    public bool HasNoteTimestamps => NoteTimestamps.Count > 0;

    // 0..999 minutes, two-digit seconds. Word boundaries keep ISO dates like
    // "2026-05-12" from matching at the seconds/minutes split. Seconds-range
    // validation happens in code so the regex stays simple.
    private static readonly Regex TimestampRegex =
        new(@"\b(\d{1,3}):(\d{2})\b", RegexOptions.Compiled);

    private static IReadOnlyList<NoteTimestamp> ExtractTimestamps(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<NoteTimestamp>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NoteTimestamp>();
        foreach (Match m in TimestampRegex.Matches(text))
        {
            if (!seen.Add(m.Value)) continue;
            if (!int.TryParse(m.Groups[1].Value, out var mins)) continue;
            if (!int.TryParse(m.Groups[2].Value, out var secs)) continue;
            if (secs >= 60) continue;  // "12:99" is ill-formed; drop it
            result.Add(new NoteTimestamp(m.Value, new TimeSpan(0, mins, secs)));
        }
        return result;
    }

    partial void OnNotesTextChanged(string value)
    {
        NoteTimestamps = ExtractTimestamps(value);
        OnPropertyChanged(nameof(NoteTimestamps));
        OnPropertyChanged(nameof(HasNoteTimestamps));
    }

    public ClipDetailsViewModel(
        ClipListViewModel list,
        IClipRepository repo,
        IDialogService dialogs,
        TagsEditorViewModel tagsEditor,
        IClipEventsLoader events,
        ILogger<ClipDetailsViewModel> logger)
    {
        _list = list;
        _repo = repo;
        _dialogs = dialogs;
        TagsEditor = tagsEditor;
        _events = events;
        _logger = logger;
        _list.PropertyChanged += OnListPropertyChanged;
        TagsEditor.TagsChanged += async (_, _) => await RefreshSelectedTagsAsync();
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipListViewModel.SelectedItem))
        {
            Selected = _list.SelectedItem;
        }
    }

    partial void OnSelectedChanged(ClipListItemViewModel? value)
    {
        _ = TagsEditor.LoadAsync(value?.Id ?? 0);
        // Pull the persisted note onto the editor field. Programmatic set — no
        // commit triggered (CommitNotesAsync is only wired to TextBox LostFocus).
        NotesText = value?.Record.Notes ?? string.Empty;

        // Reset any prior events-sidecar state, cancelling an in-flight load
        // so a slow-loading prior selection can't overwrite the new one.
        _loadCts?.Cancel();
        _document = null;
        RaiseEventsProperties();

        // Common scalar projections are derived from Selected so they all need
        // a re-emit. The per-type Is* flags also live on Selected.
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(ResultBrush));
        OnPropertyChanged(nameof(StartedAtText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(EncounterIdText));
        OnPropertyChanged(nameof(KeystoneLevelText));
        OnPropertyChanged(nameof(VideoPath));
        OnPropertyChanged(nameof(IsRaid));
        OnPropertyChanged(nameof(IsMythicPlus));
        OnPropertyChanged(nameof(IsArena));
        OnPropertyChanged(nameof(IsSoloShuffle));
        OnPropertyChanged(nameof(IsBattleground));
        OnPropertyChanged(nameof(DifficultyText));
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(InstanceText));
        OnPropertyChanged(nameof(HasDungeon));
        OnPropertyChanged(nameof(DungeonText));
        OnPropertyChanged(nameof(HasBracket));
        OnPropertyChanged(nameof(BracketText));
        OnPropertyChanged(nameof(HasMap));
        OnPropertyChanged(nameof(MapText));
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(CharacterText));
        OnPropertyChanged(nameof(HasSpec));
        OnPropertyChanged(nameof(SpecText));
        OnPropertyChanged(nameof(HasWipeHp));
        OnPropertyChanged(nameof(WipeHpText));
        OnPropertyChanged(nameof(HasTypeDetails));

        if (value is null || string.IsNullOrEmpty(value.Record.VideoPath))
        {
            EventsLoadTask = Task.CompletedTask;
            return;
        }

        _loadCts = new CancellationTokenSource();
        EventsLoadTask = LoadEventsAsync(value.Record.VideoPath, _loadCts.Token);
    }

    private async Task LoadEventsAsync(string videoPath, CancellationToken ct)
    {
        try
        {
            var doc = await _events.LoadAsync(videoPath, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            _document = doc;
            RaiseEventsProperties();
        }
        catch (OperationCanceledException)
        {
            // Selection changed mid-load; nothing to do.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load events sidecar for {Path}", videoPath);
        }
    }

    private void RaiseEventsProperties()
    {
        OnPropertyChanged(nameof(HasComp));
        OnPropertyChanged(nameof(TankCount));
        OnPropertyChanged(nameof(HealerCount));
        OnPropertyChanged(nameof(DpsCount));
        OnPropertyChanged(nameof(HasTank));
        OnPropertyChanged(nameof(HasHealer));
        OnPropertyChanged(nameof(HasDps));
        OnPropertyChanged(nameof(HasDeaths));
        OnPropertyChanged(nameof(DeathCount));
        OnPropertyChanged(nameof(HasAnyBadge));
    }

    public bool HasSelection => Selected is not null;

    public string DisplayName => Selected?.Record.DisplayName ?? string.Empty;

    // ── Per-type discrimination ──────────────────────────────────────────────
    // Each flag reads ContentType once and is bound via IsVisible on the
    // matching row block in the view.

    public bool IsRaid          => Selected?.Record.ContentType == ContentType.RaidEncounter;
    public bool IsMythicPlus    => Selected?.Record.ContentType == ContentType.MythicPlus;
    public bool IsArena         => Selected?.Record.ContentType == ContentType.Arena;
    public bool IsSoloShuffle   => Selected?.Record.ContentType == ContentType.SoloShuffle;
    public bool IsBattleground  => Selected?.Record.ContentType
                                     is ContentType.Battleground
                                     or ContentType.RatedBattleground;

    // ── Pills above the metadata grid ────────────────────────────────────────

    public string BadgeText
    {
        get
        {
            var r = Selected?.Record;
            if (r is null) return string.Empty;
            return r.ContentType switch
            {
                ContentType.MythicPlus =>
                    $"M+ +{r.KeystoneLevel?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
                ContentType.RaidEncounter =>
                    $"Raid {r.Difficulty ?? "?"}",
                ContentType.Arena =>
                    r.ArenaBracket ?? "Arena",
                ContentType.SoloShuffle =>
                    "Solo Shuffle",
                ContentType.Battleground =>
                    "Battleground",
                ContentType.RatedBattleground =>
                    "Rated BG",
                // Manual clips are free-form so there's no per-type context
                // to fold into the badge; the pill itself is enough signal.
                ContentType.Manual => "Manual",
                _ => string.Empty,
            };
        }
    }

    public string ResultText
    {
        get
        {
            var r = Selected?.Record;
            if (r is null) return string.Empty;
            return r.ContentType switch
            {
                ContentType.MythicPlus =>
                    r.Success
                        ? (r.KeyUpgradeLevel is { } n and > 0 ? $"Timed +{n}" : "Timed")
                        : "Depleted",
                ContentType.RaidEncounter => r.Success ? "Kill" : "Wipe",
                // Arena resolves win/loss at finalize via ArenaMatchEnd.WinningFaction
                // vs the player's faction — Success is meaningful here.
                ContentType.Arena => r.Success ? "Win" : "Loss",
                // Solo Shuffle has no single winner (per-round structure) and
                // BG win/loss isn't a clean log signal yet — neutral pill for now.
                ContentType.SoloShuffle
                    or ContentType.Battleground
                    or ContentType.RatedBattleground => "Complete",
                // Manual recordings: success=true on a clean Stop click,
                // false only if the recorder process died mid-recording.
                ContentType.Manual => r.Success ? "Saved" : "Incomplete",
                _ => string.Empty,
            };
        }
    }

    public IBrush ResultBrush =>
        Selected?.Record.Success == true
            ? new SolidColorBrush(Color.Parse("#23a55a"))
            : new SolidColorBrush(Color.Parse("#f23f42"));

    // ── Common metadata rows (always visible) ────────────────────────────────

    public string StartedAtText =>
        Selected?.Record.StartedAt.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    public string DurationText
    {
        get
        {
            var r = Selected?.Record;
            if (r is null) return string.Empty;
            var ts = TimeSpan.FromSeconds(r.DurationSeconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    public string FileSizeText
    {
        get
        {
            var bytes = Selected?.Record.FileSizeBytes ?? 0;
            return bytes switch
            {
                >= 1L * 1024 * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
                >= 1L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
                >= 1024 => $"{bytes / 1024.0:F1} KB",
                _ => $"{bytes} B",
            };
        }
    }

    public bool HasCharacter => !string.IsNullOrEmpty(Selected?.Record.PlayerName);

    public string CharacterText
    {
        get
        {
            var r = Selected?.Record;
            if (r is null || string.IsNullOrEmpty(r.PlayerName)) return string.Empty;
            var realmRegion = string.Join("-", new[] { r.PlayerRealm, r.PlayerRegion }
                .Where(s => !string.IsNullOrEmpty(s)));
            return string.IsNullOrEmpty(realmRegion)
                ? r.PlayerName
                : $"{r.PlayerName} · {realmRegion}";
        }
    }

    public bool HasSpec => !string.IsNullOrEmpty(Selected?.Record.PlayerClass);

    public string SpecText
    {
        get
        {
            var r = Selected?.Record;
            if (r is null) return string.Empty;
            return string.IsNullOrEmpty(r.PlayerSpec)
                ? r.PlayerClass ?? string.Empty
                : $"{r.PlayerClass} {r.PlayerSpec}";
        }
    }

    // ── Per-type metadata rows ──────────────────────────────────────────────
    // Each has a Has* gate so the row's IsVisible binding can hide the entire
    // label+value pair (not just the value), matching the existing
    // HasCharacter / HasSpec pattern.

    public bool HasInstance => IsRaid && !string.IsNullOrEmpty(Selected?.Record.InstanceName);
    public string InstanceText => Selected?.Record.InstanceName ?? string.Empty;

    // Raid wipes only: surface the boss's remaining HP at the moment the
    // recording ended. The DB column lands here via the indexer / library
    // sink; non-raid content, kills, and pre-feature clips stay null.
    public bool HasWipeHp =>
        IsRaid &&
        Selected?.Record.Success == false &&
        Selected?.Record.BossHpPercent is not null;

    public string WipeHpText
    {
        get
        {
            var pct = Selected?.Record.BossHpPercent;
            if (pct is null) return string.Empty;
            // One decimal up to 9.9%, whole numbers above (so we never show
            // "27.0%" but a really-close-to-kill "0.4%" stays useful).
            var formatted = pct.Value < 10
                ? pct.Value.ToString("0.0", CultureInfo.InvariantCulture)
                : pct.Value.ToString("0", CultureInfo.InvariantCulture);
            return $"Wiped at {formatted}%";
        }
    }

    public bool HasDungeon => IsMythicPlus && !string.IsNullOrEmpty(Selected?.Record.DungeonName);
    public string DungeonText => Selected?.Record.DungeonName ?? string.Empty;

    public bool HasBracket => (IsArena || IsSoloShuffle)
                              && !string.IsNullOrEmpty(Selected?.Record.ArenaBracket);
    public string BracketText => Selected?.Record.ArenaBracket ?? string.Empty;

    public bool HasMap => IsBattleground && !string.IsNullOrEmpty(Selected?.Record.DisplayName);
    public string MapText => Selected?.Record.DisplayName ?? string.Empty;

    // True when any type-specific metadata row is shown below the core trio,
    // so the details panel only draws the divider when there's content under it.
    public bool HasTypeDetails =>
        IsRaid || HasInstance || HasDungeon || IsMythicPlus
        || HasBracket || HasMap || HasCharacter || HasSpec || HasWipeHp;

    public string DifficultyText => Selected?.Record.Difficulty ?? string.Empty;
    public string EncounterIdText =>
        Selected?.Record.EncounterId?.ToString(CultureInfo.InvariantCulture) ?? "—";
    public string KeystoneLevelText =>
        Selected?.Record.KeystoneLevel?.ToString(CultureInfo.InvariantCulture) ?? "—";
    public string VideoPath => Selected?.Record.VideoPath ?? string.Empty;

    // ── Comp / death chip strip (restored from the #3 commit) ────────────────

    public bool HasComp => _document is { Combatants.Count: > 0 };
    public int TankCount => CountRole(SpecRole.Tank);
    public int HealerCount => CountRole(SpecRole.Healer);
    public int DpsCount => CountRole(SpecRole.Dps);
    public bool HasTank => TankCount > 0;
    public bool HasHealer => HealerCount > 0;
    public bool HasDps => DpsCount > 0;
    public bool HasDeaths => DeathCount > 0;
    public int DeathCount => _document is null
        ? 0
        : _document.Events.OfType<PlayerDeathEvent>().Count();

    public bool HasAnyBadge => HasComp || HasDeaths;

    private int CountRole(SpecRole role) =>
        _document?.Combatants.Count(c => c.Role == role) ?? 0;

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var sel = Selected;
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

    /// <summary>
    /// Persists the current <see cref="NotesText"/> to the selected clip's
    /// row when it differs from the stored value. Called from the view's
    /// LostFocus handler so we save once when the user finishes editing,
    /// not on every keystroke. Whitespace-only input is treated as "no
    /// notes" — repo normalises to null on disk.
    /// </summary>
    public async Task CommitNotesAsync()
    {
        var sel = Selected;
        if (sel is null) return;
        var current = sel.Record.Notes ?? string.Empty;
        var draft = NotesText ?? string.Empty;
        if (current == draft) return;
        try
        {
            await _repo.SetNotesAsync(sel.Id, draft, CancellationToken.None);
            // Update the cached record so a later selection-return doesn't
            // re-load the pre-edit value and overwrite the textbox.
            var normalized = string.IsNullOrWhiteSpace(draft) ? null : draft;
            sel.Refresh(sel.Record with { Notes = normalized }, sel.Tags);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving notes failed for clip {Id}", sel.Id);
        }
    }

    private async Task RefreshSelectedTagsAsync()
    {
        var sel = Selected;
        if (sel is null) return;
        var tags = await _repo.GetTagsForClipAsync(sel.Id, CancellationToken.None);
        sel.Refresh(sel.Record, tags);
    }
}

/// <summary>
/// A single MM:SS-style timestamp pulled out of the notes textbox. The view
/// renders these as clickable chips and dispatches each click to
/// <c>PlayerViewModel.SeekAbsolute(Offset)</c>.
/// </summary>
public sealed record NoteTimestamp(string Label, TimeSpan Offset);
