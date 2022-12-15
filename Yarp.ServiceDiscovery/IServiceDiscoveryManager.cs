namespace Yarp.ServiceDiscovery;

/// <summary>
/// The manager to register and unregister available
/// service destinations dynamically.
/// </summary>
internal interface IServiceDiscoveryManager
{
    /// <summary>
    /// Registers a new service destination to be available for a given cluster.
    /// </summary>
    /// <param name="uri">The URI under which the service is reachable.</param>
    /// <param name="clusterId">The cluster to which the service will be added.</param>
    /// <returns>A registration token to use for unregistering.</returns>
    object RegisterDestination(Uri uri, string clusterId);
    
    /// <summary>
    /// Unregisters a service destination using a registration token recieved as part of <see cref="RegisterDestination"/>. 
    /// </summary>
    /// <param name="registration">The registration token to unregister.</param>
    void UnregisterDestination(object registration);
}
