# YARP example with a built-in service discovery.

## Yarp.ServiceDiscovery
Holds the components for the server (YARP) and clients (APIs) to talk to each other
and establish a service-discovery lookup. 

It uses the `IProxyConfigProvider` from YARP to feed in a custom dynamic configuration
based on application registrations. 

## Yarp.ServiceDiscovery.Api1

One API connecting to the Gateway exposing itself to a given cluster. 

## Yarp.ServiceDiscovery.Api2

A second API connecting to the Gateway exposing itself to a given cluster. 

## Yarp.ServiceDiscovery.ApiGateway

A server using YARP to be the reverse proxy. 