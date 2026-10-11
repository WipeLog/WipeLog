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

using Wipelog.Core.Data;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Library;

namespace Wipelog.App.Snapshots;

/// <summary>A believable week of clips across every content type.</summary>
public static class SampleLibrary
{
    public static async Task SeedAsync(SnapshotWorld world, CancellationToken ct)
    {
        var now = SnapshotWorld.Now;
        long n = 0;
        async Task Add(ContentType type, string name, TimeSpan ago, int durationSec, bool success,
            string? difficulty = null, int? encounterId = null, int? key = null,
            string? instance = null, string? dungeon = null, string? bracket = null,
            double? bossHp = null, int? upgrade = null, bool favorite = false, string? notes = null,
            IReadOnlyList<string>? tags = null)
        {
            n++;
            var start = now - ago;
            var stem = $"{start:yyyy-MM-dd_HH-mm-ss}_{n:00}";
            var video = Path.Combine(world.ClipsDirectory, stem + ".mp4");
            await File.WriteAllBytesAsync(video, [], ct);
            var id = await world.Repository.InsertOrUpdateClipAsync(new ClipRecord(
                Id: 0, VideoPath: video, SidecarPath: Path.ChangeExtension(video, ".json"),
                ContentType: type, DisplayName: name, Difficulty: difficulty,
                EncounterId: encounterId, KeystoneLevel: key, Success: success,
                StartedAt: start, EndedAt: start.AddSeconds(durationSec), DurationSeconds: durationSec,
                FileSizeBytes: (long)durationSec * 3_400_000, Favorite: favorite, IndexedAt: now,
                PlayerName: "Wonsupi", PlayerRealm: "DefiasBrotherhood", PlayerRegion: "EU",
                PlayerClass: "Priest", PlayerSpec: "Discipline",
                InstanceName: instance, DungeonName: dungeon,
                EventsSidecarPath: Path.Combine(world.ClipsDirectory, stem + ".events.json"),
                HasEvents: true, ArenaBracket: bracket, Notes: notes,
                BossHpPercent: bossHp, KeyUpgradeLevel: upgrade), ct);
            if (tags is { Count: > 0 }) await world.Repository.SetTagsForClipAsync(id, tags, ct);
        }

        await Add(ContentType.MythicPlus, "Ara-Kara, City of Echoes +12", TimeSpan.FromHours(2), 1712, true,
            key: 12, dungeon: "Ara-Kara, City of Echoes", upgrade: 2, favorite: true,
            notes: "Pull at 04:12 went sideways — kick rotation on Ixin.\nClean boss at 21:40.",
            tags: ["pug", "timed"]);
        await Add(ContentType.MythicPlus, "The Stonevault +11", TimeSpan.FromHours(3.5), 2104, false,
            key: 11, dungeon: "The Stonevault", upgrade: 0);
        await Add(ContentType.MythicPlus, "Priory of the Sacred Flame +10", TimeSpan.FromDays(1.2), 1890, true,
            key: 10, dungeon: "Priory of the Sacred Flame", upgrade: 1);
        await Add(ContentType.MythicPlus, "Operation: Floodgate +13", TimeSpan.FromDays(2.4), 2240, true,
            key: 13, dungeon: "Operation: Floodgate", upgrade: 1, tags: ["guild"]);
        await Add(ContentType.MythicPlus, "Eco-Dome Al'dani +9", TimeSpan.FromDays(5), 1640, true,
            key: 9, dungeon: "Eco-Dome Al'dani", upgrade: 3);
        await Add(ContentType.RaidEncounter, "Plexus Sentinel", TimeSpan.FromDays(1), 312, false,
            difficulty: "Mythic", encounterId: 3129, instance: "Manaforge Omega", bossHp: 12.4,
            tags: ["prog"]);
        await Add(ContentType.RaidEncounter, "Loom'ithar", TimeSpan.FromDays(1.05), 405, true,
            difficulty: "Heroic", encounterId: 3131, instance: "Manaforge Omega");
        await Add(ContentType.Arena, "Nagrand Arena", TimeSpan.FromHours(6), 214, true, bracket: "Arena3v3");
        await Add(ContentType.SoloShuffle, "Tol'viron Arena", TimeSpan.FromHours(7), 1180, true,
            bracket: "Rated Solo Shuffle");
        await Add(ContentType.Battleground, "Warsong Gulch", TimeSpan.FromDays(3), 1020, true);
        await Add(ContentType.Manual, "Manual recording - " + (now - TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm"),
            TimeSpan.FromHours(9), 95, true);
    }

    /// <summary>Deaths, boss markers and a 5-player comp for the timeline / comp strip.</summary>
    public static TimelineEventDocument Events(int durationSec = 312,
        string boss = "Plexus Sentinel", int encounterId = 3129)
    {
        var start = SnapshotWorld.Now.AddHours(-2);
        return new TimelineEventDocument(2, start, start.AddSeconds(durationSec),
        [
            new BossStartEvent(0, encounterId, boss),
            new PlayerDeathEvent(48_200, "Player-1-0A", "Thrallmaster", boss, "Atomize", 812_330),
            new PlayerDeathEvent(131_900, "Player-1-0B", "Frostytånegl", "Arcanomatrix Atomizer", "Eradicating Salvo", 1_204_000),
            new PlayerDeathEvent(133_400, "Player-1-0C", "Wonsupi", boss, "Manifest Matrices", 640_100),
            new PlayerDeathEvent(260_000, "Player-1-0D", "Bubblez", null, null, null),
            new BossEndEvent(durationSec * 1000 - 500, encounterId, boss, false),
        ])
        {
            Combatants =
            [
                Combatant("Player-1-0A", 73),   // Protection Warrior
                Combatant("Player-1-0C", 256),  // Discipline Priest
                Combatant("Player-1-0B", 64),   // Frost Mage
                Combatant("Player-1-0D", 65),   // Holy Paladin
                Combatant("Player-1-0E", 1467), // Devastation Evoker
            ],
        };
    }

    private static CombatantRecord Combatant(string guid, int specId)
    {
        var (cls, spec, role) = SpecMap.Lookup(specId)!.Value;
        return new CombatantRecord(guid, specId, cls, spec, role);
    }
}
