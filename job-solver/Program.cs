using System.Text.Json.Serialization;
using Azure.Messaging.WebPubSub.Clients;
using Dapr;

var builder = WebApplication.CreateBuilder(args);

var iotManagerUrl = builder.Configuration["WEBPUBSUB_SERVER_URL"];
var username = builder.Configuration["HOSTNAME"] ?? Environment.MachineName;
string apiKey = builder.Configuration["ApiKey"] ?? throw new ArgumentNullException("ApiKey");

var app = builder.Build();

app.Logger.LogInformation("WebPubSub server url: {iotManagerUrl}", iotManagerUrl);

// Dapr will send serialized event object vs. being raw CloudEvent
// app.UseCloudEvents();

// needed for Dapr pub/sub routing
app.MapSubscribeHandler();

if (app.Environment.IsDevelopment()) { app.UseDeveloperExceptionPage(); }

// Dapr subscription in [Topic] routes jobs topic to this route
app.MapPost("/jobs", [Topic("jobspubsub", "jobs")] async (ILogger<Program> logger, Job job, CancellationToken cancellationToken) =>
{
    if(string.IsNullOrEmpty(job?.CorrelationId ))
    {
        logger.LogError("Job correlation id is required");
        return Results.BadRequest("Correlation id is required");
    }

    logger.LogInformation($"Received job {job.Name} with correlation id {job.CorrelationId}");
    Uri webPubSubServerUri = new Uri($"{iotManagerUrl}/negotiate/{username}/{job.CorrelationId}");
    logger.LogInformation($"Negotiate url: {webPubSubServerUri}");

    // get connection string for WebPubSub
    using HttpClient httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

    string webPubSubConnectionString = string.Empty;
    try
    {
        var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(webPubSubServerUri);
        webPubSubConnectionString = response!.Url;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Failed to negotiate with WebPubSub server: {webPubSubServerUri}");
        return Results.StatusCode(500);
    }

    var webPubSubClient = new WebPubSubClient(new Uri(webPubSubConnectionString));
    await webPubSubClient.StartAsync();
    logger.LogInformation($"WebPubSub client started for job {job.Name} 🔥");

    CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    webPubSubClient.GroupMessageReceived += async (args) =>
    {
        if (args.Message.FromUserId == username)
        {
            // ignore messages from self
            return;
        }
        var jobUpdate = args.Message.Data.ToObjectFromJson<JobUpdate>();

        if (jobUpdate.Status == "Cancelled")
        {
            logger.LogInformation($"Job {jobUpdate.CorrelationId} has been cancelled");
            cts.Cancel();
            await webPubSubClient.DisposeAsync();
        }
    };

    foreach (var step in job.Steps)
    {
        var jobUpdate = new JobUpdate(job.Name, job.CorrelationId, step, "In Progress");
        await webPubSubClient.SendToGroupAsync(job.CorrelationId, BinaryData.FromObjectAsJson(jobUpdate), WebPubSubDataType.Json, cancellationToken: cts.Token);

        logger.LogInformation($"Job {job.Name} is processing step {step}");
        await Task.Delay(6000, cts.Token);

        jobUpdate = new JobUpdate(job.Name, job.CorrelationId, step, "Completed");
        await webPubSubClient.SendToGroupAsync(job.CorrelationId, BinaryData.FromObjectAsJson(jobUpdate), WebPubSubDataType.Json, cancellationToken: cts.Token);
    }

    logger.LogInformation($"Job {job.Name} has been completed");
    await webPubSubClient.DisposeAsync();

    return Results.Ok(job);
});

await app.RunAsync();

record Job(string Name, string CorrelationId, string[] Steps);
record JobUpdate(string Name, string CorrelationId, string Step, string Status);
record NegotiateResponse(string Url);

