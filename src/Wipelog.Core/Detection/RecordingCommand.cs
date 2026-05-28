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

namespace Wipelog.Core.Detection;

public enum ContentType
{
    RaidEncounter,
    MythicPlus,
    Arena,
    SoloShuffle,
    Battleground,
    RatedBattleground,
    // User-initiated free-form recording triggered from the GUI (not the
    // combat-log state machine). Has no encounter / keystone / instance
    // metadata; only StartedAt + DisplayName + Duration.
    Manual,
}

public sealed record RecordingMetadata(
    ContentType Type,
    string DisplayName,
    int? DifficultyId,
    int? KeystoneLevel,
    int? EncounterId,
    int? InstanceId,
    DateTimeOffset StartedAt,
    string? ArenaBracket = null,
    // M+ only: the CHALLENGE_MODE_START challengeModeId — the stable
    // Blizzard ID for the dungeon used to key DungeonTimers. Null for
    // non-M+ content. Surfaced so the detector can compute key upgrade
    // level when ChallengeModeEnd arrives.
    int? ChallengeModeId = null
);

public abstract record RecordingCommand;

public sealed record StartRecording(
    string SuggestedFilename,
    RecordingMetadata Metadata
) : RecordingCommand;

public sealed record StopRecording(
    bool Success,
    RecordingMetadata Metadata,
    string SuggestedFilename,
    DateTimeOffset EndedAt,
    int? WinningFaction = null,
    // M+ only: 1/2/3 for an in-time finish (chest count), 0 for an
    // over-par finish (depleted), null when the run isn't M+ or the
    // dungeon's par time is unknown to DungeonTimers.
    int? KeyUpgradeLevel = null
) : RecordingCommand;
