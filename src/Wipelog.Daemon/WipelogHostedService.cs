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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Recording;
using Wipelog.Library;

namespace Wipelog.Daemon;

internal sealed class WipelogHostedService : BackgroundService
{
    private readonly LogTailer _tailer;
    private readonly RecorderController _controller;
    private readonly DaemonOptions _options;
    private readonly ILibraryIndexer _indexer;
    private readonly ILogger<WipelogHostedService> _logger;

    public WipelogHostedService(
        LogTailer tailer,
        RecorderController controller,
        DaemonOptions options,
        ILibraryIndexer indexer,
        ILogger<WipelogHostedService> logger)
    {
        _tailer = tailer;
        _controller = controller;
        _options = options;
        _indexer = indexer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var source = _options.LogDirectory is not null
            ? $"{_options.LogDirectory} (pattern {_options.LogPattern})"
            : _options.LogPath ?? "(none)";
        _logger.LogInformation(
            "Wipelog starting — log: {Source}, output: {OutputDirectory}, db: {DbPath}",
            source,
            _options.OutputDirectory,
            _options.DatabasePath);
        if (string.IsNullOrEmpty(_options.PlayerName))
        {
            _logger.LogInformation(
                "Player identity will be auto-detected per recording.");
        }
        else
        {
            _logger.LogInformation(
                "Player identity hint: {Name}", _options.PlayerName);
        }

        try
        {
            var result = await _indexer.ReconcileAsync(_options.OutputDirectory, stoppingToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Indexer: added {Added}, removed {Removed}, errors {Errors}.",
                result.ClipsAdded, result.ClipsRemoved, result.Errors);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Indexer reconcile failed; continuing without it.");
        }

        await _tailer.StartAsync(stoppingToken).ConfigureAwait(false);
        await _controller.RunAsync(_tailer.Events, stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _tailer.DisposeAsync().ConfigureAwait(false);
        await _controller.DisposeAsync().ConfigureAwait(false);
    }
}
