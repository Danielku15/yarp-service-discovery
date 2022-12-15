using Yarp.ServiceDiscovery;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceDiscoveryClient(builder.Configuration.GetSection(ServiceDiscoveryClientServiceOptions.Key));

var app = builder.Build();
app.MapGet("/", () => "Hello From API 1!");
app.Run();