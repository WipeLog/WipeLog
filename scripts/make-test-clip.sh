#!/usr/bin/env bash
# Generate a fake WoW clip — a white-screen H.264 mp4 plus the two JSON
# sidecars (clip metadata + .events.json) — so the GUI's timeline strip
# and raid-comp/death chips can be exercised without a real recording.
# Drop the output into the daemon's --output directory, then hit
# "Re-index library" in Settings (or restart the app — the indexer runs
# on startup).
#
# The events sidecar is written at schema v2 with a Combatants list so
# the details panel shows tank / healer / dps / death chips. Pass
# --tanks 0 --healers 0 --dps 0 to simulate an old v1 clip (chips hide).
#
# Usage:
#   scripts/make-test-clip.sh [--output DIR] [--duration SECONDS]
#                             [--deaths N] [--name STEM]
#                             [--raid|--mplus|--arena|--solo-shuffle|--bg]
#                             [--tanks N] [--healers N] [--dps N]
#                             [--keystone N] [--bosses N]
#                             [--started-at ISO]
#
# --started-at takes any string `date -d` understands (ISO 8601, "yesterday",
# "2 weeks ago", "2026-03-15 20:00", etc.) and backdates the clip's start
# time. Useful for exercising the date-range filter in the GUI: generate a
# handful of clips spread across the calendar, then poke the range button.
#
# With --mplus the script picks a random dungeon from a built-in pool,
# a random keystone level between +10 and +20, and 1-3 of that dungeon's
# bosses spaced across the clip. Each boss's encounter region runs from
# its BossStart offset to the next boss's start (or the clip end) - the
# scrub-bar paints an amber band for each. Override the randomness with
# --keystone N (10-20) or --bosses N (1-3).
set -euo pipefail

OUTPUT_DIR="$HOME/Videos/wow"
DURATION=180
DEATHS=5
TANKS=1
HEALERS=1
DPS=3
STEM=""
CONTENT_TYPE="RaidEncounter"
PLAYER_NAME="Wonsupi"
PLAYER_REALM="DefiasBrotherhood"
PLAYER_REGION="EU"
PLAYER_CLASS="Mage"
PLAYER_SPEC="Frost"
KEYSTONE_OVERRIDE=""
BOSSES_OVERRIDE=""
STARTED_AT=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output)     OUTPUT_DIR="$2"; shift 2 ;;
        --duration)   DURATION="$2"; shift 2 ;;
        --deaths)     DEATHS="$2"; shift 2 ;;
        --tanks)      TANKS="$2"; shift 2 ;;
        --healers)    HEALERS="$2"; shift 2 ;;
        --dps)        DPS="$2"; shift 2 ;;
        --name)       STEM="$2"; shift 2 ;;
        --raid)         CONTENT_TYPE="RaidEncounter"; shift ;;
        --mplus)        CONTENT_TYPE="MythicPlus"; shift ;;
        --arena)        CONTENT_TYPE="Arena"; shift ;;
        --solo-shuffle) CONTENT_TYPE="SoloShuffle"; shift ;;
        --bg)           CONTENT_TYPE="Battleground"; shift ;;
        --keystone)     KEYSTONE_OVERRIDE="$2"; shift 2 ;;
        --bosses)       BOSSES_OVERRIDE="$2"; shift 2 ;;
        --started-at)   STARTED_AT="$2"; shift 2 ;;
        -h|--help)
            sed -n '2,24p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *) echo "Unknown arg: $1" >&2; exit 1 ;;
    esac
done

command -v ffmpeg >/dev/null || { echo "ffmpeg required on PATH" >&2; exit 1; }
mkdir -p "$OUTPUT_DIR"

DURATION_MS=$((DURATION * 1000))

# Resolve the clip's start time. --started-at lets the caller backdate the
# clip for date-range-filter testing; without it we use "now minus duration"
# so the clip's end falls on the current wall clock.
if [[ -n "$STARTED_AT" ]]; then
    START_EPOCH=$(date -d "$STARTED_AT" +%s) \
        || { echo "Could not parse --started-at value: $STARTED_AT" >&2; exit 1; }
    END_EPOCH=$((START_EPOCH + DURATION))
else
    END_EPOCH=$(date +%s)
    START_EPOCH=$((END_EPOCH - DURATION))
fi

if [[ -z "$STEM" ]]; then
    STEM="test-$(date -d "@$START_EPOCH" +%Y%m%d-%H%M%S)"
fi
VIDEO_PATH="$OUTPUT_DIR/$STEM.mp4"
SIDECAR_PATH="$OUTPUT_DIR/$STEM.json"
EVENTS_PATH="$OUTPUT_DIR/$STEM.events.json"

# Wall-clock timestamps. Use the local TZ offset so the GUI's tooltip
# (which renders in the clip's TZ) shows times that match the user's clock.
TZ_OFFSET=$(date +%:z)
START_ISO=$(date -d "@$START_EPOCH" +%Y-%m-%dT%H:%M:%S)"$TZ_OFFSET"
END_ISO=$(date -d "@$END_EPOCH"     +%Y-%m-%dT%H:%M:%S)"$TZ_OFFSET"

echo "Generating $VIDEO_PATH (${DURATION}s, $DEATHS deaths, ${TANKS}/${HEALERS}/${DPS} comp, $CONTENT_TYPE)..."

ffmpeg -y -loglevel error \
    -f lavfi -i "color=c=white:s=640x360:r=30:d=$DURATION" \
    -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
    "$VIDEO_PATH"

# M+ dungeon pool. Each entry is pipe-delimited:
#   "Dungeon Name|encounterId:Boss Name|encounterId:Boss Name|..."
# Encounter IDs are best-effort and not strictly authoritative — they
# only need to round-trip into the sidecar so the timeline strip and
# tooltip have something to show.
MPLUS_DUNGEONS=(
    "Ara-Kara, City of Echoes|2902:Avanoxx|2917:Anub'zekt|2918:Ki'katal the Harvester"
    "City of Threads|2926:Orator Krix'vizk|2901:The Coaglamation|2906:Fangs of the Queen|2924:Izo, the Grand Splicer"
    "The Dawnbreaker|2932:Speaker Shadowcrown|2937:Anub'ikkaj|2934:Rasha'nan"
    "The Necrotic Wake|2387:Blightbone|2390:Amarth, The Harvester|2388:Surgeon Stitchflesh|2389:Nalthor the Rimebinder"
    "Siege of Boralus|2095:Chopper Redhook|2167:Dread Captain Lockwood|2169:Hadal Darkfathom|2170:Viq'Goth"
    "Mists of Tirna Scithe|2392:Ingra Maloch|2393:Mistcaller|2394:Tred'ova"
    "Grim Batol|1051:General Umbriss|1050:Forgemaster Throngus|1048:Drahga Shadowburner|1049:Erudax"
    "The Stonevault|2854:E.D.N.A.|2880:Skarmorak|2888:Speaker Brokk and Speaker Dorlita|2890:Void Speaker Eirich"
)

# Build the clip sidecar (v2 shape, matches what the daemon writes).
SELECTED_BOSS_IDS=()
SELECTED_BOSS_NAMES=()
SELECTED_BOSS_OFFSETS_MS=()
ARENA_BRACKET="null"

if [[ "$CONTENT_TYPE" == "RaidEncounter" ]]; then
    DISPLAY_NAME="Chimaerus (Mythic, Wipe)"
    DIFFICULTY="\"Mythic\""
    ENCOUNTER_ID=2902
    KEYSTONE_LEVEL="null"
    INSTANCE_NAME="\"Manaforge Omega\""
    DUNGEON_NAME="null"
    SUCCESS=false
elif [[ "$CONTENT_TYPE" == "Arena" ]]; then
    # Randomise the bracket so a batch of --arena clips has visible variety.
    ARENA_BRACKETS=("Arena2v2" "Arena3v3" "Skirmish")
    BR="${ARENA_BRACKETS[$((RANDOM % ${#ARENA_BRACKETS[@]}))]}"
    DISPLAY_NAME="$BR"
    ARENA_BRACKET="\"$BR\""
    DIFFICULTY="null"
    ENCOUNTER_ID="null"
    KEYSTONE_LEVEL="null"
    INSTANCE_NAME="null"
    DUNGEON_NAME="null"
    SUCCESS=true
elif [[ "$CONTENT_TYPE" == "SoloShuffle" ]]; then
    DISPLAY_NAME="Rated Solo Shuffle"
    ARENA_BRACKET="\"Rated Solo Shuffle\""
    DIFFICULTY="null"
    ENCOUNTER_ID="null"
    KEYSTONE_LEVEL="null"
    INSTANCE_NAME="null"
    DUNGEON_NAME="null"
    SUCCESS=true
elif [[ "$CONTENT_TYPE" == "Battleground" ]]; then
    # Pool matches the encounter detector's BattlegroundZoneIds whitelist.
    BG_NAMES=(
        "Alterac Valley"
        "Warsong Gulch"
        "Arathi Basin"
        "Eye of the Storm"
        "Strand of the Ancients"
        "Isle of Conquest"
        "Twin Peaks"
        "Battle for Gilneas"
    )
    DISPLAY_NAME="${BG_NAMES[$((RANDOM % ${#BG_NAMES[@]}))]}"
    DIFFICULTY="null"
    ENCOUNTER_ID="null"
    KEYSTONE_LEVEL="null"
    INSTANCE_NAME="null"
    DUNGEON_NAME="null"
    SUCCESS=true
else
    # ---- M+ randomization ----
    DUNGEON_ENTRY="${MPLUS_DUNGEONS[$((RANDOM % ${#MPLUS_DUNGEONS[@]}))]}"
    IFS='|' read -ra DFIELDS <<< "$DUNGEON_ENTRY"
    DUNGEON_DISPLAY="${DFIELDS[0]}"
    BOSS_PAIRS=("${DFIELDS[@]:1}")

    if [[ -n "$KEYSTONE_OVERRIDE" ]]; then
        KEYSTONE_LEVEL="$KEYSTONE_OVERRIDE"
    else
        KEYSTONE_LEVEL=$((10 + RANDOM % 11))   # 10-20 inclusive
    fi

    if [[ -n "$BOSSES_OVERRIDE" ]]; then
        NUM_BOSSES="$BOSSES_OVERRIDE"
    else
        NUM_BOSSES=$((1 + RANDOM % 3))         # 1, 2, or 3
    fi
    if (( NUM_BOSSES > ${#BOSS_PAIRS[@]} )); then NUM_BOSSES=${#BOSS_PAIRS[@]}; fi
    if (( NUM_BOSSES < 1 )); then NUM_BOSSES=1; fi

    # Pick NUM_BOSSES distinct dungeon-boss indices, preserving dungeon order.
    ALL_IDX=()
    for ((i=0; i<${#BOSS_PAIRS[@]}; i++)); do ALL_IDX+=("$i"); done
    SHUF_IDX=($(printf '%s\n' "${ALL_IDX[@]}" | shuf | head -n "$NUM_BOSSES" | sort -n))

    for idx in "${SHUF_IDX[@]}"; do
        pair="${BOSS_PAIRS[$idx]}"
        SELECTED_BOSS_IDS+=("${pair%%:*}")
        SELECTED_BOSS_NAMES+=("${pair#*:}")
    done

    # Distribute boss starts across the clip with a 5% lead-in. Each boss
    # gets a roughly equal slice; the implicit "end" is the next boss's
    # start (or the clip end), which is what the scrub-bar renderer uses
    # to paint the encounter region.
    LEAD_MS=$((DURATION_MS / 20))
    USABLE=$((DURATION_MS - LEAD_MS - 1000))
    if (( USABLE < NUM_BOSSES * 1000 )); then USABLE=$((NUM_BOSSES * 1000)); fi
    SLICE=$((USABLE / NUM_BOSSES))
    for ((i=0; i<NUM_BOSSES; i++)); do
        base=$((LEAD_MS + i * SLICE))
        jitter=$((RANDOM % (SLICE / 4 + 1)))
        SELECTED_BOSS_OFFSETS_MS+=( $((base + jitter)) )
    done

    if (( KEYSTONE_LEVEL <= 13 )); then
        SUCCESS=true
        STATUS_LABEL="In Time"
    else
        SUCCESS=false
        STATUS_LABEL="Depleted"
    fi

    DISPLAY_NAME="$DUNGEON_DISPLAY +$KEYSTONE_LEVEL ($STATUS_LABEL)"
    DIFFICULTY="null"
    ENCOUNTER_ID="null"
    INSTANCE_NAME="null"
    DUNGEON_NAME="\"$DUNGEON_DISPLAY\""
fi

cat > "$SIDECAR_PATH" <<EOF
{
  "version": 2,
  "contentType": "$CONTENT_TYPE",
  "displayName": "$DISPLAY_NAME",
  "difficulty": $DIFFICULTY,
  "encounterId": $ENCOUNTER_ID,
  "keystoneLevel": $KEYSTONE_LEVEL,
  "success": $SUCCESS,
  "startedAt": "$START_ISO",
  "endedAt": "$END_ISO",
  "durationSeconds": $DURATION,
  "videoFile": "$STEM.mp4",
  "playerName": "$PLAYER_NAME",
  "playerRealm": "$PLAYER_REALM",
  "playerRegion": "$PLAYER_REGION",
  "playerClass": "$PLAYER_CLASS",
  "playerSpec": "$PLAYER_SPEC",
  "instanceName": $INSTANCE_NAME,
  "dungeonName": $DUNGEON_NAME,
  "arenaBracket": $ARENA_BRACKET
}
EOF

# Generic party-member names. The death-attribution loop draws from
# PARTY_NAMES; index 0 must stay the recording player so the timeline
# shows the "mine" (bright red) colour for at least one death. Two
# flavour names with diacritics keep that path exercised; the rest are
# random picks from the generic pool below.
GENERIC_PARTY_NAMES=(
    "Aelara"  "Brennor" "Cadeyrn" "Daelin"  "Eryn"   "Fenwick"
    "Garrik"  "Halia"   "Ilyan"   "Jorah"   "Kaela"  "Lior"
    "Maevra"  "Nyra"    "Orin"    "Pellor"  "Quenna" "Rylan"
    "Sylvar"  "Thal"    "Uthred"  "Vasha"   "Wren"   "Xela"
    "Yorin"   "Zara"
)
GENERIC_REALMS=(
    "Ravenholdt-EU"
    "DefiasBrotherhood-EU"
    "TwistingNether-EU"
    "Stormrage-US"
    "Illidan-US"
    "ArgentDawn-EU"
    "Silvermoon-EU"
    "TarrenMill-EU"
    "Area52-US"
)
PARTY_NAMES=(
    "$PLAYER_NAME-$PLAYER_REALM-$PLAYER_REGION"
    "Frostytånegl-Ravenholdt-EU"
    "Bjørnar-DefiasBrotherhood-EU"
)
for ((i=0; i<5; i++)); do
    rn="${GENERIC_PARTY_NAMES[$((RANDOM % ${#GENERIC_PARTY_NAMES[@]}))]}"
    rr="${GENERIC_REALMS[$((RANDOM % ${#GENERIC_REALMS[@]}))]}"
    PARTY_NAMES+=("$rn-$rr")
done

SOURCES=("Chimaerus" "Avanoxx" "Spectral Tank" "Volatile Stitching" "Necrotic Pool")
SPELLS=("Consuming Miasma" "Voracious Bite" "Shadow Bolt Volley" "Frost Nova" "")

# Generate $DEATHS unique-ish offsets, sorted. Bash arithmetic keeps this
# self-contained (no python required).
OFFSETS=()
for ((i = 0; i < DEATHS; i++)); do
    # Stay clear of the very start so a real recording timeline shape emerges.
    JITTER_MIN=$((DURATION_MS / 10))
    SPAN=$((DURATION_MS - JITTER_MIN - 1000))
    if [[ $SPAN -lt 1000 ]]; then SPAN=1000; fi
    OFFSETS+=( $((JITTER_MIN + RANDOM % SPAN)) )
done
# Sort ascending.
IFS=$'\n' SORTED_OFFSETS=($(sort -n <<<"${OFFSETS[*]}")); unset IFS

# Build the events array.
EVENTS_JSON=""
for off in "${SORTED_OFFSETS[@]}"; do
    p_idx=$((RANDOM % ${#PARTY_NAMES[@]}))
    s_idx=$((RANDOM % ${#SOURCES[@]}))
    sp_idx=$((RANDOM % ${#SPELLS[@]}))
    spell="${SPELLS[$sp_idx]}"
    src="${SOURCES[$s_idx]}"
    pname="${PARTY_NAMES[$p_idx]}"
    if [[ -n "$EVENTS_JSON" ]]; then EVENTS_JSON+=",
"; fi
    if [[ -n "$spell" ]]; then
        EVENTS_JSON+="    { \"kind\": \"PlayerDeath\", \"offsetMs\": $off, \"playerGuid\": \"Player-1234-Fake$p_idx\", \"playerName\": \"$pname\", \"killedBySource\": \"$src\", \"killedBySpell\": \"$spell\" }"
    else
        EVENTS_JSON+="    { \"kind\": \"PlayerDeath\", \"offsetMs\": $off, \"playerGuid\": \"Player-1234-Fake$p_idx\", \"playerName\": \"$pname\", \"killedBySource\": \"$src\" }"
    fi
done

# Emit a BossStart for each picked M+ boss. Raid clips legitimately
# have an empty boss-start track and skip this loop.
for ((i=0; i<${#SELECTED_BOSS_IDS[@]}; i++)); do
    bid="${SELECTED_BOSS_IDS[$i]}"
    bname="${SELECTED_BOSS_NAMES[$i]}"
    boff="${SELECTED_BOSS_OFFSETS_MS[$i]}"
    if [[ -n "$EVENTS_JSON" ]]; then EVENTS_JSON+=",
"; fi
    EVENTS_JSON+="    { \"kind\": \"BossStart\", \"offsetMs\": $boff, \"encounterId\": $bid, \"encounterName\": \"$bname\" }"
done

# Pool of spec IDs per role. Triples are "specId|Class|Spec" — they must
# round-trip through src/Wipelog.Core/Data/SpecMap.cs so the daemon
# would resolve them identically. Role is implicit by which pool we draw
# from. Add to these if you want more variety in screenshots.
TANK_POOL=(
    "250|Death Knight|Blood"
    "66|Paladin|Protection"
    "581|Demon Hunter|Vengeance"
    "104|Druid|Guardian"
    "268|Monk|Brewmaster"
    "73|Warrior|Protection"
)
HEALER_POOL=(
    "105|Druid|Restoration"
    "1468|Evoker|Preservation"
    "65|Paladin|Holy"
    "256|Priest|Discipline"
    "257|Priest|Holy"
    "264|Shaman|Restoration"
    "270|Monk|Mistweaver"
)
DPS_POOL=(
    "64|Mage|Frost"
    "70|Paladin|Retribution"
    "258|Priest|Shadow"
    "252|Death Knight|Unholy"
    "577|Demon Hunter|Havoc"
    "1480|Demon Hunter|Devourer"
    "102|Druid|Balance"
    "253|Hunter|Beast Mastery"
    "1467|Evoker|Devastation"
    "259|Rogue|Assassination"
    "262|Shaman|Elemental"
    "267|Warlock|Destruction"
    "72|Warrior|Fury"
    "269|Monk|Windwalker"
)

COMBATANTS_JSON=""
GUID_IDX=0
emit_combatants() {
    local count="$1"; local role="$2"; local -n pool=$3
    for ((c = 0; c < count; c++)); do
        local triple="${pool[$((c % ${#pool[@]}))]}"
        local spec_id="${triple%%|*}"
        local rest="${triple#*|}"
        local cls="${rest%%|*}"
        local spec="${rest#*|}"
        if [[ -n "$COMBATANTS_JSON" ]]; then COMBATANTS_JSON+=",
"; fi
        COMBATANTS_JSON+="    { \"playerGuid\": \"Player-1234-Comp$GUID_IDX\", \"specId\": $spec_id, \"class\": \"$cls\", \"spec\": \"$spec\", \"role\": \"$role\" }"
        GUID_IDX=$((GUID_IDX + 1))
    done
}
emit_combatants "$TANKS"   "Tank"   TANK_POOL
emit_combatants "$HEALERS" "Healer" HEALER_POOL
emit_combatants "$DPS"     "Dps"    DPS_POOL

cat > "$EVENTS_PATH" <<EOF
{
  "version": 2,
  "recordingStartedAt": "$START_ISO",
  "recordingEndedAt": "$END_ISO",
  "events": [
$EVENTS_JSON
  ],
  "combatants": [
$COMBATANTS_JSON
  ]
}
EOF

echo
echo "Wrote:"
echo "  $VIDEO_PATH"
echo "  $SIDECAR_PATH"
echo "  $EVENTS_PATH"
echo
echo "In the GUI: open Settings → Re-index library (or restart) to pick it up."
