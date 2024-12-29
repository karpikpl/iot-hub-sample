// Sample requires event-hub compatible endpoint connection string
// See https://github.com/Azure/azure-iot-sdk-csharp/blob/main/iothub/service/samples/getting%20started/ReadD2cMessages/README.md

using System.Text;
using Azure.Identity;
using Azure.Messaging.EventHubs.Consumer;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Configuration;

namespace console_cloud
{
    /// <summary>
    /// The Program class is the entry point for the IoT Hub sample application.
    /// It demonstrates how to connect to an Azure IoT Hub, retrieve device statistics,
    /// list devices, and read device-to-cloud messages.
    /// </summary>
    /// <remarks>
    /// This sample performs the following tasks:
    /// <list type="bullet">
    /// <item><description>Builds a configuration from various sources including JSON files, environment variables, and command-line arguments.</description></item>
    /// <item><description>Retrieves IoT Hub connection details from the configuration.</description></item>
    /// <item><description>Creates service and registry clients using Azure credentials.</description></item>
    /// <item><description>Fetches and displays the total number of connected devices.</description></item>
    /// <item><description>Lists all devices in the IoT Hub along with their connection state and last activity time.</description></item>
    /// <item><description>Sets up a cancellation token to gracefully handle application shutdown.</description></item>
    /// <item><description>Reads messages from devices asynchronously and displays message details including application and system properties.</description></item>
    /// </list>
    /// </remarks>
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
            var iotHubHostName = configuration["Iot:HubHostName"] ?? throw new InvalidOperationException("Iot:HubHostName is missing in configuration");
            var iotEndpoint = configuration["Iot:Endpoint"] ?? throw new InvalidOperationException("Iot:Endpoint is missing. See: https://github.com/Azure/azure-iot-sdk-csharp/blob/main/iothub/service/samples/getting%20started/ReadD2cMessages/README.md how to get it.");

            var credential = new DefaultAzureCredential(includeInteractiveCredentials: true);
            var serviceClient = ServiceClient.Create(iotHubHostName, credential);
            var registryClient = RegistryManager.Create(iotHubHostName, credential);

            // read device-to-cloud messages https://github.com/Azure/azure-iot-sdk-csharp/blob/main/iothub/service/samples/getting%20started/ReadD2cMessages/Parameters.cs

            var stats = await serviceClient.GetServiceStatisticsAsync();
            Console.WriteLine($"Total Connected devices: {stats.ConnectedDeviceCount}");

            // connect to the hub
            await serviceClient.OpenAsync();
            var query = registryClient.CreateQuery("SELECT * FROM devices");

            Console.WriteLine("Devices in the hub:");
            while (query.HasMoreResults)
            {
                var page = await query.GetNextAsTwinAsync();
                foreach (var twin in page)
                {
                    Console.WriteLine($"\t* Device: {twin.DeviceId} Connection: {twin.ConnectionState} LastActivity: {twin.LastActivityTime}");
                }
            }

            // Set up a way for the user to gracefully shutdown
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Exiting...");
            };

            Console.WriteLine("Reading messages from devices...");
            Console.WriteLine("Press Ctrl+C to stop the receiver.");
            // Run the sample
            await ReceiveMessagesFromDeviceAsync(iotEndpoint, cts.Token);

            Console.WriteLine("Cloud message reader finished.");
        }

        // Asynchronously create a PartitionReceiver for a partition and then start
        // reading any messages sent from the simulated client.
        private static async Task ReceiveMessagesFromDeviceAsync(string connectionString, CancellationToken ct)
        {
            // Create the consumer using the default consumer group using a direct connection to the service.
            // Information on using the client with a proxy can be found in the README for this quick start, here:
            // https://github.com/Azure-Samples/azure-iot-samples-csharp/tree/main/iot-hub/Quickstarts/ReadD2cMessages/README.md#websocket-and-proxy-support
            await using var consumer = new EventHubConsumerClient(
                EventHubConsumerClient.DefaultConsumerGroupName,
                connectionString);

            Console.WriteLine("Listening for messages on all partitions.");

            try
            {
                // Begin reading events for all partitions, starting with the first event in each partition and waiting indefinitely for
                // events to become available. Reading can be canceled by breaking out of the loop when an event is processed or by
                // signaling the cancellation token.
                //
                // The "ReadEventsAsync" method on the consumer is a good starting point for consuming events for prototypes
                // and samples. For real-world production scenarios, it is strongly recommended that you consider using the
                // "EventProcessorClient" from the "Azure.Messaging.EventHubs.Processor" package.
                //
                // More information on the "EventProcessorClient" and its benefits can be found here:
                //   https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/eventhub/Azure.Messaging.EventHubs.Processor/README.md
                await foreach (PartitionEvent partitionEvent in consumer.ReadEventsAsync(ct))
                {
                    Console.WriteLine($"\nMessage received on partition {partitionEvent.Partition.PartitionId}:");

                    string data = Encoding.UTF8.GetString(partitionEvent.Data.Body.ToArray());
                    Console.WriteLine($"\tMessage body: {data}");

                    Console.WriteLine("\tApplication properties (set by device):");
                    foreach (KeyValuePair<string, object> prop in partitionEvent.Data.Properties)
                    {
                        PrintProperties(prop);
                    }

                    Console.WriteLine("\tSystem properties (set by IoT hub):");
                    foreach (KeyValuePair<string, object> prop in partitionEvent.Data.SystemProperties)
                    {
                        PrintProperties(prop);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // This is expected when the token is signaled; it should not be considered an
                // error in this scenario.
            }
        }

        private static void PrintProperties(KeyValuePair<string, object> prop)
        {
            string? propValue = prop.Value is DateTime time
                ? time.ToString("O") // using a built-in date format here that includes milliseconds
                : prop.Value.ToString();

            Console.WriteLine($"\t\t{prop.Key}: {propValue}");
        }
    }
}