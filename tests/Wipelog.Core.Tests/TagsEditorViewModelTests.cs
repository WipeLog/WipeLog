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

using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class TagsEditorViewModelTests
{
    private static (TagsEditorViewModel Vm, ClipRepository Repo, long ClipId, TempDirectory Dir) NewEditor()
    {
        var dir = new TempDirectory();
        var db = new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
        var repo = new ClipRepository(db);
        var clip = new ClipRecord(
            Id: 0, VideoPath: "/x.mp4", SidecarPath: "/x.json",
            ContentType: ContentType.RaidEncounter, DisplayName: "Boss",
            Difficulty: "Mythic", EncounterId: 1, KeystoneLevel: null,
            Success: true,
            StartedAt: DateTimeOffset.UtcNow, EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
            DurationSeconds: 300, FileSizeBytes: 1, Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow);
        var id = repo.InsertOrUpdateClipAsync(clip, CancellationToken.None).GetAwaiter().GetResult();
        var vm = new TagsEditorViewModel(repo);
        vm.LoadAsync(id).GetAwaiter().GetResult();
        return (vm, repo, id, dir);
    }

    [Fact]
    public async Task Add_valid_tag_persists_and_clears_draft()
    {
        var (vm, repo, id, dir) = NewEditor();
        using (dir)
        {
            vm.DraftTag = "Prog";
            await vm.AddTagCommand.ExecuteAsync(null);
            Assert.Contains("prog", vm.Tags);
            Assert.Equal(string.Empty, vm.DraftTag);
            Assert.Null(vm.ValidationError);
            var stored = await repo.GetTagsForClipAsync(id, TestContext.Current.CancellationToken);
            Assert.Contains("prog", stored);
        }
    }

    [Fact]
    public async Task Add_invalid_tag_sets_validation_error()
    {
        var (vm, _, _, dir) = NewEditor();
        using (dir)
        {
            vm.DraftTag = "bad!";
            await vm.AddTagCommand.ExecuteAsync(null);
            Assert.NotNull(vm.ValidationError);
            Assert.DoesNotContain("bad!", vm.Tags);
        }
    }

    [Fact]
    public async Task Add_duplicate_tag_rejects_and_sets_error()
    {
        var (vm, _, _, dir) = NewEditor();
        using (dir)
        {
            vm.DraftTag = "Prog";
            await vm.AddTagCommand.ExecuteAsync(null);
            vm.DraftTag = "PROG";
            await vm.AddTagCommand.ExecuteAsync(null);
            Assert.Single(vm.Tags);
            Assert.NotNull(vm.ValidationError);
        }
    }

    [Fact]
    public async Task Remove_tag_persists()
    {
        var (vm, repo, id, dir) = NewEditor();
        using (dir)
        {
            vm.DraftTag = "prog";
            await vm.AddTagCommand.ExecuteAsync(null);

            await vm.RemoveTagCommand.ExecuteAsync("prog");

            Assert.Empty(vm.Tags);
            var stored = await repo.GetTagsForClipAsync(id, TestContext.Current.CancellationToken);
            Assert.Empty(stored);
        }
    }

    [Fact]
    public async Task Load_populates_tags_and_available_tags()
    {
        var (vm, repo, id, dir) = NewEditor();
        using (dir)
        {
            await repo.SetTagsForClipAsync(id, new[] { "alpha", "beta" }, TestContext.Current.CancellationToken);
            await vm.LoadAsync(id);

            Assert.Equal(2, vm.Tags.Count);
            Assert.Contains("alpha", vm.AvailableTags);
            Assert.Contains("beta", vm.AvailableTags);
        }
    }

    [Fact]
    public async Task Load_with_zero_id_clears_state()
    {
        var (vm, _, _, dir) = NewEditor();
        using (dir)
        {
            vm.DraftTag = "leftover";
            await vm.LoadAsync(0);

            Assert.Empty(vm.Tags);
            Assert.Equal(string.Empty, vm.DraftTag);
        }
    }
}
