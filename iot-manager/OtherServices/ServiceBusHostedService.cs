// using https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-7.0&tabs=visual-studio

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.WebPubSub;

namespace iotManager.OtherServices;

public class ServiceBusHostedService : BackgroundService
{
    private readonly WebPubSubServiceClient _webPubSubServiceClient;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<ServiceBusHostedService> _logger;
    private const string queueName = "notifications";

    private readonly ServiceBusProcessorOptions _options = new()
    {
        // By default or when AutoCompleteMessages is set to true, the processor will complete the message after executing the message handler
        // Set AutoCompleteMessages to false to [settle messages](/azure/service-bus-messaging/message-transfers-locks-settlement#peeklock) on your own.
        // In both cases, if the message handler throws an exception without settling the message, the processor will abandon the message.
        AutoCompleteMessages = true,

        // I can also allow for multi-threading
        MaxConcurrentCalls = 1
    };

    public ServiceBusHostedService(WebPubSubServiceClient webPubSubServiceClient, ServiceBusClient serviceBusClient, ILogger<ServiceBusHostedService> logger)
    {
        _webPubSubServiceClient = webPubSubServiceClient;
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
            //var notification = JsonSerializer.Deserialize<Notification>(body);

            await args.CompleteMessageAsync(message);

            var notification = new 
            {
                Id = message.MessageId,
                When = args.Message.EnqueuedTime,
                MessageType = string.IsNullOrWhiteSpace(args.Message.Subject) ? "Notification" : args.Message.Subject,
                Data = body
            };
            _logger.LogInformation("Sending notification to subscribers");

            await _webPubSubServiceClient.SendToAllAsync(JsonSerializer.Serialize(notification), Azure.Core.ContentType.ApplicationJson);
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

            await _webPubSubServiceClient.SendToAllAsync(JsonSerializer.Serialize(errorNotification), Azure.Core.ContentType.ApplicationJson);
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
}