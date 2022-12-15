using Yarp.ServiceDiscovery;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDiscovery(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.MapReverseProxy();
app.MapServiceDiscovery();
app.MapGet("/", () => "Hello World from API Gateway");

await app.RunAsync();