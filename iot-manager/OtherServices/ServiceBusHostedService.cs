// using https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-7.0&tabs=visual-studio

using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;

namespace iotManager.OtherServices;

public class ServiceBusHostedService : BackgroundService
{
    private readonly ServiceClient _iotHubServiceClient;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<ServiceBusHostedService> _logger;
    private const string queueName = "device-messages";

    private readonly ServiceBusProcessorOptions _options = new()
    {
        // By default or when AutoCompleteMessages is set to true, the processor will complete the message after executing the message handler
        // Set AutoCompleteMessages to false to [settle messages](/azure/service-bus-messaging/message-transfers-locks-settlement#peeklock) on your own.
        // In both cases, if the message handler throws an exception without settling the message, the processor will abandon the message.
        AutoCompleteMessages = true,

        // I can also allow for multi-threading
        MaxConcurrentCalls = 1
    };

    public ServiceBusHostedService(ServiceClient iotHubServiceClient, ServiceBusClient serviceBusClient, ILogger<ServiceBusHostedService> logger)
    {
        _iotHubServiceClient = iotHubServiceClient;
        _serviceBusClient = serviceBusClient;
        _logger = logger;
    }

    override protected async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tcs = new TaskCompletionSource();
        stoppingToken.Register(() => tcs.TrySetCanceled(), false);

        await using ServiceBusProcessor processor = _serviceBusClient.CreateProcessor(queueName, _options);

        processor.ProcessMessageAsync += async args =>
        {
            _logger.LogInformation("Received message: {MessageId}", args.Message.MessageId);

            var message = args.Message;
            var body = message.Body.ToString();
            var fromDeviceId = message.ApplicationProperties["iothub-connection-device-id"]?.ToString();

            if (fromDeviceId == null || !fromDeviceId.Contains("::"))
            {
                _logger.LogWarning("Device id not found in message properties");
                await args.DeadLetterMessageAsync(message, deadLetterReason: "DeviceIdNotFound", deadLetterErrorDescription: "Device id not found in message properties");
                return;
            }

            var messageToDeviceId = fromDeviceId.StartsWith("device-solver::")
                ? fromDeviceId.Replace("device-solver::", "device-scheduler::")
                : fromDeviceId.Replace("device-scheduler::", "device-solver::");

            try
            {
                _logger.LogInformation("Sending message from {deviceId} to device: {toDeviceId}", fromDeviceId, messageToDeviceId);
                await SendCloudToDeviceMessageAsync(messageToDeviceId, fromDeviceId, message.Body.ToStream());
            }
            catch (DeviceNotFoundException deviceNotFoundEx)
            {
                _logger.LogWarning(deviceNotFoundEx, "Device {deviceId} does not exist", messageToDeviceId);
                await args.DeadLetterMessageAsync(message, deadLetterReason: "DeviceNotFound", deadLetterErrorDescription: "Device " + messageToDeviceId + "does not exist");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to device: {deviceId}", fromDeviceId);
                await args.DeadLetterMessageAsync(message, deadLetterReason: "DeviceMessageFailed", deadLetterErrorDescription: ex.Message);
                return;
            }
        };

        processor.ProcessErrorAsync += async args =>
        {
            _logger.LogError(args.Exception, "Error processing message: {MessageId}", args.Exception.Message);

            // send error
            var errorNotification = new
            {
                Id = args.Identifier,
                When = DateTime.UtcNow,
                MessageType = "Error",
                Data = args.Exception.Message
            };

            await Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);

        await tcs.Task;

        _logger.LogInformation("Stopping listener");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping notifications loop");
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("Disposing notifications loop");
        base.Dispose();
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting notifications loop");
        return base.StartAsync(cancellationToken);
    }

    private async Task SendCloudToDeviceMessageAsync(string deviceId, string fromDeviceId, Stream message)
    {
        var commandMessage = new Message(message)
        {
            Ack = DeliveryAcknowledgement.Full,
            Properties = { ["from-device"] = fromDeviceId }
        };

        await _iotHubServiceClient.SendAsync(deviceId, commandMessage);
        _logger.LogInformation("Sent message to device: {deviceId}", deviceId);
    }
}