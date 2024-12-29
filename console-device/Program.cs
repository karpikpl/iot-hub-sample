using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;

namespace console_device
{
    /// <summary>
    /// This program is a sample IoT device simulator that connects to an Azure IoT Hub.
    /// It performs the following tasks:
    /// 1. Reads configuration settings from JSON files, environment variables, and command line arguments.
    /// 2. Retrieves an IoT Hub connection string from a remote service using an API key.
    /// 3. Connects to the IoT Hub using the retrieved connection string.
    /// 4. Sets up handlers for direct methods and cloud-to-device messages.
    /// 5. Sends simulated telemetry data (provided by the user using terminal) to the IoT Hub at regular intervals.
    /// 6. Allows the user to exit the program gracefully using a control-C command.
    /// 
    /// Original sample: https://github.com/Azure/azure-iot-sdk-csharp/blob/main/iothub/device/samples/getting%20started/SimulatedDevice/Program.cs
    /// </summary>
    class Program
    {
        private static TimeSpan s_telemetryInterval = TimeSpan.FromSeconds(20);

        private static async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            try
            {
                var tcs = new TaskCompletionSource<string?>();

                using (ct.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false))
                {
                    var inputTask = Task.Run(() => Console.ReadLine());
                    var completedTask = await Task.WhenAny(inputTask, tcs.Task);

                    if (completedTask == tcs.Task)
                    {
                        throw new OperationCanceledException(ct);
                    }

                    return await inputTask;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("----------------------------------------");
            Console.WriteLine("Azure IoT Hub Device Simulator");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine();
            Console.WriteLine("Startup instructions:");
            Console.WriteLine("  - Ensure you have the necessary configuration files (appsettings.json, appsettings.local.json) with the required settings.");
            Console.WriteLine("  - You can override the DeviceId by passing them as command line arguments:");
            Console.WriteLine("      dotnet run -- DeviceId=<your_device_id>");
            Console.WriteLine("  - Alternatively, set the environment variable DeviceId'.");
            Console.WriteLine("To send a message to another device start one device with a name DeviceId=device-scheduler::foo and another one with DeviceId=device-solver::foo.");
            Console.WriteLine();
            Console.WriteLine("Usage instructions:");
            Console.WriteLine("  - Enter a message and press Enter to send it to the IoT Hub.");
            Console.WriteLine("  - To exit, press Enter without typing any message.");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine();

            ConfigurationBuilder builder = new ConfigurationBuilder();
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
            builder.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
            builder.AddEnvironmentVariables();
            builder.AddCommandLine(args);

            var configuration = builder.Build();
            string iotManagerUrl = configuration["IoT:ManagerUrl"] ?? throw new InvalidOperationException("IoT:ManagerUrl setting is missing.");
            string apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey setting is missing.");

            // using this device ID will cause the server to send messages back to this device
            string jobId = "console-test";
            string deviceId = configuration["DeviceId"] ?? $"console-device::{jobId}";

            // get connection string for IoT Hub
            using HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(new Uri($"{iotManagerUrl}/negotiate/{deviceId}"));

            var connectionString = response!.Url;

            using var deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Amqp);
            await deviceClient.OpenAsync();
            Console.WriteLine($"Device '{deviceId}' connected.");

            // Set up a condition to quit the sample
            Console.WriteLine("Press control-C or [enter] to exit.");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Exiting...");
            };

            await deviceClient.SetMethodDefaultHandlerAsync(DirectMethodCallback, null, cts.Token);
            await deviceClient.SetReceiveMessageHandlerAsync((message, userCtx) =>
            {
                var properties = string.Join("; ", message.Properties.Select(p => $"{p.Key}={p.Value}").ToList());
                Console.WriteLine($"Received message from {message.ConnectionDeviceId} : {Encoding.UTF8.GetString(message.GetBytes())}\n\tProperties: {properties}");
                deviceClient.CompleteAsync(message);
                return Task.CompletedTask;
            }, null);

            // Run the telemetry loop
            await SendDeviceToCloudMessagesAsync(deviceClient, cts.Token);

            // SendDeviceToCloudMessagesAsync is designed to run until cancellation has been explicitly requested by Console.CancelKeyPress.
            // As a result, by the time the control reaches the call to close the device client, the cancellation token source would
            // have already had cancellation requested.
            // Hence, if you want to pass a cancellation token to any subsequent calls, a new token needs to be generated.
            // For device client APIs, you can also call them without a cancellation token, which will set a default
            // cancellation timeout of 4 minutes: https://github.com/Azure/azure-iot-sdk-csharp/blob/64f6e9f24371bc40ab3ec7a8b8accbfb537f0fe1/iothub/device/src/InternalClient.cs#L1922
            await deviceClient.CloseAsync();

            Console.WriteLine("Device simulator finished.");
        }

        private static Task<MethodResponse> DirectMethodCallback(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"Received direct method [{methodRequest.Name}] with payload [{methodRequest.DataAsJson}].");

            switch (methodRequest.Name)
            {
                case "SetTelemetryInterval":
                    try
                    {
                        int telemetryIntervalSeconds = JsonSerializer.Deserialize<int>(methodRequest.DataAsJson);
                        s_telemetryInterval = TimeSpan.FromSeconds(telemetryIntervalSeconds);
                        Console.WriteLine($"Setting the telemetry interval to {s_telemetryInterval}.");
                        return Task.FromResult(new MethodResponse(200));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed ot parse the payload for direct method {methodRequest.Name} due to {ex}");
                        break;
                    }
            }

            return Task.FromResult(new MethodResponse(400));
        }

        // Async method to send simulated telemetry
        private static async Task SendDeviceToCloudMessagesAsync(DeviceClient deviceClient, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var messageText = await ReadLineAsync(ct);

                    if (string.IsNullOrEmpty(messageText))
                    {
                        break;
                    }

                    // Create JSON message
                    string messageBody = JsonSerializer.Serialize(
                        new
                        {
                            messageText
                        });
                    using var message = new Message(Encoding.ASCII.GetBytes(messageBody))
                    {
                        ContentType = "application/json",
                        ContentEncoding = "utf-8",
                    };

                    // Add a custom application property to the message.
                    // An IoT hub can filter on these properties without access to the message body.
                    message.Properties.Add("currentInterval_in_s", s_telemetryInterval.Seconds.ToString());

                    // Send the telemetry message
                    await deviceClient.SendEventAsync(message, ct);
                    Console.WriteLine($"{DateTime.Now} > Sending message: {messageBody}");
                }
            }
            catch (TaskCanceledException) { } // ct was signaled
        }
    }
}

record NegotiateResponse(string Url);