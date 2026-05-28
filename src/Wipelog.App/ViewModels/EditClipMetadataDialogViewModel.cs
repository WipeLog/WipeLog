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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wipelog.Core.Data;
using Wipelog.Core.Detection;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Backs the right-click → Edit metadata dialog. Lets the user override
/// player name/realm/class/spec and the kill/wipe outcome of one clip,
/// then persists via <see cref="IClipRepository.UpdateClipMetadataAsync"/>.
/// </summary>
public sealed partial class EditClipMetadataDialogViewModel : ViewModelBase
{
    public const string NoneValue = "(none)";

    private readonly ClipRecord _record;

    public EditClipMetadataDialogViewModel(ClipRecord record)
    {
        _record = record;
        DisplayName = record.DisplayName;
        ContentLabel = record.ContentType switch
        {
            ContentType.MythicPlus => "Mythic+ run",
            _ => "Raid encounter",
        };
        IsMythicPlus = record.ContentType == ContentType.MythicPlus;

        AvailableClasses = new ObservableCollection<string> { NoneValue };
        foreach (var c in SpecMap.Known.Values.Select(v => v.Class).Distinct().OrderBy(c => c))
            AvailableClasses.Add(c);

        AvailableSpecs = new ObservableCollection<string> { NoneValue };

        PlayerName = record.PlayerName ?? string.Empty;
        PlayerRealm = record.PlayerRealm ?? string.Empty;
        PlayerRegion = record.PlayerRegion ?? string.Empty;
        SelectedClass = record.PlayerClass ?? NoneValue;
        SelectedSpec = record.PlayerSpec ?? NoneValue;
        Success = record.Success;
    }

    public string DisplayName { get; }
    public string ContentLabel { get; }
    public bool IsMythicPlus { get; }

    public string SuccessLabel => IsMythicPlus ? "Timed" : "Kill";
    public string FailureLabel => IsMythicPlus ? "Depleted" : "Wipe";

    public ObservableCollection<string> AvailableClasses { get; }
    public ObservableCollection<string> AvailableSpecs { get; }

    [ObservableProperty]
    private string _playerName = string.Empty;

    [ObservableProperty]
    private string _playerRealm = string.Empty;

    [ObservableProperty]
    private string _playerRegion = string.Empty;

    [ObservableProperty]
    private string _selectedClass = NoneValue;

    [ObservableProperty]
    private string _selectedSpec = NoneValue;

    [ObservableProperty]
    private bool _success;

    public bool IsSpecEnabled => !IsClassEmpty(SelectedClass);

    /// <summary>True when the form has changes vs the original record.</summary>
    public bool IsDirty =>
        Trim(PlayerName) != (_record.PlayerName ?? string.Empty)
        || Trim(PlayerRealm) != (_record.PlayerRealm ?? string.Empty)
        || Trim(PlayerRegion) != (_record.PlayerRegion ?? string.Empty)
        || ToValueOrNull(SelectedClass) != _record.PlayerClass
        || ToValueOrNull(SelectedSpec) != _record.PlayerSpec
        || Success != _record.Success;

    public bool CanSave => IsDirty;

    partial void OnSelectedClassChanged(string value)
    {
        RebuildSpecList(value);
        // Reset spec selection when class changes — old spec is unlikely to
        // belong to the new class, and an empty class disables the spec list.
        SelectedSpec = NoneValue;
        OnPropertyChanged(nameof(IsSpecEnabled));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnSelectedSpecChanged(string value) => OnPropertyChanged(nameof(CanSave));
    partial void OnPlayerNameChanged(string value) => OnPropertyChanged(nameof(CanSave));
    partial void OnPlayerRealmChanged(string value) => OnPropertyChanged(nameof(CanSave));
    partial void OnPlayerRegionChanged(string value) => OnPropertyChanged(nameof(CanSave));
    partial void OnSuccessChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    private void RebuildSpecList(string className)
    {
        AvailableSpecs.Clear();
        AvailableSpecs.Add(NoneValue);
        if (IsClassEmpty(className)) return;
        foreach (var spec in SpecMap.Known.Values
                                 .Where(v => v.Class == className)
                                 .Select(v => v.Spec)
                                 .Distinct()
                                 .OrderBy(s => s))
        {
            AvailableSpecs.Add(spec);
        }
    }

    /// <summary>Builds the patch the dialog will hand to the repository.</summary>
    public ClipMetadataPatch BuildPatch()
    {
        // The patch type's null fields mean "leave alone"; v1 has no way to
        // explicitly clear a column, so trimmed-to-empty edits don't produce
        // a write. Class is similar: switching to "(none)" leaves the
        // existing class alone.
        return new ClipMetadataPatch(
            PlayerName: NonEmptyDiff(Trim(PlayerName), _record.PlayerName),
            PlayerRealm: NonEmptyDiff(Trim(PlayerRealm), _record.PlayerRealm),
            PlayerRegion: NonEmptyDiff(Trim(PlayerRegion), _record.PlayerRegion),
            PlayerClass: NonEmptyDiff(ToValueOrNull(SelectedClass), _record.PlayerClass),
            PlayerSpec: NonEmptyDiff(ToValueOrNull(SelectedSpec), _record.PlayerSpec),
            Success: Success != _record.Success ? Success : null);
    }

    private static string? NonEmptyDiff(string? newValue, string? old)
    {
        if (string.IsNullOrEmpty(newValue)) return null; // can't clear in v1
        return newValue == old ? null : newValue;
    }

    private static string Trim(string value) => value?.Trim() ?? string.Empty;

    private static string? ToValueOrNull(string value) =>
        IsClassEmpty(value) ? null : value;

    private static bool IsClassEmpty(string value) =>
        string.IsNullOrEmpty(value) || value == NoneValue;
}
