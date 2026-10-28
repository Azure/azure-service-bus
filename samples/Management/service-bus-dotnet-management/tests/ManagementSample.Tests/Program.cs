using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.ResourceManager;
using AzureServiceBusDotNetManagement;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action action) => tests.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => tests.Add((name, action));

Test("checked-in JSON binds Standard to namespace SKU and partitioned entity options", () =>
{
    var options = AppOptions.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), _ => null);
    Check.Equal("Standard", options.ServiceBusSku);
    options.SubscriptionId = Fixtures.SubscriptionId;
    options.TenantId = Fixtures.TenantId;
    options.Credential = "azure-cli";
    var plan = new ResourcePlan(options);
    var data = options.CreateNamespaceData(plan.RunId);
    Check.Equal("Standard", data.Sku.Name.ToString());
    Check.Equal("Standard", data.Sku.Tier.ToString());
    Check.True(data.DisableLocalAuth == true);
    Check.True(plan.QueueOptions(options).EnablePartitioning && plan.TopicOptions(options).EnablePartitioning);
    Check.Equal(10, plan.SubscriptionOptions().MaxDeliveryCount);
});
Test("nonsecret environment overrides are actually loaded", () =>
{
    var environment = new Dictionary<string, string>
    {
        ["SB_SAMPLE_SUBSCRIPTION_ID"] = Fixtures.SubscriptionId,
        ["SB_SAMPLE_TENANT_ID"] = Fixtures.TenantId,
        ["SB_SAMPLE_CREDENTIAL"] = "azure-cli",
        ["SB_SAMPLE_SKU"] = "Premium",
        ["SB_SAMPLE_LOCATION"] = "westus2"
    };
    var options = AppOptions.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), key => environment.GetValueOrDefault(key));
    options.Validate();
    Check.Equal("Premium", options.ServiceBusSku);
    Check.Equal("westus2", options.DataCenterLocation);
});
foreach (var partitions in new[] { 1, 2, 4 })
    Test($"Premium entity partitioning matches {partitions} namespace partitions, not capacity", () =>
    {
        var options = Fixtures.Options();
        options.ServiceBusSku = "Premium";
        options.PremiumCapacity = 4;
        options.PremiumMessagingPartitions = partitions;
        var plan = new ResourcePlan(options);
        var data = options.CreateNamespaceData(plan.RunId);
        Check.Equal("Premium", data.Sku.Name.ToString());
        Check.Equal(4, data.Sku.Capacity);
        Check.Equal(partitions, data.PremiumMessagingPartitions);
        Check.Equal(partitions > 1, plan.QueueOptions(options).EnablePartitioning);
        Check.Equal(partitions > 1, plan.TopicOptions(options).EnablePartitioning);
    });
foreach (var invalid in new[] { "Basic", "", "standard", "Unknown" })
    Test($"tier rejected before credential acquisition: '{invalid}'", () =>
    {
        var options = Fixtures.Options();
        options.ServiceBusSku = invalid;
        Check.Throws<SampleException>(() => options.CreateCredential());
    });
Test("invalid capacity, partitions, timeouts, names and subscription rejected", () =>
{
    Action<AppOptions>[] mutations =
    [
        o => o.PremiumCapacity = 3, o => o.PremiumMessagingPartitions = 3,
        o => { o.ServiceBusSku = "Premium"; o.PremiumMessagingPartitions = 4; },
        o => o.OperationTimeoutSeconds = 0, o => o.CleanupTimeoutSeconds = 1801,
        o => o.NamespacePrefix = "existing/namespace", o => o.ResourceGroupPrefix = null!,
        o => o.SubscriptionId = Guid.Empty.ToString(), o => o.DataCenterLocation = "East US"
    ];
    foreach (var mutate in mutations)
    {
        var options = Fixtures.Options();
        mutate(options);
        Check.Throws<SampleException>(options.Validate);
    }
});
Test("legacy secret and misspelled configuration fields fail closed without echoing values", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "invalid-config.json");
    try
    {
        File.WriteAllText(path, """{"ClientSecret":"do-not-echo-this-marker"}""");
        var exception = Check.Throws<SampleException>(() => AppOptions.Load(path, _ => null));
        Check.True(!exception.Message.Contains("do-not-echo-this-marker", StringComparison.Ordinal));
        File.WriteAllText(path, """{"ServiceBusSkuName":"Standard"}""");
        Check.Throws<SampleException>(() => AppOptions.Load(path, _ => null));
        File.WriteAllText(path, "null");
        Check.Throws<SampleException>(() => AppOptions.Load(path, _ => null));
    }
    finally { File.Delete(path); }
});
Test("names are generated per invocation, never adopted from user input", () =>
{
    var first = new ResourcePlan(Fixtures.Options());
    var second = new ResourcePlan(Fixtures.Options());
    Check.True(first.RunId != second.RunId);
    Check.Equal(32, first.RunId.Length);
    Check.True(first.NamespaceName.EndsWith(first.RunId, StringComparison.Ordinal));
    Check.True(first.ResourceGroupName.EndsWith(first.RunId, StringComparison.Ordinal));
});
Test("credential selection is explicit, secretless, and has no fallback chain", () =>
{
    var options = Fixtures.Options();
    Check.True(options.CreateCredential() is AzureCliCredential);
    options.Credential = "";
    Check.Throws<SampleException>(() => options.CreateCredential());
    options.Credential = "default";
    Check.Throws<SampleException>(() => options.CreateCredential());
    options.Credential = "managed-identity";
    options.TenantId = "";
    Check.True(options.CreateCredential() is ManagedIdentityCredential);
    options.ManagedIdentityClientId = Fixtures.TenantId;
    Check.True(options.CreateCredential() is ManagedIdentityCredential);
    options.Credential = "azure-cli";
    Check.Throws<SampleException>(() => options.CreateCredential());
});
Test("default help and all supported CLI variants parse without acquiring credentials", () =>
{
    Check.Equal("--help", CommandLine.Parse([]).Mode);
    string[][] variants =
    [
        ["--help"], ["--validate"], ["--plan"], ["--validate", "--config", "nonsecret.json"],
        ["--run", "--confirm-create"], ["--run", "--confirm-create", "--cleanup-owned"],
        ["--run", "--confirm-create", "--verify-routing", "--confirm-messaging"],
        ["--run", "--confirm-create", "--verify-routing", "--confirm-messaging", "--cleanup-owned"]
    ];
    foreach (var variant in variants)
        CommandLine.Parse(variant);
});
Test("missing consent, unknown secret arguments, duplicate and contradictory CLI flags fail closed", () =>
{
    string[][] variants =
    [
        ["--run"], ["--run", "--confirm-create", "--verify-routing"],
        ["--run", "--confirm-create", "--confirm-messaging"], ["--plan", "--cleanup-owned"],
        ["--help", "--run"], ["--plan", "--plan"], ["--config"], ["--config", "--plan"],
        ["--client-secret", "do-not-echo-this-marker"], ["--cleanup-owned"]
    ];
    foreach (var variant in variants)
        Check.Throws<SampleException>(() => CommandLine.Parse(variant));
});
Test("initial SQL rule replaces real $Default, no match-all rule is requested", () =>
{
    var initial = ResourcePlan.InitialRule();
    Check.Equal("rule1", initial.Name);
    Check.Equal("myproperty='test'", ((SqlRuleFilter)initial.Filter).SqlExpression);
    Check.True(initial.Action is null);
    var rule = Fixtures.Rule("rule1", new SqlRuleFilter(ResourcePlan.SqlExpression));
    ResourcePlan.VerifyRules([rule]);
    Check.Throws<SampleException>(() => ResourcePlan.VerifyRules([rule, Fixtures.Rule("$Default", new TrueRuleFilter())]));
    Check.Throws<SampleException>(() => ResourcePlan.VerifyRules([]));
    Check.Throws<SampleException>(() => ResourcePlan.VerifyRules([Fixtures.Rule("rule1", new TrueRuleFilter())]));
    rule.Action = new SqlRuleAction("SET extra = 1");
    Check.Throws<SampleException>(() => ResourcePlan.VerifyRules([rule]));
});
Test("routing inventory includes matching, nonmatching and missing property messages", () =>
{
    var messages = AzureManagementOperations.RoutingMessages("offline");
    Check.Equal(3, messages.Length);
    Check.Equal("test", messages[0].ApplicationProperties["myproperty"]);
    Check.Equal("different", messages[1].ApplicationProperties["myproperty"]);
    Check.True(!messages[2].ApplicationProperties.ContainsKey("myproperty"));
    Check.Equal(3, messages.Select(message => message.MessageId).Distinct().Count());
});
AsyncTest("successful full lifecycle awaits reverse-order cleanup and readback", async () =>
{
    var fake = new FakeOperations();
    var snapshots = new List<string>();
    var report = await Fixtures.Run(fake, cleanup: true, routing: true,
        persist: report => snapshots.Add(JsonSerializer.Serialize(report, RunReport.JsonOptions)));
    Check.True(report.Success);
    Check.Equal(5, fake.Created.Count);
    Check.Equal(5, fake.Verified.Count);
    Check.Equal(1, fake.RoutingCalls);
    Check.True(fake.Deleted.SequenceEqual(Enum.GetValues<ResourceKind>().Reverse()));
    Check.True(report.Resources.All(record => record.Owned && record.State == "deleted"));
    Check.True(snapshots.Any(snapshot => snapshot.Contains("\"attempted\"", StringComparison.Ordinal)));
    Check.Equal("created-verified-and-deleted", report.Status);
});
AsyncTest("without cleanup consent, successful resources remain owned and inventoried", async () =>
{
    var fake = new FakeOperations();
    var report = await Fixtures.Run(fake, cleanup: false);
    Check.True(report.Success);
    Check.Equal(0, fake.Deleted.Count);
    Check.True(report.Resources.All(record => record.State == "owned"));
});
foreach (var kind in Enum.GetValues<ResourceKind>())
{
    AsyncTest($"observed {kind} collision causes no create/adoption/deletion of that resource", async () =>
    {
        var fake = new FakeOperations { Collision = kind };
        var report = await Fixtures.Run(fake);
        Check.True(!report.Success);
        Check.True(!fake.Created.Contains(kind) && !fake.Deleted.Contains(kind));
        Check.True(!report.Resources.Single(record => record.Resource.Kind == kind).Owned);
        Check.Equal("collision", report.Resources.Single(record => record.Resource.Kind == kind).State);
    });
    AsyncTest($"ambiguous {kind} create blocks ownership and ancestor deletion", async () =>
    {
        var fake = new FakeOperations { Ambiguous = kind };
        var report = await Fixtures.Run(fake);
        Check.True(!report.Success);
        var uncertain = report.Resources.Single(record => record.Resource.Kind == kind);
        Check.Equal("ambiguous", uncertain.State);
        Check.True(!uncertain.Owned && !fake.Deleted.Contains(kind));
        if (uncertain.Resource.ParentId is { } parent)
            Check.Equal("retained", report.Resources.Single(record => record.Resource.Id == parent).State);
    });
}
AsyncTest("create-only service collision after preflight is unowned", async () =>
{
    var fake = new FakeOperations { RaceCollision = ResourceKind.Queue };
    var report = await Fixtures.Run(fake);
    Check.True(!report.Success);
    Check.Equal("collision", report.Resources[2].State);
    Check.True(!report.Resources[2].Owned && !fake.Deleted.Contains(ResourceKind.Namespace));
});
AsyncTest("authorization or failed existence read is never absence", async () =>
{
    var fake = new FakeOperations { ReadFailure = ResourceKind.Namespace };
    var report = await Fixtures.Run(fake);
    Check.True(!report.Success);
    Check.True(!fake.Created.Contains(ResourceKind.Namespace));
    Check.Equal("checking", report.Resources[1].State);
    Check.Equal("retained", report.Resources[0].State);
});
AsyncTest("readback failure is retained even when cleanup succeeds", async () =>
{
    var fake = new FakeOperations { VerifyFailure = true };
    var report = await Fixtures.Run(fake);
    Check.True(!report.Success && report.Failures.Count == 1);
    Check.Equal(5, fake.Deleted.Count);
});
AsyncTest("independent cleanup failures are both retained; parents are not deleted", async () =>
{
    var fake = new FakeOperations { CleanupFailures = [ResourceKind.Subscription, ResourceKind.Queue] };
    var report = await Fixtures.Run(fake);
    Check.True(!report.Success);
    Check.Equal(2, report.Resources.Count(record => record.State == "cleanup-failed"));
    Check.True(report.Failures.Count >= 2);
    Check.True(fake.Deleted.Contains(ResourceKind.Queue) && fake.Deleted.Contains(ResourceKind.Subscription));
    Check.True(!fake.Deleted.Contains(ResourceKind.Topic) && !fake.Deleted.Contains(ResourceKind.Namespace));
});
AsyncTest("cleanup timeout does not abandon independent siblings or return success", async () =>
{
    var fake = new FakeOperations { CleanupTimeout = ResourceKind.Subscription };
    var report = await Fixtures.Run(fake, cleanupTimeout: TimeSpan.FromMilliseconds(30));
    Check.True(!report.Success && fake.ObservedCleanupCancellation);
    Check.True(fake.Deleted.Contains(ResourceKind.Queue));
    Check.Equal("cleanup-failed", report.Resources[^1].State);
});
AsyncTest("caller cancellation leaves attempted create ambiguous and cleanup has a fresh token", async () =>
{
    using var cancellation = new CancellationTokenSource();
    var fake = new FakeOperations { CancelDuringCreate = cancellation };
    var report = await Fixtures.Run(fake, cancellation: cancellation.Token);
    Check.True(!report.Success);
    Check.Equal("ambiguous", report.Resources[^1].State);
    Check.True(fake.Deleted.Contains(ResourceKind.Queue));
    Check.True(!fake.CleanupUsedCancelledToken);
});
AsyncTest("operation deadline bounds provisioning and reports ambiguity", async () =>
{
    var fake = new FakeOperations { CreateTimeout = ResourceKind.Namespace };
    var report = await Fixtures.Run(fake, operationTimeout: TimeSpan.FromMilliseconds(30));
    Check.True(!report.Success);
    Check.Equal("ambiguous", report.Resources[1].State);
});
AsyncTest("routing failure remains a failure after successful cleanup", async () =>
{
    var fake = new FakeOperations { RoutingFailure = true };
    var report = await Fixtures.Run(fake, routing: true);
    Check.True(!report.Success && fake.Deleted.Count == 5);
});
Test("diagnostics suppress arbitrary exception contents and token material", () =>
{
    const string marker = "do-not-echo-this-marker";
    foreach (var exception in new Exception[]
    {
        new InvalidOperationException(marker), new RequestFailedException(403, marker),
        new AuthenticationFailedException(marker), new ServiceBusException(marker, ServiceBusFailureReason.ServiceBusy)
    })
        Check.True(!SafeError.Describe(exception).Contains(marker, StringComparison.Ordinal));
});
foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.Accepted })
    AsyncTest($"actual ARM adapter rejects ambiguous initial {(int)status} without adopting tags", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        var step = plan.Steps[status == HttpStatusCode.Accepted ? 1 : 0];
        using var handler = new OfflineHandler(request =>
            OfflineHandler.Json(status, Fixtures.ArmBody(plan, step.Kind)));
        var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
        await Check.ThrowsAsync<SampleException>(() => operations.CreateAsync(step, CancellationToken.None));
        Check.Equal(1, handler.Requests.Count);
    });
AsyncTest("actual ARM request serializes Standard tier, disables keys and propagates ownership", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new OfflineHandler(request =>
        OfflineHandler.Json(HttpStatusCode.Created, Fixtures.ArmBody(plan, ResourceKind.Namespace)));
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
    using var body = JsonDocument.Parse(handler.Requests[0].Body);
    Check.Equal("Standard", body.RootElement.GetProperty("sku").GetProperty("name").GetString());
    Check.Equal("Standard", body.RootElement.GetProperty("sku").GetProperty("tier").GetString());
    Check.True(body.RootElement.GetProperty("properties").GetProperty("disableLocalAuth").GetBoolean());
    Check.Equal(plan.RunId, body.RootElement.GetProperty("tags").GetProperty(ResourcePlan.OwnershipTag).GetString());
});
AsyncTest("actual administration request atomically creates SQL initial rule without $Default or upsert", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new OfflineHandler(request => OfflineHandler.Xml(HttpStatusCode.Created,
        Fixtures.SubscriptionXml(plan.RunId)));
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[^1], CancellationToken.None);
    var request = handler.Requests.Single();
    Check.Equal("PUT", request.Method);
    Check.True(!request.IfMatch);
    var body = XDocument.Parse(request.Body);
    Check.True(body.Descendants().Any(element => element.Name.LocalName == "SqlExpression" && element.Value == ResourcePlan.SqlExpression));
    Check.True(body.Descendants().Any(element => element.Name.LocalName == "Name" && element.Value == ResourcePlan.RuleName));
    Check.True(!request.Body.Contains("$Default", StringComparison.Ordinal));
});
AsyncTest("actual administration 409 is reported as a collision, without retry or adoption", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new OfflineHandler(_ => OfflineHandler.Xml(HttpStatusCode.Conflict, "<Error><Code>409</Code><Detail>exists</Detail></Error>"));
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    var exception = await Check.ThrowsAsync<ServiceBusException>(() => operations.CreateAsync(plan.Steps[2], CancellationToken.None));
    Check.True(SafeError.IsCollision(exception));
    Check.Equal(1, handler.Requests.Count);
});
foreach (var minutes in new[] { -1, 1, 3 })
    AsyncTest($"supported SDK credential policy handles token lifetime at now{minutes:+#;-#} minutes", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        var credential = new OfflineCredential(TimeSpan.FromMinutes(minutes));
        using var handler = new OfflineHandler(_ => OfflineHandler.Json(HttpStatusCode.NotFound, """{"error":{"code":"ResourceGroupNotFound"}}"""));
        var operations = Fixtures.Azure(options, plan, handler, credential);
        await operations.ExistsAsync(plan.Steps[0], CancellationToken.None);
        await operations.ExistsAsync(plan.Steps[0], CancellationToken.None);
        Check.True(credential.Calls >= 1);
        if (minutes <= 1)
        {
            await credential.Refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check.True(credential.Calls >= 2);
        }
        Check.True(credential.Scopes.All(scope => scope == ArmEnvironment.AzurePublicCloud.DefaultScope));
        Check.Equal(2, handler.Requests.Count);
    });

AsyncTest("inventory write failure stops provisioning and retains confirmed resources without deletion", async () =>
{
    var fake = new FakeOperations();
    var report = await Fixtures.Run(fake, persist: snapshot =>
    {
        if (snapshot.Resources[2].Owned)
            throw new IOException("offline write failure");
    });
    Check.True(!report.Success);
    Check.Equal(3, fake.Created.Count);
    Check.Equal(0, fake.Deleted.Count);
    Check.True(report.Resources.Where(record => record.Owned).All(record => record.State != "deleted"));
    Check.Equal("cleanup-failed", report.Resources[2].State);
    Check.True(report.Failures.Any(failure => failure.StartsWith("Resource inventory write failed", StringComparison.Ordinal)));
});
AsyncTest("deletion inventory is persisted before every cleanup dispatch", async () =>
{
    var fake = new FakeOperations();
    var deletionCheckpoints = new List<ResourceKind>();
    var report = await Fixtures.Run(fake, persist: snapshot =>
    {
        if (snapshot.Resources.SingleOrDefault(record => record.State == "deleting") is { } deleting)
        {
            Check.Equal(fake.Deleted.Count, deletionCheckpoints.Count);
            Check.True(!fake.Deleted.Contains(deleting.Resource.Kind));
            deletionCheckpoints.Add(deleting.Resource.Kind);
        }
    });
    Check.True(report.Success);
    Check.True(deletionCheckpoints.SequenceEqual(Enum.GetValues<ResourceKind>().Reverse()));
    Check.True(fake.Deleted.SequenceEqual(deletionCheckpoints));
});
foreach (var kind in Enum.GetValues<ResourceKind>())
{
    AsyncTest($"{kind} deleting inventory failure prevents that and all later deletion requests", async () =>
    {
        var fake = new FakeOperations();
        var persistenceFailures = 0;
        var report = await Fixtures.Run(fake, persist: snapshot =>
        {
            if (snapshot.Resources.Single(record => record.Resource.Kind == kind).State == "deleting")
            {
                persistenceFailures++;
                throw new IOException("offline deletion write failure");
            }
        });
        Check.True(!report.Success);
        Check.Equal(1, persistenceFailures);
        Check.Equal(5, fake.Created.Count);
        Check.True(fake.Deleted.SequenceEqual(
            Enum.GetValues<ResourceKind>().Reverse().TakeWhile(candidate => candidate != kind)));
        var refused = report.Resources.Single(record => record.Resource.Kind == kind);
        Check.Equal("cleanup-failed", refused.State);
        Check.Equal("Cleanup refused: deletion inventory could not be persisted.", refused.Error);
        Check.True(report.Failures.Any(failure =>
            failure.StartsWith("Resource inventory write failed", StringComparison.Ordinal)));
    });
}
AsyncTest("initial inventory failure makes zero resource calls", async () =>
{
    var fake = new FakeOperations();
    var report = await Fixtures.Run(fake, persist: _ => throw new IOException("offline write failure"));
    Check.True(!report.Success);
    Check.Equal(0, fake.Created.Count);
    Check.Equal(0, fake.Deleted.Count);
});
AsyncTest("scoped access checkpoints run before dependent writes and cancellation does not hide owned parents", async () =>
{
    var plan = new ResourcePlan(Fixtures.Options());
    var fake = new FakeOperations();
    var checkpoints = new List<ResourceKind>();
    var report = await ServiceBusManagementSample.RunAsync(plan, fake,
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), true, false, _ => { },
        beforeCreate: (step, token) =>
        {
            checkpoints.Add(step.Kind);
            if (step.Kind == ResourceKind.Queue)
                throw new SampleException("Access checkpoint declined.");
            return Task.CompletedTask;
        });
    Check.True(!report.Success);
    Check.True(checkpoints.SequenceEqual(new[] { ResourceKind.ResourceGroup, ResourceKind.Namespace, ResourceKind.Queue }));
    Check.True(fake.Created.SequenceEqual(new[] { ResourceKind.ResourceGroup, ResourceKind.Namespace }));
    Check.Equal(2, fake.Deleted.Count);
});
AsyncTest("access input accepts only continue, and EOF or other input fails", async () =>
{
    await AzureServiceBusDotNetManagement.Program.ConfirmAccessAsync(new StringReader("continue"), CancellationToken.None);
    await Check.ThrowsAsync<SampleException>(() =>
        AzureServiceBusDotNetManagement.Program.ConfirmAccessAsync(new StringReader(""), CancellationToken.None));
    await Check.ThrowsAsync<SampleException>(() =>
        AzureServiceBusDotNetManagement.Program.ConfirmAccessAsync(new StringReader("yes"), CancellationToken.None));
});
AsyncTest("blocked console-style input cannot defeat the operation deadline", async () =>
{
    using var input = new BlockingReader();
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    try
    {
        await Check.ThrowsAsync<OperationCanceledException>(() =>
            AzureServiceBusDotNetManagement.Program.ConfirmAccessAsync(input, cancellation.Token));
    }
    finally { input.Release(); }
    await input.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
});
Test("incomplete inventories never claim success", () =>
{
    var report = new RunReport(new ResourcePlan(Fixtures.Options()));
    foreach (var state in new[] { "planned", "creating", "verifying", "cleaning", "failed" })
    {
        report.Status = state;
        Check.True(!report.Success);
    }
});
AsyncTest("actual ARM Premium request binds both namespace capacity and partitions", async () =>
{
    var options = Fixtures.Options();
    options.ServiceBusSku = "Premium";
    options.PremiumCapacity = 4;
    options.PremiumMessagingPartitions = 2;
    var plan = new ResourcePlan(options);
    using var handler = new OfflineHandler(_ => OfflineHandler.Json(HttpStatusCode.Created, Fixtures.ArmBody(plan, ResourceKind.Namespace)));
    await Fixtures.Azure(options, plan, handler, new OfflineCredential()).CreateAsync(plan.Steps[1], CancellationToken.None);
    using var body = JsonDocument.Parse(handler.Requests[0].Body);
    Check.Equal("Premium", body.RootElement.GetProperty("sku").GetProperty("name").GetString());
    Check.Equal(4, body.RootElement.GetProperty("sku").GetProperty("capacity").GetInt32());
    Check.Equal(2, body.RootElement.GetProperty("properties").GetProperty("premiumMessagingPartitions").GetInt32());
});
foreach (var kind in new[] { ResourceKind.Queue, ResourceKind.Topic })
    AsyncTest($"actual {kind} request is create-only, tier-correct, and carries run metadata", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        using var handler = new OfflineHandler(_ => OfflineHandler.Xml(HttpStatusCode.Created, Fixtures.EntityXml(kind, plan.RunId)));
        await Fixtures.Azure(options, plan, handler, new OfflineCredential()).CreateAsync(
            plan.Steps.Single(step => step.Kind == kind), CancellationToken.None);
        var request = handler.Requests.Single();
        Check.True(!request.IfMatch);
        var body = XDocument.Parse(request.Body);
        Check.True(body.Descendants().Any(element => element.Name.LocalName == "EnablePartitioning" && element.Value == "true"));
        Check.True(body.Descendants().Any(element => element.Name.LocalName == "UserMetadata" && element.Value == plan.RunId));
    });
foreach (var partitions in new[] { 1, 2, 4 })
    foreach (var kind in new[] { ResourceKind.Queue, ResourceKind.Topic })
        AsyncTest($"actual Premium {kind} request matches {partitions} namespace partitions", async () =>
        {
            var options = Fixtures.Options();
            options.ServiceBusSku = "Premium";
            options.PremiumCapacity = 4;
            options.PremiumMessagingPartitions = partitions;
            var plan = new ResourcePlan(options);
            using var handler = new OfflineHandler(_ => OfflineHandler.Xml(HttpStatusCode.Created, Fixtures.EntityXml(kind, plan.RunId)));
            await Fixtures.Azure(options, plan, handler, new OfflineCredential()).CreateAsync(
                plan.Steps.Single(step => step.Kind == kind), CancellationToken.None);
            var body = XDocument.Parse(handler.Requests.Single().Body);
            Check.Equal(partitions > 1, bool.Parse(body.Descendants().Single(
                element => element.Name.LocalName == "EnablePartitioning").Value));
        });
AsyncTest("actual adapter refuses ownership-marker mismatch before a delete", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new OfflineHandler(_ => OfflineHandler.Json(HttpStatusCode.OK,
        Fixtures.ArmBody(plan, ResourceKind.ResourceGroup).Replace(plan.RunId, "unowned", StringComparison.Ordinal)));
    await Check.ThrowsAsync<SampleException>(() =>
        Fixtures.Azure(options, plan, handler, new OfflineCredential()).DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    Check.True(handler.Requests.All(request => request.Method == "GET"));
});
AsyncTest("actual adapter refuses recursive group deletion when an unexpected resource remains", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    var inventories = 0;
    using var handler = new OfflineHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath.Equals($"{plan.Steps[0].Id}/resources", StringComparison.OrdinalIgnoreCase))
        {
            inventories++;
            return OfflineHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                value = new[] { new { id = $"{plan.Steps[0].Id}/providers/Microsoft.Storage/storageAccounts/foreign", name = "foreign", type = "Microsoft.Storage/storageAccounts" } }
            }));
        }
        return OfflineHandler.Json(HttpStatusCode.OK, Fixtures.ArmBody(plan, ResourceKind.ResourceGroup));
    });
    await Check.ThrowsAsync<SampleException>(() =>
        Fixtures.Azure(options, plan, handler, new OfflineCredential()).DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    Check.True(handler.Requests.All(request => request.Method == "GET"));
    Check.Equal(1, inventories);
});
AsyncTest("namespace inventory reconciliation accepts the captured exact owned deleted namespace scenario", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new NamespaceCleanupHandler(plan);
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
    await operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None);
    await operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None);
    Check.Equal(1, handler.NamespaceDeletes);
    Check.Equal(1, handler.NamespaceDeletionPolls);
    Check.Equal(1, handler.PostDeleteReads);
    Check.Equal(1, handler.ResourceLists);
    Check.Equal(1, handler.ReconciliationReads);
    Check.Equal(1, handler.GroupDeletes);
    Check.Equal(1, handler.GroupDeletionPolls);
    Check.Equal(1, handler.GroupAbsenceReads);
});
foreach (var kind in new[] { ResourceKind.Queue, ResourceKind.Topic })
    AsyncTest($"namespace cleanup still refuses a remaining {kind}", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        using var handler = new NamespaceCleanupHandler(plan) { UnexpectedEntityKind = kind };
        var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
        await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
        var failure = await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None));
        Check.Equal($"Namespace cleanup refused: unexpected {(kind == ResourceKind.Queue ? "queues" : "topics")} remain.", failure.Message);
        Check.Equal(0, handler.NamespaceDeletes);
        Check.Equal(0, handler.GroupDeletes);
    });
foreach (var variant in new[] { "foreign ID", "same prefix", "different group", "wrong type", "missing type" })
    AsyncTest($"namespace inventory reconciliation refuses {variant}", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        using var handler = new NamespaceCleanupHandler(plan);
        handler.Resources.Clear();
        handler.Resources.Add(variant switch
        {
            "foreign ID" => ($"{plan.Steps[0].Id}/providers/Microsoft.Storage/storageAccounts/foreign", "Microsoft.Storage/storageAccounts"),
            "same prefix" => ($"{plan.Steps[1].Id}-foreign", "Microsoft.ServiceBus/namespaces"),
            "different group" => (plan.Steps[1].Id.Replace(plan.ResourceGroupName, $"{plan.ResourceGroupName}-foreign", StringComparison.Ordinal), "Microsoft.ServiceBus/namespaces"),
            "wrong type" => (plan.Steps[1].Id, "Microsoft.Storage/storageAccounts"),
            _ => (plan.Steps[1].Id, null)
        });
        var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
        await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
        await operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None);
        await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
        Check.Equal(1, handler.ResourceLists);
        Check.Equal(0, handler.ReconciliationReads);
        Check.Equal(0, handler.GroupDeletes);
    });
foreach (var nextPage in new[] { false, true })
    AsyncTest($"namespace inventory reconciliation refuses a foreign item on {(nextPage ? "the next page" : "the same page")}", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        using var handler = new NamespaceCleanupHandler(plan) { ForeignOnNextPage = nextPage };
        if (!nextPage) handler.Resources.Add(($"{plan.Steps[1].Id}-foreign", "Microsoft.ServiceBus/namespaces"));
        var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
        await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
        await operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None);
        await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
        Check.Equal(nextPage ? 2 : 1, handler.ResourceLists);
        Check.Equal(1, handler.ReconciliationReads);
        Check.Equal(0, handler.GroupDeletes);
    });
foreach (var creation in new[] { "not attempted", "HTTP 200", "HTTP 202", "wrong ID", "wrong marker" })
    AsyncTest($"namespace inventory reconciliation refuses unconfirmed creation: {creation}", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        using var handler = new NamespaceCleanupHandler(plan)
        {
            CreateStatus = creation == "HTTP 200" ? HttpStatusCode.OK :
                creation == "HTTP 202" ? HttpStatusCode.Accepted : HttpStatusCode.Created,
            WrongCreatedId = creation == "wrong ID",
            WrongCreatedMarker = creation == "wrong marker"
        };
        var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
        if (creation != "not attempted")
            await Check.ThrowsAsync<SampleException>(() => operations.CreateAsync(plan.Steps[1], CancellationToken.None));
        await operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None);
        await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
        Check.Equal(1, handler.ResourceLists);
        Check.Equal(1, handler.PostDeleteReads);
        Check.Equal(0, handler.ReconciliationReads);
        Check.Equal(0, handler.GroupDeletes);
    });
AsyncTest("namespace inventory reconciliation refuses a namespace never deleted by this instance", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new NamespaceCleanupHandler(plan);
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
    await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    Check.Equal(0, handler.NamespaceDeletes);
    Check.Equal(1, handler.ResourceLists);
    Check.Equal(0, handler.ReconciliationReads);
    Check.Equal(0, handler.GroupDeletes);
});
AsyncTest("namespace inventory reconciliation refuses in-flight and cancelled deletion", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new NamespaceCleanupHandler(plan) { HoldNamespaceDeletion = true };
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
    using var cancellation = new CancellationTokenSource();
    var deletion = operations.DeleteOwnedAsync(plan.Steps[1], cancellation.Token);
    try
    {
        await handler.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check.True(!deletion.IsCompleted);
        await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    }
    finally { cancellation.Cancel(); }
    await Check.ThrowsAsync<OperationCanceledException>(() => deletion);
    await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    Check.Equal(2, handler.ResourceLists);
    Check.Equal(0, handler.ReconciliationReads);
    Check.Equal(0, handler.GroupDeletes);
});
AsyncTest("namespace inventory reconciliation refuses failed deletion", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new NamespaceCleanupHandler(plan) { DeleteCompletionStatus = HttpStatusCode.InternalServerError };
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
    var failure = await Check.ThrowsAsync<RequestFailedException>(() => operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None));
    Check.Equal(500, failure.Status);
    await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    Check.Equal(1, handler.ResourceLists);
    Check.Equal(0, handler.PostDeleteReads);
    Check.Equal(0, handler.ReconciliationReads);
    Check.Equal(0, handler.GroupDeletes);
});
AsyncTest("namespace inventory reconciliation requires exact absence after completed deletion", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new NamespaceCleanupHandler(plan) { PostDeleteStatus = HttpStatusCode.OK };
    var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
    await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
    await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None));
    await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
    Check.Equal(1, handler.NamespaceDeletionPolls);
    Check.Equal(1, handler.PostDeleteReads);
    Check.Equal(1, handler.ResourceLists);
    Check.Equal(0, handler.ReconciliationReads);
    Check.Equal(0, handler.GroupDeletes);
});
foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.Accepted, HttpStatusCode.NoContent,
    HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
    AsyncTest($"namespace inventory reconciliation refuses exact GET HTTP {(int)status}", async () =>
    {
        var options = Fixtures.Options();
        var plan = new ResourcePlan(options);
        using var handler = new NamespaceCleanupHandler(plan) { ReconciliationStatus = status };
        var operations = Fixtures.Azure(options, plan, handler, new OfflineCredential());
        await operations.CreateAsync(plan.Steps[1], CancellationToken.None);
        await operations.DeleteOwnedAsync(plan.Steps[1], CancellationToken.None);
        if (status == HttpStatusCode.OK)
        {
            var failure = await Check.ThrowsAsync<SampleException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
            Check.Equal("Resource-group cleanup refused: unexpected resources remain.", failure.Message);
        }
        else
        {
            var requestFailure = await Check.ThrowsAsync<RequestFailedException>(() => operations.DeleteOwnedAsync(plan.Steps[0], CancellationToken.None));
            Check.Equal((int)status, requestFailure.Status);
        }
        Check.Equal(1, handler.ResourceLists);
        Check.Equal(1, handler.ReconciliationReads);
        Check.Equal(0, handler.GroupDeletes);
    });
AsyncTest("actual group and namespace readback rejects a different location before cleanup", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    foreach (var step in plan.Steps.Take(2))
    {
        using var handler = new OfflineHandler(_ => OfflineHandler.Json(HttpStatusCode.OK,
            Fixtures.ArmBody(plan, step.Kind).Replace("eastus", "westus2", StringComparison.Ordinal)));
        await Check.ThrowsAsync<SampleException>(() =>
            Fixtures.Azure(options, plan, handler, new OfflineCredential()).DeleteOwnedAsync(step, CancellationToken.None));
        Check.True(handler.Requests.All(request => request.Method == "GET"));
    }
});
AsyncTest("actual ARM deletion waits for its asynchronous operation and confirms absence", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    using var handler = new DeleteHandler(plan);
    var task = Fixtures.Azure(options, plan, handler, new OfflineCredential())
        .DeleteOwnedAsync(plan.Steps[0], CancellationToken.None);
    try
    {
        await handler.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check.True(!task.IsCompleted);
    }
    finally { handler.Complete.TrySetResult(); }
    await task.WaitAsync(TimeSpan.FromSeconds(5));
    Check.True(handler.Deleted && handler.AbsenceRead);
});
AsyncTest("actual ARM and administration clients share the selected credential with service-specific scopes", async () =>
{
    var options = Fixtures.Options();
    var plan = new ResourcePlan(options);
    var credential = new OfflineCredential();
    using var handler = new OfflineHandler(request => request.RequestUri!.Host == "management.azure.com"
        ? OfflineHandler.Json(HttpStatusCode.Created, Fixtures.ArmBody(plan, ResourceKind.ResourceGroup))
        : OfflineHandler.Xml(HttpStatusCode.Created, Fixtures.EntityXml(ResourceKind.Queue, plan.RunId)));
    var operations = Fixtures.Azure(options, plan, handler, credential);
    Check.Equal(0, credential.Calls);
    await operations.CreateAsync(plan.Steps[0], CancellationToken.None);
    await operations.CreateAsync(plan.Steps[2], CancellationToken.None);
    Check.True(credential.Scopes.Contains(ArmEnvironment.AzurePublicCloud.DefaultScope));
    Check.True(credential.Scopes.Contains("https://servicebus.azure.net/.default"));
});

var selected = args.Length == 0 ? tests : tests.Where(test => args.Contains(test.Name, StringComparer.Ordinal)).ToList();
var unknown = args.Except(tests.Select(test => test.Name), StringComparer.Ordinal).ToArray();
if (unknown.Length > 0)
{
    Console.Error.WriteLine($"Unknown offline test name(s): {string.Join(", ", unknown)}");
    return 2;
}
var failed = 0;
foreach (var (name, run) in selected)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
Console.WriteLine($"Offline tests: {selected.Count - failed} passed, {failed} failed, {tests.Count - selected.Count} skipped; no Azure credentials or network used.");
return failed == 0 ? 0 : 1;

static class Check
{
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}

static class Fixtures
{
    public const string SubscriptionId = "11111111-1111-4111-8111-111111111111";
    public const string TenantId = "22222222-2222-4222-8222-222222222222";
    public static AppOptions Options() => new() { SubscriptionId = SubscriptionId, TenantId = TenantId, Credential = "azure-cli" };
    public static RuleProperties Rule(string name, RuleFilter filter) =>
        ServiceBusModelFactory.RuleProperties(name: name, filter: filter);
    public static Task<RunReport> Run(FakeOperations operations, bool cleanup = true, bool routing = false,
        TimeSpan? cleanupTimeout = null, TimeSpan? operationTimeout = null, Action<RunReport>? persist = null,
        CancellationToken cancellation = default) =>
        ServiceBusManagementSample.RunAsync(new ResourcePlan(Options()), operations,
            operationTimeout ?? TimeSpan.FromSeconds(5), cleanupTimeout ?? TimeSpan.FromSeconds(1),
            cleanup, routing, persist ?? (_ => { }), cancellation);
    public static AzureManagementOperations Azure(AppOptions options, ResourcePlan plan, HttpMessageHandler handler, TokenCredential credential)
    {
        var transport = new HttpClientTransport(new HttpClient(handler, disposeHandler: false));
        return new AzureManagementOperations(options, plan, credential,
            new ArmClientOptions { Transport = transport },
            new ServiceBusAdministrationClientOptions { Transport = transport });
    }
    public static string ArmBody(ResourcePlan plan, ResourceKind kind) => JsonSerializer.Serialize(new
    {
        id = plan.Steps[kind == ResourceKind.ResourceGroup ? 0 : 1].Id,
        name = kind == ResourceKind.ResourceGroup ? plan.ResourceGroupName : plan.NamespaceName,
        type = kind == ResourceKind.ResourceGroup ? "Microsoft.Resources/resourceGroups" : "Microsoft.ServiceBus/namespaces",
        location = "eastus",
        tags = new Dictionary<string, string> { [ResourcePlan.OwnershipTag] = plan.RunId },
        sku = new { name = "Standard", tier = "Standard" },
        properties = new { provisioningState = "Succeeded", disableLocalAuth = true }
    });
    public static string SubscriptionXml(string metadata) => $"""
        <entry xmlns="http://www.w3.org/2005/Atom">
          <title>topic1</title><content type="application/xml">
            <SubscriptionDescription xmlns="http://schemas.microsoft.com/netservices/2010/10/servicebus/connect">
              <MaxDeliveryCount>10</MaxDeliveryCount><UserMetadata>{metadata}</UserMetadata>
            </SubscriptionDescription>
          </content>
        </entry>
        """;
    public static string EntityXml(ResourceKind kind, string metadata) => $"""
        <entry xmlns="http://www.w3.org/2005/Atom">
          <title>{(kind == ResourceKind.Queue ? ResourcePlan.QueueName : ResourcePlan.TopicName)}</title>
          <content type="application/xml">
            <{kind}Description xmlns="http://schemas.microsoft.com/netservices/2010/10/servicebus/connect">
              <EnablePartitioning>true</EnablePartitioning><UserMetadata>{metadata}</UserMetadata>
            </{kind}Description>
          </content>
        </entry>
        """;
}

sealed class FakeOperations : IManagementOperations
{
    public ResourceKind? Collision { get; init; }
    public ResourceKind? RaceCollision { get; init; }
    public ResourceKind? Ambiguous { get; init; }
    public ResourceKind? ReadFailure { get; init; }
    public ResourceKind? CreateTimeout { get; init; }
    public ResourceKind? CleanupTimeout { get; init; }
    public HashSet<ResourceKind> CleanupFailures { get; init; } = [];
    public bool VerifyFailure { get; init; }
    public bool RoutingFailure { get; init; }
    public CancellationTokenSource? CancelDuringCreate { get; init; }
    public bool ObservedCleanupCancellation { get; private set; }
    public bool CleanupUsedCancelledToken { get; private set; }
    public int RoutingCalls { get; private set; }
    public List<ResourceKind> Created { get; } = [];
    public List<ResourceKind> Verified { get; } = [];
    public List<ResourceKind> Deleted { get; } = [];
    public Task<bool> ExistsAsync(ResourceStep resource, CancellationToken cancellationToken) =>
        resource.Kind == ReadFailure
            ? throw new RequestFailedException(403, "not absence")
            : Task.FromResult(resource.Kind == Collision);
    public async Task CreateAsync(ResourceStep resource, CancellationToken cancellationToken)
    {
        Created.Add(resource.Kind);
        if (resource.Kind == Ambiguous)
            throw new IOException("lost response");
        if (resource.Kind == RaceCollision)
            throw new ServiceBusException("exists", ServiceBusFailureReason.MessagingEntityAlreadyExists);
        if (resource.Kind == CreateTimeout)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (resource.Kind == ResourceKind.Subscription && CancelDuringCreate is { } source)
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
    public Task VerifyAsync(ResourceStep resource, CancellationToken cancellationToken)
    {
        Verified.Add(resource.Kind);
        return VerifyFailure ? throw new SampleException("Readback mismatch.") : Task.CompletedTask;
    }
    public async Task DeleteOwnedAsync(ResourceStep resource, CancellationToken cancellationToken)
    {
        Deleted.Add(resource.Kind);
        CleanupUsedCancelledToken |= cancellationToken.IsCancellationRequested;
        if (CleanupFailures.Contains(resource.Kind))
            throw new RequestFailedException(403, "delete failure");
        if (resource.Kind == CleanupTimeout)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { ObservedCleanupCancellation = true; throw; }
        }
    }
    public Task VerifyRoutingAsync(CancellationToken cancellationToken)
    {
        RoutingCalls++;
        return RoutingFailure ? throw new SampleException("Routing mismatch.") : Task.CompletedTask;
    }
}

sealed record CapturedRequest(string Method, string Body, bool IfMatch);

sealed class OfflineHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(request.Method.Method,
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Headers.Contains("If-Match")));
        var response = respond(request);
        response.RequestMessage = request;
        return response;
    }
    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Xml(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/atom+xml") };
}

sealed class OfflineCredential(TimeSpan? initialLifetime = null) : TokenCredential
{
    private int calls;
    public int Calls => calls;
    public List<string> Scopes { get; } = [];
    public TaskCompletionSource Refreshed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();
    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref calls);
        Scopes.AddRange(requestContext.Scopes);
        if (count > 1) Refreshed.TrySetResult();
        return ValueTask.FromResult(new AccessToken($"offline-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow.Add(count == 1 ? initialLifetime ?? TimeSpan.FromHours(1) : TimeSpan.FromHours(1))));
    }

}

sealed class BlockingReader : TextReader
{
    private readonly ManualResetEventSlim released = new();
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Release() => released.Set();
    public override string? ReadLine()
    {
        released.Wait();
        Finished.TrySetResult();
        return null;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) released.Dispose();
        base.Dispose(disposing);
    }
}

sealed class DeleteHandler(ResourcePlan plan) : HttpMessageHandler
{
    public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Deleted { get; private set; }
    public bool AbsenceRead { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Delete)
        {
            var accepted = OfflineHandler.Json(HttpStatusCode.Accepted, "");
            accepted.Headers.Location = new Uri("https://management.azure.com/operations/offline-delete");
            accepted.Headers.Add("Retry-After", "0");
            return accepted;
        }
        if (request.RequestUri!.AbsolutePath == "/operations/offline-delete")
        {
            PollStarted.TrySetResult();
            await Complete.Task.WaitAsync(cancellationToken);
            Deleted = true;
            return OfflineHandler.Json(HttpStatusCode.NoContent, "");
        }
        if (request.RequestUri.AbsolutePath.EndsWith("/resources", StringComparison.Ordinal))
            return OfflineHandler.Json(HttpStatusCode.OK, """{"value":[]}""");
        if (Deleted)
        {
            AbsenceRead = true;
            return OfflineHandler.Json(HttpStatusCode.NotFound, """{"error":{"code":"ResourceGroupNotFound"}}""");
        }
        return OfflineHandler.Json(HttpStatusCode.OK, Fixtures.ArmBody(plan, ResourceKind.ResourceGroup));
    }
}

sealed class NamespaceCleanupHandler(ResourcePlan plan) : HttpMessageHandler
{
    public HttpStatusCode CreateStatus { get; init; } = HttpStatusCode.Created;
    public bool WrongCreatedId { get; init; }
    public bool WrongCreatedMarker { get; init; }
    public HttpStatusCode DeleteCompletionStatus { get; init; } = HttpStatusCode.NoContent;
    public HttpStatusCode PostDeleteStatus { get; init; } = HttpStatusCode.NotFound;
    public HttpStatusCode ReconciliationStatus { get; init; } = HttpStatusCode.NotFound;
    public bool HoldNamespaceDeletion { get; init; }
    public bool ForeignOnNextPage { get; init; }
    public ResourceKind? UnexpectedEntityKind { get; init; }
    public List<(string Id, string? Type)> Resources { get; } = [(plan.Steps[1].Id, "Microsoft.ServiceBus/namespaces")];
    public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int NamespaceDeletes { get; private set; }
    public int NamespaceDeletionPolls { get; private set; }
    public int PostDeleteReads { get; private set; }
    public int ResourceLists { get; private set; }
    public int ReconciliationReads { get; private set; }
    public int GroupDeletes { get; private set; }
    public int GroupDeletionPolls { get; private set; }
    public int GroupAbsenceReads { get; private set; }
    private bool namespaceDeleted;
    private bool groupDeleted;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        if (request.Method == HttpMethod.Get && uri.Host == plan.FullyQualifiedNamespace &&
            (path.Equals("/$Resources/Queues", StringComparison.OrdinalIgnoreCase) ||
             path.Equals("/$Resources/Topics", StringComparison.OrdinalIgnoreCase)))
        {
            var kind = path.Equals("/$Resources/Queues", StringComparison.OrdinalIgnoreCase) ? ResourceKind.Queue : ResourceKind.Topic;
            var entity = UnexpectedEntityKind == kind ? Fixtures.EntityXml(kind, plan.RunId) : "";
            return OfflineHandler.Xml(HttpStatusCode.OK, $"""
                <feed xmlns="http://www.w3.org/2005/Atom">
                  <title type="text">{path.Split('/')[^1]}</title>
                  <id>{uri.GetLeftPart(UriPartial.Path)}</id>
                  <updated>2026-01-01T00:00:00Z</updated>
                  {entity}
                </feed>
                """);
        }
        Check.Equal("management.azure.com", uri.Host);
        if (path.Equals(plan.Steps[1].Id, StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Put)
            {
                var body = Fixtures.ArmBody(plan, ResourceKind.Namespace);
                if (WrongCreatedId) body = body.Replace(plan.NamespaceName, $"{plan.NamespaceName}-foreign", StringComparison.Ordinal);
                if (WrongCreatedMarker) body = body.Replace(plan.RunId, "unowned", StringComparison.Ordinal);
                return OfflineHandler.Json(CreateStatus, body);
            }
            if (request.Method == HttpMethod.Delete)
            {
                NamespaceDeletes++;
                return Accepted("namespace");
            }
            Check.Equal(HttpMethod.Get, request.Method);
            if (!namespaceDeleted) return OfflineHandler.Json(HttpStatusCode.OK, Fixtures.ArmBody(plan, ResourceKind.Namespace));
            if (ResourceLists == 0) PostDeleteReads++;
            else ReconciliationReads++;
            return NamespaceResponse(ResourceLists == 0 ? PostDeleteStatus : ReconciliationStatus);
        }
        if (request.Method == HttpMethod.Get && path == "/operations/offline-namespace-delete")
        {
            NamespaceDeletionPolls++;
            PollStarted.TrySetResult();
            if (HoldNamespaceDeletion)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            namespaceDeleted = DeleteCompletionStatus == HttpStatusCode.NoContent;
            return NamespaceResponse(DeleteCompletionStatus);
        }
        if (path.Equals($"{plan.Steps[0].Id}/resources", StringComparison.OrdinalIgnoreCase))
        {
            Check.Equal(HttpMethod.Get, request.Method);
            ResourceLists++;
            var nextPage = uri.Query.Contains("page=2", StringComparison.Ordinal);
            var rows = nextPage
                ? new List<(string Id, string? Type)> { ($"{plan.Steps[1].Id}-foreign", "Microsoft.ServiceBus/namespaces") }
                : Resources;
            return OfflineHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                value = rows.Select(row => row.Type is null
                    ? new Dictionary<string, string> { ["id"] = row.Id }
                    : new Dictionary<string, string> { ["id"] = row.Id, ["type"] = row.Type }),
                nextLink = ForeignOnNextPage && !nextPage ? $"https://management.azure.com{path}?page=2" : null
            }));
        }
        if (path.Equals(plan.Steps[0].Id, StringComparison.OrdinalIgnoreCase))
        {
            if (request.Method == HttpMethod.Delete)
            {
                GroupDeletes++;
                return Accepted("group");
            }
            Check.Equal(HttpMethod.Get, request.Method);
            if (!groupDeleted) return OfflineHandler.Json(HttpStatusCode.OK, Fixtures.ArmBody(plan, ResourceKind.ResourceGroup));
            GroupAbsenceReads++;
            return OfflineHandler.Json(HttpStatusCode.NotFound, """{"error":{"code":"ResourceGroupNotFound"}}""");
        }
        if (request.Method == HttpMethod.Get && path == "/operations/offline-group-delete")
        {
            GroupDeletionPolls++;
            groupDeleted = true;
            return OfflineHandler.Json(HttpStatusCode.NoContent, "");
        }
        throw new InvalidOperationException($"Unexpected offline request: {request.Method} {path}");
    }

    private HttpResponseMessage NamespaceResponse(HttpStatusCode status) =>
        OfflineHandler.Json(status, status == HttpStatusCode.OK ? Fixtures.ArmBody(plan, ResourceKind.Namespace) :
            status == HttpStatusCode.NoContent ? "" : """{"error":{"code":"OfflineNamespaceResponse"}}""");

    private static HttpResponseMessage Accepted(string resource)
    {
        var response = OfflineHandler.Json(HttpStatusCode.Accepted, "");
        response.Headers.Location = new Uri($"https://management.azure.com/operations/offline-{resource}-delete");
        response.Headers.Add("Retry-After", "0");
        return response;
    }
}
