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

using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class EditClipMetadataDialogViewModelTests
{
    private static ClipRecord MakeRecord(
        string? playerName = null,
        string? playerRealm = null,
        string? playerClass = null,
        string? playerSpec = null,
        bool success = true,
        ContentType type = ContentType.RaidEncounter)
    {
        var start = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        return new ClipRecord(
            Id: 42,
            VideoPath: "/clips/x.mp4",
            SidecarPath: "/clips/x.json",
            ContentType: type,
            DisplayName: "Boss",
            Difficulty: type == ContentType.RaidEncounter ? "Mythic" : null,
            EncounterId: type == ContentType.RaidEncounter ? 2902 : null,
            KeystoneLevel: type == ContentType.MythicPlus ? 12 : null,
            Success: success,
            StartedAt: start,
            EndedAt: start.AddSeconds(300),
            DurationSeconds: 300,
            FileSizeBytes: 1_000_000,
            Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow,
            PlayerName: playerName,
            PlayerRealm: playerRealm,
            PlayerClass: playerClass,
            PlayerSpec: playerSpec);
    }

    [Fact]
    public void Initial_state_is_not_dirty_and_save_disabled()
    {
        var vm = new EditClipMetadataDialogViewModel(MakeRecord(playerName: "Frostytanegl"));
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public void Editing_player_name_marks_dirty()
    {
        var vm = new EditClipMetadataDialogViewModel(MakeRecord(playerName: "Old"));
        vm.PlayerName = "New";
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSave);

        var patch = vm.BuildPatch();
        Assert.Equal("New", patch.PlayerName);
        Assert.Null(patch.PlayerRealm);
    }

    [Fact]
    public void Class_change_resets_spec_and_disables_spec_until_class_set()
    {
        var vm = new EditClipMetadataDialogViewModel(
            MakeRecord(playerClass: "Mage", playerSpec: "Frost"));
        Assert.True(vm.IsSpecEnabled);
        Assert.Equal("Frost", vm.SelectedSpec);

        vm.SelectedClass = "Druid";

        Assert.True(vm.IsSpecEnabled);
        Assert.Equal(EditClipMetadataDialogViewModel.NoneValue, vm.SelectedSpec);
        Assert.Contains("Restoration", vm.AvailableSpecs);
        Assert.DoesNotContain("Frost", vm.AvailableSpecs);
    }

    [Fact]
    public void Class_set_to_none_disables_spec_dropdown()
    {
        var vm = new EditClipMetadataDialogViewModel(
            MakeRecord(playerClass: "Mage", playerSpec: "Frost"));

        vm.SelectedClass = EditClipMetadataDialogViewModel.NoneValue;

        Assert.False(vm.IsSpecEnabled);
    }

    [Fact]
    public void BuildPatch_only_includes_changed_fields()
    {
        var vm = new EditClipMetadataDialogViewModel(MakeRecord(
            playerName: "Old",
            playerRealm: "Stormscale",
            playerClass: "Mage",
            playerSpec: "Frost"));

        vm.PlayerName = "New";
        var patch = vm.BuildPatch();

        Assert.Equal("New", patch.PlayerName);
        Assert.Null(patch.PlayerRealm);
        Assert.Null(patch.PlayerClass);
        Assert.Null(patch.PlayerSpec);
        Assert.Null(patch.Success);
    }

    [Fact]
    public void BuildPatch_can_set_class_and_spec_together()
    {
        var vm = new EditClipMetadataDialogViewModel(MakeRecord());

        vm.SelectedClass = "Mage";
        vm.SelectedSpec = "Frost";
        var patch = vm.BuildPatch();

        Assert.Equal("Mage", patch.PlayerClass);
        Assert.Equal("Frost", patch.PlayerSpec);
    }

    [Fact]
    public void Demon_hunter_spec_list_includes_devourer()
    {
        var vm = new EditClipMetadataDialogViewModel(MakeRecord());

        vm.SelectedClass = "Demon Hunter";

        Assert.Contains("Devourer", vm.AvailableSpecs);
        Assert.Contains("Havoc", vm.AvailableSpecs);
        Assert.Contains("Vengeance", vm.AvailableSpecs);
    }

    [Fact]
    public void Success_toggle_marks_dirty_and_emits_patch()
    {
        var vm = new EditClipMetadataDialogViewModel(MakeRecord(success: true));
        Assert.False(vm.CanSave);

        vm.Success = false;

        Assert.True(vm.CanSave);
        Assert.False(vm.BuildPatch().Success);
    }

    [Fact]
    public void Mythic_plus_label_swaps_to_timed_depleted()
    {
        var raid = new EditClipMetadataDialogViewModel(MakeRecord(type: ContentType.RaidEncounter));
        var mplus = new EditClipMetadataDialogViewModel(MakeRecord(type: ContentType.MythicPlus));

        Assert.Equal("Kill", raid.SuccessLabel);
        Assert.Equal("Wipe", raid.FailureLabel);
        Assert.Equal("Timed", mplus.SuccessLabel);
        Assert.Equal("Depleted", mplus.FailureLabel);
    }

    [Fact]
    public void Trimmed_to_empty_player_name_does_not_emit_patch_field()
    {
        // v1 limitation: this dialog can't clear an existing field. A trimmed
        // -to-empty box leaves the column alone.
        var vm = new EditClipMetadataDialogViewModel(MakeRecord(playerName: "Frostytanegl"));

        vm.PlayerName = "  ";
        var patch = vm.BuildPatch();

        Assert.Null(patch.PlayerName);
    }
}
