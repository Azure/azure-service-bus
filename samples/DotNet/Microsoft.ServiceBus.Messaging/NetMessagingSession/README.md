#WCF NetMessagingSession Sample

This sample illustrates how to use the WCF NetMessagingBinding for Service Bus with sessions. 

## Prerequisites and Setup

All samples share the same basic setup, explained in the main [README](../README.md) file. There are no extra setup steps for this sample.
The application entry points are in [Main.cs](../common/Main.md), which is shared across all samples. The sample implementations generally
reside in *Program.cs*, starting with *Run()*.

You can build the sample from the command line with the [build.bat](build.bat) or [build.ps1](build.ps1) scripts. This assumes that you
have the .NET Build tools in the path. You can also open up the [TopicFilters.sln](TopicFilters.sln) solution file with Visual Studio and build.
With either option, the NuGet package manager should download and install the **WindowsAzure.ServiceBus** package containing the
Microsoft.ServiceBus.dll assembly, including dependencies.



##The Sample
  

The sample shows the use of the WCF service model to accomplish session-based communication via a Service Bus queue. The sample demonstrates this using an sequence service scenario. In the scenario, different customers send sequences to the sequence service. All the sequence items are grouped together in a single session using a customer Id. The service creates a new instance for each session to process all related messages. The service prints out the total items sequenceed by the customer. Once the service has processed all the messages it closes the instance. 

SequenceService 

The sample prompts for service namespace credentials for the purpose of creating and deleting the queues. The credentials are used to authenticate with the Access Control service, and acquire an access token that proves to the Service Bus infrastructure that the client is authorized to create or delete the queue. The sender and service use the credentials defined in the config file. 

The sender and receiver use NetMessagingBinding which is defined in their respective App.config files. NetMessagingBinding uses BinaryMessageEncoding as its encoder and NetMessagingTransportBindingElement as its transport. The TransportSettings property, which is a part of the transport element, represents the runtime factory used by the Service Bus. An extension section is required be added to the configuration file in sequence to use Service Bus components with WCF.

In addition to the binding, both the configuration files have a <behaviors> section which defines TransportClientEndpointBehavior. Service Bus credentials are passed to the client and service via this endpoint behavior. 



App.Config - Config Extensions and Binding

                                                <extensions>
                                                    <bindingElementExtensions>
                                                        <add name="netMessagingTransport" type="Microsoft.ServiceBus.Messaging.Configuration.NetMessagingTransportExtensionElement, 
                                                                    Microsoft.ServiceBus, Culture=neutral, PublicKeyToken=31bf3856ad364e35" />
                                                    </bindingElementExtensions>
                                                    <bindingExtensions>
                                                        <add name="netMessagingBinding" type="Microsoft.ServiceBus.Messaging.Configuration.NetMessagingBindingCollectionElement, 
                                                                    Microsoft.ServiceBus, Culture=neutral, PublicKeyToken=31bf3856ad364e35" />
                                                    </bindingExtensions>
                                                </extensions>

                                                <bindings>
                                                  <netMessagingBinding>
                                                    <binding name="messagingBinding" sendTimeout="00:03:00" receiveTimeout="00:03:00" openTimeout="00:03:00" closeTimeout="00:03:00">
                                                    </binding>
                                                  </netMessagingBinding>
                                                </bindings>
 
 
In the config files, NetMessagingBinding exposes SessionIdleTimeOut, PrefetchCount, and BatchFlushInterval. The SessionIdleTimeOut property allows the Service Bus dispatcher to close an instance of the service if it is idle for more than the specified time interval. The default value for SessionIdleTimeout is 1 minute. BatchFlushInterval is responsible for implicitly batching the send operation or the complete operation. The Service Bus implicitly batches the send operation from sender or complete operation from receiver for the specified time to avoid multiple round-trips. The default value of BatchFlushInterval is 20 milliseconds.

The client configuration file defines the client object and the service configuration file defines the service object.



App.Config - Client and Service Definition

                                                    <client>
                                                      <endpoint name="sequenceSendClient"
                                                                address="[Enter Endpoint address]"
                                                                contract="Microsoft.Samples.SessionMessages.ISequenceServiceContract"
                                                                binding="netMessagingBinding" bindingConfiguration="messagingBinding"
                                                                behaviorConfiguration="securityBehavior"/>
                                                    </client>

                                                    <services>
                                                        <service name="Microsoft.Samples.SessionMessages.SequenceService">
                                                        <endpoint name="SessionServiceEndPoint"
                                                                    address="[Enter Endpoint address]"
                                                                    binding="netMessagingBinding" bindingConfiguration="messagingBinding"
                                                                    contract="Microsoft.Samples.SessionMessages.ISequenceServiceContractSessionful"
                                                                    behaviorConfiguration="securityBehavior" />
                                                        </service>
                                                    </services>
 
 
The client configuration file defines the client object and the service configuration file defines the service object.
 
Credentials


The sample obtains user credentials and creates a NamespaceManager object. This entity holds the credentials and is used for all messaging management operations - in this case, to create and delete queues.



C#

                                                public static void GetUserCredentials()
                                                {
                                                    // User namespace
                                                    Console.WriteLine("Please provide the namespace to use:");
                                                    serviceBusNamespace = Console.ReadLine();

                                                    // Issuer name
                                                    Console.WriteLine("Please provide the Issuer name to use:");
                                                    serviceBusIssuerName = Console.ReadLine();

                                                    // Issuer key
                                                    Console.WriteLine("Please provide the Issuer key to use:");
                                                    serviceBusIssuerKey = Console.ReadLine();
                                                }

                                                // Create the NamespaceManager for management operations (queue)
                                                static void CreateNamespaceManager()
                                                {
                                                    // Create SharedSecretCredential object for access control service
                                                    TokenProvider credentials = TokenProvider.CreateSharedSecretTokenProvider(serviceBusIssuerName, serviceBusIssuerKey);

                                                    // Create the management Uri
                                                    Uri managementUri = ServiceBusEnvironment.CreateServiceUri("sb", serviceBusNamespace, string.Empty);
                                                    namespaceManager = new NamespaceManager(managementUri, credentials);
                                                }

                                                // Create the entity (queue)
                                                static QueueDescription CreateQueue(bool session)
                                                {
                                                    QueueDescription queueDescription = new QueueDescription(sequenceQueueName) { RequiresSession = session };           

                                                    // Try deleting the queue before creation. Ignore exception if queue does not exist.
                                                    try
                                                    {
                                                        namespaceManager.DeleteQueue(sequenceQueueName);
                                                    }
                                                    catch (MessagingEntityNotFoundException)
                                                    {
                                                    }

                                                    return namespaceManager.CreateQueue(queueDescription);
                                                }
 
 
The preceding code prompts for the issuer credential and then constructs the listening URI using that information. The static ServiceBusEnvironment.CreateServiceUri function is provided to help construct the URI with the correct format and domain name. It is strongly recommended that you use this function instead of building the URI from scratch because the URI construction logic and format might change in future releases. At present, the resulting URI is scheme://<service-namespace>.servicebus.windows.net/. 

The CreateNamespaceManager() function creates the object to perform management operations, in this case creating and deleting queues. Both ‘https’ and ‘sb’ Uri schemes are allowed as a part of service Uri.

The CreateQueue(bool session) function creates a queue with the RequireSession property set as per the argument passed.
 
Data Contract


The sample uses an SequenceItem data contract to communicate between client and service. This data contract has two data members: ProductId and Quantity.



C#

                                                [DataContract(Name="SequenceDataContract", Namespace="Microsoft.Samples.SessionMessages")]
                                                public class SequenceItem
                                                {
                                                    [DataMember]
                                                    public string ProductId;

                                                    [DataMember]
                                                    public int Quantity;

                                                    public SequenceItem(string productId)
                                                        : this(productId, 1)
                                                    {
                                                    }

                                                    public SequenceItem(string productId, int quantity)
                                                    {
                                                        this.ProductId = productId;
                                                        this.Quantity = quantity;
                                                    }
                                                }
 
  
Sender


The Service Bus only supports IOutputChannel for sending messages using NetMessagingBinding. To accomplish sessionful communication over NetMessagingBinding the BrokeredMessageProperty.SessionId must be set to the the desired session value. All the messages with the same SessionId are grouped together in a single session. This property is required to be set for session-based communication and is optional for non-session communication. The lifetime of a session is based on the SessionIdleTimeout property as discussed above.

In this sample the clients or customers create sequences and send it to the sequence service. The sequence message that is sent has the SessionId property set to the customer Id. The client is defined in its App.config file.



C#

                                                static void Main(string[] args)
                                                {
                                                    ParseArgs(args);

                                                    // Send messages to queue which does not require session
                                                    Console.Title = "Sequence Client";

                                                    // Create sender to Sequence Service
                                                    ChannelFactory<ISequenceServiceContract> sendChannelFactory = new ChannelFactory<ISequenceServiceContract>(SampleManager.SequenceSendClientConfigName);
                                                    ISequenceServiceContract clientChannel = sendChannelFactory.CreateChannel();
                                                    ((IChannel)clientChannel).Open();

                                                    // Send messages
                                                    sequenceQuantity = new Random().Next(10, 30);
                                                    Console.WriteLine("Sending {0} messages to {1}...", sequenceQuantity, SampleManager.SequenceQueueName);
                                                    PlaceSequence(clientChannel);

                                                    // Close sender
                                                    ((IChannel)clientChannel).Close();
                                                    sendChannelFactory.Close();

                                                    Console.WriteLine("\nSender complete.");
                                                    Console.WriteLine("\nPress [Enter] to exit.");
                                                    Console.ReadLine();
                                                }

                                                static void PlaceSequence(ISequenceServiceContract clientChannel)
                                                {
                                                    // Send messages to queue which requires session:
                                                    for(int i = 0; i < sequenceQuantity; i++)
                                                    {
                                                        using (OperationContextScope scope = new OperationContextScope((IContextChannel)clientChannel))
                                                        {
                                                            SequenceItem sequenceItem = RandomizeSequence();

                                                            // Assign the session name
                                                            BrokeredMessageProperty property = new BrokeredMessageProperty();

                                                            // Correlating ServiceBus SessionId to ContextId 
                                                            property.SessionId = customerId;

                                                            // Add BrokeredMessageProperty to the OutgoingMessageProperties bag to pass on the session information 
                                                            OperationContext.Current.OutgoingMessageProperties.Add(BrokeredMessageProperty.Name, property);
                                                            clientChannel.Sequence(sequenceItem);
                                                            SampleManager.OutputMessageInfo("Sequence", string.Format("{0} [{1}]", sequenceItem.ProductId, sequenceItem.Quantity), customerId);
                                                            Thread.Sleep(200);
                                                        }
                                                    }
                                                }

                                                private static SequenceItem RandomizeSequence()
                                                {
                                                    // Generating a random sequence
                                                    string productId = SampleManager.Products[new Random().Next(0, 6)];
                                                    int quantity = new Random().Next(1, 100);
                                                    return new SequenceItem(productId, quantity);
                                                }

                                                static void ParseArgs(string[] args)
                                                {
                                                    if (args.Length != 1)
                                                    {
                                                        // Customer Id is needed to identify the sender
                                                        customerId = new Random().Next(1, 7).ToString();
                                                    }
                                                    else
                                                    {
                                                        customerId = args[0];
                                                    }
                                                }
 
  
Service


The sample illustrates an sequence service as described above. The SequenceService implements ISequenceServiceContractSessionful operation contract which implements ISequenceServiceContract. Because the Service Bus does not support IOutputSessionChannel, all senders sending messages to session-based queues must use a contract which does not enforce SessionMode.Required. However, Service Bus supports IInputSessionChannel and so the service implements the sessionful contract. The operation attribute ReceiveContextEnabled is set with manual control set to true. This requires an explicit ReceiveContext.Complete operation to be performed for every message received. The service has ServiceBehavior.InstanceContextMode set to per-session. The ServiceHost will create a new instance every time a new session is available in the queue. The life-time of the instance is controlled by setting the SessionIdleTimeout property of the binding.

Note that NetMessagingBinding only supports one-way communication. Therefore, OperationContract must explicitly set the attribute IsOneWay to true.

In the sample, the service collects all the items in a single session and then displays the total at the end. The service is defined in its App.config file.



C#

                                                // ServiceBus does not support IOutputSessionChannel.
                                                // All senders sending messages to sessionful queue must use a contract which does not enforce SessionMode.Required.
                                                // Sessionful messages are sent by setting the SessionId property of the BrokeredMessageProperty object.
                                                [ServiceContract]
                                                public interface ISequenceServiceContract
                                                {
                                                    [OperationContract(IsOneWay = true)]
                                                    [ReceiveContextEnabled(ManualControl = true)]
                                                    void Sequence(SequenceItem sequenceItem);
                                                }

                                                // ServiceBus supports both IInputChannel and IInputSessionChannel. 
                                                // A sessionful service listening to a sessionful queue must have SessionMode.Required in its contract.
                                                [ServiceContract(SessionMode = SessionMode.Required)]
                                                public interface ISequenceServiceContractSessionful : ISequenceServiceContract
                                                {
                                                }

                                                [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession, ConcurrencyMode = ConcurrencyMode.Single)]
                                                public class SequenceService : ISequenceServiceContractSessionful, IDisposable
                                                {
                                                    #region Service variables
                                                    List<SequenceItem> sequenceItems;
                                                    int messageCounter;
                                                    string sessionId;
                                                    #endregion

                                                    public SequenceService()
                                                    {
                                                        this.messageCounter = 0;
                                                        this.sequenceItems = new List<SequenceItem>();
                                                        this.sessionId = string.Empty;
                                                    }

                                                    public void Dispose()
                                                    {
                                                        SampleManager.OutputMessageInfo("Process Sequence", string.Format("Finished processing sequence. Total {0} items", sequenceItems.Count), this.sessionId);
                                                    }

                                                    public void Sequence(SequenceItem sequenceItem)
                                                    {
                                                        // Get the BrokeredMessageProperty from OperationContext
                                                        var incomingProperties = OperationContext.Current.IncomingMessageProperties;
                                                        BrokeredMessageProperty property = (BrokeredMessageProperty)incomingProperties[BrokeredMessageProperty.Name];
            
                                                        // Get the current ServiceBus SessionId
                                                        if (this.sessionId == string.Empty)
                                                        {
                                                            this.sessionId = property.SessionId;
                                                        }

                                                        // Print message
                                                        if (this.messageCounter == 0)
                                                        {
                                                            SampleManager.OutputMessageInfo("Process Sequence", "Started processing sequence.", this.sessionId);
                                                        }

                                                        //Complete the Message
                                                        ReceiveContext receiveContext;
                                                        if (ReceiveContext.TryGet(incomingProperties, out receiveContext))
                                                        {
                                                            receiveContext.Complete(TimeSpan.FromSeconds(10.0d));
                                                            this.sequenceItems.Add(sequenceItem);
                                                            this.messageCounter++;
                                                        }
                                                        else
                                                        {
                                                            throw new InvalidOperationException("Receiver is in peek lock mode but receive context is not available!");
                                                        }
                                                    }
                                                }
 
 
The service subscribes to the faulted event. This will notify the service if any fault occurred during execution and can be handled properly. In the sample, the service is aborted on fault. The service also implements a ErrorServiceBehavior for handling exceptions during execution.



C#

                                                static void Main(string[] args)
                                                {
                                                    // Create MessageReceiver for queue which requires session
                                                    Console.Title = "Sequence Service";
                                                    Console.WriteLine("Ready to receive messages from {0}...", SampleManager.SequenceQueueName);

                                                    // Creating the service host object as defined in config
                                                    ServiceHost serviceHost = new ServiceHost(typeof(SequenceService));

                                                    // Add ErrorServiceBehavior for handling errors encounter by servicehost during execution.
                                                    serviceHost.Description.Behaviors.Add(new ErrorServiceBehavior());

                                                    // Subscribe to the faulted event.
                                                    serviceHost.Faulted += new EventHandler(serviceHost_Faulted);

                                                    // Start service
                                                    serviceHost.Open();

                                                    Console.WriteLine("\nPress [Enter] to exit.");
                                                    Console.ReadLine();

                                                    // Close the service
                                                    serviceHost.Close();
                                                }

                                                static void serviceHost_Faulted(object sender, EventArgs e)
                                                {
                                                    Console.WriteLine("Fault occured. Aborting the service host object ...");
                                                    ((ServiceHost)sender).Abort();
                                                }
 
 
The service also implements an ErrorServiceBehavior for unhandled exceptions during service execution. ErrorServiceBehavior is a service behavior which adds an IErrorHandler object to the dispatcher. This object simply prints out all the exceptions except CommunicationException.



C#

                                                public class ErrorHandler: IErrorHandler
                                                {
                                                    public bool HandleError(Exception error)
                                                    {
                                                        if (!error.GetType().Equals(typeof(CommunicationException)))
                                                        {
                                                            // Handle the exception as required by the application
                                                            Console.WriteLine("Service encountered an exception.");
                                                            Console.WriteLine(error.ToString());
                                                        }

                                                        return true;
                                                    }

                                                    public void ProvideFault(Exception error, MessageVersion version, ref Message fault)
                                                    {
                                                    }
                                                }

                                                public class ErrorServiceBehavior : IServiceBehavior
                                                {
                                                    public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, 
                                                        Collection<ServiceEndpoint> endpoints, BindingParameterCollection bindingParameters)
                                                    {
                                                    }

                                                    public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
                                                    {
                                                        foreach (ChannelDispatcher dispatcher in serviceHostBase.ChannelDispatchers)
                                                        {
                                                            dispatcher.ErrorHandlers.Add(new ErrorHandler());
                                                        }
                                                    }

                                                    public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
                                                    {
                                                    }
                                                }
 
  
Running the Sample


To run the sample, build the solution in Visual Studio or from the command line, then run the executable ‘SampleManager.exe’. The program prompts for your Service Bus namespace and the issuer credentials. For the issuer secret, be sure to use the Default Issuer Key value (typically "owner") from the Azure portal, rather than one of the management keys. 

Expected Output - Sample Manager 


                                             Please provide the namespace to use: <Service Namespace>
                                             Please provide the Issuer name to use: <Issuer Name>
                                             Please provide the Issuer key to use: <Issuer Key>
                                             Creating Queues...
                                             Created SequenceQueue, Queue.RequiresSession = True                                        

                                             Launching senders and receivers...                                         

                                             Press [Enter] to exit
 

Expected Output – Sequence Client [ContextId 0] 


                                             Sending 7 messages to SequenceQueue...
                                             Sequence: Product5 [81] - ContextId 0.
                                             Sequence: Product6 [84] - ContextId 0.
                                             Sequence: Product1 [9] - ContextId 0.
                                             Sequence: Product3 [34] - ContextId 0.
                                             Sequence: Product4 [58] - ContextId 0.
                                             Sequence: Product5 [83] - ContextId 0.
                                             Sequence: Product1 [8] - ContextId 0.                                        

                                             Sender complete.                                        

                                             Press [Enter] to exit.
 

Expected Output – Sequence Client [ContextId 1] 


                                             Sending 8 messages to SequenceQueue...
                                             Sequence: Product5 [67] - ContextId 1.
                                             Sequence: Product6 [92] - ContextId 1.
                                             Sequence: Product2 [17] - ContextId 1.
                                             Sequence: Product5 [80] - ContextId 1.
                                             Sequence: Product1 [6] - ContextId 1.
                                             Sequence: Product2 [30] - ContextId 1.
                                             Sequence: Product4 [55] - ContextId 1.
                                             Sequence: Product5 [79] - ContextId 1.                                        

                                             Sender complete.                                        

                                             Press [Enter] to exit.
 

Expected Output - Sequence Service 


                                             Ready to receive messages from SequenceQueue...                                       

                                             Press [Enter] to exit.
                                             Process Sequence: Started processing sequence. - ContextId 1.
                                             Process Sequence: Started processing sequence. - ContextId 0.
                                             Process Sequence: Finished processing sequence. Total 8 items - ContextId 1.
                                             Process Sequence: Finished processing sequence. Total 7 items - ContextId 0.
 
 


Did you find this information useful?  Please send your suggestions and comments about the documentation.  
 
