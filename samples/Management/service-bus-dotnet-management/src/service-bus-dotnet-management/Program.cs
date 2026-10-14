// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;

namespace AzureServiceBusDotNetManagement;

public sealed record CommandLine(string Mode, string ConfigPath, bool CleanupOwned, bool VerifyRouting)
{
    public static CommandLine Parse(string[] args)
    {
        string? mode = null;
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var cleanup = false;
        var routing = false;
        var createConsent = false;
        var messagingConsent = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            if (!seen.Add(args[index]))
                throw new SampleException("Duplicate option; use --help.");
            switch (args[index])
            {
                case "--help" or "--validate" or "--plan" or "--run":
                    if (mode is not null)
                        throw new SampleException("Choose only one mode: --help, --validate, --plan or --run.");
                    mode = args[index];
                    break;
                case "--config":
                    if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                        throw new SampleException("--config requires a JSON path.");
                    path = args[index];
                    break;
                case "--confirm-create": createConsent = true; break;
                case "--cleanup-owned": cleanup = true; break;
                case "--verify-routing": routing = true; break;
                case "--confirm-messaging": messagingConsent = true; break;
                default: throw new SampleException("Unknown option; use --help. Credentials and secrets are not accepted on the command line.");
            }
        }
        mode ??= "--help";
        if (mode != "--run" && (cleanup || routing || createConsent || messagingConsent))
            throw new SampleException("Mutation and messaging options require --run.");
        if (mode == "--run" && !createConsent)
            throw new SampleException("--run requires --confirm-create; this provisions billable resources.");
        if (routing != messagingConsent)
            throw new SampleException("--verify-routing and --confirm-messaging must be supplied together.");
        return new CommandLine(mode, path, cleanup, routing);
    }
}

public static class Program
{
    public const string Help = """
        Service Bus management sample (.NET 10; Azure public cloud)
          --help                         Offline; also the no-argument default.
          --validate [--config PATH]     Offline configuration validation, no credential acquisition.
          --plan [--config PATH]         Offline resource/SQL-rule preview, no Azure calls.
          --run --confirm-create        Create fresh billable resources and read back their settings.
            [--config PATH]             Nonsecret JSON; default is beside the executable.
            [--cleanup-owned]           Await bounded deletion of positively owned resources, also after failure.
            [--verify-routing --confirm-messaging]  Send three test messages; verify the SQL filter.
        Explicit credentials: azure-cli (TenantId required), or managed-identity
        (optional ManagedIdentityClientId; no fallback to other accounts).
        Environment overrides: SB_SAMPLE_SUBSCRIPTION_ID, SB_SAMPLE_TENANT_ID,
        SB_SAMPLE_CREDENTIAL, SB_SAMPLE_MANAGED_IDENTITY_CLIENT_ID, SB_SAMPLE_LOCATION, SB_SAMPLE_SKU.
        Names use configured prefixes plus a fresh random suffix; existing resources cannot be selected.
        No keys, secrets, role assignments, provider registration or token output.
        A local management-run-<id>.json records attempted/owned/ambiguous/remaining resources.
        After group and namespace creation, execution pauses for narrowly scoped namespace
        management and entity-administration access to be arranged externally.
        Enter continue at each checkpoint on stdin; EOF/cancellation fails safely.
        Cleanup uses independent per-resource budgets, never a fire-and-forget delete.
        Reserve the generated scope for this run: ARM root creation and empty-parent deletion
        are not atomic against concurrent writers. See README before authorizing live execution.
        Exit: 0 successful requested work, 1 runtime/cleanup failure, 2 invalid input.
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = CommandLine.Parse(args);
            if (command.Mode == "--help")
            {
                Console.WriteLine(Help);
                return 0;
            }
            var options = AppOptions.Load(command.ConfigPath);
            options.Validate();
            if (command.Mode == "--validate")
            {
                Console.WriteLine($"Configuration valid: {options.ServiceBusSku}, {options.Credential}. No Azure calls made.");
                return 0;
            }
            var plan = new ResourcePlan(options);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                plan.RunId,
                plan.Steps,
                options.ServiceBusSku,
                options.EnablePartitioning,
                options.PremiumCapacity,
                options.PremiumMessagingPartitions,
                InitialRule = new { Name = ResourcePlan.RuleName, Filter = ResourcePlan.SqlExpression },
                Cleanup = command.CleanupOwned,
                Routing = command.VerifyRouting
            }, RunReport.JsonOptions));
            if (command.Mode == "--plan")
                return 0;

            var reportPath = Path.GetFullPath($"management-run-{plan.RunId}.json");
            using (File.Open(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            Console.WriteLine($"Resource inventory: {reportPath}");
            void Persist(RunReport report)
            {
                var temporary = reportPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(report, RunReport.JsonOptions));
                File.Move(temporary, reportPath, overwrite: true);
            }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                var operations = new AzureManagementOperations(options, plan, options.CreateCredential());
                async Task BeforeCreate(ResourceStep step, CancellationToken token)
                {
                    if (step.Kind is not (ResourceKind.Namespace or ResourceKind.Queue))
                        return;
                    var parent = step.Kind == ResourceKind.Namespace ? plan.Steps[0] : plan.Steps[1];
                    Console.WriteLine($"Scope ready: {parent.Id}");
                    Console.WriteLine(step.Kind == ResourceKind.Namespace
                        ? "Arrange namespace-management permission at this resource group only (see README)."
                        : "Arrange entity-administration permission at this namespace only (see README).");
                    Console.WriteLine("No role is assigned by this sample. Enter continue after access has propagated:");
                    await ConfirmAccessAsync(Console.In, token);
                }
                var report = await ServiceBusManagementSample.RunAsync(plan, operations,
                    TimeSpan.FromSeconds(options.OperationTimeoutSeconds), TimeSpan.FromSeconds(options.CleanupTimeoutSeconds),
                    command.CleanupOwned, command.VerifyRouting, Persist, cancellation.Token, BeforeCreate);
                Console.WriteLine(JsonSerializer.Serialize(report, RunReport.JsonOptions));
                return report.Success ? 0 : 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }
        catch (SampleException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(SafeError.Describe(exception));
            return 1;
        }
    }

    public static async Task ConfirmAccessAsync(TextReader input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Console.In may implement ReadLineAsync synchronously, ignoring cancellation while blocked.
        if (await Task.Run(input.ReadLine, CancellationToken.None).WaitAsync(cancellationToken) != "continue")
            throw new SampleException("Creation was not confirmed after the scoped-access checkpoint.");
    }
}
