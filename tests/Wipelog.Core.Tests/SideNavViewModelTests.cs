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
using Wipelog.Core.Status;
using Wipelog.Core.Time;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class SideNavViewModelTests
{
    private static (SideNavViewModel Nav, ClipFiltersViewModel Filters, StubRepo Repo, TempDirectory Dir)
        NewVm()
    {
        var dir = new TempDirectory();
        var repo = new StubRepo();
        var filters = new ClipFiltersViewModel();
        var daemon = NewDaemonStatusVm();
        var nav = new SideNavViewModel(repo, filters, daemon, NullLogger<SideNavViewModel>.Instance);
        return (nav, filters, repo, dir);
    }

    private static DaemonStatusViewModel NewDaemonStatusVm() =>
        new(new StubDaemonStatusReader(), new StubDaemonLifecycle(), new StubDaemonControl(),
            new FakeClock(DateTimeOffset.UtcNow),
            NullLogger<DaemonStatusViewModel>.Instance);

    [Fact]
    public void Default_active_category_is_mythic_plus()
    {
        var (nav, filters, _, dir) = NewVm();
        using (dir)
        {
            Assert.Equal(ContentTypeFilter.MythicPlus, nav.ActiveCategory);
            Assert.Equal(ContentTypeFilter.MythicPlus, filters.ContentType);
        }
    }

    [Fact]
    public void Active_category_change_propagates_to_filters_content_type()
    {
        var (nav, filters, _, dir) = NewVm();
        using (dir)
        {
            nav.ActiveCategory = ContentTypeFilter.Raid;
            Assert.Equal(ContentTypeFilter.Raid, filters.ContentType);

            nav.ActiveCategory = ContentTypeFilter.Battleground;
            Assert.Equal(ContentTypeFilter.Battleground, filters.ContentType);
        }
    }

    [Fact]
    public void External_filter_change_reflects_back_into_active_category()
    {
        var (nav, filters, _, dir) = NewVm();
        using (dir)
        {
            filters.ContentType = ContentTypeFilter.Clip;
            Assert.Equal(ContentTypeFilter.Clip, nav.ActiveCategory);
        }
    }

    [Fact]
    public async Task Refresh_counts_pulls_m_plus_and_raid_from_repo()
    {
        var (nav, _, repo, dir) = NewVm();
        using (dir)
        {
            repo.Counts[ContentType.MythicPlus] = 27;
            repo.Counts[ContentType.RaidEncounter] = 294;

            await nav.RefreshCountsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(27, nav.MythicPlusCount);
            Assert.Equal(294, nav.RaidCount);
            Assert.Equal(0, nav.BattlegroundCount);
            Assert.Equal(0, nav.ClipCount);
        }
    }

    [Fact]
    public async Task Refresh_counts_zero_when_repo_empty()
    {
        var (nav, _, _, dir) = NewVm();
        using (dir)
        {
            await nav.RefreshCountsAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, nav.MythicPlusCount);
            Assert.Equal(0, nav.RaidCount);
        }
    }

    [Fact]
    public void OpenSettings_command_raises_event()
    {
        var (nav, _, _, dir) = NewVm();
        using (dir)
        {
            var fired = 0;
            nav.OpenSettingsRequested += (_, _) => fired++;
            nav.OpenSettingsCommand.Execute(null);
            Assert.Equal(1, fired);
        }
    }

    [Fact]
    public void OpenScene_command_raises_event()
    {
        var (nav, _, _, dir) = NewVm();
        using (dir)
        {
            var fired = 0;
            nav.SceneStubRequested += (_, _) => fired++;
            nav.OpenSceneCommand.Execute(null);
            Assert.Equal(1, fired);
        }
    }

    private sealed class StubRepo : IClipRepository
    {
        public Dictionary<ContentType, int> Counts { get; } = new();

        public Task<IReadOnlyDictionary<ContentType, int>> GetCountsByContentTypeAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ContentType, int>>(Counts);

        public Task<long> InsertOrUpdateClipAsync(ClipRecord clip, CancellationToken ct) => Task.FromResult(0L);
        public Task<ClipRecord?> GetClipAsync(long id, CancellationToken ct) => Task.FromResult<ClipRecord?>(null);
        public Task<ClipRecord?> GetClipByVideoPathAsync(string videoPath, CancellationToken ct) => Task.FromResult<ClipRecord?>(null);
        public Task<IReadOnlyList<ClipRecord>> QueryClipsAsync(ClipQuery query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClipRecord>>(Array.Empty<ClipRecord>());
        public Task<IReadOnlyList<ClipRecord>> GetOldestNonFavoritedClipsAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClipRecord>>(Array.Empty<ClipRecord>());
        public Task SetFavoriteAsync(long clipId, bool favorite, CancellationToken ct) => Task.CompletedTask;
        public Task SetNotesAsync(long clipId, string? notes, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetTagsForClipAsync(long clipId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task SetTagsForClipAsync(long clipId, IReadOnlyList<string> tagNames, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> GetTagsForClipsAsync(IReadOnlyList<long> clipIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<string>>>(new Dictionary<long, IReadOnlyList<string>>());
        public Task<IReadOnlyList<string>> GetDistinctPlayerNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> GetDistinctPlayerRegionsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<(string Class, string Spec)>> GetDistinctSpecsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(string, string)>>(Array.Empty<(string, string)>());
        public Task<IReadOnlyList<string>> GetDistinctDungeonNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<string>> GetDistinctInstanceNamesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task DeleteClipAsync(long clipId, CancellationToken ct) => Task.CompletedTask;
        public Task<BulkDeleteResult> DeleteClipsAsync(IReadOnlyList<long> ids, CancellationToken ct) =>
            Task.FromResult(new BulkDeleteResult(0, 0));
        public Task UpdateClipMetadataAsync(long clipId, ClipMetadataPatch patch, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubDaemonStatusReader : IDaemonStatusReader
    {
        public DaemonStatus? Current => null;
        public event EventHandler<DaemonStatus?>? StatusChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class StubDaemonLifecycle : IDaemonLifecycleController
    {
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RestartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task OpenJournalAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubDaemonControl : IDaemonControlClient
    {
        public Task<ControlCallResult> StopRecordingAsync(CancellationToken ct) =>
            Task.FromResult(new ControlCallResult(ControlCallStatus.Unreachable));
        public Task<ControlCallResult> StartManualRecordingAsync(CancellationToken ct) =>
            Task.FromResult(new ControlCallResult(ControlCallStatus.Unreachable));
    }
}
