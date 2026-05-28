# Chat overlay masking

An opt-in privacy feature. When enabled, the daemon composites a black panel —
branded with the Wipelog logo and wordmark, centred over the box — across a
configured region of every newly-saved clip before it lands in the library, so
chat, whispers, and guild/member names are hidden from clips you share. Default
is **off**: clips are recorded and stored unchanged, and no post-processing
runs, so users who never share pay nothing.

There are two ways to mask:

- **Automatic** (this whole doc, below): the Settings toggle masks *every* new
  recording. Re-encodes each clip — best left off unless you share most of what
  you record.
- **On-demand** (["Masking one pull"](#masking-one-pull-on-demand)): right-click
  a single clip → mask just that one into a shareable copy. Only pay the
  re-encode for the pulls you actually share.

The reference for the code (`ChatOverlayProcessor`, `RecorderController`
wiring) is [`../CLAUDE.md`](../CLAUDE.md) under "Chat overlay masking". This doc
is the user/operator view: how to turn it on, what to expect, how to test it,
and how to reason about speed.

## Masking one pull (on-demand)

To mask just a specific pull — e.g. to submit it to a raid leader for review —
**right-click the clip → "Create masked copy for sharing…"**. This:

- masks only that clip into a sibling `*.masked.<ext>` using the region from
  Settings → Recording → CHAT OVERLAY (configure it there first; the explicit
  action masks regardless of the auto-toggle);
- shows a cancellable progress bar (the re-encode cost is the same as automatic
  masking — see [Performance](#performance--what-to-expect));
- leaves the original clip completely untouched;
- copies the timeline (`.events.json`) so death/boss markers still show for
  review;
- adds the result to the **Clips** tab as a "<name> (masked)" entry and selects
  it.

The masked copy is an ordinary library clip; delete it (right-click → Delete)
when you're done sharing.

## Enabling it

### From the GUI

Settings → **Recording** tab → **CHAT OVERLAY** section:

1. Tick **Mask chat region in recordings**.
2. Set **X / Y** and **Width / Height** of the region to cover, in screen pixels.
   **X** is measured from the left edge; **Y** from the **bottom** edge to the
   bottom of the box (WoW's chat sits bottom-left, so `Y = 0` puts the box flush
   with the bottom). Defaults are `0, 0, 540, 320` (a bottom-left chat box).
3. Click **Test mask** to screenshot the monitor selected in **Capture target**
   with the rectangle painted on it and open the result — iterate on the numbers
   until it covers your chat.

Changes are saved immediately, but the **daemon reads this setting at startup**,
so it takes effect on the next service reinstall/restart (the section's note
says so). It is *not* part of the systemd unit, so editing it does not trip the
"reinstall to apply" banner.

### In `preferences.json`

`~/.config/wipelog/preferences.json`, top-level `chatOverlay` block (camelCase,
pixels):

```json
{
  "chatOverlay": {
    "enabled": true,
    "x": 0,
    "y": 0,
    "width": 540,
    "height": 320
  }
}
```

Missing block or `enabled: false` ⇒ no masking. `width`/`height` must be > 0.

## How it works

Masking is a **post-processing re-encode**. The mask (a black box with the
Wipelog logo + wordmark composited on top) changes pixels, and video frames
reference each other across time, so the whole clip must be re-encoded — you
cannot patch one region without a full pass. The branded image ships as an
embedded asset in `Wipelog.Core` and is overlaid scaled-to-fit and centred, so
it never distorts; if the asset can't be loaded, masking falls back to a plain
black box (privacy is unaffected either way). The daemon runs
this right after a clip is finalized (after any pre-roll is prepended), *before*
the clip is indexed, so the library only ever sees the masked version. If
masking fails for any reason the **original unmasked clip is kept** and an error
notification fires — a masking failure never loses a recording.

### Encoder selection

`ChatOverlayProcessor.SelectEncodePlan` picks the fastest encoder available for
the clip's video codec, in this order:

1. **NVENC** (`h264_nvenc` / `hevc_nvenc` / `av1_nvenc`) — NVIDIA GPUs. Also
   decodes on the GPU (`-hwaccel cuda`).
2. **VAAPI** (`*_vaapi`) — AMD / Intel on Linux. Requires a `/dev/dri/renderD*`
   render node; builds a `drawbox,format=nv12,hwupload` filter chain.
3. **Software** (`libx264` / `libx265` / `libsvtav1`) — CPU fallback when no
   hardware encoder is available, and the automatic retry if a hardware encode
   errors out at runtime.

Audio is always stream-copied (`-c:a copy`); only video is re-encoded.

### Quality and size

The re-encode **targets the source clip's bitrate** (measured from file size ÷
duration), capped via VBR, so the masked clip stays ~the same size and quality
as the original. (An earlier version used a fixed quality target, which could
balloon a modestly-encoded clip 2-3x; that is fixed.) When the bitrate can't be
measured it falls back to a constant-quality re-encode.

## Performance — what to expect

Masking re-encodes the whole video, so wall time scales with **resolution ×
frame rate × duration**. It is GPU-bound on the hardware path; the dominant
factor is your capture resolution.

Measured on an RTX 3070:

| Capture resolution | NVENC throughput | A 3-4 min pull | A 29 min M+ run |
|---|---|---|---|
| 1080p60 | very fast | a few seconds | ~2 min |
| **1440p60** | **~6x realtime** | **~30-40 s** | ~5 min |
| 4K (3840x2160) 60 | ~3.3x realtime | ~1 min | ~9 min |

Two things follow from this:

- **Your capture resolution is the biggest lever.** 4K has 2.25x the pixels of
  1440p, so it masks ~2.25x slower. If masking feels slow, check the clip's
  resolution first (see below) — it usually reflects your **monitor/desktop**
  resolution at capture time, not the in-game render resolution, because
  `gpu-screen-recorder` captures actual screen pixels.
- A long 4K clip is the worst case on both axes. ~9 minutes to mask a 29-minute
  4K60 clip is expected, not a bug — the GPU's NVENC encoder is simply running
  flat-out.

## Testing on an existing clip

The `apply-overlay` daemon subcommand masks a clip you already have, so you can
dial in the region and check timing/encoder without recording:

```sh
Wipelog.Daemon apply-overlay --clip ~/Videos/wow/some-pull.mp4
```

- Region defaults to your `chatOverlay` settings; override per-run with
  `--x`, `--y`, `--width`, `--height`.
- Writes a sibling `some-pull.masked.mp4` by default (original preserved); pass
  `--output <file>` for a specific path, or `--in-place` to overwrite.
- Applies the mask regardless of the saved `enabled` toggle (it's an explicit
  test).

It logs which encoder it chose, the target bitrate, and how long it took:

```
info: ChatOverlay[0] Applying chat overlay to ... (encoder: h264_nvenc, target: 18000 kbps)
info: ChatOverlay[0] Chat overlay applied: ... (41250ms)
```

If a hardware encode fails you'll see a warning and a software retry:

```
warn: ChatOverlay[0] hardware encoder hevc_vaapi failed; retrying with software encoder libx265.
```

— the ffmpeg stderr printed just above it says why (useful for diagnosing VAAPI
flag issues on AMD).

## Inspecting a clip

Check a clip's resolution, frame rate, and duration:

```sh
ffprobe -v error -select_streams v:0 \
  -show_entries stream=width,height,r_frame_rate \
  -show_entries format=duration -of default=noprint_wrappers=1 \
  ~/Videos/wow/some-pull.mp4
```

Just the resolution, quick:

```sh
ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 some-pull.mp4
```

Confirm masking produced the expected output bitrate (should be ≈ the source):

```sh
ffprobe -v error -show_entries format=bit_rate -of csv=p=0 some-pull.masked.mp4
```

## Troubleshooting

| Symptom | Likely cause | What to do |
|---|---|---|
| Masking takes many minutes | High-resolution and/or long clip (4K, 29 min) | `ffprobe` the clip (above). 4K masks ~2.25x slower than 1440p; lower your capture resolution if you don't need 4K. |
| Masked file much larger than source | Older build with fixed-quality re-encode | Rebuild/redeploy; current builds match the source bitrate. |
| Slow **and** software encoder in the log | Hardware encoder not engaging | `ffmpeg -hide_banner -encoders \| grep -E "nvenc\|vaapi"` — if empty, install a full ffmpeg build. Otherwise check the retry warning's stderr. |
| `hevc_vaapi failed; retrying with software` (AMD) | VAAPI rate-control / device issue | Read the ffmpeg stderr above the warning; confirm `/dev/dri/renderD128` exists and your Mesa VAAPI driver supports the codec. |
| Masking changed nothing | `enabled: false`, or `width`/`height` ≤ 0 | Enable it; ensure the region is non-empty. |
| Setting changed but clips still unmasked | Daemon reads `chatOverlay` at startup | Reinstall/restart the service (Service tab). |

## Limitations

- **Full re-encode is unavoidable** with the current architecture. Tools like
  Warcraft Recorder get a "free" overlay because they're OBS-based and composite
  it *live* during capture; `gpu-screen-recorder` has no overlay support, so
  Wipelog must post-process. Matching that would mean changing the capture
  backend.
- One rectangular region, masked with a fixed Wipelog-branded black panel. No
  multiple regions, blur, or user-supplied overlay image (yet).
- In the live recording flow the mask runs before the clip is indexed, so a slow
  (e.g. 4K) mask delays the clip appearing in the library until it finishes.
