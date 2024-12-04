﻿using System.Net.Http.Json;
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
    /// 5. Sends simulated telemetry data (temperature and humidity) to the IoT Hub at regular intervals.
    /// 6. Allows the user to exit the program gracefully using a control-C command.
    /// 
    /// Original sample: https://github.com/Azure/azure-iot-sdk-csharp/blob/main/iothub/device/samples/getting%20started/SimulatedDevice/Program.cs
    /// </summary>
    class Program
    {
        private static TimeSpan s_telemetryInterval = TimeSpan.FromSeconds(20);

        static async Task Main(string[] args)
        {
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
            string deviceId = $"console-device::{jobId}";

            // get connection string for IoT Hub
            using HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            var response = await httpClient.GetFromJsonAsync<NegotiateResponse>(new Uri($"{iotManagerUrl}/negotiate/{deviceId}"));

            var connectionString = response!.Url;

            using var deviceClient = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Amqp);
            await deviceClient.OpenAsync();
            Console.WriteLine("Device connected.");

            // Set up a condition to quit the sample
            Console.WriteLine("Press control-C to exit.");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Exiting...");
            };

            await deviceClient.SetMethodDefaultHandlerAsync(DirectMethodCallback, null, cts.Token);
            await deviceClient.SetReceiveMessageHandlerAsync((message, _) =>
            {
                Console.WriteLine($"Received message: {Encoding.UTF8.GetString(message.GetBytes())}");
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
            // Initial telemetry values
            double minTemperature = 20;
            double minHumidity = 60;
            var rand = new Random();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    double currentTemperature = minTemperature + rand.NextDouble() * 15;
                    double currentHumidity = minHumidity + rand.NextDouble() * 20;

                    // Create JSON message
                    string messageBody = JsonSerializer.Serialize(
                        new
                        {
                            temperature = currentTemperature,
                            humidity = currentHumidity,
                        });
                    using var message = new Message(Encoding.ASCII.GetBytes(messageBody))
                    {
                        ContentType = "application/json",
                        ContentEncoding = "utf-8",
                    };

                    // Add a custom application property to the message.
                    // An IoT hub can filter on these properties without access to the message body.
                    message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");

                    // Send the telemetry message
                    await deviceClient.SendEventAsync(message, ct);
                    Console.WriteLine($"{DateTime.Now} > Sending message: {messageBody}");

                    await Task.Delay(s_telemetryInterval, ct);
                }
            }
            catch (TaskCanceledException) { } // ct was signaled
        }
    }
}

record NegotiateResponse(string Url);