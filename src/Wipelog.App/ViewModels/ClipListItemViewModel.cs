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
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Wipelog.Core.Data;
using Wipelog.Core.Detection;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

public sealed partial class ClipListItemViewModel : ViewModelBase
{
    public ClipRecord Record { get; private set; }

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private IReadOnlyList<string> _tags = Array.Empty<string>();

    public ClipListItemViewModel(ClipRecord record, IReadOnlyList<string> tags)
    {
        Record = record;
        _isFavorite = record.Favorite;
        _tags = tags;
    }

    public long Id => Record.Id;
    public string DisplayName => Record.DisplayName;
    public bool IsResultSuccess => Record.Success;

    /// <summary>"Ara-Kara, City of Echoes" for M+; instance/encounter title
    /// otherwise. The table's Dungeon column uses this as its primary text.</summary>
    public string DungeonOrEncounter =>
        Record.DungeonName
        ?? Record.InstanceName
        ?? Record.DisplayName;

    /// <summary>"+18" for an M+ keystone, "" otherwise.</summary>
    public string KeystoneText => ClipPovFormatter.FormatKey(Record.KeystoneLevel);

    /// <summary>"Timed +N" / "Timed" / "Depleted" / "Kill" / "Wipe" / "Saved".</summary>
    public string ResultText => Record.ContentType switch
    {
        Wipelog.Core.Detection.ContentType.MythicPlus =>
            Record.Success
                ? (Record.KeyUpgradeLevel is { } n and > 0 ? $"Timed +{n}" : "Timed")
                : "Depleted",
        Wipelog.Core.Detection.ContentType.Manual =>
            Record.Success ? "Saved" : "Incomplete",
        _ => Record.Success ? "Kill" : "Wipe",
    };

    public string PovText => ClipPovFormatter.FormatPov(
        Record.PlayerClass, Record.PlayerSpec, Record.PlayerName);

    public string ClassColorHex => ClipPovFormatter.ClassColorHex(Record.PlayerClass);

    public IBrush ClassColorBrush => new SolidColorBrush(Color.Parse(ClassColorHex));

    /// <summary>Cached spec icon (Wowhead/Zamimg jpg) for the POV cell, or
    /// null when class+spec don't resolve to a known icon — in which case the
    /// view falls back to the class-color swatch.</summary>
    public IImage? SpecIcon => SpecIconLoader.Load(Record.PlayerClass, Record.PlayerSpec);

    public bool HasSpecIcon => SpecIcon is not null;

    public IBrush ResultBackgroundBrush => Record.Success
        ? new SolidColorBrush(Color.Parse("#3323a55a"))   // green @ 0.2 alpha
        : new SolidColorBrush(Color.Parse("#33f23f42")); // red   @ 0.2 alpha

    public IBrush ResultForegroundBrush => Record.Success
        ? new SolidColorBrush(Color.Parse("#23a55a"))
        : new SolidColorBrush(Color.Parse("#f23f42"));

    /// <summary>Display label for the Raid table's Difficulty pill (e.g.
    /// "Mythic"). Empty for M+ clips.</summary>
    public string DifficultyText => Record.Difficulty ?? string.Empty;

    /// <summary>The "All" variant's Content cell text: an icon glyph + key
    /// level / difficulty inline (e.g. "⚔ +18" or "💀 Mythic"). Manual
    /// recordings carry no per-type context so they fall back to a plain
    /// "Manual" badge.</summary>
    public string ContentBadgeText => Record.ContentType switch
    {
        Wipelog.Core.Detection.ContentType.MythicPlus =>
            $"⚔ {ClipPovFormatter.FormatKey(Record.KeystoneLevel)}".TrimEnd(),
        Wipelog.Core.Detection.ContentType.Manual => "Manual",
        _ => $"💀 {Record.Difficulty ?? string.Empty}".TrimEnd(),
    };

    public string Subtitle => Record.ContentType switch
    {
        ContentType.MythicPlus =>
            $"+{Record.KeystoneLevel?.ToString(CultureInfo.InvariantCulture) ?? "?"}  {ResultLabel()}  ·  {RelativeTime(Record.StartedAt)}",
        // Manual: no difficulty / keystone column to fill; drop straight to
        // result + time so the row doesn't show a phantom "?".
        ContentType.Manual =>
            $"{ResultLabel()}  ·  {RelativeTime(Record.StartedAt)}",
        _ =>
            $"{Record.Difficulty ?? "?"}  {ResultLabel()}  ·  {RelativeTime(Record.StartedAt)}",
    };

    public string DurationText => FormatDuration(Record.DurationSeconds);

    /// <summary>Absolute date stamp for the Stage 7 table's Date column,
    /// e.g. "15:30 9 May" — no year unless the clip is older than this year.</summary>
    public string DateText => FormatDate(Record.StartedAt);

    public bool HasTags => Tags.Count > 0;

    public IEnumerable<string> VisibleTags => Tags.Take(3);

    public string ExtraTagText =>
        Tags.Count > 3 ? $"+{Tags.Count - 3}" : string.Empty;

    public bool HasExtraTags => Tags.Count > 3;

    public void Refresh(ClipRecord newRecord, IReadOnlyList<string> newTags)
    {
        Record = newRecord;
        IsFavorite = newRecord.Favorite;
        Tags = newTags;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(ExtraTagText));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasExtraTags));
        OnPropertyChanged(nameof(IsResultSuccess));
        OnPropertyChanged(nameof(DungeonOrEncounter));
        OnPropertyChanged(nameof(KeystoneText));
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(PovText));
        OnPropertyChanged(nameof(ClassColorHex));
        OnPropertyChanged(nameof(ClassColorBrush));
        OnPropertyChanged(nameof(SpecIcon));
        OnPropertyChanged(nameof(HasSpecIcon));
        OnPropertyChanged(nameof(ResultBackgroundBrush));
        OnPropertyChanged(nameof(ResultForegroundBrush));
        OnPropertyChanged(nameof(DifficultyText));
        OnPropertyChanged(nameof(ContentBadgeText));
    }

    private string ResultLabel() => Record.ContentType switch
    {
        ContentType.MythicPlus =>
            Record.Success
                ? (Record.KeyUpgradeLevel is { } n and > 0 ? $"Timed +{n}" : "Timed")
                : "Depleted",
        ContentType.Manual => Record.Success ? "Saved" : "Incomplete",
        _ => Record.Success ? "Kill" : "Wipe",
    };

    private static string FormatDuration(int seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    internal static string FormatDate(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        var now = DateTimeOffset.Now;
        return local.Year == now.Year
            ? local.ToString("HH:mm d MMM", CultureInfo.InvariantCulture)
            : local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private static string RelativeTime(DateTimeOffset when)
    {
        var diff = DateTimeOffset.Now - when;
        if (diff < TimeSpan.FromMinutes(1)) return "just now";
        if (diff < TimeSpan.FromHours(1)) return $"{(int)diff.TotalMinutes}m ago";
        if (diff < TimeSpan.FromHours(24)) return $"{(int)diff.TotalHours}h ago";
        if (diff < TimeSpan.FromHours(48)) return "yesterday";
        if (diff < TimeSpan.FromDays(7)) return $"{(int)diff.TotalDays}d ago";
        return when.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
