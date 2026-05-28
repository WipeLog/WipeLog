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
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.Preferences;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class ChatOverlayProcessorTests
{
    private static readonly NullLogger Logger = NullLogger.Instance;

    // ---- Pure helpers (no ffmpeg needed) -----------------------------------

    private static readonly IReadOnlyList<string> RenderNode = new[] { "/dev/dri/renderD128" };
    private static readonly IReadOnlyList<string> NoRenderNodes = Array.Empty<string>();

    [Fact]
    public void SelectEncodePlan_prefers_nvenc_when_available_and_nvidia_present()
    {
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            "libx264", "h264_nvenc", "h264_vaapi", "hevc_nvenc", "av1_nvenc",
        };
        // NVENC wins even with a render node present.
        var h264 = ChatOverlayProcessor.SelectEncodePlan("h264", available, RenderNode, nvidiaPresent: true);
        Assert.Equal("h264_nvenc", h264.Encoder);
        Assert.True(h264.IsHardware);
        Assert.False(h264.Vaapi);
        Assert.Equal("hevc_nvenc", ChatOverlayProcessor.SelectEncodePlan("hevc", available, RenderNode, nvidiaPresent: true).Encoder);
        Assert.Equal("av1_nvenc", ChatOverlayProcessor.SelectEncodePlan("av1", available, RenderNode, nvidiaPresent: true).Encoder);
    }

    [Fact]
    public void SelectEncodePlan_skips_nvenc_without_an_nvidia_device()
    {
        // ffmpeg lists *_nvenc (compiled-in) but the box has no NVIDIA GPU — the
        // AMD/Intel reality. Must fall to VAAPI, not skip past it to software.
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            "libx264", "h264_nvenc", "h264_vaapi",
        };
        var plan = ChatOverlayProcessor.SelectEncodePlan("h264", available, RenderNode, nvidiaPresent: false);
        Assert.Equal("h264_vaapi", plan.Encoder);
        Assert.True(plan.Vaapi);
        Assert.Equal("/dev/dri/renderD128", plan.VaapiDevice);
    }

    [Fact]
    public void SelectEncodePlan_skips_nvenc_without_nvidia_and_no_render_node_goes_software()
    {
        // No NVIDIA device, no render node either — software is all that's left.
        var available = new HashSet<string>(StringComparer.Ordinal) { "libx264", "h264_nvenc", "h264_vaapi" };
        var plan = ChatOverlayProcessor.SelectEncodePlan("h264", available, NoRenderNodes, nvidiaPresent: false);
        Assert.Equal("libx264", plan.Encoder);
        Assert.False(plan.IsHardware);
    }

    [Fact]
    public void SelectEncodePlan_uses_vaapi_when_render_node_and_encoder_present()
    {
        // AMD/Intel on Linux: no NVENC, but VAAPI encoders + a render node.
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            "libx264", "libx265", "libsvtav1", "h264_vaapi", "hevc_vaapi", "av1_vaapi",
        };
        var hevc = ChatOverlayProcessor.SelectEncodePlan("hevc", available, RenderNode, nvidiaPresent: false);
        Assert.Equal("hevc_vaapi", hevc.Encoder);
        Assert.True(hevc.IsHardware);
        Assert.True(hevc.Vaapi);
        Assert.Equal("/dev/dri/renderD128", hevc.VaapiDevice);

        Assert.Equal("av1_vaapi", ChatOverlayProcessor.SelectEncodePlan("av1", available, RenderNode, nvidiaPresent: false).Encoder);
    }

    [Fact]
    public void SelectEncodePlan_skips_vaapi_without_a_render_node()
    {
        // VAAPI encoder is listed but no /dev/dri render node — go software.
        var available = new HashSet<string>(StringComparer.Ordinal) { "libx264", "h264_vaapi" };
        var plan = ChatOverlayProcessor.SelectEncodePlan("h264", available, NoRenderNodes, nvidiaPresent: false);
        Assert.Equal("libx264", plan.Encoder);
        Assert.False(plan.IsHardware);
        Assert.False(plan.Vaapi);
    }

    [Fact]
    public void SelectEncodePlan_falls_back_to_software_without_hardware()
    {
        var available = new HashSet<string>(StringComparer.Ordinal) { "libx264", "libx265", "libsvtav1" };
        Assert.Equal("libx264", ChatOverlayProcessor.SelectEncodePlan("h264", available, RenderNode, nvidiaPresent: false).Encoder);
        Assert.Equal("libx265", ChatOverlayProcessor.SelectEncodePlan("hevc", available, RenderNode, nvidiaPresent: false).Encoder);
        Assert.Equal("libsvtav1", ChatOverlayProcessor.SelectEncodePlan("av1", available, RenderNode, nvidiaPresent: false).Encoder);
    }

    [Fact]
    public void SelectEncodePlan_unknown_codec_defaults_to_h264()
    {
        var available = new HashSet<string>(StringComparer.Ordinal) { "libx264" };
        Assert.Equal("libx264", ChatOverlayProcessor.SelectEncodePlan("vp9", available, RenderNode, nvidiaPresent: true).Encoder);
        Assert.Equal("libx264", ChatOverlayProcessor.SelectEncodePlan("", available, RenderNode, nvidiaPresent: true).Encoder);
    }

    [Fact]
    public void BuildDrawboxFilter_default_scale_is_pixel_identical()
    {
        // scale=1.0 must reproduce the pre-resolution-feature filter text byte
        // for byte so native-resolution recordings stay unchanged.
        var o = new ChatOverlay { X = 10, Y = 760, Width = 540, Height = 320 };
        Assert.Equal(
            "drawbox=x=10:y=ih-760-320:w=540:h=320:color=black:t=fill",
            ChatOverlayProcessor.BuildDrawboxFilter(o));
    }

    [Fact]
    public void BuildDrawboxFilter_half_scale_halves_all_four_coordinates()
    {
        // 4K mask on a 1080p output → scale = 0.5. All four dims halve.
        var o = new ChatOverlay { X = 40, Y = 800, Width = 1000, Height = 600 };
        Assert.Equal(
            "drawbox=x=20:y=ih-400-300:w=500:h=300:color=black:t=fill",
            ChatOverlayProcessor.BuildDrawboxFilter(o, scale: 0.5));
    }

    [Fact]
    public void BuildArgs_with_logo_scales_overlay_geometry()
    {
        // At scale=0.5 the logo dimensions (80% of region) and the centring
        // expression both follow the scaled W/H.
        var overlay = new ChatOverlay { Enabled = true, X = 40, Y = 760, Width = 540, Height = 320 };
        var plan = new ChatOverlayProcessor.EncodePlan("libx264", IsHardware: false, Vaapi: false, VaapiDevice: null);
        var args = ChatOverlayProcessor.BuildArgs(
            "in.mp4", "out.mp4", overlay, plan, logoPath: "logo.png", scale: 0.5);

        var fc = args[args.IndexOf("-filter_complex") + 1];
        // Scaled W=270 H=160 → drawbox at x=20 y=ih-380-160 w=270 h=160.
        Assert.Contains("drawbox=x=20:y=ih-380-160:w=270:h=160:color=black:t=fill", fc);
        // Logo scaled to 80% of the *scaled* region: 270*0.8=216, 160*0.8=128.
        Assert.Contains("scale=216:128:force_original_aspect_ratio=decrease", fc);
        Assert.Contains("overlay=x=20+(270-overlay_w)/2:y=H-380-160+(160-overlay_h)/2", fc);
    }

    [Fact]
    public void BuildArgs_software_emits_plain_drawbox_with_crf_and_no_hwaccel()
    {
        var overlay = new ChatOverlay { Enabled = true, X = 10, Y = 760, Width = 540, Height = 320 };
        var plan = new ChatOverlayProcessor.EncodePlan("libx264", IsHardware: false, Vaapi: false, VaapiDevice: null);
        var args = ChatOverlayProcessor.BuildArgs("in.mp4", "out.mp4", overlay, plan);

        var filter = args[args.IndexOf("-vf") + 1];
        // Y is bottom-anchored: box top edge = ih-Y-Height.
        Assert.Equal("drawbox=x=10:y=ih-760-320:w=540:h=320:color=black:t=fill", filter);
        Assert.DoesNotContain("-vaapi_device", args);
        Assert.DoesNotContain("-hwaccel", args);
        // Constant quality so the mask doesn't degrade the clip.
        Assert.Equal("23", args[args.IndexOf("-crf") + 1]);
        Assert.Equal("copy", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("libx264", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("in.mp4", args[args.IndexOf("-i") + 1]);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void BuildArgs_nvenc_decodes_on_gpu_and_falls_back_to_cq_without_bitrate()
    {
        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 100, Height = 100 };
        var plan = new ChatOverlayProcessor.EncodePlan("h264_nvenc", IsHardware: true, Vaapi: false, VaapiDevice: null);
        var args = ChatOverlayProcessor.BuildArgs("in.mp4", "out.mp4", overlay, plan);

        // GPU decode offload; no measured bitrate -> constant-quality fallback.
        Assert.Equal("cuda", args[args.IndexOf("-hwaccel") + 1]);
        Assert.Equal("26", args[args.IndexOf("-cq") + 1]);
        Assert.DoesNotContain("-b:v", args);
        Assert.Equal("h264_nvenc", args[args.IndexOf("-c:v") + 1]);
        // No VAAPI hwupload tail on the plain drawbox path.
        Assert.Equal("drawbox=x=0:y=ih-0-100:w=100:h=100:color=black:t=fill", args[args.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void BuildArgs_targets_source_bitrate_when_known()
    {
        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 100, Height = 100 };
        var plan = new ChatOverlayProcessor.EncodePlan("h264_nvenc", IsHardware: true, Vaapi: false, VaapiDevice: null);
        var args = ChatOverlayProcessor.BuildArgs("in.mp4", "out.mp4", overlay, plan, targetKbps: 25000);

        // VBR capped at the source bitrate so the masked clip doesn't balloon.
        Assert.Equal("25000k", args[args.IndexOf("-b:v") + 1]);
        Assert.Equal("25000k", args[args.IndexOf("-maxrate") + 1]);
        Assert.Equal("50000k", args[args.IndexOf("-bufsize") + 1]);
        Assert.DoesNotContain("-cq", args);     // bitrate target replaces constant-quality
        Assert.Equal("p1", args[args.IndexOf("-preset") + 1]); // still fast preset
    }

    [Fact]
    public void QualityArgs_vaapi_uses_plain_vbr_not_cqp_when_bitrate_known()
    {
        // CQP is what some AMD drivers reject; a measured bitrate switches to VBR.
        var args = ChatOverlayProcessor.QualityArgs("hevc_vaapi", targetKbps: 30000).ToList();
        Assert.Equal("30000k", args[args.IndexOf("-b:v") + 1]);
        Assert.DoesNotContain("-qp", args);
        Assert.DoesNotContain("-rc_mode", args);
    }

    [Fact]
    public void BuildArgs_with_logo_composites_branded_overlay_over_black_box()
    {
        var overlay = new ChatOverlay { Enabled = true, X = 40, Y = 760, Width = 540, Height = 320 };
        var plan = new ChatOverlayProcessor.EncodePlan("libx264", IsHardware: false, Vaapi: false, VaapiDevice: null);
        var args = ChatOverlayProcessor.BuildArgs("in.mp4", "out.mp4", overlay, plan, logoPath: "logo.png");

        // Two inputs (clip + logo), filter_complex (not -vf), explicit maps.
        Assert.DoesNotContain("-vf", args);
        Assert.Equal("logo.png", args[args.LastIndexOf("-i") + 1]);
        var fc = args[args.IndexOf("-filter_complex") + 1];
        Assert.Contains("drawbox=x=40:y=ih-760-320:w=540:h=320:color=black:t=fill", fc);
        Assert.Contains("scale=432:256:force_original_aspect_ratio=decrease", fc); // 80% of region
        // Logo centred within the bottom-anchored box (top edge = H-Y-Height).
        Assert.Contains("overlay=x=40+(540-overlay_w)/2:y=H-760-320+(320-overlay_h)/2", fc);
        Assert.Equal("[v]", args[args.IndexOf("-map") + 1]);
        Assert.Equal("0:a?", args[args.LastIndexOf("-map") + 1]);
    }

    [Fact]
    public void BuildArgs_with_logo_vaapi_uploads_after_overlay()
    {
        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 500, Height = 300 };
        var plan = new ChatOverlayProcessor.EncodePlan(
            "h264_vaapi", IsHardware: true, Vaapi: true, VaapiDevice: "/dev/dri/renderD128");
        var args = ChatOverlayProcessor.BuildArgs("in.mp4", "out.mp4", overlay, plan, logoPath: "logo.png");

        var fc = args[args.IndexOf("-filter_complex") + 1];
        Assert.EndsWith(",format=nv12,hwupload[v]", fc);
        Assert.Equal("/dev/dri/renderD128", args[args.IndexOf("-vaapi_device") + 1]);
    }

    [Fact]
    public void BuildArgs_vaapi_uploads_frames_sets_device_and_qp()
    {
        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 100, Height = 100 };
        var plan = new ChatOverlayProcessor.EncodePlan(
            "hevc_vaapi", IsHardware: true, Vaapi: true, VaapiDevice: "/dev/dri/renderD128");
        var args = ChatOverlayProcessor.BuildArgs("in.mp4", "out.mp4", overlay, plan);

        Assert.Equal("/dev/dri/renderD128", args[args.IndexOf("-vaapi_device") + 1]);
        Assert.DoesNotContain("-hwaccel", args); // VAAPI decodes in software; encode path uploads
        var filter = args[args.IndexOf("-vf") + 1];
        Assert.Equal("drawbox=x=0:y=ih-0-100:w=100:h=100:color=black:t=fill,format=nv12,hwupload", filter);
        Assert.Equal("hevc_vaapi", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("26", args[args.IndexOf("-qp") + 1]);
        Assert.Equal("copy", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void ParseEncoderNames_extracts_names_from_encoders_listing()
    {
        const string listing =
            "Encoders:\n" +
            " V..... = Video\n" +
            " ------\n" +
            " V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)\n" +
            " V....D libx264              libx264 H.264 / AVC\n" +
            " A....D aac                  AAC (Advanced Audio Coding)\n";
        var names = ChatOverlayProcessor.ParseEncoderNames(listing).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("h264_nvenc", names);
        Assert.Contains("libx264", names);
        Assert.Contains("aac", names);
        // Header / separator lines must not be mistaken for encoder names.
        Assert.DoesNotContain("=", names);
        Assert.DoesNotContain("------", names);
    }

    // ---- Best-effort guards (no ffmpeg needed) -----------------------------

    [Fact]
    public async Task ApplyAsync_returns_false_for_disabled_overlay()
    {
        var overlay = new ChatOverlay { Enabled = false };
        var ok = await ChatOverlayProcessor.ApplyAsync(
            "does-not-matter.mp4", overlay, Logger, TestContext.Current.CancellationToken);
        Assert.False(ok);
    }

    [Fact]
    public async Task ApplyAsync_returns_false_for_zero_sized_region()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "clip.mp4");
        await File.WriteAllTextAsync(path, "not really a video", TestContext.Current.CancellationToken);

        var overlay = new ChatOverlay { Enabled = true, Width = 0, Height = 320 };
        var ok = await ChatOverlayProcessor.ApplyAsync(
            path, overlay, Logger, TestContext.Current.CancellationToken);

        Assert.False(ok);
        // Original untouched.
        Assert.Equal("not really a video", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyAsync_returns_false_for_missing_input()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "missing.mp4");
        var overlay = new ChatOverlay { Enabled = true, Width = 100, Height = 100 };

        var ok = await ChatOverlayProcessor.ApplyAsync(
            path, overlay, Logger, TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ApplyAsync_returns_false_when_ffmpeg_missing()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "clip.mp4");
        await File.WriteAllTextAsync(path, "stub", TestContext.Current.CancellationToken);

        var overlay = new ChatOverlay { Enabled = true, Width = 100, Height = 100 };
        var ok = await ChatOverlayProcessor.ApplyAsync(
            path, overlay, Logger, TestContext.Current.CancellationToken,
            ffmpegPath: "definitely-not-ffmpeg-xyz", ffprobePath: "definitely-not-ffprobe-xyz");

        Assert.False(ok);
        // Original preserved on launch failure.
        Assert.Equal("stub", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    // ---- Real-ffmpeg integration (skipped when ffmpeg is absent) -----------

    [Fact]
    public async Task ApplyAsync_blacks_out_the_configured_region()
    {
        if (!FfmpegAvailable()) return;
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "clip.mp4");
        Assert.True(await GenerateSolidMp4Async(path, "white", ct));

        // Mask the bottom-left 100x100 corner (Y is measured from the bottom, so
        // Y=0 sits the box flush with the bottom edge). On a 240px-tall clip that
        // blacks out rows 140..240.
        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 100, Height = 100 };
        var ok = await ChatOverlayProcessor.ApplyAsync(path, overlay, Logger, ct);

        Assert.True(ok);
        Assert.True(File.Exists(path));

        var maskedLuma = await RegionAvgLumaAsync(path, 0, 140, 100, 100, ct);
        var clearLuma = await RegionAvgLumaAsync(path, 200, 10, 100, 100, ct);

        Assert.True(maskedLuma < 50, $"masked region should be dark, got YAVG={maskedLuma}");
        Assert.True(clearLuma > 150, $"un-masked region should stay bright, got YAVG={clearLuma}");
    }

    [Fact]
    public async Task MaskToFileAsync_writes_dest_and_leaves_source_untouched()
    {
        if (!FfmpegAvailable()) return;
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "source.mp4");
        var dest = Path.Combine(dir.Path, "source.masked.mp4");
        Assert.True(await GenerateSolidMp4Async(source, "white", ct));
        var sourceBytesBefore = await File.ReadAllBytesAsync(source, ct);

        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 100, Height = 100 };
        var ok = await ChatOverlayProcessor.MaskToFileAsync(source, dest, overlay, Logger, ct);

        Assert.True(ok);
        Assert.True(File.Exists(dest));
        // Source is byte-for-byte unchanged.
        Assert.Equal(sourceBytesBefore, await File.ReadAllBytesAsync(source, ct));
        // Dest has the region blacked out (bottom-anchored Y=0 -> rows 140..240);
        // an untouched region stays bright.
        Assert.True(await RegionAvgLumaAsync(dest, 0, 140, 100, 100, ct) < 50);
        Assert.True(await RegionAvgLumaAsync(dest, 200, 10, 100, 100, ct) > 150);
    }

    [Fact]
    public async Task MaskToFileAsync_returns_false_for_missing_source()
    {
        using var dir = new TempDirectory();
        var overlay = new ChatOverlay { Enabled = true, Width = 100, Height = 100 };
        var ok = await ChatOverlayProcessor.MaskToFileAsync(
            Path.Combine(dir.Path, "nope.mp4"), Path.Combine(dir.Path, "out.mp4"),
            overlay, Logger, TestContext.Current.CancellationToken);
        Assert.False(ok);
        Assert.False(File.Exists(Path.Combine(dir.Path, "out.mp4")));
    }

    [Fact]
    public async Task MaskToFileAsync_returns_false_for_zero_sized_region()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "source.mp4");
        await File.WriteAllTextAsync(source, "stub", TestContext.Current.CancellationToken);
        var overlay = new ChatOverlay { Enabled = true, Width = 0, Height = 100 };
        var ok = await ChatOverlayProcessor.MaskToFileAsync(
            source, Path.Combine(dir.Path, "out.mp4"), overlay, Logger,
            TestContext.Current.CancellationToken);
        Assert.False(ok);
        Assert.False(File.Exists(Path.Combine(dir.Path, "out.mp4")));
    }

    [Theory]
    [InlineData("libx264", "h264")]
    [InlineData("libx265", "hevc")]
    public async Task ApplyAsync_preserves_input_codec_family(string inputEncoder, string expectedCodec)
    {
        if (!FfmpegAvailable()) return;
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "clip.mp4");
        Assert.True(await GenerateSolidMp4Async(path, "white", ct, inputEncoder));

        var overlay = new ChatOverlay { Enabled = true, X = 0, Y = 0, Width = 80, Height = 80 };
        var ok = await ChatOverlayProcessor.ApplyAsync(path, overlay, Logger, ct);

        Assert.True(ok);
        var outCodec = await FfmpegProbe.GetVideoCodecNameAsync(path, Logger, ct);
        Assert.Equal(expectedCodec, outCodec);
    }

    // ---- ffmpeg test helpers -----------------------------------------------

    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            p.WaitForExit(3000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<bool> GenerateSolidMp4Async(
        string path, string color, CancellationToken ct, string encoder = "libx264")
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add($"color=c={color}:s=320x240:r=10:d=1");
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add(encoder);
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi);
        if (p is null) return false;
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 && File.Exists(path);
    }

    /// <summary>
    /// Average luma (signalstats YAVG, 0..255) of a cropped region of the first
    /// frame. Used to assert that drawbox actually blacked out the region.
    /// </summary>
    private static async Task<double> RegionAvgLumaAsync(
        string path, int x, int y, int w, int h, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add($"crop={w}:{h}:{x}:{y},signalstats,metadata=mode=print");
        psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");

        using var p = Process.Start(psi)!;
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        var m = Regex.Match(stderr, @"YAVG=([\d.]+)");
        Assert.True(m.Success, $"signalstats produced no YAVG. stderr:\n{stderr}");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
