using Yarp.ServiceDiscovery;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDiscoveryClient(builder.Configuration.GetSection(ServiceDiscoveryClientServiceOptions.Key));

var app = builder.Build();
app.MapGet("/", () => "Hello World from API 2!");
app.Run();