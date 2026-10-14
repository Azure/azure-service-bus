using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;

namespace BasicSendReceiveTutorialWithFilters
{
    internal static class OfflineChecks
    {
        public static async Task RunAsync()
        {
            var rules = new HashSet<string> { "sample-one", "sample-two", "$Default", "business-rule" };
            var owned = new List<(string Subscription, string Rule)> { ("S1", "sample-one"), ("S2", "sample-two") };
            await Program.CleanupRulesAsync(owned, rule =>
            {
                rules.Remove(rule.Rule);
                return Task.CompletedTask;
            });
            Check(owned.Count == 0 && rules.SetEquals(new[] { "$Default", "business-rule" }), "Unowned rules changed.");

            owned.Add(("S1", "sample-fails"));
            owned.Add(("S2", "sample-succeeds"));
            var attempts = new List<string>();
            await ExpectAsync<AggregateException>(() => Program.CleanupRulesAsync(owned, rule =>
            {
                attempts.Add(rule.Rule);
                return rule.Rule == "sample-fails" ? Task.FromException(new InvalidOperationException("delete failed")) : Task.CompletedTask;
            }));
            Check(attempts.Count == 2 && owned.Count == 1 && owned[0].Rule == "sample-fails", "Cleanup lost remaining ownership.");

            var sample = new SendReceive { TopicName = "offline", Subscriptions = new[] { "S1" }, Store = new[] { "Store1" }, NrOfMessagesPerStore = 1 };
            var client = new RecordingClient();
            var original = Console.Out;
            using var output = new StringWriter();
            try
            {
                Console.SetOut(output);
                await ExpectAsync<InvalidOperationException>(() => sample.SendMessages(client, CancellationToken.None));
            }
            finally { Console.SetOut(original); }
            Check(!output.ToString().Contains("All messages sent") && client.Sender.Disposed, "Failed send reported success or leaked its sender.");

            await ExpectAsync<UnauthorizedAccessException>(() => sample.Receive(client, CancellationToken.None));
            Check(client.Receiver.Calls == 1 && client.Receiver.Disposed, "Receive failure retried indefinitely or leaked its receiver.");
            client.Receiver = new RecordingReceiver { Malformed = true };
            await ExpectAsync<JsonException>(() => sample.Receive(client, CancellationToken.None));
            Check(client.Receiver.Completed == 0 && client.Receiver.Disposed, "Malformed work was settled or receiver leaked.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            client.Receiver = new RecordingReceiver();
            await ExpectAsync<OperationCanceledException>(() => sample.Receive(client, cancelled.Token));
            Check(client.Receiver.Disposed && client.Receiver.Completed == 0, "Cancellation did not dispose without settlement.");
            Console.WriteLine("PASS: 6 offline rule-ownership, failure, disposal and cancellation checks. No Azure calls.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
        {
            try { await action(); }
            catch (T) { return; }
            throw new InvalidOperationException($"Expected {typeof(T).Name}.");
        }

        private sealed class RecordingClient : ServiceBusClient
        {
            public RecordingSender Sender { get; } = new();
            public RecordingReceiver Receiver { get; set; } = new();
            public override ServiceBusSender CreateSender(string queueOrTopicName, ServiceBusSenderOptions options = null) => Sender;
            public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName, ServiceBusReceiverOptions options = null) => Receiver;
        }

        private sealed class RecordingSender : ServiceBusSender
        {
            public bool Disposed { get; private set; }
            public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default) =>
                Task.FromException(new InvalidOperationException("send failed"));
            public override ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }

        private sealed class RecordingReceiver : ServiceBusReceiver
        {
            public bool Malformed { get; set; }
            public int Calls { get; private set; }
            public int Completed { get; private set; }
            public bool Disposed { get; private set; }
            public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
                int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
            {
                Calls++;
                cancellationToken.ThrowIfCancellationRequested();
                if (!Malformed) throw new UnauthorizedAccessException("offline authorization failure");
                return Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>(
                    new[] { ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("not-json")) });
            }
            public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
            {
                Completed++;
                return Task.CompletedTask;
            }
            public override ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }
    }
}
