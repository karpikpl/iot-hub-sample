using System.Net.Http.Json;
using Azure.Messaging.WebPubSub.Clients;
using Microsoft.Extensions.Configuration;

namespace subscriber
{
    record NegotiateResponse(string Url);
    record JobUpdate(string Name, string CorrelationId, string Step, string Status);
    class Program
    {
        static async Task Main(string[] args)
        {
            ConfigurationBuilder builder = new ConfigurationBuilder();
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            builder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
            builder.AddEnvironmentVariables();
            builder.AddCommandLine(args);

            var configuration = builder.Build();

            string userid = configuration["WebPubSub:UserId"] ?? "console-subscriber";
            string groupName = configuration["WebPubSub:GroupName"] ?? "group";
            string iotManagerUrl = configuration["WebPubSub:ServerUrl"] ?? throw new ArgumentNullException("WebPubSub:ServerUrl");
            string apiKey = configuration["ApiKey"] ?? throw new ArgumentNullException("ApiKey");

            Uri webPubSubServerUri = new Uri($"{iotManagerUrl}/negotiate/{userid}/{groupName}");

            using HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(webPubSubServerUri);

            var client = new WebPubSubClient(new Uri(response!.Url));

            await client.StartAsync();

            client.Connected += (args) =>
            {
                Console.WriteLine("Connected with id: " + args.ConnectionId);
                return Task.CompletedTask;
            };

            client.ServerMessageReceived += (args) =>
            {
                Console.WriteLine("Server message received: " + args.Message.Data.ToString());
                return Task.CompletedTask;
            };

            client.Disconnected += (args) =>
            {
                Console.WriteLine("Disconnected with error: " + args.DisconnectedMessage.Reason);
                return Task.CompletedTask;
            };

            client.GroupMessageReceived += (args) =>
            {
                if (args.Message.FromUserId == userid)
                {
                    // Skip messages from self
                    return Task.CompletedTask;
                }

                var jobUpdate = args.Message.Data.ToObjectFromJson<JobUpdate>();
                Console.WriteLine("Group message received: " + jobUpdate);

                if (jobUpdate.Status == "Cancelled")
                {
                    Console.WriteLine("Job cancelled, disconnecting...");
                    client.DisposeAsync();
                    Environment.Exit(0);
                }
                return Task.CompletedTask;
            };

            while (true)
            {
                var command = Console.ReadLine();

                if (string.IsNullOrEmpty(command))
                {
                    await client.DisposeAsync();
                    break;
                }
                else
                {
                    var jobUpdate = new JobUpdate("?", groupName, command, $"Update on {DateTime.Now} for {command}");
                    await client.SendToGroupAsync(groupName, BinaryData.FromObjectAsJson(jobUpdate), WebPubSubDataType.Json);
                }
            }
        }
    }
}