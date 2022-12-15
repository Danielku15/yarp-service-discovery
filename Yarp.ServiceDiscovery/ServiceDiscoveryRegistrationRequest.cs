namespace Yarp.ServiceDiscovery;

/// <summary>
/// The request a service will send to get exposed on a cluster with a
/// given base url to reach the service. 
/// </summary>
public class ServiceDiscoveryRegistrationRequest
{
    /// <summary>
    /// Gets or sets the ID of the cluster under which the application should be exposed on the
    /// reverse proxy. 
    /// </summary>
    /// <remarks>
    /// This cluster must be configured on the API gateway side. 
    /// </remarks>
    public string ClusterId { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the base URL on which the service is reachable and the
    /// reverse proxy will call the service.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
