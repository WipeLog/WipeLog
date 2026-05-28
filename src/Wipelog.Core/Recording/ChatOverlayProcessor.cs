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

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Preferences;

namespace Wipelog.Core.Recording;

/// <summary>
/// Composites a solid black rectangle over a configured screen region of a
/// saved clip using ffmpeg's <c>drawbox</c> filter — the opt-in "chat overlay"
/// privacy mask. Unlike the pre-roll concat, a pixel filter cannot stream-copy,
/// so the video is re-encoded. We pick the fastest available encoder for the
/// input codec: NVENC (NVIDIA) first, then VAAPI (AMD/Intel on Linux), then a
/// software encoder. Audio is stream-copied.
///
/// The whole step is best-effort: <see cref="ApplyAsync"/> returns <c>false</c>
/// and leaves the original clip untouched on any failure, so a masking failure
/// never loses a recording. A hardware encode that fails (driver/device issues)
/// automatically retries once in software before giving up.
/// </summary>
public static class ChatOverlayProcessor
{
    private static IReadOnlySet<string>? _cachedEncoders;
    private static readonly SemaphoreSlim _encoderLock = new(1, 1);

    /// <summary>How a given clip will be re-encoded.</summary>
    internal sealed record EncodePlan(string Encoder, bool IsHardware, bool Vaapi, string? VaapiDevice);

    /// <summary>
    /// Masks <paramref name="clipPath"/> in place. Returns <c>true</c> if the
    /// clip was re-encoded with the overlay and atomically replaced; <c>false</c>
    /// (original untouched) on any failure or no-op (disabled / zero-sized box /
    /// missing file). Thin wrapper around <see cref="MaskToFileAsync"/>: mask to
    /// a temp sibling, then rename over the original.
    ///
    /// <paramref name="scale"/> multiplies the mask coordinates so the same
    /// configured region covers the same visual area when the clip was recorded
    /// at a lower resolution than the user's monitor. <c>1.0</c> = pixel-identical
    /// to the configured region (the default for native-resolution recordings).
    /// </summary>
    public static async Task<bool> ApplyAsync(
        string clipPath,
        ChatOverlay overlay,
        ILogger logger,
        CancellationToken ct,
        string ffmpegPath = "ffmpeg",
        string ffprobePath = "ffprobe",
        double scale = 1.0)
    {
        if (overlay is null || !overlay.Enabled)
        {
            return false;
        }

        var tmp = clipPath + ".mask.tmp" + Path.GetExtension(clipPath);
        var ok = await MaskToFileAsync(clipPath, tmp, overlay, logger, ct, ffmpegPath, ffprobePath, scale: scale)
            .ConfigureAwait(false);
        if (!ok)
        {
            TryDelete(tmp);
            return false;
        }

        try
        {
            File.Move(tmp, clipPath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Chat overlay failed: could not replace {Clip} with masked output.", clipPath);
            TryDelete(tmp);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Masks <paramref name="sourcePath"/> into a new file <paramref name="destPath"/>,
    /// leaving the source untouched (ffmpeg reads the source and writes a fresh
    /// file natively — no pre-copy). Returns <c>true</c> if <paramref name="destPath"/>
    /// was written; <c>false</c> (dest removed) on any failure. Always masks —
    /// the <see cref="ChatOverlay.Enabled"/> toggle is the caller's concern.
    /// When <paramref name="progress"/> is supplied, reports 0..100 from ffmpeg's
    /// <c>-progress</c> output against the source duration.
    /// </summary>
    public static async Task<bool> MaskToFileAsync(
        string sourcePath,
        string destPath,
        ChatOverlay overlay,
        ILogger logger,
        CancellationToken ct,
        string ffmpegPath = "ffmpeg",
        string ffprobePath = "ffprobe",
        IProgress<double>? progress = null,
        double scale = 1.0)
    {
        if (overlay is null || overlay.Width <= 0 || overlay.Height <= 0)
        {
            logger.LogWarning(
                "Chat overlay failed: width and height must be greater than 0 (got {W}x{H}); skipping.",
                overlay?.Width ?? 0, overlay?.Height ?? 0);
            return false;
        }
        if (!File.Exists(sourcePath))
        {
            logger.LogWarning("Chat overlay failed: source {Path} does not exist; skipping.", sourcePath);
            return false;
        }

        var sw = Stopwatch.StartNew();
        var codecName = await FfmpegProbe.GetVideoCodecNameAsync(sourcePath, logger, ct, ffprobePath)
            .ConfigureAwait(false) ?? "h264";
        var available = await GetAvailableEncodersAsync(ffmpegPath, logger, ct).ConfigureAwait(false);
        var plan = SelectEncodePlan(codecName, available, RenderNodes(), NvidiaDevicePresent());
        var durationSec = await FfmpegProbe.GetDurationSecondsAsync(sourcePath, logger, ct, ffprobePath)
            .ConfigureAwait(false);
        var targetKbps = TargetBitrateKbps(sourcePath, durationSec);
        // Branded mask (logo + wordmark) composited over the black box. Null if
        // the embedded asset can't be extracted — then we fall back to a plain
        // black box (privacy is unaffected either way).
        var logoPath = await EnsureLogoAsync(logger, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Applying chat overlay to {Source} (region: {X},{Y} {W}x{H}, scale: {Scale}, encoder: {Encoder}, target: {Kbps})",
            sourcePath, overlay.X, overlay.Y, overlay.Width, overlay.Height,
            scale.ToString("0.###", CultureInfo.InvariantCulture),
            plan.Encoder,
            targetKbps is int k ? $"{k} kbps" : "constant-quality");

        var ok = await RunEncodeAsync(
                ffmpegPath, BuildArgs(sourcePath, destPath, overlay, plan, targetKbps, logoPath, scale),
                logger, ct, durationSec, progress)
            .ConfigureAwait(false);

        // A hardware encode can fail at runtime (missing VAAPI device, driver
        // mismatch, NVENC session limit). Fall back to software once before
        // giving up so the mask still lands on machines we couldn't probe.
        if (!ok && plan.IsHardware)
        {
            TryDelete(destPath);
            var software = SoftwareEncoder(codecName);
            logger.LogWarning(
                "Chat overlay: hardware encoder {Hw} failed; retrying with software encoder {Sw}.",
                plan.Encoder, software);
            var softwarePlan = new EncodePlan(software, IsHardware: false, Vaapi: false, VaapiDevice: null);
            ok = await RunEncodeAsync(
                    ffmpegPath, BuildArgs(sourcePath, destPath, overlay, softwarePlan, targetKbps, logoPath, scale),
                    logger, ct, durationSec, progress)
                .ConfigureAwait(false);
        }

        if (!ok)
        {
            TryDelete(destPath);
            return false;
        }

        sw.Stop();
        logger.LogInformation("Chat overlay written: {Dest} ({Duration}ms)", destPath, sw.ElapsedMilliseconds);
        return true;
    }

    private static async Task<bool> RunEncodeAsync(
        string ffmpegPath, List<string> args, ILogger logger, CancellationToken ct,
        double? totalSeconds = null, IProgress<double>? progress = null)
    {
        var reportProgress = progress is not null && totalSeconds is > 0;

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (reportProgress)
        {
            // -progress writes machine-readable key=value blocks to stdout,
            // independent of -loglevel; we parse out_time_us to drive a bar.
            psi.ArgumentList.Add("-progress"); psi.ArgumentList.Add("pipe:1");
            psi.ArgumentList.Add("-nostats");
        }
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Chat overlay failed: could not launch ffmpeg ({Path}). " +
                "Install ffmpeg via your package manager to enable chat masking.",
                ffmpegPath);
            return false;
        }
        if (proc is null)
        {
            logger.LogError("Chat overlay failed: Process.Start returned null for ffmpeg path '{Path}'.", ffmpegPath);
            return false;
        }

        using (proc)
        {
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            Task stdoutTask = reportProgress
                ? PumpProgressAsync(proc.StandardOutput, totalSeconds!.Value, progress!, ct)
                : proc.StandardOutput.ReadToEndAsync(ct); // drain to avoid a full pipe
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                var stderr = await stderrTask.ConfigureAwait(false);
                logger.LogError(
                    "Chat overlay failed: ffmpeg exited with code {Code}. stderr:\n{Stderr}",
                    proc.ExitCode, stderr);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Reads ffmpeg's <c>-progress</c> stdout and reports 0..100 from
    /// <c>out_time_us</c> against the known total duration. Best-effort: parse
    /// failures simply leave the bar where it was.
    /// </summary>
    private static async Task PumpProgressAsync(
        StreamReader stdout, double totalSeconds, IProgress<double> progress, CancellationToken ct)
    {
        string? line;
        while ((line = await stdout.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (!line.StartsWith("out_time_us=", StringComparison.Ordinal)) continue;
            if (long.TryParse(
                    line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us)
                && us >= 0)
            {
                progress.Report(Math.Clamp(us / 1_000_000.0 / totalSeconds * 100.0, 0, 100));
            }
        }
    }

    /// <summary>
    /// Builds the ffmpeg argument list for the drawbox re-encode. Pure; no I/O.
    ///
    /// Three tuning concerns are baked in per encoder:
    /// - <b>Decode acceleration</b>: NVENC decodes on the GPU (<c>-hwaccel cuda</c>,
    ///   frames auto-download for the CPU drawbox), which roughly halves wall time
    ///   for high-res clips. VAAPI keeps software decode because the encode path
    ///   already uploads frames (<c>format=nv12,hwupload</c> + <c>-vaapi_device</c>),
    ///   and double-accelerating would clash with that upload.
    /// - <b>Quality</b>: a constant-quality target so the mask doesn't degrade the
    ///   clip. ffmpeg's default rate control on NVENC drops the bitrate ~10x, so
    ///   this is essential, not cosmetic.
    /// - <b>Speed</b>: the fastest hardware preset — with constant quality the
    ///   preset only trades file size, not visible quality, so favour speed.
    /// </summary>
    /// <summary>
    /// The <c>drawbox</c> filter for the mask. <see cref="ChatOverlay.Y"/> is
    /// measured from the <b>bottom</b> edge of the frame (WoW's chat sits
    /// bottom-left): <c>Y</c> is the gap from the screen bottom up to the box's
    /// bottom edge, so the box top is <c>ih-Y-Height</c>. Expressing it via
    /// ffmpeg's <c>ih</c> keeps the mask glued to the bottom regardless of the
    /// clip's actual height — a region dialled in at one resolution still masks
    /// the same chat area on a differently-sized clip. <c>X</c> stays measured
    /// from the left. Shared with the GUI "Test mask" snapshot so the preview
    /// and the recorded mask use identical geometry.
    ///
    /// <paramref name="scale"/> multiplies X/Y/Width/Height so that overlay
    /// coordinates configured against the user's monitor still cover the same
    /// proportional region on a downscaled recording. <c>1.0</c> = identity.
    /// </summary>
    public static string BuildDrawboxFilter(ChatOverlay o, double scale = 1.0)
    {
        var (x, y, w, h) = ScaledRegion(o, scale);
        return $"drawbox=x={x}:y=ih-{y}-{h}:w={w}:h={h}:color=black:t=fill";
    }

    /// <summary>
    /// Scales (and rounds) the overlay's X/Y/W/H by <paramref name="scale"/>.
    /// Width and height are clamped to at least 1 so ffmpeg doesn't reject a
    /// zero-size drawbox at low scale factors.
    /// </summary>
    internal static (int X, int Y, int W, int H) ScaledRegion(ChatOverlay o, double scale)
    {
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1.0;
        int x = (int)Math.Round(o.X * scale);
        int y = (int)Math.Round(o.Y * scale);
        int w = Math.Max(1, (int)Math.Round(o.Width * scale));
        int h = Math.Max(1, (int)Math.Round(o.Height * scale));
        return (x, y, w, h);
    }

    internal static List<string> BuildArgs(
        string input, string output, ChatOverlay o, EncodePlan plan,
        int? targetKbps = null, string? logoPath = null, double scale = 1.0)
    {
        var (sx, sy, sw, sh) = ScaledRegion(o, scale);
        var box = BuildDrawboxFilter(o, scale);
        var args = new List<string> { "-y", "-loglevel", "error" };
        if (plan.Vaapi && !string.IsNullOrEmpty(plan.VaapiDevice))
        {
            args.Add("-vaapi_device"); args.Add(plan.VaapiDevice!);
        }
        else if (IsNvenc(plan.Encoder))
        {
            args.Add("-hwaccel"); args.Add("cuda");
        }
        args.Add("-i"); args.Add(input);

        if (logoPath is not null)
        {
            // Black box for privacy, then the Wipelog logo+wordmark composited on
            // top — scaled to fit within 80% of the region (aspect preserved) and
            // centred, so it never distorts regardless of region dimensions.
            var lw = Math.Max(1, sw * 8 / 10);
            var lh = Math.Max(1, sh * 8 / 10);
            var fc =
                $"[1:v]scale={lw}:{lh}:force_original_aspect_ratio=decrease[logo];" +
                $"[0:v]{box}[bg];" +
                // Y is bottom-anchored (see BuildDrawboxFilter): the box top edge
                // is H-Y-Height, so centre the logo within [boxTop, boxTop+Height].
                $"[bg][logo]overlay=x={sx}+({sw}-overlay_w)/2:y=H-{sy}-{sh}+({sh}-overlay_h)/2" +
                (plan.Vaapi ? ",format=nv12,hwupload[v]" : "[v]");
            args.Add("-i"); args.Add(logoPath);
            args.Add("-filter_complex"); args.Add(fc);
            args.Add("-map"); args.Add("[v]");
            args.Add("-map"); args.Add("0:a?");
        }
        else
        {
            args.Add("-vf"); args.Add(plan.Vaapi ? $"{box},format=nv12,hwupload" : box);
        }

        args.Add("-c:v"); args.Add(plan.Encoder);
        args.AddRange(QualityArgs(plan.Encoder, targetKbps));
        args.Add("-c:a"); args.Add("copy");
        args.Add(output);
        return args;
    }

    private static string? _logoPath;
    private static readonly SemaphoreSlim _logoLock = new(1, 1);

    /// <summary>
    /// Extracts the embedded branded-mask PNG to a temp file (once, cached) and
    /// returns its path. Returns <c>null</c> if the resource is missing or can't
    /// be written — callers then fall back to a plain black box.
    /// </summary>
    private static async Task<string?> EnsureLogoAsync(ILogger logger, CancellationToken ct)
    {
        if (_logoPath is not null && File.Exists(_logoPath)) return _logoPath;
        await _logoLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_logoPath is not null && File.Exists(_logoPath)) return _logoPath;
            var asm = typeof(ChatOverlayProcessor).Assembly;
            await using var stream = asm.GetManifestResourceStream("wipelog-mask.png");
            if (stream is null)
            {
                logger.LogDebug("Branded mask asset not found; falling back to a plain black box.");
                return null;
            }
            var path = Path.Combine(Path.GetTempPath(), "wipelog-mask.png");
            await using (var file = File.Create(path))
            {
                await stream.CopyToAsync(file, ct).ConfigureAwait(false);
            }
            _logoPath = path;
            return path;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not extract branded mask asset; falling back to a plain black box.");
            return null;
        }
        finally
        {
            _logoLock.Release();
        }
    }

    private static bool IsNvenc(string encoder) => encoder.EndsWith("_nvenc", StringComparison.Ordinal);

    /// <summary>
    /// Encoder-specific speed + rate-control flags.
    ///
    /// When the source bitrate is known we target it (VBR capped at the source
    /// rate via <c>-maxrate</c>/<c>-bufsize</c>) so the masked clip stays ~the
    /// same size and quality as the original — a fixed quality target (crf/cq)
    /// ignores the source and can balloon a modestly-encoded clip 2-3x. Plain
    /// VBR (<c>-b:v</c>) is also the most widely supported VAAPI rate-control
    /// mode, unlike CQP which some AMD drivers reject. The fast preset only
    /// trades file size, not visible quality. When the bitrate can't be measured
    /// we fall back to a constant-quality target.
    /// </summary>
    internal static IReadOnlyList<string> QualityArgs(string encoder, int? targetKbps)
    {
        var list = new List<string>();
        if (IsNvenc(encoder)) { list.Add("-preset"); list.Add("p1"); }
        else if (encoder is "libx264" or "libx265") { list.Add("-preset"); list.Add("veryfast"); }
        else if (encoder == "libsvtav1") { list.Add("-preset"); list.Add("8"); }

        if (targetKbps is int k && k > 0)
        {
            var kbps = k.ToString(CultureInfo.InvariantCulture);
            var bufsize = (k * 2).ToString(CultureInfo.InvariantCulture);
            list.Add("-b:v"); list.Add($"{kbps}k");
            list.Add("-maxrate"); list.Add($"{kbps}k");
            list.Add("-bufsize"); list.Add($"{bufsize}k");
        }
        else if (IsNvenc(encoder)) { list.Add("-cq"); list.Add("26"); }
        else if (encoder.EndsWith("_vaapi", StringComparison.Ordinal)) { list.Add("-qp"); list.Add("26"); }
        else if (encoder == "libx265") { list.Add("-crf"); list.Add("26"); }
        else if (encoder == "libsvtav1") { list.Add("-crf"); list.Add("30"); }
        else { list.Add("-crf"); list.Add("23"); } // libx264 / fallback
        return list;
    }

    /// <summary>
    /// Target video bitrate in kbps from file size and duration — robust against
    /// missing container <c>bit_rate</c> metadata. <c>null</c> when the duration
    /// is unknown, so the caller falls back to a constant-quality re-encode.
    /// </summary>
    private static int? TargetBitrateKbps(string sourcePath, double? durationSec)
    {
        if (durationSec is not > 0) return null;
        try
        {
            var bytes = new FileInfo(sourcePath).Length;
            if (bytes <= 0) return null;
            var kbps = (int)Math.Round(bytes * 8.0 / durationSec.Value / 1000.0);
            return kbps > 0 ? kbps : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Picks the encode plan for <paramref name="codecName"/> (the input video
    /// codec): NVENC when a matching encoder exists <i>and</i>
    /// <paramref name="nvidiaPresent"/> (a real NVIDIA device node), else VAAPI
    /// when a render node and a matching VAAPI encoder both exist, else the
    /// matching software encoder. Unknown codecs fall back to the H.264 family.
    ///
    /// The NVIDIA-device gate matters because ffmpeg advertises <c>*_nvenc</c>
    /// encoders whenever it was <i>compiled</i> with NVENC support — even on
    /// AMD/Intel machines with no NVIDIA GPU. Selecting NVENC there fails at
    /// runtime (<c>CUDA_ERROR_NO_DEVICE</c>) and would skip straight past the
    /// VAAPI tier into slow software encoding.
    /// </summary>
    internal static EncodePlan SelectEncodePlan(
        string codecName, IReadOnlySet<string> available, IReadOnlyList<string> renderNodes, bool nvidiaPresent)
    {
        var (_, nvenc, vaapi, software) = NormalizeCodec(codecName);
        if (nvidiaPresent && available.Contains(nvenc))
        {
            return new EncodePlan(nvenc, IsHardware: true, Vaapi: false, VaapiDevice: null);
        }
        if (renderNodes.Count > 0 && available.Contains(vaapi))
        {
            return new EncodePlan(vaapi, IsHardware: true, Vaapi: true, VaapiDevice: renderNodes[0]);
        }
        return new EncodePlan(software, IsHardware: false, Vaapi: false, VaapiDevice: null);
    }

    internal static string SoftwareEncoder(string codecName) => NormalizeCodec(codecName).Software;

    private static (string Family, string Nvenc, string Vaapi, string Software) NormalizeCodec(string codecName)
    {
        switch (codecName?.Trim().ToLowerInvariant())
        {
            case "hevc":
            case "h265":
                return ("hevc", "hevc_nvenc", "hevc_vaapi", "libx265");
            case "av1":
                return ("av1", "av1_nvenc", "av1_vaapi", "libsvtav1");
            case "h264":
            case "avc":
            default:
                return ("h264", "h264_nvenc", "h264_vaapi", "libx264");
        }
    }

    /// <summary>
    /// DRM render nodes (<c>/dev/dri/renderD*</c>) usable for VAAPI, lowest
    /// number first. Empty when none exist (no GPU / container without /dev/dri),
    /// in which case the VAAPI tier is skipped.
    /// </summary>
    internal static IReadOnlyList<string> RenderNodes()
    {
        try
        {
            if (!Directory.Exists("/dev/dri")) return Array.Empty<string>();
            return Directory.GetFiles("/dev/dri", "renderD*")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Whether a real NVIDIA GPU is present — the NVIDIA kernel driver creates
    /// <c>/dev/nvidia0</c>, <c>/dev/nvidiactl</c>, etc. ffmpeg listing an
    /// <c>*_nvenc</c> encoder only means it was built with NVENC support, not
    /// that the hardware exists, so we gate the NVENC tier on this.
    /// </summary>
    internal static bool NvidiaDevicePresent()
    {
        try
        {
            return Directory.Exists("/dev") && Directory.EnumerateFiles("/dev", "nvidia*").Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates the encoders ffmpeg reports via <c>-encoders</c>, cached for the
    /// process lifetime. Returns an empty set if ffmpeg can't be run (callers then
    /// fall back to software encoders, which are always built in).
    /// </summary>
    internal static async Task<IReadOnlySet<string>> GetAvailableEncodersAsync(
        string ffmpegPath, ILogger logger, CancellationToken ct)
    {
        if (_cachedEncoders is { } cached)
        {
            return cached;
        }
        await _encoderLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedEncoders is { } c) return c;

            var set = new HashSet<string>(StringComparer.Ordinal);
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");

            try
            {
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                    await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                    var stdout = await stdoutTask.ConfigureAwait(false);
                    foreach (var name in ParseEncoderNames(stdout))
                    {
                        set.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not enumerate ffmpeg encoders ({Path}); assuming software only.", ffmpegPath);
            }

            _cachedEncoders = set;
            return set;
        }
        finally
        {
            _encoderLock.Release();
        }
    }

    /// <summary>
    /// Parses encoder names from <c>ffmpeg -encoders</c> output. Each entry line
    /// has the shape <c>" V....D h264_nvenc           NVIDIA NVENC ..."</c>: six
    /// capability flag columns, the encoder name, then a description.
    /// </summary>
    internal static IEnumerable<string> ParseEncoderNames(string stdout)
    {
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 8) continue;
            // Entry lines start with a space then six flag chars (e.g. " V....D ").
            if (line[0] != ' ') continue;
            var parts = line.TrimStart().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var flags = parts[0];
            // Capability column is exactly six chars of [VAS.][.F][.S][.X][.B][.D].
            if (flags.Length != 6) continue;
            var name = parts[1];
            // The header legend ("V..... = Video") also has six flag-ish chars;
            // a real encoder name is a bare identifier, so reject anything else.
            if (!IsEncoderName(name)) continue;
            yield return name;
        }
    }

    private static bool IsEncoderName(string s)
    {
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return s.Length > 0;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
