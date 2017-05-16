//   
//   Copyright © Microsoft Corporation, All Rights Reserved
// 
//   Licensed under the Apache License, Version 2.0 (the "License"); 
//   you may not use this file except in compliance with the License. 
//   You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0 
// 
//   THIS CODE IS PROVIDED ON AN *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
//   OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION
//   ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A
//   PARTICULAR PURPOSE, MERCHANTABILITY OR NON-INFRINGEMENT.
// 
//   See the Apache License, Version 2.0 for the specific language
//   governing permissions and limitations under the License. 

namespace MessagingSamples
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.ServiceBus;

    // IF YOU ARE JUST GETTING STARTED, 
    // THESE ARE NOT THE DROIDS YOU ARE LOOKING FOR
    // PLEASE REVIEW "Program.cs" IN THE SAMPLE PROJECT

    // This is a common entry point class for all samples that provides
    // the Main() method entry point called by the CLR. It loads the properties
    // stored in the "azure-msg-samples.properties" file from the user profile
    // and then allows override of the settings from environment variables.
    class AppEntryPoint
    {
        const string RootSampleSendKeyName = "rootsamplesend";
        const string RootSampleManageKeyName = "rootsamplemanage";
        const string BasicQueueName = "BasicQueue";
        const string PartitionedQueueName = "PartitionedQueue";
        const string DupdetectQueueName = "DupdetectQueue";
        const string BasicTopicName = "BasicTopic";
        const string SendKeyName = "samplesend";
        const string ReceiveKeyName = "samplelisten";
        const string SessionQueueName = "SessionQueue";
        const string BasicQueue2Name = "BasicQueue2";
        const string ManageKeyName = "samplemanage";
        static readonly string servicebusNamespace = "SERVICEBUS_NAMESPACE";
        static readonly string servicebusEntityPath = "SERVICEBUS_ENTITY_PATH";
        static readonly string servicebusFqdnSuffix = "SERVICEBUS_FQDN_SUFFIX";
        static readonly string servicebusSendKey = "SERVICEBUS_SEND_KEY";
        static readonly string servicebusListenKey = "SERVICEBUS_LISTEN_KEY";
        static readonly string servicebusManageKey = "SERVICEBUS_MANAGE_KEY";
        static readonly string samplePropertiesFileName = "azure-msg-config.properties";
#if STA
        [STAThread]
#endif

        static void Main(string[] args)
        {
            Run();
        }

        // [DebuggerStepThrough]
        static void Run()
        {
            var properties = new Dictionary<string, string>
            {
                {servicebusNamespace, null},
                {servicebusEntityPath, null},
                {servicebusFqdnSuffix, null},
                {servicebusSendKey, null},
                {servicebusListenKey, null},
                {servicebusManageKey, null}
            };

            // read the settings file created by the ./setup.ps1 file
            var settingsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                samplePropertiesFileName);
            if (File.Exists(settingsFile))
            {
                using (var fs = new StreamReader(settingsFile))
                {
                    while (!fs.EndOfStream)
                    {
                        var readLine = fs.ReadLine();
                        if (readLine != null)
                        {
                            var propl = readLine.Trim();
                            var cmt = propl.IndexOf('#');
                            if (cmt > -1)
                            {
                                propl = propl.Substring(0, cmt).Trim();
                            }
                            if (propl.Length > 0)
                            {
                                var propi = propl.IndexOf('=');
                                if (propi == -1)
                                {
                                    continue;
                                }
                                var propKey = propl.Substring(0, propi).Trim();
                                var propVal = propl.Substring(propi + 1).Trim();
                                if (properties.ContainsKey(propKey))
                                {
                                    properties[propKey] = propVal;
                                }
                            }
                        }
                    }
                }
            }

            // get overrides from the environment
            foreach (var prop in properties)
            {
                var env = Environment.GetEnvironmentVariable(prop.Key);
                if (env != null)
                {
                    properties[prop.Key] = env;
                }
            }

            var hostName = properties[servicebusNamespace] + "." + properties[servicebusFqdnSuffix];
            var rootUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, "/").ToString();
            var sbUri = new UriBuilder("sb", hostName, -1, "/").ToString();

            var program = Activator.CreateInstance(typeof(Program));

            if (program is IDynamicSample)
            {
                var token =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        RootSampleManageKeyName,
                        properties[servicebusManageKey])
                        .GetWebTokenAsync(rootUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
                ((IDynamicSample)program).Run(sbUri, token).GetAwaiter().GetResult();
            }
            else if (program is IDynamicSampleWithKeys)
            {
                ((IDynamicSampleWithKeys)program).Run(
                    sbUri,
                    RootSampleManageKeyName,
                    properties[servicebusManageKey],
                    RootSampleSendKeyName,
                    properties[servicebusSendKey],
                    RootSampleSendKeyName,
                    properties[servicebusListenKey]).GetAwaiter().GetResult();
            }
            else if (program is IBasicQueueSendReceiveSample)
            {
                var entityName = BasicQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IBasicQueueSendReceiveSample)program).Run(sbUri, entityName, sendToken, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IBasicQueueSendSample)
            {
                var entityName = BasicQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IBasicQueueSendSample)program).Run(sbUri, entityName, sendToken).GetAwaiter().GetResult();
            }
            else if (program is IBasicQueueReceiveSample)
            {
                var entityName = BasicQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IBasicQueueReceiveSample)program).Run(sbUri, entityName, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IPartitionedQueueSendReceiveSample)
            {
                var entityName = PartitionedQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IPartitionedQueueSendReceiveSample)program).Run(sbUri, entityName, sendToken, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IPartitionedQueueSendSample)
            {
                var entityName = PartitionedQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IPartitionedQueueSendSample)program).Run(sbUri, entityName, sendToken).GetAwaiter().GetResult();
            }
            else if (program is IPartitionedQueueReceiveSample)
            {
                var entityName = PartitionedQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IPartitionedQueueReceiveSample)program).Run(sbUri, entityName, receiveToken).GetAwaiter().GetResult();
            }
            else
                if (program is ISessionQueueSendReceiveSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, SessionQueueName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((ISessionQueueSendReceiveSample)program).Run(sbUri, SessionQueueName, sendToken, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is ISessionQueueSendSample)
            {
                var entityName = SessionQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((ISessionQueueSendSample)program).Run(sbUri, entityName, sendToken).GetAwaiter().GetResult();
            }
            else if (program is ISessionQueueReceiveSample)
            {
                var entityName = SessionQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((ISessionQueueReceiveSample)program).Run(sbUri, entityName, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IDupdetectQueueSendReceiveSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, DupdetectQueueName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IDupdetectQueueSendReceiveSample)program).Run(sbUri, DupdetectQueueName, sendToken, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IDupdetectQueueSendSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, DupdetectQueueName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IDupdetectQueueSendSample)program).Run(sbUri, DupdetectQueueName, sendToken).GetAwaiter().GetResult();
            }
            else if (program is IDupdetectQueueReceiveSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, DupdetectQueueName).ToString();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IDupdetectQueueReceiveSample)program).Run(sbUri, DupdetectQueueName, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IBasicTopicSendReceiveSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, BasicTopicName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IBasicTopicSendReceiveSample)program).Run(sbUri, BasicTopicName, sendToken, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IBasicTopicSendSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, BasicTopicName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IBasicTopicSendSample)program).Run(sbUri, BasicTopicName, sendToken).GetAwaiter().GetResult();
            }
            else if (program is IBasicTopicReceiveSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, BasicTopicName).ToString();
                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IBasicTopicReceiveSample)program).Run(sbUri, BasicTopicName, receiveToken).GetAwaiter().GetResult();
            }
            else if (program is IBasicTopicConnectionStringSample)
            {
                var connectionString =
                    ServiceBusConnectionStringBuilder.CreateUsingSharedAccessKey(
                        new Uri(rootUri),
                        RootSampleManageKeyName,
                        properties[servicebusManageKey]);

                ((IBasicTopicConnectionStringSample)program).Run(BasicTopicName, connectionString).GetAwaiter().GetResult();
            }

            else if (program is IConnectionStringSample)
            {
                var connectionString =
                    ServiceBusConnectionStringBuilder.CreateUsingSharedAccessKey(
                        new Uri(rootUri),
                        RootSampleManageKeyName,
                        properties[servicebusManageKey]);

                ((IConnectionStringSample)program).Run(connectionString).GetAwaiter().GetResult();
            }
            else if (program is IBasicQueueConnectionStringSample)
            {
                var connectionString =
                    ServiceBusConnectionStringBuilder.CreateUsingSharedAccessKey(
                        new Uri(rootUri),
                        RootSampleManageKeyName,
                        properties[servicebusManageKey]);

                ((IBasicQueueConnectionStringSample)program).Run(BasicQueueName, connectionString).GetAwaiter().GetResult();
            }
            else if (program is IDualQueueSendReceiveSample)
            {
                var entityName = BasicQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                var entity2Name = BasicQueue2Name;
                var entity2Uri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entity2Name).ToString();

                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entity2Uri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IDualQueueSendReceiveSample)program).Run(sbUri, entityName, sendToken, entity2Name, receiveToken).GetAwaiter().GetResult();
            }

            else if (program is IDualQueueSampleWithFullRights)
            {
                var entityName = BasicQueueName;
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entityName).ToString();

                var token1 =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ManageKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                var entity2Name = BasicQueue2Name;
                var entity2Uri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entity2Name).ToString();

                var token2 =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ManageKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entity2Uri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IDualQueueSampleWithFullRights)program).Run(sbUri, entityName, token1, entity2Name, token2).GetAwaiter().GetResult();
            }

            else if (program is IDualQueueSendReceiveFlipsideSample)
            {
                var entityUri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, BasicQueue2Name).ToString();

                var sendToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        SendKeyName,
                        properties[servicebusSendKey])
                        .GetWebTokenAsync(entityUri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                var entity2Name = BasicQueueName;
                var entity2Uri = new UriBuilder(Uri.UriSchemeHttp, hostName, -1, entity2Name).ToString();

                var receiveToken =
                    TokenProvider.CreateSharedAccessSignatureTokenProvider(
                        ReceiveKeyName,
                        properties[servicebusListenKey])
                        .GetWebTokenAsync(entity2Uri, string.Empty, true, TimeSpan.FromHours(1)).GetAwaiter().GetResult();

                ((IDualQueueSendReceiveFlipsideSample)program).Run(sbUri, BasicQueue2Name, sendToken, entity2Name, receiveToken).
                    GetAwaiter().
                    GetResult();
            }
            else if (program is IBasicQueueSampleWithKeys)
            {
                ((IBasicQueueSampleWithKeys)program).Run(
                    sbUri,
                    BasicQueueName,
                    SendKeyName,
                    properties[servicebusSendKey],
                    ReceiveKeyName,
                    properties[servicebusListenKey]).GetAwaiter().GetResult();
                ;
            }
            else if (program is IDualBasicQueueSampleWithKeys)
            {
                ((IDualBasicQueueSampleWithKeys)program).Run(
                    sbUri,
                    BasicQueueName,
                    BasicQueue2Name,
                    SendKeyName,
                    properties[servicebusSendKey],
                    ReceiveKeyName,
                    properties[servicebusListenKey]).GetAwaiter().GetResult();
            }
        }
    }

    interface IBasicQueueSendReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken);
    }

    interface IBasicQueueSampleWithKeys
    {
        Task Run(
            string namespaceAddress,
            string basicQueueName,
            string sendKeyName,
            string sendKey,
            string receiveKeyName,
            string receiveKey);
    }

    interface IBasicQueueSendSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken);
    }

    interface IBasicQueueReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string receiveToken);
    }

    interface IPartitionedQueueSendReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken);
    }

    interface IPartitionedQueueSendSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken);
    }

    interface IPartitionedQueueReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string receiveToken);
    }

    interface ISessionQueueSendReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken);
    }

    interface ISessionQueueSendSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken);
    }

    interface ISessionQueueReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string receiveToken);
    }

    interface IBasicTopicSendReceiveSample
    {
        Task Run(string namespaceAddress, string TopicName, string sendToken, string receiveToken);
    }

    interface IBasicTopicSendSample
    {
        Task Run(string namespaceAddress, string TopicName, string sendToken);
    }

    interface IBasicTopicReceiveSample
    {
        Task Run(string namespaceAddress, string TopicName, string receiveToken);
    }


    interface IPartitionedTopicSendReceiveSample
    {
        Task Run(string namespaceAddress, string TopicName, string sendToken, string receiveToken);
    }

    interface IPartitionedTopicSendSample
    {
        Task Run(string namespaceAddress, string TopicName, string sendToken);
    }

    interface IPartitionedTopicReceiveSample
    {
        Task Run(string namespaceAddress, string TopicName, string receiveToken);
    }


    interface IDupdetectQueueSendReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken, string receiveToken);
    }

    interface IDupdetectQueueSendSample
    {
        Task Run(string namespaceAddress, string queueName, string sendToken);
    }

    interface IDupdetectQueueReceiveSample
    {
        Task Run(string namespaceAddress, string queueName, string receiveToken);
    }

    interface IConnectionStringSample
    {
        Task Run(string connectionString);
    }

    interface IBasicQueueConnectionStringSample
    {
        Task Run(string queueName, string connectionString);
    }

    interface IBasicTopicConnectionStringSample
    {
        Task Run(string queueName, string connectionString);
    }

    interface IDynamicSample
    {
        Task Run(string namespaceAddress, string manageToken);
    }

    interface IDynamicSampleWithKeys
    {
        Task Run(
            string namespaceAddress,
            string manageKeyName,
            string manageKey,
            string sendKeyName,
            string sendKey,
            string receiveKeyName,
            string receiveKey);
    }

    interface IDualQueueSendReceiveSample
    {
        Task Run(string namespaceAddress, string sendQueueName, string sendToken, string receiveQueueName, string receiveToken);
    }

    interface IDualQueueSampleWithFullRights
    {
        Task Run(string namespaceAddress, string queueName1, string manageToken1, string queueName2, string manageToken2);
    }

    interface IDualQueueSendReceiveFlipsideSample
    {
        Task Run(string namespaceAddress, string sendQueueName, string sendToken, string receiveQueueName, string receiveToken);
    }

    interface IDualBasicQueueSampleWithKeys
    {
        Task Run(
            string namespaceAddress,
            string basicQueueName,
            string basicQueue2Name,
            string sendKeyName,
            string sendKey,
            string receiveKeyName,
            string receiveKey);
    }
}
