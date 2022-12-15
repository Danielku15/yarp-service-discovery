using Microsoft.Extensions.Configuration;

namespace Yarp.ServiceDiscovery;

/// <summary>
/// The options to configure the client/service part of the service discovery.
/// </summary>
public class ServiceDiscoveryClientServiceOptions
{
    /// <summary>
    /// The default key in the <see cref="IConfiguration"/> to load these options. 
    /// </summary>
    public const string Key = "ServiceDiscovery";

    /// <summary>
    /// Gets or sets the URI under which the service discovery is reachable.
    /// </summary>
    public Uri ServiceDiscoveryEndpoint { get; set; } = null!;
    
    /// <summary>
    /// Gets or sets the timeout for connecting to the service discovery.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// Gets or sets the delay to wait before reconnecting to the service discovery in case of
    /// interruptions or errors.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
    
    /// <summary>
    /// Gets or sets the keep alive interval in which the local service will ping the service discovery
    /// to indicate availability.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Gets or sets the ID of ther cluster to which the service should be added.
    /// </summary>
    public string ClusterId { get; set; } = string.Empty;
}