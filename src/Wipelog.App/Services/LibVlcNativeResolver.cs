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

namespace Wipelog.App.Services;

/// <summary>
/// LibVLCSharp P/Invokes "libvlc", which .NET resolves as libvlc.so — the
/// unversioned dev symlink that only the -devel/-dev packages ship (Fedora
/// vlc-devel, Debian libvlc-dev). A stock VLC install has just the versioned
/// sonames, so without this the app reports "VLC is required" even though
/// VLC is installed. Try the versioned names first; returning Zero falls
/// back to .NET's default probing.
/// </summary>
internal static class LibVlcNativeResolver
{
    private static readonly Dictionary<string, string[]> Sonames = new()
    {
        ["libvlc"] = ["libvlc.so.5"],
        ["libvlccore"] = ["libvlccore.so.9"],
    };

    public static void Register()
    {
        if (!OperatingSystem.IsLinux()) return;
        NativeLibrary.SetDllImportResolver(typeof(LibVLCSharp.Shared.LibVLC).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        if (!Sonames.TryGetValue(name, out var candidates)) return IntPtr.Zero;
        foreach (var soname in candidates)
        {
            if (NativeLibrary.TryLoad(soname, assembly, path, out var handle)) return handle;
        }
        return IntPtr.Zero;
    }
}
