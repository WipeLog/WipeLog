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
using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipListViewModelTests
{
    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCallCount { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastMessage { get; private set; }
        public string? SavePickerResult { get; set; }
        public string? LastSuggestedName { get; private set; }
        public int ShowProgressCallCount { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message,
            string confirmLabel = "Delete", string cancelLabel = "Cancel")
        {
            ConfirmCallCount++;
            LastTitle = title;
            LastMessage = message;
            return Task.FromResult(ConfirmResult);
        }
        public Task<string?> SaveFilePickerAsync(
            string suggestedName, string filterName,
            IReadOnlyList<string> extensions, CancellationToken ct)
        {
            LastSuggestedName = suggestedName;
            return Task.FromResult(SavePickerResult);
        }
        public Task<T> ShowProgressAsync<T>(string title,
            Func<IProgress<ProgressUpdate>, CancellationToken, Task<T>> work)
        {
            ShowProgressCallCount++;
            return work(new Progress<ProgressUpdate>(_ => { }), CancellationToken.None);
        }
    }

    private sealed class FakeArchiver : IClipArchiver
    {
        public Func<IReadOnlyList<ClipRecord>, string, ArchiveResult> Handler { get; set; }
            = (clips, output) => new ArchiveResult(clips.Count, 0, 0, output, false);
        public IReadOnlyList<ClipRecord>? LastClips { get; private set; }
        public string? LastOutputPath { get; private set; }
        public Task<ArchiveResult> ArchiveAsync(
            IReadOnlyList<ClipRecord> clips, string outputPath,
            IProgress<ArchiveProgress>? progress, CancellationToken ct)
        {
            LastClips = clips;
            LastOutputPath = outputPath;
            return Task.FromResult(Handler(clips, outputPath));
        }
    }

    private static (ClipListViewModel Vm, ClipRepository Repo, FakeDialogService Dialogs, FakeArchiver Archiver, TempDirectory Dir) NewVm()
    {
        var dir = new TempDirectory();
        var db = new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance);
        var repo = new ClipRepository(db);
        var filters = new ClipFiltersViewModel();
        var dialogs = new FakeDialogService();
        var archiver = new FakeArchiver();
        var prefs = new Wipelog.Core.Preferences.UserPreferencesService(Path.Combine(dir.Path, "prefs.json"));
        var vm = new ClipListViewModel(repo, filters, dialogs, archiver, prefs, new SukiUI.Toasts.SukiToastManager(), NullLogger<ClipListViewModel>.Instance);
        return (vm, repo, dialogs, archiver, dir);
    }

    private static ClipListItemViewModel MakeItem(long id, string name = "Clip") =>
        new ClipListItemViewModel(
            new ClipRecord(
                Id: id,
                VideoPath: $"/x{id}.mp4",
                SidecarPath: $"/x{id}.json",
                ContentType: ContentType.RaidEncounter,
                DisplayName: name,
                Difficulty: "Mythic",
                EncounterId: 1,
                KeystoneLevel: null,
                Success: true,
                StartedAt: DateTimeOffset.UtcNow,
                EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
                DurationSeconds: 300,
                FileSizeBytes: 1,
                Favorite: false,
                IndexedAt: DateTimeOffset.UtcNow),
            Array.Empty<string>());

    [Fact]
    public void Adding_to_SelectedItems_updates_count_and_HasSelection()
    {
        var (vm, _, _, _, dir) = NewVm();
        using (dir)
        {
            Assert.Equal(0, vm.SelectedCount);
            Assert.False(vm.HasSelection);

            vm.SelectedItems.Add(MakeItem(1));
            vm.SelectedItems.Add(MakeItem(2));

            Assert.Equal(2, vm.SelectedCount);
            Assert.True(vm.HasSelection);
        }
    }

    [Fact]
    public void Removing_from_SelectedItems_updates_count_and_HasSelection()
    {
        var (vm, _, _, _, dir) = NewVm();
        using (dir)
        {
            var a = MakeItem(1);
            var b = MakeItem(2);
            vm.SelectedItems.Add(a);
            vm.SelectedItems.Add(b);
            Assert.Equal(2, vm.SelectedCount);

            vm.SelectedItems.Remove(a);

            Assert.Equal(1, vm.SelectedCount);
            Assert.True(vm.HasSelection);
        }
    }

    [Fact]
    public void Clearing_SelectedItems_resets_count_and_HasSelection()
    {
        var (vm, _, _, _, dir) = NewVm();
        using (dir)
        {
            vm.SelectedItems.Add(MakeItem(1));
            vm.SelectedItems.Add(MakeItem(2));
            Assert.True(vm.HasSelection);

            vm.SelectedItems.Clear();

            Assert.Equal(0, vm.SelectedCount);
            Assert.False(vm.HasSelection);
        }
    }

    [Fact]
    public void SelectedCount_and_HasSelection_emit_PropertyChanged()
    {
        var (vm, _, _, _, dir) = NewVm();
        using (dir)
        {
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            vm.SelectedItems.Add(MakeItem(1));

            Assert.Contains(nameof(ClipListViewModel.SelectedCount), changed);
            Assert.Contains(nameof(ClipListViewModel.HasSelection), changed);
        }
    }

    private static async Task<long> InsertClipWithFilesAsync(
        ClipRepository repo, TempDirectory dir, string stem)
    {
        var video = Path.Combine(dir.Path, stem + ".mp4");
        var sidecar = Path.Combine(dir.Path, stem + ".json");
        var events = Path.Combine(dir.Path, stem + ".events.json");
        await File.WriteAllTextAsync(video, "fake");
        await File.WriteAllTextAsync(sidecar, "{}");
        await File.WriteAllTextAsync(events, "{}");
        var clip = new ClipRecord(
            Id: 0,
            VideoPath: video,
            SidecarPath: sidecar,
            ContentType: ContentType.RaidEncounter,
            DisplayName: stem,
            Difficulty: "Mythic",
            EncounterId: 1,
            KeystoneLevel: null,
            Success: true,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: DateTimeOffset.UtcNow.AddMinutes(5),
            DurationSeconds: 300,
            FileSizeBytes: 1,
            Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow);
        return await repo.InsertOrUpdateClipAsync(clip, CancellationToken.None);
    }

    [Fact]
    public async Task DeleteClipsAsync_removes_all_rows_and_three_files_per_clip()
    {
        var (vm, repo, _, _, dir) = NewVm();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var ids = new[]
            {
                await InsertClipWithFilesAsync(repo, dir, "a"),
                await InsertClipWithFilesAsync(repo, dir, "b"),
                await InsertClipWithFilesAsync(repo, dir, "c"),
            };

            var result = await repo.DeleteClipsAsync(ids, ct);

            Assert.Equal(3, result.Succeeded);
            Assert.Equal(0, result.FailedFiles);
            foreach (var stem in new[] { "a", "b", "c" })
            {
                Assert.False(File.Exists(Path.Combine(dir.Path, stem + ".mp4")));
                Assert.False(File.Exists(Path.Combine(dir.Path, stem + ".json")));
                Assert.False(File.Exists(Path.Combine(dir.Path, stem + ".events.json")));
            }
            foreach (var id in ids)
            {
                Assert.Null(await repo.GetClipAsync(id, ct));
            }
        }
    }

    [Fact]
    public async Task DeleteClipsAsync_empty_list_is_a_no_op()
    {
        var (_, repo, _, _, dir) = NewVm();
        using (dir)
        {
            var result = await repo.DeleteClipsAsync(Array.Empty<long>(), TestContext.Current.CancellationToken);
            Assert.Equal(0, result.Succeeded);
            Assert.Equal(0, result.FailedFiles);
        }
    }

    [Fact]
    public async Task DeleteSelectedAsync_when_confirmed_clears_selection_and_deletes()
    {
        var (vm, repo, dialogs, _, dir) = NewVm();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id1 = await InsertClipWithFilesAsync(repo, dir, "x");
            var id2 = await InsertClipWithFilesAsync(repo, dir, "y");
            await vm.InitializeAsync();
            // Manually populate SelectedItems with the two items from the table.
            foreach (var item in vm.Items) vm.SelectedItems.Add(item);
            Assert.Equal(2, vm.SelectedCount);
            dialogs.ConfirmResult = true;

            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.ConfirmCallCount);
            Assert.Equal(0, vm.SelectedCount);
            Assert.Null(await repo.GetClipAsync(id1, ct));
            Assert.Null(await repo.GetClipAsync(id2, ct));
            Assert.Contains("Deleted 2", vm.StatusText);
        }
    }

    [Fact]
    public async Task DeleteSelectedAsync_when_cancelled_keeps_everything()
    {
        var (vm, repo, dialogs, _, dir) = NewVm();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await InsertClipWithFilesAsync(repo, dir, "keep");
            await vm.InitializeAsync();
            foreach (var item in vm.Items) vm.SelectedItems.Add(item);
            dialogs.ConfirmResult = false;

            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.ConfirmCallCount);
            Assert.Equal(1, vm.SelectedCount); // selection intact
            Assert.NotNull(await repo.GetClipAsync(id, ct));
            Assert.True(File.Exists(Path.Combine(dir.Path, "keep.mp4")));
        }
    }

    [Fact]
    public async Task DeleteSelectedAsync_uses_singular_message_for_single_clip()
    {
        var (vm, repo, dialogs, _, dir) = NewVm();
        using (dir)
        {
            await InsertClipWithFilesAsync(repo, dir, "solo");
            await vm.InitializeAsync();
            vm.SelectedItems.Add(vm.Items.Single());

            await vm.DeleteSelectedCommand.ExecuteAsync(null);

            Assert.Equal("Delete 1 clip?", dialogs.LastTitle);
            Assert.Contains("Delete this clip", dialogs.LastMessage);
        }
    }

    [Fact]
    public async Task ArchiveSelectedAsync_with_no_save_path_bails_silently()
    {
        var (vm, repo, dialogs, archiver, dir) = NewVm();
        using (dir)
        {
            await InsertClipWithFilesAsync(repo, dir, "x");
            await vm.InitializeAsync();
            vm.SelectedItems.Add(vm.Items.Single());
            dialogs.SavePickerResult = null;   // user cancelled the picker

            await vm.ArchiveSelectedCommand.ExecuteAsync(null);

            Assert.Equal(0, dialogs.ShowProgressCallCount);
            Assert.Null(archiver.LastClips);
            Assert.Equal(1, vm.SelectedCount); // selection intact on cancel
        }
    }

    [Fact]
    public async Task ArchiveSelectedAsync_with_save_path_calls_archiver_and_clears_selection()
    {
        var (vm, repo, dialogs, archiver, dir) = NewVm();
        using (dir)
        {
            await InsertClipWithFilesAsync(repo, dir, "a");
            await InsertClipWithFilesAsync(repo, dir, "b");
            await vm.InitializeAsync();
            foreach (var item in vm.Items) vm.SelectedItems.Add(item);
            dialogs.SavePickerResult = Path.Combine(dir.Path, "out.zip");

            await vm.ArchiveSelectedCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.ShowProgressCallCount);
            Assert.NotNull(archiver.LastClips);
            Assert.Equal(2, archiver.LastClips!.Count);
            Assert.Equal(dialogs.SavePickerResult, archiver.LastOutputPath);
            Assert.Equal(0, vm.SelectedCount);
            Assert.Contains("Archived 2 clips", vm.StatusText);
        }
    }

    [Fact]
    public async Task ArchiveSelectedAsync_when_cancelled_surfaces_cancelled_status()
    {
        var (vm, repo, dialogs, archiver, dir) = NewVm();
        using (dir)
        {
            await InsertClipWithFilesAsync(repo, dir, "a");
            await InsertClipWithFilesAsync(repo, dir, "b");
            await InsertClipWithFilesAsync(repo, dir, "c");
            await vm.InitializeAsync();
            foreach (var item in vm.Items) vm.SelectedItems.Add(item);
            dialogs.SavePickerResult = Path.Combine(dir.Path, "out.zip");
            // Simulate the user clicking Cancel after 1 clip finished.
            archiver.Handler = (clips, output) => new ArchiveResult(1, 0, 0, output, WasCancelled: true);

            await vm.ArchiveSelectedCommand.ExecuteAsync(null);

            Assert.Contains("cancelled", vm.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1 of 3", vm.StatusText);
        }
    }

    [Fact]
    public async Task ArchiveSelectedAsync_uses_dated_default_filename()
    {
        var (vm, repo, dialogs, _, dir) = NewVm();
        using (dir)
        {
            await InsertClipWithFilesAsync(repo, dir, "x");
            await vm.InitializeAsync();
            vm.SelectedItems.Add(vm.Items.Single());
            dialogs.SavePickerResult = null; // bail before archiver runs

            await vm.ArchiveSelectedCommand.ExecuteAsync(null);

            Assert.NotNull(dialogs.LastSuggestedName);
            Assert.StartsWith("wipelog-clips-", dialogs.LastSuggestedName);
            Assert.EndsWith(".zip", dialogs.LastSuggestedName);
        }
    }
}
