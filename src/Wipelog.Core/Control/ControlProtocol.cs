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

using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wipelog.Core.Control;

/// <summary>
/// Shape of a single request the GUI sends to the daemon over the Unix
/// control socket. One JSON object per connection; the daemon answers with
/// a single <see cref="ControlResponse"/> and closes the socket.
///
/// <para>
/// Recognised today: <c>stopRecording</c> (finalises the current clip;
/// success=true for Manual, success=false otherwise) and
/// <c>startManualRecording</c> (begins a free-form Manual recording).
/// Unknown commands answer with <c>ok=false</c> and a descriptive error
/// so new commands can land without bumping a protocol version.
/// </para>
/// </summary>
public sealed record ControlRequest(
    [property: JsonPropertyName("command")] string Command);

/// <summary>
/// Single response the daemon writes back. <see cref="Ok"/> distinguishes
/// success from a recoverable failure; <see cref="Error"/> carries a user-
/// presentable reason (e.g. "no active recording") on the failure path.
/// <see cref="State"/> is informational — at the moment one of "recording"
/// or "idle" — and tells the GUI what to display after the call returns.
/// </summary>
public sealed record ControlResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("state")] string? State = null,
    [property: JsonPropertyName("error")] string? Error = null);

public static class ControlCommands
{
    public const string StopRecording = "stopRecording";
    public const string StartManualRecording = "startManualRecording";
}

public static class ControlProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// Resolves the canonical Unix-domain socket path the daemon listens on.
/// Both client and server compute this so neither side has to be configured
/// with an absolute path.
///
/// <para>
/// Order of preference:
/// <list type="number">
/// <item><c>$XDG_RUNTIME_DIR/wipelog/control.sock</c> — the standard per-user
///     runtime dir on systemd-managed sessions; cleaned up at logout.</item>
/// <item><c>/tmp/wipelog-{uid}/control.sock</c> — fallback when
///     <c>XDG_RUNTIME_DIR</c> isn't set (rare on a desktop session, but
///     happens for non-login shells and some CI environments).</item>
/// </list>
/// </para>
/// </summary>
public static class ControlSocketPath
{
    public const string SocketFileName = "control.sock";
    private const string DirName = "wipelog";

    public static string Resolve()
    {
        var dir = ResolveDirectory();
        return Path.Combine(dir, SocketFileName);
    }

    public static string ResolveDirectory()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdg) && Directory.Exists(xdg))
        {
            return Path.Combine(xdg, DirName);
        }
        var uid = GetUid();
        return Path.Combine(Path.GetTempPath(), $"{DirName}-{uid}");
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint Getuid();

    private static uint GetUid()
    {
        try { return Getuid(); }
        catch { return 0; }   // platform without libc — caller's path may collide but won't crash
    }
}
