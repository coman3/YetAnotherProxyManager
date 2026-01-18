using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class UdpForwarderService : BackgroundService
{
    private readonly ConfigurationService _configService;
    private readonly ILogger<UdpForwarderService> _logger;
    private readonly ConcurrentDictionary<Guid, UdpForwarder> _forwarders = new();

    public UdpForwarderService(ConfigurationService configService, ILogger<UdpForwarderService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UDP Forwarder Service started");
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
        _logger.LogInformation("UDP configuration changed, resyncing forwarders");
        SyncForwarders();
    }

    private void SyncForwarders()
    {
        var routes = _configService.GetEnabledUdpRoutes()
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
                    _logger.LogInformation("Stopped UDP forwarder: {Id}", id);
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

            var forwarder = new UdpForwarder(
                config.ListenPort,
                config.UpstreamHost,
                config.UpstreamPort,
                config.BufferSize,
                config.TimeoutSeconds ?? 60,
                _logger);

            if (_forwarders.TryAdd(route.Id, forwarder))
            {
                forwarder.Start();
                _logger.LogInformation("Started UDP forwarder: {Name} (:{Port} -> {Host}:{UpstreamPort})",
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

    public long GetTotalPacketsForwarded()
    {
        return _forwarders.Values.Sum(f => f.PacketsForwarded);
    }
}

internal class UdpForwarder
{
    private readonly ILogger _logger;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private long _packetsForwarded;
    private readonly ConcurrentDictionary<IPEndPoint, UdpClientSession> _sessions = new();

    public int ListenPort { get; }
    public string UpstreamHost { get; }
    public int UpstreamPort { get; }
    public int BufferSize { get; }
    public int TimeoutSeconds { get; }
    public long PacketsForwarded => _packetsForwarded;

    public UdpForwarder(int listenPort, string upstreamHost, int upstreamPort, int bufferSize, int timeoutSeconds, ILogger logger)
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
        _listener = new UdpClient(ListenPort);

        _ = ReceiveLoopAsync(_cts.Token);
        _ = CleanupSessionsAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Dispose();
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
        _cts?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var upstreamEndpoint = new IPEndPoint(
            (await Dns.GetHostAddressesAsync(UpstreamHost, cancellationToken))[0],
            UpstreamPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync(cancellationToken);
                var clientEndpoint = result.RemoteEndPoint;

                if (!_sessions.TryGetValue(clientEndpoint, out var session))
                {
                    session = new UdpClientSession(clientEndpoint, _listener, upstreamEndpoint, this, _logger);
                    _sessions[clientEndpoint] = session;
                    session.Start(cancellationToken);
                }

                session.LastActivity = DateTime.UtcNow;
                await session.ForwardToUpstreamAsync(result.Buffer, cancellationToken);
                Interlocked.Increment(ref _packetsForwarded);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving UDP packet on port {Port}", ListenPort);
            }
        }
    }

    private async Task CleanupSessionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                var expiredSessions = _sessions
                    .Where(kvp => (DateTime.UtcNow - kvp.Value.LastActivity).TotalSeconds > TimeoutSeconds)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var endpoint in expiredSessions)
                {
                    if (_sessions.TryRemove(endpoint, out var session))
                    {
                        session.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void IncrementPacketsForwarded()
    {
        Interlocked.Increment(ref _packetsForwarded);
    }
}

internal class UdpClientSession : IDisposable
{
    private readonly IPEndPoint _clientEndpoint;
    private readonly UdpClient _listenerSocket;
    private readonly IPEndPoint _upstreamEndpoint;
    private readonly UdpForwarder _parent;
    private readonly ILogger _logger;
    private readonly UdpClient _upstreamClient;

    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    public UdpClientSession(IPEndPoint clientEndpoint, UdpClient listenerSocket, IPEndPoint upstreamEndpoint, UdpForwarder parent, ILogger logger)
    {
        _clientEndpoint = clientEndpoint;
        _listenerSocket = listenerSocket;
        _upstreamEndpoint = upstreamEndpoint;
        _parent = parent;
        _logger = logger;
        _upstreamClient = new UdpClient();
        _upstreamClient.Connect(upstreamEndpoint);
    }

    public void Start(CancellationToken cancellationToken)
    {
        _ = ReceiveFromUpstreamAsync(cancellationToken);
    }

    public async Task ForwardToUpstreamAsync(byte[] data, CancellationToken cancellationToken)
    {
        await _upstreamClient.SendAsync(data, cancellationToken);
    }

    private async Task ReceiveFromUpstreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _upstreamClient.ReceiveAsync(cancellationToken);
                LastActivity = DateTime.UtcNow;
                await _listenerSocket.SendAsync(result.Buffer, _clientEndpoint, cancellationToken);
                _parent.IncrementPacketsForwarded();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UDP session receive error");
        }
    }

    public void Dispose()
    {
        _upstreamClient.Dispose();
    }
}
