using System.Globalization;
using System.Net.Http.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.WebPubSub.Clients;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Configuration;

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
string iotManagerUrl = configuration["WebPubSub:ServerUrl"] ?? throw new ArgumentNullException("WebPubSub:ServerUrl");
string apiKey = configuration["ApiKey"] ?? throw new ArgumentNullException("ApiKey");

string jobId = $"job-{Environment.UserName}-{DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture)}";
string userid = $"scheduler-{jobId}";

Uri webPubSubServerUri = new Uri($"{iotManagerUrl}/negotiate/{userid}/{jobId}");

// get connection string for WebPubSub
using HttpClient httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(webPubSubServerUri);

// Create a ServiceBusClient using DefaultAzureCredential
var client = new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());

// Create a WebPubSub client
var webPubSubClient = new WebPubSubClient(new Uri(response!.Url));
await webPubSubClient.StartAsync();
Console.WriteLine($"WebPubSub client started for job {jobId} 🔥");

webPubSubClient.GroupMessageReceived+= (args) =>
{
    if(args.Message.DataType == WebPubSubDataType.Json)
    {
        var jobUpdate = args.Message.Data.ToObjectFromJson<JobUpdate>()
            ?? throw new InvalidOperationException("Failed to deserialize JobUpdate message.");

        Console.WriteLine($"Message received in group {args.Message.Group}: {jobUpdate.Name} - {jobUpdate.Step} - {jobUpdate.Status}");

        if(jobUpdate.Step == "Done" && jobUpdate.Status == "Completed")
        {
            isDoneEvent.Set();
            Console.WriteLine("Job is done. 🔥🔥🔥");
        }
    }
    else
    Console.WriteLine($"Uknown Message received in group {args.Message.Group}: {args.Message.Data.ToString()}");

    return Task.CompletedTask;
};

// Create a sender for the queue or topic
ServiceBusSender sender = client.CreateSender(queueOrTopicName);

var job = new Job("Job 1", jobId, new string[] { "Read all the data", "Build in-memory model", "Train the model", "Evaluate the model", "Gather results", "Send Response", "Done" });
// Create a message to send
ServiceBusMessage message = new ServiceBusMessage(BinaryData.FromObjectAsJson(job));

// Send the message
await sender.SendMessageAsync(message);

// Send the event to WebPubSub
var submittedResponse = await webPubSubClient.SendEventAsync("asp_job_submitted", BinaryData.FromObjectAsJson(job), WebPubSubDataType.Json, fireAndForget: true);

Console.WriteLine("Message sent.");

Console.CancelKeyPress += async (sender, e) => {
    e.Cancel = true; // Prevent the process from terminating immediately
    Console.WriteLine("Are you sure you want to cancel? (y/n)");
    var response = Console.ReadKey(intercept: true).Key;
    if (response == ConsoleKey.Y) {
        // letting the solver know that the job has been cancelled
        await webPubSubClient.SendToGroupAsync(jobId, BinaryData.FromObjectAsJson(new JobUpdate(job.Name, job.CorrelationId, "Cancelled", "Cancelled")), WebPubSubDataType.Json);
        Console.WriteLine("Cancellation requested for the job.");
        isDoneEvent.Set();
    } else {
        Console.WriteLine("Continuing execution.");
    }
};

await Task.Run(() => isDoneEvent.Wait());

record Job(string Name, string CorrelationId, string[] Steps);
record JobUpdate(string Name, string CorrelationId, string Step, string Status);
record NegotiateResponse(string Url);