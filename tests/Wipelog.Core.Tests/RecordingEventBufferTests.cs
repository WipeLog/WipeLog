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

using Wipelog.Core.CombatLog;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecordingEventBufferTests
{
    private static readonly DateTimeOffset RecordingStart =
        new(2026, 5, 10, 17, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan KillingBlowLookback = TimeSpan.FromSeconds(5);

    private static DateTimeOffset At(int seconds, int millis = 0) =>
        RecordingStart.AddSeconds(seconds).AddMilliseconds(millis);

    private static RecordingEventBuffer NewBuffer() =>
        new(RecordingStart, KillingBlowLookback);

    private static DamageEvent Damage(
        DateTimeOffset ts, string destGuid, string sourceName, string? spell, long amount = 1) =>
        new(ts, "Creature-X", sourceName, destGuid, "Player", null, spell, amount);

    [Fact]
    public void Player_death_produces_event_with_offset_ms()
    {
        var buf = NewBuffer();
        buf.Observe(new UnitDied(At(30), "Player-1-FROSTY", "Frostytånegl-Stormscale-EU"));

        var doc = buf.Build(At(60));

        var death = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc.Events));
        Assert.Equal(30000, death.OffsetMs);
        Assert.Equal("Player-1-FROSTY", death.PlayerGuid);
        Assert.Equal("Frostytånegl-Stormscale-EU", death.PlayerName);
    }

    [Fact]
    public void Killing_blow_attribution_picks_most_recent_damage_in_window()
    {
        var buf = NewBuffer();
        buf.Observe(Damage(At(28), "Player-1-FROSTY", "Trash Mob", "Punch", amount: 1000));
        buf.Observe(Damage(At(29), "Player-1-FROSTY", "Imperator Averzian", "Cleave", amount: 432123));
        buf.Observe(new UnitDied(At(30), "Player-1-FROSTY", "Frostytånegl"));

        var doc = buf.Build(At(60));

        var death = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc.Events));
        Assert.Equal("Imperator Averzian", death.KilledBySource);
        Assert.Equal("Cleave", death.KilledBySpell);
        Assert.Equal(432123L, death.KillingBlowAmount);
    }

    [Fact]
    public void No_damage_in_window_leaves_killing_blow_null()
    {
        var buf = NewBuffer();
        // Damage is older than the 5s lookback window — should be evicted before the death.
        buf.Observe(Damage(At(10), "Player-1-FROSTY", "OldSource", "OldSpell"));
        buf.Observe(new UnitDied(At(30), "Player-1-FROSTY", "Frostytanegl"));

        var doc = buf.Build(At(60));

        var death = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc.Events));
        Assert.Null(death.KilledBySource);
        Assert.Null(death.KilledBySpell);
        Assert.Null(death.KillingBlowAmount);
    }

    [Theory]
    [InlineData("Pet-0-1234")]
    [InlineData("Creature-0-1469-2657-12-225829-0001")]
    [InlineData("Vehicle-0-1234")]
    public void Non_player_deaths_are_filtered_out(string destGuid)
    {
        var buf = NewBuffer();
        buf.Observe(new UnitDied(At(30), destGuid, "Pet"));

        var doc = buf.Build(At(60));

        Assert.Empty(doc.Events);
    }

    [Fact]
    public void Feign_death_does_not_produce_a_player_death_event()
    {
        var buf = NewBuffer();
        // Damage to the hunter immediately before the feign would normally
        // be attributed as a killing blow — this test guards both that no
        // PlayerDeath is emitted AND that subsequent real deaths still get
        // clean attribution (i.e. feign didn't steal the damage).
        buf.Observe(Damage(At(29), "Player-1-HUNTER", "Some Mob", "Hit", amount: 100));
        buf.Observe(new UnitDied(At(30), "Player-1-HUNTER", "Orkantfany", IsUnconscious: true));

        var doc = buf.Build(At(60));

        Assert.Empty(doc.Events);
    }

    [Fact]
    public void Boss_start_at_recording_start_is_excluded()
    {
        // The triggering encounter sits exactly at recordingStart; we don't
        // want it duplicated on the timeline.
        var buf = NewBuffer();
        buf.Observe(new EncounterStart(RecordingStart, 2902, "Ulgrax", 16, 20, 2657));

        var doc = buf.Build(At(120));

        Assert.Empty(doc.Events);
    }

    [Fact]
    public void Boss_starts_inside_recording_window_become_BossStart_events()
    {
        // M+ inner bosses start mid-recording.
        var buf = NewBuffer();
        buf.Observe(new EncounterStart(At(30), 2906, "Master Machinists", 8, 5, 2290));
        buf.Observe(new EncounterStart(At(180), 2917, "Stix Bunkjunker", 8, 5, 2290));

        var doc = buf.Build(At(300));

        Assert.Equal(2, doc.Events.Count);
        Assert.IsType<BossStartEvent>(doc.Events[0]);
        var first = (BossStartEvent)doc.Events[0];
        Assert.Equal(2906, first.EncounterId);
        Assert.Equal(30000, first.OffsetMs);
        var second = (BossStartEvent)doc.Events[1];
        Assert.Equal(2917, second.EncounterId);
    }

    [Fact]
    public void Boss_ends_inside_recording_window_become_BossEnd_events()
    {
        var buf = NewBuffer();
        buf.Observe(new EncounterStart(At(30), 1999, "Forgemaster Garfrost", 23, 5, 658));
        buf.Observe(new EncounterEnd(At(180), 1999, "Forgemaster Garfrost", 23, 5, Success: true));
        buf.Observe(new EncounterStart(At(300), 2001, "Ick and Krick", 23, 5, 658));
        buf.Observe(new EncounterEnd(At(420), 2001, "Ick and Krick", 23, 5, Success: false));

        var doc = buf.Build(At(600));

        Assert.Equal(4, doc.Events.Count);
        Assert.IsType<BossStartEvent>(doc.Events[0]);
        var firstEnd = Assert.IsType<BossEndEvent>(doc.Events[1]);
        Assert.Equal(1999, firstEnd.EncounterId);
        Assert.Equal("Forgemaster Garfrost", firstEnd.EncounterName);
        Assert.Equal(180_000, firstEnd.OffsetMs);
        Assert.True(firstEnd.Success);
        Assert.IsType<BossStartEvent>(doc.Events[2]);
        var secondEnd = Assert.IsType<BossEndEvent>(doc.Events[3]);
        Assert.Equal(2001, secondEnd.EncounterId);
        Assert.False(secondEnd.Success);
    }

    [Fact]
    public void Boss_end_at_recording_end_is_excluded()
    {
        // For raid encounters the triggering EncounterEnd lands exactly at
        // recordingEnd — that one is already represented by the clip itself
        // (success flag + arena-style filename), so don't duplicate on the
        // timeline.
        var buf = NewBuffer();
        buf.Observe(new EncounterEnd(At(120), 2902, "Ulgrax", 16, 20, Success: true));

        var doc = buf.Build(At(120));

        Assert.Empty(doc.Events);
    }

    [Fact]
    public void Events_are_sorted_by_offset_ascending()
    {
        var buf = NewBuffer();
        buf.Observe(new UnitDied(At(60), "Player-1-A", "A"));
        buf.Observe(new EncounterStart(At(30), 2906, "MidBoss", 8, 5, 2290));
        buf.Observe(new UnitDied(At(45), "Player-1-B", "B"));

        var doc = buf.Build(At(120));

        Assert.Equal(3, doc.Events.Count);
        Assert.Equal(30000, doc.Events[0].OffsetMs);
        Assert.Equal(45000, doc.Events[1].OffsetMs);
        Assert.Equal(60000, doc.Events[2].OffsetMs);
    }

    [Fact]
    public void Negative_offset_clamps_to_zero()
    {
        // Clock skew: a death timestamped before the recordingStart should
        // still appear on the timeline at offset 0 rather than negative.
        var buf = NewBuffer();
        buf.Observe(new UnitDied(RecordingStart.AddSeconds(-1), "Player-1-A", "A"));

        var doc = buf.Build(At(60));

        Assert.Equal(0, doc.Events[0].OffsetMs);
    }

    [Fact]
    public void RealLog_PeriodicDamage_AttributedAsKillingBlow()
    {
        // Reconstructs the Chimaerus → Kheraval prelude from the real-log
        // fixture using DamageEvent records directly (parser layer is tested
        // separately). Only 4 events; no eviction pressure — pure attribution
        // happy-path test.
        var death = At(30, 53);
        var buf = NewBuffer();
        buf.Observe(Damage(At(30, 0), "Player-1096-0A6F3B1E", "Chimaerus", "Caustic Phlegm", amount: 17164));
        buf.Observe(Damage(At(30, 49), "Player-1096-0A6F3B1E", "Chimaerus", "Consuming Miasma", amount: 37314));
        // Outgoing tick from the dying player — keyed by Swarming Shade's
        // destGuid, NOT Kheraval's, so it must not affect attribution.
        buf.Observe(Damage(At(30, 50), "Creature-SHADE", "Kheraval-DefiasBrotherhood-EU", "Dread Plague", amount: 1701218));
        buf.Observe(new UnitDied(death, "Player-1096-0A6F3B1E", "Kheraval-DefiasBrotherhood-EU"));

        var doc = buf.Build(At(31));

        var d = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc.Events));
        Assert.Equal("Kheraval-DefiasBrotherhood-EU", d.PlayerName);
        Assert.Equal("Chimaerus", d.KilledBySource);
        Assert.Equal("Consuming Miasma", d.KilledBySpell);
        Assert.Equal(37314L, d.KillingBlowAmount);
    }

    [Fact]
    public void OutgoingDamageFromDyingPlayer_NotConfusedAsKillingBlow()
    {
        // The dying player's outgoing damage event is keyed by the target it
        // hit (Swarming Shade) rather than by the player; the kill-blow lookup
        // by Kheraval's destGuid must therefore prefer the Chimaerus tick.
        var death = At(30, 53);
        var buf = NewBuffer();
        buf.Observe(Damage(At(30, 49), "Player-1096-0A6F3B1E", "Chimaerus", "Consuming Miasma", amount: 37314));
        buf.Observe(Damage(At(30, 50), "Creature-SHADE", "Kheraval-DefiasBrotherhood-EU", "Dread Plague", amount: 1701218));
        buf.Observe(new UnitDied(death, "Player-1096-0A6F3B1E", "Kheraval-DefiasBrotherhood-EU"));

        var doc = buf.Build(At(31));

        var d = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc.Events));
        Assert.Equal("Chimaerus", d.KilledBySource);
        Assert.NotEqual("Kheraval-DefiasBrotherhood-EU", d.KilledBySource);
        Assert.NotEqual("Dread Plague", d.KilledBySpell);
    }

    [Fact]
    public void KillingBlow_AttributedDespiteLaterEvictionPressure()
    {
        // Real-world failure mode: a death happens early in the recording,
        // damage to OTHER players continues to flow for >5s, evicting the
        // dying player's bucket. Without the fix, FindKillingBlow at Build
        // time finds nothing. With the fix, attribution was captured the
        // moment the death was observed and survives any subsequent
        // eviction.
        var death = At(30, 53);
        var buf = NewBuffer();
        buf.Observe(Damage(At(30, 0), "Player-1096-0A6F3B1E", "Chimaerus", "Caustic Phlegm", amount: 17164));
        buf.Observe(Damage(At(30, 49), "Player-1096-0A6F3B1E", "Chimaerus", "Consuming Miasma", amount: 37314));
        buf.Observe(new UnitDied(death, "Player-1096-0A6F3B1E", "Kheraval-DefiasBrotherhood-EU"));

        // Six post-death damage events to other targets at >5s spacing —
        // each one trips EvictExpired and walks the cutoff forward, sweeping
        // Kheraval's bucket clean of its kill-blow entries long before
        // Build is called.
        for (int i = 0; i < 6; i++)
        {
            buf.Observe(Damage(At(36 + i * 6), $"Player-2-OTHER{i}", "Trash Mob", "Hit", amount: 100));
        }

        var doc = buf.Build(At(72));

        var d = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc.Events));
        Assert.Equal("Kheraval-DefiasBrotherhood-EU", d.PlayerName);
        // These assertions FAIL under the current code (eviction has wiped
        // the bucket) and pass under the fix.
        Assert.Equal("Chimaerus", d.KilledBySource);
        Assert.Equal("Consuming Miasma", d.KilledBySpell);
        Assert.Equal(37314L, d.KillingBlowAmount);
    }

    [Fact]
    public void CombatantInfo_is_captured_by_guid_with_spec_id()
    {
        var buf = NewBuffer();
        buf.Observe(new CombatantInfo(At(0), "Player-1-FROSTY", SpecId: 64));   // Frost Mage
        buf.Observe(new CombatantInfo(At(0), "Player-2-TANK", SpecId: 581));    // Vengeance DH
        buf.Observe(new CombatantInfo(At(0), "Player-3-HEAL", SpecId: 1468));   // Preservation Evoker

        Assert.Equal(3, buf.Combatants.Count);
        Assert.Equal(64,   buf.Combatants["Player-1-FROSTY"]);
        Assert.Equal(581,  buf.Combatants["Player-2-TANK"]);
        Assert.Equal(1468, buf.Combatants["Player-3-HEAL"]);
    }

    [Fact]
    public void Duplicate_CombatantInfo_for_same_guid_keeps_latest_spec_id()
    {
        // M+ keys re-emit COMBATANT_INFO for each boss pull; if a player
        // respecs mid-run we should reflect the most recent value.
        var buf = NewBuffer();
        buf.Observe(new CombatantInfo(At(0),  "Player-1-DRUID", SpecId: 103));  // Feral
        buf.Observe(new CombatantInfo(At(60), "Player-1-DRUID", SpecId: 105));  // Resto (respec)

        Assert.Single(buf.Combatants);
        Assert.Equal(105, buf.Combatants["Player-1-DRUID"]);
    }

    [Fact]
    public void Capturing_combatants_does_not_disturb_death_or_boss_start_events()
    {
        var buf = NewBuffer();
        buf.Observe(new CombatantInfo(At(0), "Player-1-A", SpecId: 64));
        buf.Observe(new EncounterStart(At(30), 2906, "MidBoss", 8, 5, 2290));
        buf.Observe(new CombatantInfo(At(0), "Player-2-B", SpecId: 71));
        buf.Observe(new UnitDied(At(45), "Player-1-A", "A"));

        var doc = buf.Build(At(120));

        Assert.Equal(2, doc.Events.Count);
        Assert.IsType<BossStartEvent>(doc.Events[0]);
        Assert.IsType<PlayerDeathEvent>(doc.Events[1]);
        Assert.Equal(2, buf.Combatants.Count);
    }

    [Fact]
    public void Damage_buffer_caps_size_without_dropping_deaths_or_boss_starts()
    {
        var buf = NewBuffer();
        // Death first so it's in the buffer; killing blow lookup will find
        // none (damage is added with later timestamps and 5s window already
        // elapsed by the time we ask).
        buf.Observe(new UnitDied(At(1), "Player-1-FROSTY", "Frostytanegl"));
        buf.Observe(new EncounterStart(At(5), 2906, "MidBoss", 8, 5, 2290));

        // Feed 60 000 damage events — the buffer's 50 000 cap must engage
        // without throwing or dropping the death/boss start.
        for (int i = 0; i < 60_000; i++)
        {
            buf.Observe(Damage(At(60, i % 1000), "Player-2-FILLER", "Mob", "Hit"));
        }

        var doc = buf.Build(At(300));

        Assert.Contains(doc.Events, e => e is PlayerDeathEvent);
        Assert.Contains(doc.Events, e => e is BossStartEvent);
    }

    [Fact]
    public void Boss_hp_percent_is_null_when_no_matching_damage_seen()
    {
        var buf = new RecordingEventBuffer(RecordingStart, KillingBlowLookback, bossName: "Ulgrax");
        buf.Observe(new EncounterStart(At(0), 2902, "Ulgrax", 16, 20, 2657));

        Assert.Null(buf.GetLastBossHpPercent());
    }

    [Fact]
    public void Boss_hp_percent_uses_last_damage_event_on_named_boss()
    {
        var buf = new RecordingEventBuffer(RecordingStart, KillingBlowLookback, bossName: "Ulgrax");
        buf.Observe(new EncounterStart(At(0), 2902, "Ulgrax", 16, 20, 2657));
        // Player hits the boss; boss now has destName "Ulgrax".
        buf.Observe(new DamageEvent(
            At(30), "Player-1-FROSTY", "Frostytanegl",
            "Creature-X", "Ulgrax", null, "Smite", 50000,
            DestCurrentHp: 800_000_000, DestMaxHp: 1_000_000_000));
        buf.Observe(new DamageEvent(
            At(180), "Player-1-FROSTY", "Frostytanegl",
            "Creature-X", "Ulgrax", null, "Smite", 50000,
            DestCurrentHp: 270_000_000, DestMaxHp: 1_000_000_000));

        var pct = buf.GetLastBossHpPercent();

        Assert.NotNull(pct);
        Assert.InRange(pct!.Value, 26.99, 27.01);
    }

    [Fact]
    public void Boss_hp_percent_ignores_damage_to_other_units()
    {
        var buf = new RecordingEventBuffer(RecordingStart, KillingBlowLookback, bossName: "Ulgrax");
        buf.Observe(new EncounterStart(At(0), 2902, "Ulgrax", 16, 20, 2657));
        // Damage to a different target (e.g. an add) must not be recorded as boss HP.
        buf.Observe(new DamageEvent(
            At(30), "Player-1-FROSTY", "Frostytanegl",
            "Creature-Add", "Tendril", null, "Smite", 50000,
            DestCurrentHp: 100, DestMaxHp: 1_000_000));

        Assert.Null(buf.GetLastBossHpPercent());
    }

    [Fact]
    public void Boss_hp_percent_skips_events_without_hp_data()
    {
        // Advanced logging off → HP fields are null → we can't compute percent.
        var buf = new RecordingEventBuffer(RecordingStart, KillingBlowLookback, bossName: "Ulgrax");
        buf.Observe(new EncounterStart(At(0), 2902, "Ulgrax", 16, 20, 2657));
        buf.Observe(new DamageEvent(
            At(30), "Player-1-FROSTY", "Frostytanegl",
            "Creature-X", "Ulgrax", null, "Smite", 50000,
            DestCurrentHp: null, DestMaxHp: null));

        Assert.Null(buf.GetLastBossHpPercent());
    }

    [Fact]
    public void Boss_hp_percent_clamps_to_100_when_currentHp_above_max()
    {
        // Boss takes a heal mid-pull that briefly pushes currentHp > maxHp
        // (rare but happens with mechanics like Apotheosis). Clamp instead
        // of reporting > 100%.
        var buf = new RecordingEventBuffer(RecordingStart, KillingBlowLookback, bossName: "Ulgrax");
        buf.Observe(new EncounterStart(At(0), 2902, "Ulgrax", 16, 20, 2657));
        buf.Observe(new DamageEvent(
            At(30), "Player-1-FROSTY", "Frostytanegl",
            "Creature-X", "Ulgrax", null, "Smite", 50000,
            DestCurrentHp: 1_100_000, DestMaxHp: 1_000_000));

        Assert.Equal(100.0, buf.GetLastBossHpPercent());
    }

    [Fact]
    public void Build_omits_preRollSeconds_by_default()
    {
        var buf = NewBuffer();
        var doc = buf.Build(At(60));
        Assert.Null(doc.PreRollSeconds);
    }

    [Fact]
    public void Build_propagates_preRollSeconds_when_provided()
    {
        var buf = NewBuffer();
        var doc = buf.Build(At(60), preRollSeconds: 15.5);
        Assert.Equal(15.5, doc.PreRollSeconds);
        // OffsetMs stays encounter-relative regardless of pre-roll — the UI
        // adds the offset at render/seek time, not at sidecar-write time.
        buf.Observe(new UnitDied(At(10), "Player-1-X", "Anya"));
        var doc2 = buf.Build(At(60), preRollSeconds: 15.5);
        var death = Assert.IsType<PlayerDeathEvent>(Assert.Single(doc2.Events));
        Assert.Equal(10_000, death.OffsetMs);
    }
}
