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

using System.Text.Json;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Data;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class TimelineEventsRoundTripTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 5, 10, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void V2_document_with_combatants_round_trips()
    {
        var original = new TimelineEventDocument(
            Version: 2,
            RecordingStartedAt: Start,
            RecordingEndedAt: Start.AddMinutes(5),
            Events: new TimelineEvent[]
            {
                new PlayerDeathEvent(45_000, "Player-1-A", "Anya", "Boss", "Cleave", 9_999),
                new BossStartEvent(30_000, 2906, "MidBoss"),
                new BossEndEvent(60_000, 2906, "MidBoss", Success: true),
            })
        {
            Combatants = new[]
            {
                new CombatantRecord("Player-1-A", 64,   "Mage",         "Frost",        SpecRole.Dps),
                new CombatantRecord("Player-2-B", 581,  "Demon Hunter", "Vengeance",    SpecRole.Tank),
                new CombatantRecord("Player-3-C", 1468, "Evoker",       "Preservation", SpecRole.Healer),
            },
        };

        var json = JsonSerializer.Serialize(original, TimelineEventsJson.Options);
        var read = JsonSerializer.Deserialize<TimelineEventDocument>(json, TimelineEventsJson.Options);

        Assert.NotNull(read);
        Assert.Equal(2, read!.Version);
        Assert.Equal(3, read.Combatants.Count);
        Assert.Equal(SpecRole.Tank, read.Combatants[1].Role);
        Assert.Equal("Demon Hunter", read.Combatants[1].Class);
        Assert.Equal(3, read.Events.Count);
        var bossEnd = Assert.IsType<BossEndEvent>(read.Events[2]);
        Assert.Equal(2906, bossEnd.EncounterId);
        Assert.True(bossEnd.Success);
    }

    [Fact]
    public void V1_document_without_combatants_field_deserialises_with_empty_list()
    {
        // Hand-written v1 sidecar: no "combatants" field. This is the exact
        // shape Wipelog wrote before schema v2. Backward compat is the whole
        // point of the init-only default.
        var v1Json = """
        {
          "version": 1,
          "recordingStartedAt": "2026-05-10T17:00:00+00:00",
          "recordingEndedAt": "2026-05-10T17:05:00+00:00",
          "events": [
            {
              "kind": "PlayerDeath",
              "offsetMs": 45000,
              "playerGuid": "Player-1-A",
              "playerName": "Anya",
              "killedBySource": "Boss",
              "killedBySpell": "Cleave",
              "killingBlowAmount": 9999
            }
          ]
        }
        """;

        var read = JsonSerializer.Deserialize<TimelineEventDocument>(v1Json, TimelineEventsJson.Options);

        Assert.NotNull(read);
        Assert.Equal(1, read!.Version);
        Assert.NotNull(read.Combatants);
        Assert.Empty(read.Combatants);
        Assert.Single(read.Events);
    }

    [Fact]
    public void SpecRole_serialises_as_string()
    {
        // String form is more robust for the sidecar — if SpecRole gains a
        // value later (e.g. Support) we want existing readers to surface
        // the unknown name rather than silently mis-decode an int.
        var doc = new TimelineEventDocument(
            Version: 2,
            RecordingStartedAt: Start,
            RecordingEndedAt: Start.AddMinutes(1),
            Events: Array.Empty<TimelineEvent>())
        {
            Combatants = new[]
            {
                new CombatantRecord("Player-1-X", 250, "Death Knight", "Blood", SpecRole.Tank),
            },
        };

        var json = JsonSerializer.Serialize(doc, TimelineEventsJson.Options);

        Assert.Contains("\"role\": \"Tank\"", json);
        Assert.DoesNotContain("\"role\": 0", json);
    }

    [Fact]
    public void Build_emits_v2_document_with_resolved_combatant_class_spec_role()
    {
        var buf = new RecordingEventBuffer(Start, TimeSpan.FromSeconds(5));
        buf.Observe(new CombatantInfo(Start, "Player-1-MAGE",  SpecId: 64));     // Frost Mage
        buf.Observe(new CombatantInfo(Start, "Player-2-TANK",  SpecId: 581));    // Vengeance DH
        buf.Observe(new CombatantInfo(Start, "Player-3-HEAL",  SpecId: 105));    // Restoration Druid

        var doc = buf.Build(Start.AddMinutes(5));

        Assert.Equal(2, doc.Version);
        Assert.Equal(3, doc.Combatants.Count);

        var dh = doc.Combatants.Single(c => c.PlayerGuid == "Player-2-TANK");
        Assert.Equal(581, dh.SpecId);
        Assert.Equal("Demon Hunter", dh.Class);
        Assert.Equal("Vengeance", dh.Spec);
        Assert.Equal(SpecRole.Tank, dh.Role);
    }

    [Fact]
    public void PreRollSeconds_round_trips_through_json()
    {
        var original = new TimelineEventDocument(
            Version: 2,
            RecordingStartedAt: Start,
            RecordingEndedAt: Start.AddMinutes(5),
            Events: Array.Empty<TimelineEvent>())
        {
            PreRollSeconds = 30.0,
        };

        var json = JsonSerializer.Serialize(original, TimelineEventsJson.Options);
        Assert.Contains("\"preRollSeconds\": 30", json);

        var read = JsonSerializer.Deserialize<TimelineEventDocument>(json, TimelineEventsJson.Options);
        Assert.Equal(30.0, read!.PreRollSeconds);
    }

    [Fact]
    public void PreRollSeconds_is_omitted_when_null()
    {
        // DefaultIgnoreCondition = WhenWritingNull keeps the field out so
        // pre-roll-aware readers don't get a phantom "0" for non-pre-roll clips.
        var doc = new TimelineEventDocument(
            Version: 2,
            RecordingStartedAt: Start,
            RecordingEndedAt: Start.AddMinutes(5),
            Events: Array.Empty<TimelineEvent>());

        var json = JsonSerializer.Serialize(doc, TimelineEventsJson.Options);
        Assert.DoesNotContain("preRollSeconds", json);
    }

    [Fact]
    public void V1_or_v2_sidecar_without_preRollSeconds_deserialises_as_null()
    {
        // Backwards compat: a v2 sidecar written before the pre-roll field
        // existed has no "preRollSeconds" key. Readers see it as null and
        // skip the offset shift.
        var v2Json = """
        {
          "version": 2,
          "recordingStartedAt": "2026-05-10T17:00:00+00:00",
          "recordingEndedAt": "2026-05-10T17:05:00+00:00",
          "events": [],
          "combatants": []
        }
        """;

        var read = JsonSerializer.Deserialize<TimelineEventDocument>(v2Json, TimelineEventsJson.Options);

        Assert.NotNull(read);
        Assert.Null(read!.PreRollSeconds);
    }

    [Fact]
    public void Build_preserves_unknown_spec_id_with_null_class_spec_role()
    {
        // Forward compat: an unknown spec ID (e.g. a new expansion spec)
        // still produces a record so the data isn't lost — just opaque.
        var buf = new RecordingEventBuffer(Start, TimeSpan.FromSeconds(5));
        buf.Observe(new CombatantInfo(Start, "Player-NEW", SpecId: 99999));

        var doc = buf.Build(Start.AddMinutes(1));

        var rec = Assert.Single(doc.Combatants);
        Assert.Equal(99999, rec.SpecId);
        Assert.Null(rec.Class);
        Assert.Null(rec.Spec);
        Assert.Null(rec.Role);
    }
}
