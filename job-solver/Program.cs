using System.Text.Json;
using Dapr;
using Microsoft.Azure.Devices.Client;

var builder = WebApplication.CreateBuilder(args);

var iotManagerUrl = builder.Configuration["IOT_MANAGER_URL"];
var username = builder.Configuration["HOSTNAME"] ?? Environment.MachineName;
string apiKey = builder.Configuration["ApiKey"] ?? throw new ArgumentNullException("ApiKey");

var app = builder.Build();

app.Logger.LogInformation("IoT manager url: {iotManagerUrl}", iotManagerUrl);

// Dapr will send serialized event object vs. being raw CloudEvent
// app.UseCloudEvents();

// needed for Dapr pub/sub routing
app.MapSubscribeHandler();

if (app.Environment.IsDevelopment()) { app.UseDeveloperExceptionPage(); }

// Dapr subscription in [Topic] routes jobs topic to this route
app.MapPost("/jobs", [Topic("jobspubsub", "jobs")] async (ILogger<Program> logger, Job job, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrEmpty(job?.CorrelationId))
    {
        logger.LogError("Job correlation id is required");
        return Results.BadRequest("Correlation id is required");
    }

    var deviceId = $"device-solver::{job.CorrelationId}";

    logger.LogInformation($"Received job {job.Name} with correlation id {job.CorrelationId}");
    Uri iotNegotiateUri = new Uri($"{iotManagerUrl}/negotiate/{deviceId}");
    logger.LogInformation($"Negotiate url: {iotNegotiateUri}");

    // get connection string for WebPubSub
    using HttpClient httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);

    string iotConnectionString = string.Empty;
    try
    {
        var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(iotNegotiateUri);
        iotConnectionString = response!.Url;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Failed to negotiate with WebPubSub server: {iotNegotiateUri}");
        return Results.StatusCode(500);
    }

    using var deviceClient = DeviceClient.CreateFromConnectionString(iotConnectionString, TransportType.Amqp);
    await deviceClient.OpenAsync();
    Console.WriteLine($"Device {deviceId} connected 🔥.");

    CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    await deviceClient.SetReceiveMessageHandlerAsync(async (message, _) =>
    {
        var jobUpdate = await JsonSerializer.DeserializeAsync<JobUpdate>(message.GetBodyStream())
            ?? throw new InvalidOperationException("Failed to deserialize JobUpdate message.");

        if (jobUpdate.Status == "Cancelled")
        {
            logger.LogInformation("Job {job} has been cancelled 💀💀💀", jobUpdate.CorrelationId);
            cts.Cancel();
            await deviceClient.DisposeAsync();
        }
    }, null);

    foreach (var step in job.Steps)
    {
        var jobUpdate = new JobUpdate(job.Name, job.CorrelationId, step, "In Progress");
        await deviceClient.SendEventAsync(new Message(BinaryData.FromObjectAsJson(jobUpdate).ToStream()), cts.Token);

        logger.LogInformation("Job {job} is processing step {step}", job.Name, step);
        await Task.Delay(6000, cts.Token);

        jobUpdate = new JobUpdate(job.Name, job.CorrelationId, step, "Completed");
        await deviceClient.SendEventAsync(new Message(BinaryData.FromObjectAsJson(jobUpdate).ToStream()), cts.Token);
    }

    logger.LogInformation("Job {job} has been completed", job.Name);
    await deviceClient.DisposeAsync();

    return Results.Ok(job);
});

await app.RunAsync();

record Job(string Name, string CorrelationId, string[] Steps);
record JobUpdate(string Name, string CorrelationId, string Step, string Status);
record NegotiateResponse(string Url);

