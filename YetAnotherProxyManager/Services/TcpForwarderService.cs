using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class TcpForwarderService : BackgroundService
{
    private readonly ConfigurationService _configService;
    private readonly ILogger<TcpForwarderService> _logger;
    private readonly ConcurrentDictionary<Guid, TcpForwarder> _forwarders = new();

    public TcpForwarderService(ConfigurationService configService, ILogger<TcpForwarderService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TCP Forwarder Service started");
        _configService.ConfigurationChanged += OnConfigurationChanged;

        SyncForwarders();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _configService.ConfigurationChanged -= OnConfigurationChanged;
            StopAllForwarders();
        }
    }

    private void OnConfigurationChanged()
    {
        _logger.LogInformation("TCP configuration changed, resyncing forwarders");
        SyncForwarders();
    }

    private void SyncForwarders()
    {
        var routes = _configService.GetEnabledTcpRoutes()
            .Where(r => r.StreamConfig != null)
            .ToDictionary(r => r.Id);

        // Stop removed/disabled forwarders
        foreach (var id in _forwarders.Keys.ToList())
        {
            if (!routes.ContainsKey(id))
            {
                if (_forwarders.TryRemove(id, out var forwarder))
                {
                    forwarder.Stop();
                    _logger.LogInformation("Stopped TCP forwarder: {Id}", id);
                }
            }
        }

        // Start/update forwarders
        foreach (var route in routes.Values)
        {
            var config = route.StreamConfig!;

            if (_forwarders.TryGetValue(route.Id, out var existing))
            {
                if (existing.ListenPort != config.ListenPort ||
                    existing.UpstreamHost != config.UpstreamHost ||
                    existing.UpstreamPort != config.UpstreamPort)
                {
                    existing.Stop();
                    _forwarders.TryRemove(route.Id, out _);
                }
                else
                {
                    continue;
                }
            }

            var forwarder = new TcpForwarder(
                config.ListenPort,
                config.UpstreamHost,
                config.UpstreamPort,
                config.BufferSize,
                config.TimeoutSeconds ?? 300,
                _logger);

            if (_forwarders.TryAdd(route.Id, forwarder))
            {
                forwarder.Start();
                _logger.LogInformation("Started TCP forwarder: {Name} (:{Port} -> {Host}:{UpstreamPort})",
                    route.Name, config.ListenPort, config.UpstreamHost, config.UpstreamPort);
            }
        }
    }

    private void StopAllForwarders()
    {
        foreach (var forwarder in _forwarders.Values)
        {
            forwarder.Stop();
        }
        _forwarders.Clear();
    }

    public int GetActiveConnectionCount()
    {
        return _forwarders.Values.Sum(f => f.ActiveConnections);
    }

    public IReadOnlyDictionary<int, int> GetConnectionCountsByPort()
    {
        return _forwarders.Values.ToDictionary(f => f.ListenPort, f => f.ActiveConnections);
    }
}

internal class TcpForwarder
{
    private readonly ILogger _logger;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _activeConnections;

    public int ListenPort { get; }
    public string UpstreamHost { get; }
    public int UpstreamPort { get; }
    public int BufferSize { get; }
    public int TimeoutSeconds { get; }
    public int ActiveConnections => _activeConnections;

    public TcpForwarder(int listenPort, string upstreamHost, int upstreamPort, int bufferSize, int timeoutSeconds, ILogger logger)
    {
        ListenPort = listenPort;
        UpstreamHost = upstreamHost;
        UpstreamPort = upstreamPort;
        BufferSize = bufferSize;
        TimeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, ListenPort);
        _listener.Start();

        _ = AcceptConnectionsAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleConnectionAsync(client, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting TCP connection on port {Port}", ListenPort);
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeConnections);
        TcpClient? upstream = null;

        try
        {
            upstream = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            await upstream.ConnectAsync(UpstreamHost, UpstreamPort, timeoutCts.Token);

            var clientStream = client.GetStream();
            var upstreamStream = upstream.GetStream();

            var clientToUpstream = CopyStreamAsync(clientStream, upstreamStream, timeoutCts.Token);
            var upstreamToClient = CopyStreamAsync(upstreamStream, clientStream, timeoutCts.Token);

            await Task.WhenAny(clientToUpstream, upstreamToClient);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TCP forward connection error");
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
            client.Dispose();
            upstream?.Dispose();
        }
    }

    private async Task CopyStreamAsync(NetworkStream source, NetworkStream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
        catch (Exception)
        {
        }
    }
}
