using System.Collections.Concurrent;
using System.Security.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Yarp.ServiceDiscovery;

public class ServiceDiscoveryProxyConfigProvider : IServiceDiscoveryManager, IProxyConfigProvider
{
    private record Registration(Uri Uri, string ClusterId)
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private readonly ConcurrentDictionary<Guid, Registration> _registrations = new();
    private readonly ILogger<ServiceDiscoveryProxyConfigProvider> _logger;
    private readonly IConfiguration _configuration;

    private CancellationTokenSource? _changeToken;
    private ServiceDiscoveryProxyConfig? _currentConfig;
    private readonly object _lockObject = new();

    public ServiceDiscoveryProxyConfigProvider(
        ILogger<ServiceDiscoveryProxyConfigProvider> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    
    public object RegisterDestination(Uri uri, string clusterId)
    {
        lock (_lockObject)
        {
            var registration = new Registration(uri, clusterId);
            _registrations[registration.Id] = registration;

            var currentConfig = _currentConfig;
            var newState = new ServiceDiscoveryProxyConfig(currentConfig);
            var cluster = newState.Clusters.FirstOrDefault(c => c.ClusterId == clusterId);

            if (cluster != null)
            {
                var destinationKey = registration.Id.ToString("D");
                ((IDictionary<string, DestinationConfig?>)cluster.Destinations!)[destinationKey] = new DestinationConfig
                {
                    Address = registration.Uri.ToString(),
                    Health = null,
                    Metadata = null
                };
            }

            UpdateConfiguration(newState);

            return registration;
        }
    }

    public void UnregisterDestination(object registration)
    {
        if (registration is Registration reg && _registrations.TryRemove(reg.Id, out var existing))
        {
            var currentConfig = _currentConfig;
            var newState = new ServiceDiscoveryProxyConfig(currentConfig);
            var cluster = newState.Clusters.FirstOrDefault(c => c.ClusterId == existing.ClusterId);

            if (cluster != null)
            {
                var destinationKey = existing.Id.ToString("D");
                ((IDictionary<string, DestinationConfig?>)cluster.Destinations!).Remove(destinationKey);
            }

            UpdateConfiguration(newState);
        }
    }

    
    #region Copy from YARP built-in config provider cannot be reused well
   
    public IProxyConfig GetConfig()
    {
        if (_currentConfig == null)
        {
            LoadConfiguration();
        }

        return _currentConfig!;
    }

    private void LoadConfiguration()
    {
        try
        {
            var newConfig = new ServiceDiscoveryProxyConfig();
            foreach (var child in _configuration.GetSection("Clusters").GetChildren())
            {
                newConfig.Clusters.Add(CreateCluster(child));
            }

            foreach (var child in _configuration.GetSection("Routes").GetChildren())
            {
                newConfig.Routes.Add(CreateRoute(child));
            }

            UpdateConfiguration(newConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration");
            if (_currentConfig != null) // ignore on existing config
            {
                return;
            }

            // throw on initial load
            throw;
        }
    }

    private void UpdateConfiguration(ServiceDiscoveryProxyConfig newState)
    {
        var oldChangeToken = _changeToken;
        _changeToken = new CancellationTokenSource();
        newState.ChangeToken = new CancellationChangeToken(_changeToken.Token);
        _currentConfig = newState;

        try
        {
            oldChangeToken?.Cancel(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error signaling change");
        }
    }
    
    private ClusterConfig CreateCluster(IConfigurationSection section)
    {
        var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var destination in section.GetSection(nameof(ClusterConfig.Destinations)).GetChildren())
        {
            destinations.Add(destination.Key, CreateDestination(destination));
        }

        return new ClusterConfig
        {
            ClusterId = section.Key,
            LoadBalancingPolicy = section[nameof(ClusterConfig.LoadBalancingPolicy)],
            SessionAffinity = CreateSessionAffinityConfig(section.GetSection(nameof(ClusterConfig.SessionAffinity))),
            HealthCheck = CreateHealthCheckConfig(section.GetSection(nameof(ClusterConfig.HealthCheck))),
            HttpClient = CreateHttpClientConfig(section.GetSection(nameof(ClusterConfig.HttpClient))),
            HttpRequest = CreateProxyRequestConfig(section.GetSection(nameof(ClusterConfig.HttpRequest))),
            Metadata = section.GetSection(nameof(ClusterConfig.Metadata)).ReadStringDictionary(),
            Destinations = destinations,
        };
    }

    private static ForwarderRequestConfig? CreateProxyRequestConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new ForwarderRequestConfig
        {
            ActivityTimeout = section.ReadTimeSpan(nameof(ForwarderRequestConfig.ActivityTimeout)),
            Version = section.ReadVersion(nameof(ForwarderRequestConfig.Version)),
            VersionPolicy = section.ReadEnum<HttpVersionPolicy>(nameof(ForwarderRequestConfig.VersionPolicy)),
            AllowResponseBuffering = section.ReadBool(nameof(ForwarderRequestConfig.AllowResponseBuffering))
        };
    }

    private static HttpClientConfig? CreateHttpClientConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        SslProtocols? sslProtocols = null;
        if (section.GetSection(nameof(HttpClientConfig.SslProtocols)) is IConfigurationSection sslProtocolsSection)
        {
            foreach (var protocolConfig in sslProtocolsSection.GetChildren()
                         .Select(s => Enum.Parse<SslProtocols>(s.Value!, ignoreCase: true)))
            {
                sslProtocols = sslProtocols is null ? protocolConfig : sslProtocols | protocolConfig;
            }
        }

        WebProxyConfig? webProxy;
        var webProxySection = section.GetSection(nameof(HttpClientConfig.WebProxy));
        if (webProxySection.Exists())
        {
            webProxy = new WebProxyConfig()
            {
                Address = webProxySection.ReadUri(nameof(WebProxyConfig.Address)),
                BypassOnLocal = webProxySection.ReadBool(nameof(WebProxyConfig.BypassOnLocal)),
                UseDefaultCredentials = webProxySection.ReadBool(nameof(WebProxyConfig.UseDefaultCredentials))
            };
        }
        else
        {
            webProxy = null;
        }

        return new HttpClientConfig
        {
            SslProtocols = sslProtocols,
            DangerousAcceptAnyServerCertificate =
                section.ReadBool(nameof(HttpClientConfig.DangerousAcceptAnyServerCertificate)),
            MaxConnectionsPerServer = section.ReadInt32(nameof(HttpClientConfig.MaxConnectionsPerServer)),
            EnableMultipleHttp2Connections = section.ReadBool(nameof(HttpClientConfig.EnableMultipleHttp2Connections)),
            RequestHeaderEncoding = section[nameof(HttpClientConfig.RequestHeaderEncoding)],
            WebProxy = webProxy
        };
    }


    private static DestinationConfig CreateDestination(IConfigurationSection section)
    {
        return new DestinationConfig
        {
            Address = section[nameof(DestinationConfig.Address)]!,
            Health = section[nameof(DestinationConfig.Health)],
            Metadata = section.GetSection(nameof(DestinationConfig.Metadata)).ReadStringDictionary(),
        };
    }

    private static SessionAffinityConfig? CreateSessionAffinityConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new SessionAffinityConfig
        {
            Enabled = section.ReadBool(nameof(SessionAffinityConfig.Enabled)),
            Policy = section[nameof(SessionAffinityConfig.Policy)],
            FailurePolicy = section[nameof(SessionAffinityConfig.FailurePolicy)],
            AffinityKeyName = section[nameof(SessionAffinityConfig.AffinityKeyName)]!,
            Cookie = CreateSessionAffinityCookieConfig(section.GetSection(nameof(SessionAffinityConfig.Cookie)))
        };
    }

    private static SessionAffinityCookieConfig? CreateSessionAffinityCookieConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new SessionAffinityCookieConfig
        {
            Path = section[nameof(SessionAffinityCookieConfig.Path)],
            SameSite = section.ReadEnum<SameSiteMode>(nameof(SessionAffinityCookieConfig.SameSite)),
            HttpOnly = section.ReadBool(nameof(SessionAffinityCookieConfig.HttpOnly)),
            MaxAge = section.ReadTimeSpan(nameof(SessionAffinityCookieConfig.MaxAge)),
            Domain = section[nameof(SessionAffinityCookieConfig.Domain)],
            IsEssential = section.ReadBool(nameof(SessionAffinityCookieConfig.IsEssential)),
            SecurePolicy = section.ReadEnum<CookieSecurePolicy>(nameof(SessionAffinityCookieConfig.SecurePolicy)),
            Expiration = section.ReadTimeSpan(nameof(SessionAffinityCookieConfig.Expiration))
        };
    }

    private static HealthCheckConfig? CreateHealthCheckConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new HealthCheckConfig
        {
            Passive = CreatePassiveHealthCheckConfig(section.GetSection(nameof(HealthCheckConfig.Passive))),
            Active = CreateActiveHealthCheckConfig(section.GetSection(nameof(HealthCheckConfig.Active))),
            AvailableDestinationsPolicy = section[nameof(HealthCheckConfig.AvailableDestinationsPolicy)]
        };
    }

    private static PassiveHealthCheckConfig? CreatePassiveHealthCheckConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new PassiveHealthCheckConfig
        {
            Enabled = section.ReadBool(nameof(PassiveHealthCheckConfig.Enabled)),
            Policy = section[nameof(PassiveHealthCheckConfig.Policy)],
            ReactivationPeriod = section.ReadTimeSpan(nameof(PassiveHealthCheckConfig.ReactivationPeriod))
        };
    }

    private static ActiveHealthCheckConfig? CreateActiveHealthCheckConfig(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return new ActiveHealthCheckConfig
        {
            Enabled = section.ReadBool(nameof(ActiveHealthCheckConfig.Enabled)),
            Interval = section.ReadTimeSpan(nameof(ActiveHealthCheckConfig.Interval)),
            Timeout = section.ReadTimeSpan(nameof(ActiveHealthCheckConfig.Timeout)),
            Policy = section[nameof(ActiveHealthCheckConfig.Policy)],
            Path = section[nameof(ActiveHealthCheckConfig.Path)]
        };
    }

    private static RouteConfig CreateRoute(IConfigurationSection section)
    {
        if (!string.IsNullOrEmpty(section["RouteId"]))
        {
            throw new Exception(
                "The route config format has changed, routes are now objects instead of an array. The route id must be set as the object name, not with the 'RouteId' field.");
        }

        return new RouteConfig
        {
            RouteId = section.Key,
            Order = section.ReadInt32(nameof(RouteConfig.Order)),
            ClusterId = section[nameof(RouteConfig.ClusterId)],
            AuthorizationPolicy = section[nameof(RouteConfig.AuthorizationPolicy)],
            CorsPolicy = section[nameof(RouteConfig.CorsPolicy)],
            Metadata = section.GetSection(nameof(RouteConfig.Metadata)).ReadStringDictionary(),
            Transforms = CreateTransforms(section.GetSection(nameof(RouteConfig.Transforms))),
            Match = CreateRouteMatch(section.GetSection(nameof(RouteConfig.Match))),
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>>? CreateTransforms(IConfigurationSection section)
    {
        if (section.GetChildren() is var children && !children.Any())
        {
            return null;
        }

        return children.Select(subSection =>
                subSection.GetChildren().ToDictionary(d => d.Key, d => d.Value!, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static RouteMatch CreateRouteMatch(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return new RouteMatch();
        }

        return new RouteMatch()
        {
            Methods = section.GetSection(nameof(RouteMatch.Methods)).ReadStringArray(),
            Hosts = section.GetSection(nameof(RouteMatch.Hosts)).ReadStringArray(),
            Path = section[nameof(RouteMatch.Path)],
            Headers = CreateRouteHeaders(section.GetSection(nameof(RouteMatch.Headers))),
            QueryParameters = CreateRouteQueryParameters(section.GetSection(nameof(RouteMatch.QueryParameters)))
        };
    }

    private static IReadOnlyList<RouteHeader>? CreateRouteHeaders(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return section.GetChildren().Select(data => CreateRouteHeader(data)).ToArray();
    }

    private static RouteHeader CreateRouteHeader(IConfigurationSection section)
    {
        return new RouteHeader()
        {
            Name = section[nameof(RouteHeader.Name)]!,
            Values = section.GetSection(nameof(RouteHeader.Values)).ReadStringArray(),
            Mode = section.ReadEnum<HeaderMatchMode>(nameof(RouteHeader.Mode)) ?? HeaderMatchMode.ExactHeader,
            IsCaseSensitive = section.ReadBool(nameof(RouteHeader.IsCaseSensitive)) ?? false,
        };
    }

    private static IReadOnlyList<RouteQueryParameter>? CreateRouteQueryParameters(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return section.GetChildren().Select(data => CreateRouteQueryParameter(data)).ToArray();
    }

    private static RouteQueryParameter CreateRouteQueryParameter(IConfigurationSection section)
    {
        return new RouteQueryParameter()
        {
            Name = section[nameof(RouteQueryParameter.Name)]!,
            Values = section.GetSection(nameof(RouteQueryParameter.Values)).ReadStringArray(),
            Mode = section.ReadEnum<QueryParameterMatchMode>(nameof(RouteQueryParameter.Mode)) ??
                   QueryParameterMatchMode.Exact,
            IsCaseSensitive = section.ReadBool(nameof(RouteQueryParameter.IsCaseSensitive)) ?? false,
        };
    }
    
    #endregion
}