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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wipelog.Core.Data;

namespace Wipelog.Core.Recording;

/// <summary>
/// Top-level shape of the .events.json sidecar that accompanies each clip.
/// Versioned so the GUI can refuse files newer than it understands and so
/// new event kinds can be added without breaking older readers.
///
/// <para>
/// Version history:
/// <list type="number">
/// <item>v1 — Events list only (PlayerDeath, BossStart).</item>
/// <item>v2 — adds <see cref="Combatants"/> derived from COMBATANT_INFO.
///     V1 sidecars deserialize cleanly: Combatants is an init-only property
///     with an empty default, so its absence in the JSON is harmless.</item>
/// </list>
/// </para>
///
/// <para>
/// <c>OffsetMs</c> on every <see cref="TimelineEvent"/> is encounter-relative
/// (T=0 = <see cref="RecordingStartedAt"/>). When pre-roll footage was prepended
/// to the video, <see cref="PreRollSeconds"/> is set; consumers must shift event
/// X positions / seek targets by <c>PreRollSeconds * 1000</c> to align with the
/// underlying video file's timebase.
/// </para>
/// </summary>
public sealed record TimelineEventDocument(
    int Version,
    DateTimeOffset RecordingStartedAt,
    DateTimeOffset RecordingEndedAt,
    IReadOnlyList<TimelineEvent> Events)
{
    /// <summary>
    /// Group composition captured during the recording window, one record
    /// per player GUID. Empty when the recording wasn't an instanced
    /// encounter (no COMBATANT_INFO was emitted) or when reading a v1 sidecar.
    /// </summary>
    public IReadOnlyList<CombatantRecord> Combatants { get; init; } =
        Array.Empty<CombatantRecord>();

    /// <summary>
    /// Seconds of pre-encounter footage prepended to the video file before
    /// <see cref="RecordingStartedAt"/>. Null/absent for clips recorded without
    /// pre-roll. UI layers add this to each event's <c>OffsetMs</c> before
    /// computing scrub-bar X positions and player seek targets.
    /// </summary>
    public double? PreRollSeconds { get; init; }

    /// <summary>
    /// Seconds of post-encounter footage appended to the video file after
    /// <see cref="RecordingEndedAt"/>. Recording was held open this long
    /// after the encounter's stop signal so the user can see the immediate
    /// aftermath (kill cam, party celebrating, body-pull setup, etc.).
    /// Events don't fall inside this window — <c>OffsetMs</c> still measures
    /// from <see cref="RecordingStartedAt"/>. UI layers extend the scrub
    /// bar's total duration by this amount.
    /// </summary>
    public double? PostRollSeconds { get; init; }
}

/// <summary>
/// Base for typed timeline events. The <c>kind</c> property is the
/// discriminator emitted as JSON polymorphism — readers switch on it to
/// project to the right derived shape, and unknown kinds can be safely
/// ignored.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind",
                 IgnoreUnrecognizedTypeDiscriminators = true,
                 UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(PlayerDeathEvent), typeDiscriminator: "PlayerDeath")]
[JsonDerivedType(typeof(BossStartEvent), typeDiscriminator: "BossStart")]
[JsonDerivedType(typeof(BossEndEvent), typeDiscriminator: "BossEnd")]
public abstract record TimelineEvent(int OffsetMs);

public sealed record PlayerDeathEvent(
    int OffsetMs,
    string PlayerGuid,
    string PlayerName,
    string? KilledBySource,
    string? KilledBySpell,
    long? KillingBlowAmount) : TimelineEvent(OffsetMs);

public sealed record BossStartEvent(
    int OffsetMs,
    int EncounterId,
    string EncounterName) : TimelineEvent(OffsetMs);

public sealed record BossEndEvent(
    int OffsetMs,
    int EncounterId,
    string EncounterName,
    bool Success) : TimelineEvent(OffsetMs);

/// <summary>
/// One row of raid/party composition. Class/Spec/Role are nullable because
/// a spec ID we haven't catalogued yet (newly-added expansion spec) still
/// produces a record — we just can't decode it. Consumers should show "?".
/// </summary>
public sealed record CombatantRecord(
    string PlayerGuid,
    int SpecId,
    string? Class,
    string? Spec,
    SpecRole? Role);

public static class TimelineEventsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
