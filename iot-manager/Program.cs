using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Identity.Web;
using Azure.Messaging.WebPubSub;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//     .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
// builder.Services.AddAuthorization();

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

var pubsubHostname = builder.Configuration.GetValue<string>("WebPubSub:Hostname")
    ?? throw new InvalidOperationException("Web PubSub hostname (WebPubSub:Hostname) is missing");
var hubName = builder.Configuration.GetValue<string>("WebPubSub:HubName")
    ?? throw new InvalidOperationException("Web PubSub hub name (WebPubSub:HubName) is missing");
var applicationKey = builder.Configuration.GetValue<string>("ApiKey")
    ?? throw new InvalidOperationException("API key (ApiKey) is missing");

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

    // using Identity: https://learn.microsoft.com/en-us/azure/azure-web-pubsub/howto-create-serviceclient-with-net-and-azure-identity
    clientBuilder.AddWebPubSubServiceClient(new Uri($"https://{pubsubHostname}"), hubName, credential);
});

builder.Services.AddHostedService<iotManager.OtherServices.ServiceBusHostedService>();
builder.Services
    .AddWebPubSub(options =>
    {
        options.ServiceEndpoint = new Microsoft.Azure.WebPubSub.AspNetCore.WebPubSubServiceEndpoint(new Uri($"https://{pubsubHostname}"), credential);
    })
    .AddWebPubSubServiceClient<iotManager.WebPubSub.AspHub>();

builder.Services.AddApplicationInsightsTelemetry();
builder.Logging.AddApplicationInsights();
builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>("iotManager", LogLevel.Trace);

// Add services to the container.

var app = builder.Build();

app.UseRouting();

// app.UseAuthentication();
// app.UseAuthorization();

// Configure REST API
app.MapWebPubSubHub<iotManager.WebPubSub.AspHub>("/eventhandler/{*path}");

app.MapGet("/", () => "Hello");

app.MapGet("/negotiate/{userId}/{groupName}", async (
    [FromRoute]string userId, 
    [FromRoute]string groupName, 
    [FromHeader(Name = "x-api-key")]string appKey, 
    [FromServices]WebPubSubServiceClient client) =>
{
    if (appKey != applicationKey)
    {
        return Results.Unauthorized();
    }

    var uri = await client.GetClientAccessUriAsync(userId: userId, 
    roles: new[] { $"webpubsub.sendToGroup.{groupName}", $"webpubsub.joinLeaveGroup.{groupName}" }, 
    groups: new[] { groupName }, clientProtocol: WebPubSubClientProtocol.Default);
    return Results.Ok( new { Url = uri.AbsoluteUri });
});

// Abuse protection: https://learn.microsoft.com/en-us/azure/azure-web-pubsub/howto-troubleshoot-common-issues#abuseprotectionresponsemissingallowedorigin
// From cloud events: https://github.com/cloudevents/spec/blob/v1.0/http-webhook.md#4-abuse-protection

IWebHostEnvironment env = app.Environment;

if (env.IsDevelopment())
{
    Console.WriteLine("Development mode");
    Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
}

app.Run();
