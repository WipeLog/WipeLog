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

using Avalonia.Headless;
using Xunit;

namespace Wipelog.App.Snapshots;

/// <summary>
/// xunit.v3 class fixture that owns one Avalonia headless session per test
/// class. Every test body must run through <see cref="Run(Func{Task})"/> —
/// that's the only way onto the headless UI thread. (Avalonia.Headless.XUnit's
/// [AvaloniaFact] targets xUnit v2, so we drive the session ourselves.)
/// The session's default isolation is per-dispatch, so each Run gets a fresh
/// <see cref="SnapshotApp"/> instance.
/// </summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.StartNew(typeof(SnapshotApp));

    // An exception thrown inside a dispatcher job (e.g. a view's Loaded
    // handler) can leave the pushed frame waiting forever; bound every test
    // so a wedged UI thread fails the test instead of hanging the run.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<T> Run<T>(Func<Task<T>> body)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);
        return await _session.Dispatch(body, cts.Token);
    }

    public Task Run(Func<Task> body) =>
        Run(async () =>
        {
            await body();
            return true;
        });

    public void Dispose() => _session.Dispose();
}
