using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Yarp.ServiceDiscovery;

/// <summary>
/// A background service which will keep a connection to the
/// reverse proxy service discovery module active to ensure
/// the service is considered reachable.
/// </summary>
internal class ServiceDiscoveryClientService : IHostedService
{
    private readonly IServer _server;
    private readonly ILogger<ServiceDiscoveryClientService> _logger;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly IOptions<ServiceDiscoveryClientServiceOptions> _options;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task _clientKeepAliveTask = null!;

    public ServiceDiscoveryClientService(
        IServer server,
        ILogger<ServiceDiscoveryClientService> logger,
        IOptions<JsonOptions> jsonOptions,
        IOptions<ServiceDiscoveryClientServiceOptions> options)
    {
        _server = server;
        _logger = logger;
        _jsonOptions = jsonOptions;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _clientKeepAliveTask = Task.Factory.StartNew(
            async () => { await RunDiscoveryServiceClientAsync(_cancellationTokenSource.Token); },
            cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunDiscoveryServiceClientAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = new ClientWebSocket
                {
                    Options =
                    {
                        KeepAliveInterval = _options.Value.KeepAliveInterval
                    }
                };

                await ConnectToServiceDiscoveryAsync(client, cancellationToken);

                var receiveBuffer = new ArraySegment<byte>(new byte[8096], 0, 8096);
                while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
                {
                    var frame = await client.ReceiveAsync(receiveBuffer, cancellationToken);
                    if (frame.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "Error during closing connection");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error on connection with the Service Discovery host");
            }

            await Task.Delay(_options.Value.ReconnectDelay, cancellationToken);
        }
    }

    private string GetBaseUrl()
    {
        var serverAddress = _server.Features.Get<IServerAddressesFeature>()!;
        return serverAddress.Addresses.First();
    }

    private async Task ConnectToServiceDiscoveryAsync(ClientWebSocket client, CancellationToken cancellationToken)
    {
        using var connectTimeoutSource = new CancellationTokenSource(_options.Value.ConnectTimeout);
        using var combinedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeoutSource.Token);

        var connectUri = new UriBuilder(_options.Value.ServiceDiscoveryEndpoint);
        connectUri.Scheme = connectUri.Scheme.Replace("http", "ws", StringComparison.OrdinalIgnoreCase);
        connectUri.Path = connectUri.Path.TrimEnd('/') + "/service-discovery";
        await client.ConnectAsync(connectUri.Uri, combinedSource.Token);

        // send request
        var request = new ServiceDiscoveryRegistrationRequest
        {
            ClusterId = _options.Value.ClusterId,
            BaseUrl = GetBaseUrl()
        };
        var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,
            _jsonOptions.Value.JsonSerializerOptions));
        await client.SendAsync(new ArraySegment<byte>(serialized, 0, serialized.Length),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource.Cancel();
        await _clientKeepAliveTask.WaitAsync(cancellationToken);
    }
}