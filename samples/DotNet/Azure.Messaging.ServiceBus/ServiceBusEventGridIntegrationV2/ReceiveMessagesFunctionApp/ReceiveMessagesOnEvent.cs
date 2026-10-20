using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ReceiveMessagesFunctionApp;

public sealed class ReceiveMessagesOnEvent(ServiceBusClient client, ILogger<ReceiveMessagesOnEvent> log)
{
    [Function("EventGridTriggerFunction")]
    public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, FunctionContext context, CancellationToken cancellationToken)
    {
        if (eventGridEvent.EventType != "Microsoft.ServiceBus.ActiveMessagesAvailableWithNoListeners")
        {
            log.LogWarning("Ignoring unsupported Event Grid event type {EventType}.", eventGridEvent.EventType);
            return;
        }

        if (!eventGridEvent.TryGetSystemEventData(out var systemEvent) ||
            systemEvent is not ServiceBusActiveMessagesAvailableWithNoListenersEventData eventData)
            throw new InvalidOperationException("The Service Bus event data is missing.");
        if (string.IsNullOrWhiteSpace(eventData.TopicName) || string.IsNullOrWhiteSpace(eventData.SubscriptionName))
            throw new InvalidOperationException("This example requires a topic subscription event, not a queue event.");

        log.LogInformation("Event Grid invocation {InvocationId} received event {Event}",
            context.InvocationId, JsonSerializer.Serialize(new
            {
                id = eventGridEvent.Id,
                eventType = eventGridEvent.EventType,
                eventTime = eventGridEvent.EventTime,
                topic = eventGridEvent.Topic,
                subject = eventGridEvent.Subject,
                dataVersion = eventGridEvent.DataVersion,
                data = eventGridEvent.Data.ToObjectFromJson<JsonElement>()
            }));
        log.LogInformation("Receiving from topic {Topic}, subscription {Subscription}.",
            eventData.TopicName, eventData.SubscriptionName);

        await using (var receiver = client.CreateReceiver(eventData.TopicName, eventData.SubscriptionName))
        {
            var messages = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(5), cancellationToken);
            foreach (var message in messages)
            {
                log.LogInformation("Received message {MessageId}: {Body}", message.MessageId, message.Body.ToString());
                await receiver.CompleteMessageAsync(message, cancellationToken);
                log.LogInformation("Event Grid invocation {InvocationId} completed message {Message}",
                    context.InvocationId, JsonSerializer.Serialize(new
                    {
                        eventId = eventGridEvent.Id,
                        messageId = message.MessageId,
                        body = message.Body.ToString()
                    }));
            }
        }
        log.LogInformation("Event Grid invocation {InvocationId} completed event {EventId}.",
            context.InvocationId, eventGridEvent.Id);
    }
}
