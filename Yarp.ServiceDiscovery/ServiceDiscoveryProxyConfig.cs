using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Yarp.ServiceDiscovery;

// mainly copy from YARP because needed for own ServiceDiscoveryProxyConfigProvider to load 
// the config from the appsettings.json
public class ServiceDiscoveryProxyConfig : IProxyConfig
{
    public List<RouteConfig> Routes { get; internal set; } = new();

    public List<ClusterConfig> Clusters { get; internal set; } = new();

    IReadOnlyList<RouteConfig> IProxyConfig.Routes => Routes;

    IReadOnlyList<ClusterConfig> IProxyConfig.Clusters => Clusters;

    public IChangeToken ChangeToken { get; internal set; }

    public ServiceDiscoveryProxyConfig()
    {
    }

    public ServiceDiscoveryProxyConfig(ServiceDiscoveryProxyConfig? other)
    {
        if (other == null)
        {
            return;
        }

        // deep clone
        Routes.AddRange(other.Routes.Select(r => new RouteConfig
        {
            RouteId = r.RouteId,
            Order = r.Order,
            ClusterId = r.ClusterId,
            AuthorizationPolicy = r.AuthorizationPolicy,
            CorsPolicy = r.CorsPolicy,
            Metadata = r.Metadata,
            Transforms = r.Transforms,
            Match = new RouteMatch
            {
                Headers = r.Match.Headers?.Select(h => new RouteHeader
                {
                    Mode = h.Mode,
                    Name = h.Name,
                    Values = h.Values?.ToList(),
                    IsCaseSensitive = h.IsCaseSensitive
                }).ToList(),
                Hosts = r.Match.Hosts?.ToList(),
                Methods = r.Match.Methods?.ToList(),
                Path = r.Match.Path,
                QueryParameters = r.Match.QueryParameters?.Select(q => new RouteQueryParameter
                {
                    Mode = q.Mode,
                    Name = q.Name,
                    Values = q.Values?.ToList(),
                    IsCaseSensitive = q.IsCaseSensitive
                }).ToList()
            }
        }));

        Clusters.AddRange(other.Clusters.Select(c => new ClusterConfig
        {
            Destinations = c.Destinations?.ToDictionary(d => d.Key, d => d.Value),
            Metadata = c.Metadata,
            ClusterId = c.ClusterId,
            HealthCheck = c.HealthCheck,
            HttpClient = c.HttpClient
        }));
    }
}