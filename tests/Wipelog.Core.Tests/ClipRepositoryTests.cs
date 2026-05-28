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
using Wipelog.Core.Detection;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipRepositoryTests
{
    private static (ClipRepository Repo, TempDirectory Dir) NewRepo()
    {
        var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "clips.db");
        var db = new ClipDatabase(dbPath, NullLogger<ClipDatabase>.Instance);
        return (new ClipRepository(db), dir);
    }

    private static ClipRecord MakeClip(
        string videoPath,
        ContentType type = ContentType.RaidEncounter,
        string displayName = "Boss",
        int? difficultyId = 16,
        int? keystoneLevel = null,
        bool success = true,
        DateTimeOffset? startedAt = null,
        int durationSeconds = 300)
    {
        var start = startedAt ?? new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        return new ClipRecord(
            Id: 0,
            VideoPath: videoPath,
            SidecarPath: Path.ChangeExtension(videoPath, ".json"),
            ContentType: type,
            DisplayName: displayName,
            Difficulty: type == ContentType.RaidEncounter
                ? (difficultyId == 16 ? "Mythic" : difficultyId == 15 ? "Heroic" : "Normal")
                : null,
            EncounterId: type == ContentType.RaidEncounter ? 2902 : null,
            KeystoneLevel: keystoneLevel,
            Success: success,
            StartedAt: start,
            EndedAt: start.AddSeconds(durationSeconds),
            DurationSeconds: durationSeconds,
            FileSizeBytes: 1_000_000,
            Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Insert_then_get_round_trips()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/clips/a.mp4"), ct);

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.NotNull(fetched);
            Assert.Equal("/clips/a.mp4", fetched!.VideoPath);
            Assert.Equal(ContentType.RaidEncounter, fetched.ContentType);
            Assert.Equal("Mythic", fetched.Difficulty);
            Assert.True(fetched.Success);
        }
    }

    [Fact]
    public async Task Insert_with_existing_video_path_updates_in_place()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id1 = await repo.InsertOrUpdateClipAsync(
                MakeClip("/clips/a.mp4", displayName: "Original"), ct);
            var id2 = await repo.InsertOrUpdateClipAsync(
                MakeClip("/clips/a.mp4", displayName: "Updated"), ct);
            Assert.Equal(id1, id2);
            var fetched = await repo.GetClipAsync(id1, ct);
            Assert.Equal("Updated", fetched!.DisplayName);
        }
    }

    [Fact]
    public async Task GetOldestNonFavoritedClips_orders_oldest_first_and_excludes_favorites()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var t0 = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
            var oldId = await repo.InsertOrUpdateClipAsync(
                MakeClip("/clips/old.mp4", startedAt: t0), ct);
            var midId = await repo.InsertOrUpdateClipAsync(
                MakeClip("/clips/mid.mp4", startedAt: t0.AddHours(1)), ct);
            var newId = await repo.InsertOrUpdateClipAsync(
                MakeClip("/clips/new.mp4", startedAt: t0.AddHours(2)), ct);
            // Favorite the oldest — it must never be returned.
            await repo.SetFavoriteAsync(oldId, true, ct);

            var result = await repo.GetOldestNonFavoritedClipsAsync(10, ct);

            Assert.Equal(new[] { midId, newId }, result.Select(c => c.Id).ToArray());
        }
    }

    [Fact]
    public async Task GetOldestNonFavoritedClips_respects_max()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var t0 = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
            for (int i = 0; i < 5; i++)
            {
                await repo.InsertOrUpdateClipAsync(
                    MakeClip($"/clips/c{i}.mp4", startedAt: t0.AddMinutes(i)), ct);
            }

            var result = await repo.GetOldestNonFavoritedClipsAsync(2, ct);

            Assert.Equal(2, result.Count);
            Assert.Equal("/clips/c0.mp4", result[0].VideoPath);
            Assert.Equal("/clips/c1.mp4", result[1].VideoPath);
        }
    }

    [Fact]
    public async Task GetCountsByContentTypeAsync_returns_empty_dict_when_no_clips()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var counts = await repo.GetCountsByContentTypeAsync(ct);
            Assert.Empty(counts);
        }
    }

    [Fact]
    public async Task GetCountsByContentTypeAsync_groups_correctly()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(MakeClip("/r1.mp4", ContentType.RaidEncounter), ct);
            await repo.InsertOrUpdateClipAsync(MakeClip("/r2.mp4", ContentType.RaidEncounter), ct);
            await repo.InsertOrUpdateClipAsync(MakeClip("/r3.mp4", ContentType.RaidEncounter), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/m1.mp4", ContentType.MythicPlus, displayName: "x", keystoneLevel: 10), ct);

            var counts = await repo.GetCountsByContentTypeAsync(ct);

            Assert.Equal(3, counts[ContentType.RaidEncounter]);
            Assert.Equal(1, counts[ContentType.MythicPlus]);
        }
    }

    [Fact]
    public async Task Query_filters_by_content_type()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(MakeClip("/raid.mp4", ContentType.RaidEncounter), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/m+.mp4", ContentType.MythicPlus, displayName: "Stonevault", keystoneLevel: 12), ct);

            var raids = await repo.QueryClipsAsync(
                new ClipQuery(ContentTypes: new[] { ContentType.RaidEncounter }), ct);
            var mplus = await repo.QueryClipsAsync(
                new ClipQuery(ContentTypes: new[] { ContentType.MythicPlus }), ct);

            Assert.Single(raids);
            Assert.Single(mplus);
            Assert.Equal(ContentType.MythicPlus, mplus[0].ContentType);
        }
    }

    [Fact]
    public async Task Query_filters_by_success()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(MakeClip("/kill.mp4", success: true), ct);
            await repo.InsertOrUpdateClipAsync(MakeClip("/wipe.mp4", success: false), ct);

            var kills = await repo.QueryClipsAsync(new ClipQuery(Success: true), ct);
            var wipes = await repo.QueryClipsAsync(new ClipQuery(Success: false), ct);

            Assert.Single(kills);
            Assert.Single(wipes);
            Assert.True(kills[0].Success);
            Assert.False(wipes[0].Success);
        }
    }

    [Fact]
    public async Task Query_filters_by_favorites_only()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var fav = await repo.InsertOrUpdateClipAsync(MakeClip("/fav.mp4"), ct);
            await repo.InsertOrUpdateClipAsync(MakeClip("/notfav.mp4"), ct);
            await repo.SetFavoriteAsync(fav, true, ct);

            var favorites = await repo.QueryClipsAsync(new ClipQuery(FavoritesOnly: true), ct);

            Assert.Single(favorites);
            Assert.True(favorites[0].Favorite);
        }
    }

    [Fact]
    public async Task Query_filters_by_display_name_substring()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(MakeClip("/u.mp4", displayName: "Ulgrax the Devourer"), ct);
            await repo.InsertOrUpdateClipAsync(MakeClip("/v.mp4", displayName: "Vexie"), ct);

            var hits = await repo.QueryClipsAsync(new ClipQuery(DisplayNameContains: "ulgrax"), ct);

            Assert.Single(hits);
            Assert.Contains("Ulgrax", hits[0].DisplayName);
        }
    }

    [Fact]
    public async Task Query_filters_by_min_keystone_level()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/k10.mp4", ContentType.MythicPlus, displayName: "X", keystoneLevel: 10), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/k15.mp4", ContentType.MythicPlus, displayName: "Y", keystoneLevel: 15), ct);

            var high = await repo.QueryClipsAsync(new ClipQuery(MinKeystoneLevel: 12), ct);

            Assert.Single(high);
            Assert.Equal(15, high[0].KeystoneLevel);
        }
    }

    [Fact]
    public async Task Query_filters_by_difficulty()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(MakeClip("/m.mp4", difficultyId: 16), ct);
            await repo.InsertOrUpdateClipAsync(MakeClip("/h.mp4", difficultyId: 15), ct);

            var mythics = await repo.QueryClipsAsync(new ClipQuery(Difficulty: "Mythic"), ct);

            Assert.Single(mythics);
        }
    }

    [Fact]
    public async Task Query_filters_by_since_date()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/old.mp4", startedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/new.mp4", startedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)), ct);

            var recent = await repo.QueryClipsAsync(
                new ClipQuery(Since: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)), ct);

            Assert.Single(recent);
        }
    }

    [Fact]
    public async Task Arena_bracket_round_trips_through_insert_and_query()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var clip = MakeClip("/arena.mp4", ContentType.Arena, displayName: "Arena3v3")
                with { ArenaBracket = "Arena3v3" };

            var id = await repo.InsertOrUpdateClipAsync(clip, ct);
            var fetched = await repo.GetClipAsync(id, ct);

            Assert.NotNull(fetched);
            Assert.Equal("Arena3v3", fetched!.ArenaBracket);
            Assert.Equal(ContentType.Arena, fetched.ContentType);
        }
    }

    [Fact]
    public async Task Null_arena_bracket_round_trips_as_null()
    {
        // Sanity: clips that aren't PvP should leave the column null on
        // round-trip, not "" or any default.
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/raid.mp4"), ct);
            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Null(fetched!.ArenaBracket);
        }
    }

    [Fact]
    public async Task Notes_round_trips_through_insert_and_query()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var clip = MakeClip("/notes.mp4") with { Notes = "0:42 missed Cinderbrew" };

            var id = await repo.InsertOrUpdateClipAsync(clip, ct);
            var fetched = await repo.GetClipAsync(id, ct);

            Assert.Equal("0:42 missed Cinderbrew", fetched!.Notes);
        }
    }

    [Fact]
    public async Task Null_notes_round_trips_as_null()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/empty.mp4"), ct);
            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Null(fetched!.Notes);
        }
    }

    [Fact]
    public async Task SetNotesAsync_persists_and_round_trips()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/n.mp4"), ct);

            await repo.SetNotesAsync(id, "later thought:\n  1:23 wrong stack", ct);
            var fetched = await repo.GetClipAsync(id, ct);

            Assert.Equal("later thought:\n  1:23 wrong stack", fetched!.Notes);
        }
    }

    [Fact]
    public async Task SetNotesAsync_with_whitespace_clears_to_null()
    {
        // Normalize "user cleared the textarea" (whitespace) and "never typed"
        // (null) to the same on-disk state so the read path doesn't have to
        // distinguish them.
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(
                MakeClip("/n.mp4") with { Notes = "original" }, ct);
            Assert.Equal("original", (await repo.GetClipAsync(id, ct))!.Notes);

            await repo.SetNotesAsync(id, "   \n  ", ct);
            Assert.Null((await repo.GetClipAsync(id, ct))!.Notes);
        }
    }

    [Fact]
    public async Task Query_filters_by_before_date()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/old.mp4", startedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/new.mp4", startedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)), ct);

            // Half-open: clips whose started_at is < @Before.
            var older = await repo.QueryClipsAsync(
                new ClipQuery(Before: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)), ct);

            Assert.Single(older);
            Assert.Equal("/old.mp4", older[0].VideoPath);
        }
    }

    [Fact]
    public async Task Query_filters_by_since_and_before_returns_half_open_interval()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            // Three clips spaced across the spectrum.
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/a.mp4", startedAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/b.mp4", startedAt: new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/c.mp4", startedAt: new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero)), ct);

            // [Mar 5, Mar 15) → only b on Mar 10.
            var window = await repo.QueryClipsAsync(
                new ClipQuery(
                    Since: new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
                    Before: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero)),
                ct);

            Assert.Single(window);
            Assert.Equal("/b.mp4", window[0].VideoPath);
        }
    }

    [Fact]
    public async Task Query_with_before_exactly_on_boundary_excludes_that_clip()
    {
        // Defensive check that the < (not <=) bound holds — a clip stamped at
        // exactly @Before must NOT be returned.
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var boundary = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
            await repo.InsertOrUpdateClipAsync(MakeClip("/on.mp4", startedAt: boundary), ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/before.mp4", startedAt: boundary.AddSeconds(-1)), ct);

            var rows = await repo.QueryClipsAsync(new ClipQuery(Before: boundary), ct);

            Assert.Single(rows);
            Assert.Equal("/before.mp4", rows[0].VideoPath);
        }
    }

    [Fact]
    public async Task Query_filters_by_any_of_tags()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var a = await repo.InsertOrUpdateClipAsync(MakeClip("/a.mp4"), ct);
            var b = await repo.InsertOrUpdateClipAsync(MakeClip("/b.mp4"), ct);
            await repo.SetTagsForClipAsync(a, new[] { "prog" }, ct);
            await repo.SetTagsForClipAsync(b, new[] { "farm" }, ct);

            var prog = await repo.QueryClipsAsync(
                new ClipQuery(AnyOfTags: new[] { "prog" }), ct);

            Assert.Single(prog);
            Assert.Equal("/a.mp4", prog[0].VideoPath);
        }
    }

    [Fact]
    public async Task Query_orders_by_started_at_desc_and_paginates()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 5; i++)
            {
                var ts = new DateTimeOffset(2026, 5, 1, 10, i, 0, TimeSpan.Zero);
                await repo.InsertOrUpdateClipAsync(MakeClip($"/c{i}.mp4", startedAt: ts), ct);
            }

            var page1 = await repo.QueryClipsAsync(new ClipQuery(Limit: 2, Offset: 0), ct);
            var page2 = await repo.QueryClipsAsync(new ClipQuery(Limit: 2, Offset: 2), ct);

            Assert.Equal(2, page1.Count);
            Assert.Equal(2, page2.Count);
            Assert.True(page1[0].StartedAt > page1[1].StartedAt);
            Assert.True(page1[1].StartedAt > page2[0].StartedAt);
        }
    }

    [Fact]
    public async Task Set_tags_replaces_full_set_and_creates_new_tags()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/a.mp4"), ct);
            await repo.SetTagsForClipAsync(id, new[] { "Prog", "Farm" }, ct);

            var first = await repo.GetTagsForClipAsync(id, ct);
            Assert.Equal(new[] { "farm", "prog" }, first.OrderBy(t => t));

            await repo.SetTagsForClipAsync(id, new[] { "kill" }, ct);

            var second = await repo.GetTagsForClipAsync(id, ct);
            Assert.Equal(new[] { "kill" }, second);

            var all = await repo.GetAllTagsAsync(ct);
            Assert.Contains("kill", all);
            Assert.Contains("prog", all);
            Assert.Contains("farm", all);
        }
    }

    [Fact]
    public async Task GetTagsForClips_returns_per_clip_lists_in_one_query()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var a = await repo.InsertOrUpdateClipAsync(MakeClip("/a.mp4"), ct);
            var b = await repo.InsertOrUpdateClipAsync(MakeClip("/b.mp4"), ct);
            var c = await repo.InsertOrUpdateClipAsync(MakeClip("/c.mp4"), ct);
            await repo.SetTagsForClipAsync(a, new[] { "prog", "two-night" }, ct);
            await repo.SetTagsForClipAsync(b, new[] { "farm" }, ct);

            var dict = await repo.GetTagsForClipsAsync(new[] { a, b, c }, ct);

            Assert.Equal(2, dict[a].Count);
            Assert.Single(dict[b]);
            Assert.False(dict.ContainsKey(c));
        }
    }

    [Fact]
    public async Task Query_filters_by_player_name()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/a.mp4") with { PlayerName = "Frostytanegl" }, ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/b.mp4") with { PlayerName = "Holytotem" }, ct);

            var hits = await repo.QueryClipsAsync(new ClipQuery(PlayerName: "Frostytanegl"), ct);

            Assert.Single(hits);
            Assert.Equal("Frostytanegl", hits[0].PlayerName);
        }
    }

    [Fact]
    public async Task Query_filters_by_class_and_spec()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/m.mp4") with { PlayerClass = "Mage", PlayerSpec = "Frost" }, ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/d.mp4") with { PlayerClass = "Druid", PlayerSpec = "Restoration" }, ct);

            var mages = await repo.QueryClipsAsync(
                new ClipQuery(PlayerClass: "Mage", PlayerSpec: "Frost"), ct);

            Assert.Single(mages);
        }
    }

    [Fact]
    public async Task Query_filters_by_dungeon_name()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/sv.mp4", ContentType.MythicPlus, displayName: "Stonevault", keystoneLevel: 12)
                    with { DungeonName = "The Stonevault" }, ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/wc.mp4", ContentType.MythicPlus, displayName: "Cinderbrew", keystoneLevel: 10)
                    with { DungeonName = "Cinderbrew Meadery" }, ct);

            var hits = await repo.QueryClipsAsync(new ClipQuery(DungeonName: "The Stonevault"), ct);
            Assert.Single(hits);
            Assert.Equal("The Stonevault", hits[0].DungeonName);
        }
    }

    [Fact]
    public async Task Query_filters_by_instance_name()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/a.mp4") with { InstanceName = "Liberation of Undermine" }, ct);
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/b.mp4") with { InstanceName = "Nerub-ar Palace" }, ct);

            var hits = await repo.QueryClipsAsync(
                new ClipQuery(InstanceName: "Liberation of Undermine"), ct);
            Assert.Single(hits);
        }
    }

    [Fact]
    public async Task Delete_removes_row_and_files_if_present()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var video = Path.Combine(dir.Path, "x.mp4");
            var sidecar = Path.Combine(dir.Path, "x.json");
            var eventsSidecar = Path.Combine(dir.Path, "x.events.json");
            await File.WriteAllTextAsync(video, "fake", ct);
            await File.WriteAllTextAsync(sidecar, "{}", ct);
            await File.WriteAllTextAsync(eventsSidecar, "{\"version\":2}", ct);
            var id = await repo.InsertOrUpdateClipAsync(MakeClip(video), ct);

            await repo.DeleteClipAsync(id, ct);

            Assert.Null(await repo.GetClipAsync(id, ct));
            Assert.False(File.Exists(video));
            Assert.False(File.Exists(sidecar));
            Assert.False(File.Exists(eventsSidecar));
        }
    }

    [Fact]
    public async Task Delete_with_missing_files_does_not_throw()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/missing.mp4"), ct);
            await repo.DeleteClipAsync(id, ct);
            Assert.Null(await repo.GetClipAsync(id, ct));
        }
    }

    [Fact]
    public async Task UpdateClipMetadata_sets_new_values()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/m.mp4"), ct);

            await repo.UpdateClipMetadataAsync(id, new ClipMetadataPatch(
                PlayerName: "Frostytånegl",
                PlayerRealm: "Stormscale",
                PlayerClass: "Mage",
                PlayerSpec: "Frost",
                Success: false), ct);

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.NotNull(fetched);
            Assert.Equal("Frostytånegl", fetched!.PlayerName);
            Assert.Equal("Stormscale", fetched.PlayerRealm);
            Assert.Equal("Mage", fetched.PlayerClass);
            Assert.Equal("Frost", fetched.PlayerSpec);
            Assert.False(fetched.Success);
        }
    }

    [Fact]
    public async Task UpdateClipMetadata_partial_patch_leaves_other_fields_alone()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(
                MakeClip("/m.mp4") with
                {
                    PlayerName = "Original",
                    PlayerClass = "Druid",
                    PlayerSpec = "Restoration",
                }, ct);

            await repo.UpdateClipMetadataAsync(id, new ClipMetadataPatch(PlayerName: "Updated"), ct);

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Equal("Updated", fetched!.PlayerName);
            Assert.Equal("Druid", fetched.PlayerClass); // untouched
            Assert.Equal("Restoration", fetched.PlayerSpec); // untouched
        }
    }

    [Fact]
    public async Task UpdateClipMetadata_writes_changes_to_sidecar_json()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var video = Path.Combine(dir.Path, "x.mp4");
            var sidecar = Path.Combine(dir.Path, "x.json");
            await File.WriteAllTextAsync(video, "fake", ct);
            // Minimal v2 sidecar with no identity fields.
            await File.WriteAllTextAsync(sidecar,
                "{\n" +
                "  \"version\": 2,\n" +
                "  \"contentType\": \"RaidEncounter\",\n" +
                "  \"displayName\": \"Boss\",\n" +
                "  \"success\": true,\n" +
                "  \"startedAt\": \"2026-05-01T10:00:00+00:00\",\n" +
                "  \"endedAt\": \"2026-05-01T10:05:00+00:00\",\n" +
                "  \"durationSeconds\": 300,\n" +
                "  \"videoFile\": \"x.mp4\"\n" +
                "}", ct);
            var id = await repo.InsertOrUpdateClipAsync(MakeClip(video), ct);

            await repo.UpdateClipMetadataAsync(id, new ClipMetadataPatch(
                PlayerName: "Frostytånegl",
                PlayerClass: "Mage",
                PlayerSpec: "Frost",
                Success: false), ct);

            var json = await File.ReadAllTextAsync(sidecar, ct);
            Assert.Contains("\"playerName\": \"Frostytånegl\"", json);
            Assert.Contains("\"playerClass\": \"Mage\"", json);
            Assert.Contains("\"playerSpec\": \"Frost\"", json);
            Assert.Contains("\"success\": false", json);
            // Existing fields untouched.
            Assert.Contains("\"displayName\": \"Boss\"", json);
            Assert.Contains("\"videoFile\": \"x.mp4\"", json);
        }
    }

    [Fact]
    public async Task UpdateClipMetadata_with_missing_sidecar_still_updates_db()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/no-sidecar.mp4"), ct);

            await repo.UpdateClipMetadataAsync(id, new ClipMetadataPatch(PlayerName: "X"), ct);

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Equal("X", fetched!.PlayerName);
        }
    }

    [Fact]
    public async Task Region_round_trips_through_player_name_filter()
    {
        // Region is still stored on the clip record even though there's no
        // distinct-region listing API or region-filter UI anymore. Filter by
        // player name to prove the round trip.
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            await repo.InsertOrUpdateClipAsync(
                MakeClip("/eu1.mp4") with { PlayerName = "A", PlayerRealm = "Stormscale", PlayerRegion = "EU" }, ct);

            var clip = await repo.GetClipByVideoPathAsync("/eu1.mp4", ct);
            Assert.NotNull(clip);
            Assert.Equal("EU", clip!.PlayerRegion);
        }
    }

    [Fact]
    public async Task UpdateClipMetadata_can_set_region()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(MakeClip("/m.mp4"), ct);

            await repo.UpdateClipMetadataAsync(id,
                new ClipMetadataPatch(PlayerRegion: "EU"), ct);

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Equal("EU", fetched!.PlayerRegion);
        }
    }

    [Fact]
    public async Task UpdateClipMetadata_with_no_fields_set_is_noop()
    {
        var (repo, dir) = NewRepo();
        using (dir)
        {
            var ct = TestContext.Current.CancellationToken;
            var id = await repo.InsertOrUpdateClipAsync(
                MakeClip("/m.mp4") with { PlayerName = "Original" }, ct);

            await repo.UpdateClipMetadataAsync(id, new ClipMetadataPatch(), ct);

            var fetched = await repo.GetClipAsync(id, ct);
            Assert.Equal("Original", fetched!.PlayerName);
        }
    }
}
