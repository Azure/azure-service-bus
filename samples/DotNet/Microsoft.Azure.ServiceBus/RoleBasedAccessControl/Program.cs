//   
//   Copyright © Microsoft Corporation, All Rights Reserved
// 
//   Licensed under the Apache License, Version 2.0 (the "License"); 
//   you may not use this file except in compliance with the License. 
//   You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0 
// 
//   THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//   OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
//   ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
//   PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
// 
//   See the Apache License, Version 2.0 for the specific language
//   governing permissions and limitations under the License. 

namespace MessagingSamples
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.Identity.Client;
    using Microsoft.Azure.ServiceBus.Primitives;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.InteropExtensions;
    using System.Threading;

    public class Program
    {
        QueueClient sendClient;
        QueueClient receiveClient;

        static readonly string TenantId = ConfigurationManager.AppSettings["tenantId"];
        static readonly string ClientId = ConfigurationManager.AppSettings["clientId"];
        static readonly string ServiceBusNamespace = ConfigurationManager.AppSettings["serviceBusNamespaceFQDN"];
        static readonly string QueueName = ConfigurationManager.AppSettings["queueName"];

        public async Task Run()
        {
            Console.WriteLine("Pick a scenario to run and hit ENTER:");
            Console.WriteLine("1) ManagedServiceIdentity (must run in an Azure VM or Web Job)");
            Console.WriteLine("2) Interactive User Login");
            Console.WriteLine("3) Client secret Login");
            Console.WriteLine("4) Client credential X.509 certificate");

            int option;
            ConsoleKeyInfo cki = Console.ReadKey();
            if (int.TryParse(cki.KeyChar.ToString(), out option))
            {
                switch (option)
                {
                    case 1:
                        await ManagedServiceIdentityScenario();
                        break;
                    case 2:
                        await UserInteractiveLoginScenario();
                        break;
                    case 3:
                        await ClientCredentialsScenario();
                        break;
                    case 4:
                        await ClientCredentialsCertScenario();
                        break;
                }
            }
        }

        async Task ManagedServiceIdentityScenario()
        {
            var msiTokenProvider = TokenProvider.CreateManagedIdentityTokenProvider();
            var qc = new QueueClient(new Uri($"sb://{ServiceBusNamespace}/").ToString(), QueueName, msiTokenProvider);

            await SendReceiveAsync(qc);

        }

        async Task SendReceiveAsync(QueueClient qc)
        {

            this.receiveClient = qc;
            this.sendClient = qc;

            // send a set of messages
            var sendTask = this.SendMessagesAsync();
            // the receive those messages
            var cts = new CancellationTokenSource();
            var receiveTask = this.ReceiveMessagesAsync(cts.Token);

            // wait until both tasks are complete
            await Task.WhenAll(
                // run 20 seconds, then cancel the receive loop
                Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith((t) => cts.Cancel()),
                // wait for the send task
                sendTask,
                // wait for the receive task
                receiveTask);
        }

        Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            var doneReceiving = new TaskCompletionSource<bool>();

            // close the receiver and factory when the CancellationToken fires 
            cancellationToken.Register(
                async () =>
                {
                    await this.receiveClient.CloseAsync();
                    doneReceiving.SetResult(true);
                });

            // register the OnMessageAsync callback
            this.receiveClient.RegisterMessageHandler(
                async (message, token) =>
                {
                    if (message.Label != null &&
                        message.ContentType != null &&
                        message.Label.Equals("Scientist", StringComparison.InvariantCultureIgnoreCase) &&
                        message.ContentType.Equals("application/json", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var body = message.Body;

                        dynamic scientist = JsonConvert.DeserializeObject(Encoding.UTF8.GetString(body));

                        lock (Console.Out)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine(
                                "\t\t\t\tMessage received: \n\t\t\t\t\t\tMessageId = {0}, \n\t\t\t\t\t\tSequenceNumber = {1}, \n\t\t\t\t\t\tEnqueuedTimeUtc = {2}," +
                                "\n\t\t\t\t\t\tExpiresAtUtc = {5}, \n\t\t\t\t\t\tContentType = \"{3}\", \n\t\t\t\t\t\tSize = {4},  \n\t\t\t\t\t\tContent: [ firstName = {6}, name = {7} ]",
                                message.MessageId,
                                message.SystemProperties.SequenceNumber,
                                message.SystemProperties.EnqueuedTimeUtc,
                                message.ContentType,
                                message.Size,
                                message.ExpiresAtUtc,
                                scientist.firstName,
                                scientist.name);
                            Console.ResetColor();
                        }
                    }
                    await this.receiveClient.CompleteAsync(message.SystemProperties.LockToken);
                },
                new MessageHandlerOptions((eventargs) => { return Task.CompletedTask; })
                {
                    AutoComplete = false,
                    MaxConcurrentCalls = 1
                });

            return doneReceiving.Task;
        }

        async Task SendMessagesAsync()
        {
            dynamic data = new[]
            {
                new {name = "Einstein", firstName = "Albert"},
                new {name = "Heisenberg", firstName = "Werner"},
                new {name = "Curie", firstName = "Marie"},
                new {name = "Hawking", firstName = "Steven"},
                new {name = "Newton", firstName = "Isaac"},
                new {name = "Bohr", firstName = "Niels"},
                new {name = "Faraday", firstName = "Michael"},
                new {name = "Galilei", firstName = "Galileo"},
                new {name = "Kepler", firstName = "Johannes"},
                new {name = "Kopernikus", firstName = "Nikolaus"}
            };


            for (int i = 0; i < data.Length; i++)
            {
                var message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data[i])))
                {
                    ContentType = "application/json",
                    Label = "Scientist",
                    MessageId = i.ToString(),
                    TimeToLive = TimeSpan.FromMinutes(2)
                };

                await this.sendClient.SendAsync(message);
                lock (Console.Out)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Message sent: Id = {0}", message.MessageId);
                    Console.ResetColor();
                }
            }
        }

        async Task UserInteractiveLoginScenario()
        {
            var aadTokenProvider = TokenProvider.CreateAzureActiveDirectoryTokenProvider(async (audience, authority, state) =>
            {
                IPublicClientApplication app = PublicClientApplicationBuilder.Create(ClientId)
                            .WithRedirectUri(ConfigurationManager.AppSettings["redirectURI"])
                            .Build();

                var serviceBusAudience = new Uri("https://servicebus.azure.net");

                AuthenticationResult authResult = await app.AcquireTokenInteractive(new string[] { $"{serviceBusAudience}/.default" }).ExecuteAsync();

                return authResult.AccessToken;
            }, $"https://login.windows.net/{TenantId}");

            var qc = new QueueClient(new Uri($"sb://{ServiceBusNamespace}/").ToString(), QueueName, aadTokenProvider);

            await SendReceiveAsync(qc);

        }

        async Task ClientCredentialsScenario()
        {
            TokenProvider aadTokenProvider = TokenProvider.CreateAzureActiveDirectoryTokenProvider
                (async (audience, authority, state) =>
            {
                IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(ClientId)
                                .WithAuthority(authority)
                                .WithClientSecret(ConfigurationManager.AppSettings["clientSecret"])
                                .Build();

                var serviceBusAudience = new Uri("https://servicebus.azure.net");

                AuthenticationResult authResult = await app.AcquireTokenForClient(new string[] { $"{serviceBusAudience}/.default" }).ExecuteAsync();
                return authResult.AccessToken;

            }, $"https://login.windows.net/{TenantId}");

            var qc = new QueueClient(new Uri($"sb://{ServiceBusNamespace}/").ToString(), QueueName, aadTokenProvider);

            await SendReceiveAsync(qc);
        }

        X509Certificate2 GetCertificate()
        {
            List<StoreLocation> locations = new List<StoreLocation>
                {
                    StoreLocation.CurrentUser,
                    StoreLocation.LocalMachine
                };

            foreach (var location in locations)
            {
                X509Store store = new X509Store(StoreName.My, location);
                try
                {
                    store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    X509Certificate2Collection certificates = store.Certificates.Find(
                        X509FindType.FindByThumbprint, ConfigurationManager.AppSettings["thumbPrint"], true);
                    if (certificates.Count >= 1)
                    {
                        return certificates[0];
                    }
                }
                finally
                {
                    store.Close();
                }
            }

            throw new ArgumentException($"A Certificate with Thumbprint '{ConfigurationManager.AppSettings["thumbPrint"]}' could not be located.");
        }

        async Task ClientCredentialsCertScenario()
        {
            X509Certificate2 certificate = GetCertificate();

            var aadTokenProvider = TokenProvider.CreateAzureActiveDirectoryTokenProvider(async (audience, authority, state) =>
            {
                IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(ClientId)
                                .WithAuthority(authority)
                                .WithCertificate(certificate)
                                .Build();

                var serviceBusAudience = new Uri("https://servicebus.azure.net");

                AuthenticationResult authResult = await app.AcquireTokenForClient(new string[] { $"{serviceBusAudience}/.default" }).ExecuteAsync();
                return authResult.AccessToken;

            }, $"https://login.windows.net/{TenantId}");

            var qc = new QueueClient(new Uri($"sb://{ServiceBusNamespace}/").ToString(), QueueName, aadTokenProvider);

            await SendReceiveAsync(qc);

        }

        public static async Task<int> Main(string[] args)
        {
            try
            {
                var app = new Program();
                await app.Run();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.ReadLine();
                return 1;
            }
            return 0;
        }
    }
}

