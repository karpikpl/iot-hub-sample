using Azure.Identity;
using Azure.Messaging.WebPubSub;
using Microsoft.Extensions.Configuration;

namespace publisher
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ConfigurationBuilder builder = new ConfigurationBuilder();
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
            builder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
            builder.AddEnvironmentVariables();
            builder.AddCommandLine(args);

            var configuration = builder.Build();
            var hostname = configuration["WebPubSub:Hostname"] ?? throw new ArgumentNullException("WebPubSub:Hostname");
            var hub = configuration["WebPubSub:HubName"] ?? throw new ArgumentNullException("WebPubSub:Hub");

            var endpoint = $"https://{hostname}";
            var message = args.Length > 0 ? args[0] : "Hello, WebPubSub!";

            // Either generate the token or fetch it from server or fetch a temp one from the portal
            var serviceClient = new WebPubSubServiceClient(new Uri(endpoint), hub, new DefaultAzureCredential());

            Console.WriteLine($"Sending message to WebPubSub hub {hub}: {message}");
            var response = await serviceClient.SendToAllAsync(message);
            Console.WriteLine($"Message sent to WebPubSub hub {hub}: {response.Status}");
        }
    }
}