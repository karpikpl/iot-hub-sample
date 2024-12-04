using System.Globalization;
using System.Net.Http.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Devices.Client;
using System.Text.Json;

var isDoneEvent = new ManualResetEventSlim(false);

ConfigurationBuilder builder = new ConfigurationBuilder();
builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.AddEnvironmentVariables();
builder.AddCommandLine(args);

var configuration = builder.Build();

// Configuration
// Replace with your Service Bus namespace and queue or topic name
string fullyQualifiedNamespace = configuration["ServiceBus:Namespace"] ?? throw new ArgumentNullException("ServiceBus:Namespace");
string queueOrTopicName = configuration["ServiceBus:TopicName"] ?? throw new ArgumentNullException("ServiceBus:TopicName");
string iotManagerUrl = configuration["IoT:ManagerUrl"] ?? throw new ArgumentNullException("IoT:ManagerUrl");
string apiKey = configuration["ApiKey"] ?? throw new ArgumentNullException("ApiKey");

string jobId = $"job-{Environment.UserName}-{DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture)}";
string deviceId = $"device-scheduler::{jobId}";
string solverId = $"device-solver::{jobId}";

Uri iotNegotiateUri = new Uri($"{iotManagerUrl}/negotiate/{deviceId}");
Uri iotNegotiateSolverUri = new Uri($"{iotManagerUrl}/negotiate/{solverId}");

// get connection string for IoT Hub
using HttpClient httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(iotNegotiateUri);

// register solver too
await httpClient.GetFromJsonAsync<NegotiateResponse>(iotNegotiateSolverUri);

// Create a ServiceBusClient using DefaultAzureCredential
var client = new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());

// Create a IoT device client
using var deviceClient = DeviceClient.CreateFromConnectionString(response!.Url, TransportType.Amqp);
await deviceClient.OpenAsync();
Console.WriteLine($"Device {deviceId} connected 🔥.");

await deviceClient.SetReceiveMessageHandlerAsync(async (message, _) =>
{
    var jobUpdate = await JsonSerializer.DeserializeAsync<JobUpdate>(message.GetBodyStream())
        ?? throw new InvalidOperationException("Failed to deserialize JobUpdate message.");

    Console.WriteLine($"Server message received {message.MessageId}: {jobUpdate.Name} - {jobUpdate.Step} - {jobUpdate.Status}");

    if (jobUpdate.Step == "Done" && jobUpdate.Status == "Completed")
    {
        isDoneEvent.Set();
        Console.WriteLine("Job is done. 🔥🔥🔥");
    }
}, null);

// Create a sender for the queue or topic
ServiceBusSender sender = client.CreateSender(queueOrTopicName);

var job = new Job("Job 1", jobId, new string[] { "Read all the data", "Build in-memory model", "Train the model", "Evaluate the model", "Gather results", "Send Response", "Done" });
// Create a message to send
ServiceBusMessage message = new ServiceBusMessage(BinaryData.FromObjectAsJson(job));

// Send the message
await sender.SendMessageAsync(message);

// Send the event to IoT Hub
await deviceClient.SendEventAsync(new Message(BinaryData.FromObjectAsJson(job).ToStream()));

Console.WriteLine("Message sent.");

Console.CancelKeyPress += async (sender, e) =>
{
    e.Cancel = true; // Prevent the process from terminating immediately
    Console.WriteLine("Are you sure you want to cancel? (y/n)");
    var response = Console.ReadKey(intercept: true).Key;
    if (response == ConsoleKey.Y)
    {
        // letting the solver know that the job has been cancelled
        await deviceClient.SendEventAsync(new Message(BinaryData.FromObjectAsJson(new JobUpdate(job.Name, job.CorrelationId, "Cancelled", "Cancelled")).ToStream()));
        Console.WriteLine("Cancellation requested for the job.");
        isDoneEvent.Set();
    }
    else
    {
        Console.WriteLine("Continuing execution.");
    }
};

await Task.Run(() => isDoneEvent.Wait());

// clean up
await deviceClient.DisposeAsync();
await client.DisposeAsync();

Console.WriteLine("Cleaning up 🧼🧼🧼...");
var deregisterResponse = await httpClient.GetAsync($"{iotManagerUrl}/deregister/{deviceId}");
var deregister2Response = await httpClient.GetAsync($"{iotManagerUrl}/deregister/{solverId}");

Console.WriteLine($"Deregistered scheduler: {deregisterResponse.StatusCode} 👋");
Console.WriteLine($"Deregistered solver: {deregister2Response.StatusCode} 👋");

Console.WriteLine("Job is done. 👋👋👋");

return 0;

record Job(string Name, string CorrelationId, string[] Steps);
record JobUpdate(string Name, string CorrelationId, string Step, string Status);
record NegotiateResponse(string Url);