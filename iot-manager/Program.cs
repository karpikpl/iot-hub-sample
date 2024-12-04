using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Devices;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
// builder.Services.AddAuthorization();

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

var applicationKey = builder.Configuration.GetValue<string>("ApiKey")
    ?? throw new InvalidOperationException("API key (ApiKey) is missing");
var iotHubHostName = builder.Configuration.GetValue<string>("Iot:HubHostName")
    ?? throw new InvalidOperationException("IoT Hub hostname (Iot:HubHostName) is missing");

var managedIdentityId = builder.Configuration.GetValue<string>("azureClientId");
Azure.Core.TokenCredential credential = managedIdentityId == null
    ? new DefaultAzureCredential(includeInteractiveCredentials: true)
    : new ManagedIdentityCredential(managedIdentityId);

builder.Services.AddAzureClients(clientBuilder =>
{
    var connectionString = builder.Configuration.GetValue<string>("ServiceBus:Namespace")
        ?? throw new InvalidOperationException("Service Bus namespace (ServiceBus:Namespace) is missing");

    if (connectionString.Contains("SharedAccessKey", StringComparison.InvariantCultureIgnoreCase))
    {
        clientBuilder.AddServiceBusClient(connectionString);
    }
    else
    {
        clientBuilder.AddServiceBusClientWithNamespace(connectionString);
        clientBuilder.UseCredential(credential);
    }
});

builder.Services.AddHostedService<iotManager.OtherServices.ServiceBusHostedService>();

builder.Services.AddSingleton((provider) => ServiceClient.Create(iotHubHostName, credential));
builder.Services.AddSingleton((provider) => RegistryManager.Create(iotHubHostName, credential));
builder.Services.AddSingleton<DeviceRegistrationService>();

builder.Services.AddApplicationInsightsTelemetry();
builder.Logging.AddApplicationInsights();
builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>("iotManager", LogLevel.Trace);

// Add services to the container.

var app = builder.Build();

app.UseRouting();

// app.UseAuthentication();
// app.UseAuthorization();


app.MapGet("/", () => "Hello");

app.MapGet("/negotiate/{id}", async (
    [FromRoute] string id,
    [FromHeader(Name = "x-api-key")] string appKey,
    [FromServices] DeviceRegistrationService registrationService,
    [FromServices] ILogger<Program> logger) =>
{
    // use Entra Authentication in production scenarios
    if (appKey != applicationKey)
    {
        return Results.Unauthorized();
    }

    // Generate a device registration ID
    var deviceConnectionString = await registrationService.RegisterDeviceAsync(id, iotHubHostName);
    
    return Results.Ok(new { Url = deviceConnectionString });
});

app.MapGet("/deregister/{id}", async (
    [FromRoute] string id,
    [FromHeader(Name = "x-api-key")] string appKey,
    [FromServices] DeviceRegistrationService registrationService,
    [FromServices] ILogger<Program> logger) =>
{
    // use Entra Authentication in production scenarios
    if (appKey != applicationKey)
    {
        return Results.Unauthorized();
    }

    await registrationService.DeregisterDeviceAsync(id);
    
    return Results.Accepted();
});


IWebHostEnvironment env = app.Environment;

if (env.IsDevelopment())
{
    Console.WriteLine("Development mode");
    Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
}

app.Run();
