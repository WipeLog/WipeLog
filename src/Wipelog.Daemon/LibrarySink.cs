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

using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Library;

namespace Wipelog.Daemon;

internal sealed class LibrarySink : IClipRecorderSink
{
    private readonly IClipRepository _repo;

    public LibrarySink(IClipRepository repo)
    {
        _repo = repo;
    }

    public Task RecordAsync(ClipFinalization clip, CancellationToken ct)
    {
        var record = new ClipRecord(
            Id: 0,
            VideoPath: clip.VideoPath,
            SidecarPath: clip.SidecarPath,
            ContentType: clip.Type,
            DisplayName: clip.DisplayName,
            Difficulty: clip.Type == ContentType.RaidEncounter
                ? DifficultyMap.DisplayName(clip.DifficultyId)
                : null,
            EncounterId: clip.EncounterId,
            KeystoneLevel: clip.KeystoneLevel,
            Success: clip.Success,
            StartedAt: clip.StartedAt,
            EndedAt: clip.EndedAt,
            DurationSeconds: clip.DurationSeconds,
            FileSizeBytes: clip.FileSizeBytes,
            Favorite: false,
            IndexedAt: DateTimeOffset.UtcNow,
            PlayerName: clip.PlayerName,
            PlayerRealm: clip.PlayerRealm,
            PlayerRegion: clip.PlayerRegion,
            PlayerClass: clip.PlayerClass,
            PlayerSpec: clip.PlayerSpec,
            InstanceName: clip.InstanceName,
            DungeonName: clip.DungeonName,
            EventsSidecarPath: clip.EventsSidecarPath,
            HasEvents: clip.EventsSidecarPath is not null,
            ArenaBracket: clip.ArenaBracket,
            BossHpPercent: clip.BossHpPercent,
            KeyUpgradeLevel: clip.KeyUpgradeLevel);
        return _repo.InsertOrUpdateClipAsync(record, ct);
    }
}
