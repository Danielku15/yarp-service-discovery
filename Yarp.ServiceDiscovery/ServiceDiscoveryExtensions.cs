using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Yarp.ServiceDiscovery;

public static class ServiceDiscoveryExtensions
{
    public static IServiceCollection AddServiceDiscovery(this IServiceCollection builder, IConfiguration configuration)
    {
        builder.AddSingleton<IProxyConfigProvider>(sp =>
            new ServiceDiscoveryProxyConfigProvider(
                sp.GetRequiredService<ILogger<ServiceDiscoveryProxyConfigProvider>>(),
                configuration));
        builder.AddSingleton<IServiceDiscoveryManager>(sp =>
            (ServiceDiscoveryProxyConfigProvider)sp.GetRequiredService<IProxyConfigProvider>());
        builder.AddReverseProxy();

        return builder;
    }

    public static IServiceCollection AddServiceDiscoveryClient(this IServiceCollection builder, Action<ServiceDiscoveryClientServiceOptions>? configure)
    {
        builder.Configure(configure);
        builder.AddHostedService<ServiceDiscoveryClientService>();
        return builder;
    }
    public static IServiceCollection AddServiceDiscoveryClient(this IServiceCollection builder, IConfiguration configuration)
    {
        builder.Configure<ServiceDiscoveryClientServiceOptions>(configuration);
        builder.AddHostedService<ServiceDiscoveryClientService>();
        return builder;
    }

    public static IEndpointConventionBuilder MapServiceDiscovery(this IEndpointRouteBuilder routes)
    {
        var conventionBuilder = routes.MapGet("service-discovery",
            static async (
                HttpContext context,
                IOptions<JsonOptions> jsonOptions,
                IServiceDiscoveryManager serviceDiscoveryManager,
                IHostApplicationLifetime lifetime) =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    return Results.BadRequest();
                }

                var ws = await context.WebSockets.AcceptWebSocketAsync();

                var receiveBuffer = new ArraySegment<byte>(new byte[8096], 0, 8096);
                // We should make this more graceful
                await using var reg = lifetime.ApplicationStopping.Register(() => ws.Abort());

                ServiceDiscoveryRegistrationRequest? message;
                try
                {
                    message = JsonSerializer.Deserialize<ServiceDiscoveryRegistrationRequest>(
                        await ReadFullMessageAsync(ws,
                            receiveBuffer,
                            WebSocketMessageType.Text,
                            context.RequestAborted),
                        jsonOptions.Value.JsonSerializerOptions);
                    if (string.IsNullOrEmpty(message?.ClusterId))
                    {
                        return Results.BadRequest();
                    }
                }
                catch (IOException)
                {
                    return Results.BadRequest();
                }

                var uri = new UriBuilder(message.BaseUrl)
                {
                    Host = context.Connection.RemoteIpAddress!.ToString()
                };

                var registration = serviceDiscoveryManager.RegisterDestination(uri.Uri, message.ClusterId);
                try
                {
                    // Keep reusing this connection while, it's still open on the backend
                    while (ws.State == WebSocketState.Open)
                    {
                        var frame = await ws.ReceiveAsync(receiveBuffer, context.RequestAborted);
                        if (frame.MessageType == WebSocketMessageType.Close)
                        {
                            try
                            {
                                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                                    null,
                                    context.RequestAborted);
                            }
                            catch
                            {
                                // ignore
                            }

                            break;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    serviceDiscoveryManager.UnregisterDestination(registration);
                }

                return Results.Ok();
            });

        // Make this endpoint do websockets automagically as middleware for this specific route
        conventionBuilder.Add(e =>
        {
            var sub = routes.CreateApplicationBuilder();
            sub.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(10)
            }).Run(e.RequestDelegate!);
            e.RequestDelegate = sub.Build();
        });

        return conventionBuilder;
    }

    private static async Task<byte[]> ReadFullMessageAsync(
        WebSocket ws,
        ArraySegment<byte> receiveBuffer,
        WebSocketMessageType expectedMessageType,
        CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(receiveBuffer, cancellationToken);
            if (result.MessageType != expectedMessageType)
            {
                throw new IOException($"Expected message type {expectedMessageType} but recieved {result.MessageType}");
            }

            await ms.WriteAsync(new ReadOnlyMemory<byte>(receiveBuffer.Array!, receiveBuffer.Offset, result.Count),
                cancellationToken);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return ms.ToArray();
    }
}
