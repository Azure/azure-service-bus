

#Message Sample Using WCF Services  


This sample demonstrates how to use the Azure Service Bus using the WCF service model.

The sample shows the use of the WCF service model to perform non-session communication via a Service Bus queue. The sample demonstrates this using a Ping service scenario. In this scenario, different senders send messages to the Ping service. All the messages are processed by the service. The service creates only one instance and processes all the messages in the same instance.

##OrderService

The sample prompts for service namespace credentials for the purpose of creating and deleting the queues. The credentials are used to authenticate with the Access Control service, and acquire an access token that proves to the Service Bus infrastructure that the client is authorized to create or delete the queue. The sender and service use the credentials defined in the config file. 

##Prerequisites


If you haven't already done so, please read the release notes document that explains how to sign up for a Azure account and how to configure your environment. 
 
##Configuring the Sample


When the solution is opened in Visual Studio, update the <behaviors> and <client> sections in the App.config file of the PingClient project. Also, update the <behaviors> and <services> sections in the App.config file of the PingService project.

The value for ‘issuerSecret’ should be available upon signup for a Azure account and upon configuring your environment. Please read the release notes for details.

The value for ‘address’ in the <client> and <services> sections is a Service Bus Uri that points to the queue entity. The Uri should be of type sb://<ServiceBus Namespace>.servicebus.windows.net/PingQueue where the ‘PingQueue’ is the entity name. Note that the Uri scheme ‘sb’ is mandatory for all runtime operations such as send/receive.



PingClient App.Config

```XML
            <behaviors>
                <endpointBehaviors>
                <behavior name="securityBehavior">
                    <transportClientEndpointBehavior>
                    <tokenProvider>
                        <sharedSecret issuerName="owner" issuerSecret="[Issuer key]" />
                    </tokenProvider>
                    </transportClientEndpointBehavior>
                </behavior>
                </endpointBehaviors>
            </behaviors>
            
            <client>
            <endpoint name="pingClient"
                        address="sb://[ServiceBus Namespace].servicebus.windows.net/PingQueue"
                        binding="netMessagingBinding" bindingConfiguration="messagingBinding"
                        contract="Microsoft.Samples.SessionMessages.IPingServiceContract"
                        behaviorConfiguration="securityBehavior"/>
            </client>

``` 

PingService App.Config 
```XML
                <behaviors>
                    <endpointBehaviors>
                    <behavior name="securityBehavior">
                        <transportClientEndpointBehavior>
                        <tokenProvider>
                            <sharedSecret issuerName="owner" issuerSecret="[Issuer key]" />
                        </tokenProvider>
                        </transportClientEndpointBehavior>
                    </behavior>
                    </endpointBehaviors>
                </behaviors>

                <services>
                    <service name="Microsoft.Samples.SessionMessages.PingService">
                    <endpoint name="pingServiceEndPoint" 
                                address="sb://[ServiceBus Namespace].servicebus.windows.net/PingQueue"
                                binding="netMessagingBinding" bindingConfiguration="messagingBinding"
                                contract="Microsoft.Samples.SessionMessages.IPingServiceContract"
                                behaviorConfiguration="securityBehavior" />
                    </service>
                </services>
 ```
  
Configuration File


The sender and receiver use NetMessagingBinding, which is defined in the respective App.config files. NetMessagingBinding uses BinaryMessageEncoding as its encoder and NetMessagingTransportBindingElement as its transport. The TransportSettings property of the transport binding element represents the runtime factory used by Service Bus. An extension section is required be added to the config file in order to use Service Bus components with WCF.

In addition to the binding, both the config files have a <behaviors> section, which defines TransportClientEndpointBehavior. Service Bus credentials are passed on to the client and service via this endpoint behavior. 



App.Config - Config Extensions and Binding

```XML
        <extensions>
            <bindingElementExtensions>
            <add name="netMessagingTransport" type="Microsoft.ServiceBus.Messaging.Configuration.NetMessagingTransportExtensionElement, 
                            Microsoft.ServiceBus, Version=1.6.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" />
            </bindingElementExtensions>
            <bindingExtensions>
            <add name="netMessagingBinding" type="Microsoft.ServiceBus.Messaging.Configuration.NetMessagingBindingCollectionElement, 
                            Microsoft.ServiceBus, Version=1.6.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" />
            </bindingExtensions>
        </extensions>
        <behaviors>
            <endpointBehaviors>
            <behavior name="securityBehavior">
                <transportClientEndpointBehavior>
                <tokenProvider>
                    <sharedSecret issuerName="owner" issuerSecret="[Issuer key]" />
                </tokenProvider>
                </transportClientEndpointBehavior>
            </behavior>
            </endpointBehaviors>
        </behaviors>
        <bindings>
            <netMessagingBinding>
            <binding name="messagingBinding" closeTimeout="00:03:00" openTimeout="00:03:00" receiveTimeout="00:03:00" sendTimeout="00:03:00" sessionIdleTimeout="00:01:00" prefetchCount="-1">
                <transportSettings batchFlushInterval="00:00:01"/>
            </binding>
            </netMessagingBinding>
        </bindings>
``` 
 
In the configuration files, NetMessagingBinding has properties for SessionIdleTimeOut, PrefetchCount, and BatchFlushInterval. SessionIdleTimeOut property allows the ServiceHost dispatcher to close an instance of the service if it is idle for more than the specified time interval. The default value for SessionIdleTimeout is 1 minute. BatchFlushInterval is responsible for implicitly batching send operations or complete operations. The Service Bus implicitly batches the send operation from sender or complete operation from receiver for the specified time to avoid multiple round-trips. The default value of BatchFlushInterval is 20 milliseconds. 

The Pingclient configuration file defines the client object and the PingService configuration file defines the service object.



App.Config - Client and Service Definition

```XML
    <client>
    <endpoint name="pingClient"
                address="[Enter Endpoint address]"
                binding="netMessagingBinding" bindingConfiguration="messagingBinding"
                contract="Microsoft.Samples.SessionMessages.IPingServiceContract"
                behaviorConfiguration="securityBehavior"/>
    </client>
    
    <services>
        <service name="Microsoft.Samples.SessionMessages.PingService">
        <endpoint name="pingServiceEndPoint" 
                    address="[Enter Endpoint address]"
                    binding="netMessagingBinding" bindingConfiguration="messagingBinding"
                    contract="Microsoft.Samples.SessionMessages.IPingServiceContract"
                    behaviorConfiguration="securityBehavior" />
        </service>
    </services>
``` 
  
Credentials


The sample obtains the user credentials and creates a Service Bus NamespaceManager object. This entity holds the credentials and is used for messaging management operations - in this case, to create and delete queues.



```C#

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
            // Create TokenProvider for access control service
            TokenProvider credentials = TokenProvider.CreateSharedSecretTokenProvider(ServiceBusIssuerName, ServiceBusIssuerKey);

            // Create the management Uri
            Uri managementUri = ServiceBusEnvironment.CreateServiceUri("sb", ServiceBusNamespace, string.Empty);
            namespaceClient = new NamespaceManager(managementUri, credentials);
        }

        // Create the entity (queue)
        static QueueDescription CreateQueue(bool session)
        {
            QueueDescription queueDescription = new QueueDescription(PingQueueName) { RequiresSession = session };

            // Try deleting the queue before creation. Ignore exception if queue does not exist.
            try
            {
                namespaceClient.DeleteQueue(queueDescription.Path);
            }
            catch (MessagingEntityNotFoundException)
            {
            }

            return namespaceClient.CreateQueue(queueDescription);
        }                                            
    
 ```
 
The preceding code prompts for the issuer credential and then constructs the listening URI using that information. The static ServiceBusEnvironment.CreateServiceUri function is provided to help construct the URI with the correct format and domain name. It is strongly recommended that you use this function instead of building the URI from scratch because the URI construction logic and format might change in future releases. At present, the resulting URI is scheme://<service-namespace>.servicebus.windows.net/. 

The CreateNamespaceManager() function creates the object to perform management operations, in this case to create and delete queues. Both ‘https’ and ‘sb’ Uri schemes are allowed as a part of service Uri.

The CreateQueue(bool session) function creates a queue with the RequireSession property set as per the argument passed.
 
Data Contract


The sample uses an PingData data contract to communicate between client and service. This data contract has two data members of type string.



C#

                                                [DataContract(Name="PingDataContract", Namespace="Microsoft.Samples.SessionMessages")]
                                                public class PingData
                                                {
                                                    [DataMember]
                                                    public string Message;

                                                    [DataMember]
                                                    public string SenderId;

                                                    public PingData()
                                                        : this(string.Empty, string.Empty)
                                                    {
                                                    }

                                                    public PingData(string message, string senderId)
                                                    {
                                                        this.Message = message;
                                                        this.SenderId = senderId;
                                                    }
                                                }
 
  
Sender


Service Bus supports IOutputChannel for sending messages using NetMessagingBinding. In the sample, the clients create a random message using the RandomString() function and then send the message to the service. The PingClient is defined in its app.config file.



C#

                                                static void Main(string[] args)
                                                {
                                                    ParseArgs(args);

                                                    // Send messages to queue which does not require session
                                                    Console.Title = "Ping Client";

                                                    // Create sender to Order Service
                                                    ChannelFactory<IPingServiceContract> factory = new ChannelFactory<IPingServiceContract>(SampleManager.PingClientConfigName);
                                                    IPingServiceContract clientChannel = factory.CreateChannel();
                                                    ((IChannel)clientChannel).Open();

                                                    // Send messages
                                                    numberOfMessages = random.Next(10, 30);
                                                    Console.WriteLine("[Client{0}] Sending {1} messages to {2}...", senderId, numberOfMessages, SampleManager.PingQueueName);
                                                    SendMessages(clientChannel);

                                                    // Close sender
                                                    ((IChannel)clientChannel).Close();
                                                    factory.Close();

                                                    Console.WriteLine("\nSender complete.");
                                                    Console.WriteLine("\nPress [Enter] to exit.");
                                                    Console.ReadLine();
                                                }

                                                static void SendMessages(IPingServiceContract clientChannel)
                                                {
                                                    // Send messages to queue which requires session:
                                                    for (int i = 0; i < numberOfMessages; i++)
                                                    {
                                                        // Send message 
                                                        PingData message = CreatePingData();
                                                        clientChannel.Ping(message);
                                                        SampleManager.OutputMessageInfo("Send", message);
                                                        Thread.Sleep(200);
                                                    }
                                                }

                                                static PingData CreatePingData()
                                                {
                                                    // Generating a random message
                                                    return new PingData(RandomString(), senderId);
                                                }

                                                // Creates a random string
                                                static string RandomString()
                                                {
                                                    StringBuilder builder = new StringBuilder();
                                                    int size = random.Next(5, 15);
                                                    char ch;
                                                    for (int i = 0; i < size; i++)
                                                    {
                                                        ch = Convert.ToChar(Convert.ToInt32(Math.Floor(26 * random.NextDouble() + 65)));
                                                        builder.Append(ch);
                                                    }

                                                    return builder.ToString();
                                                }
 
  
Service


The sample illustrates a Ping service as described above. The Ping service implements IPingServiceContract service contract. The operation attribute ReceiveContextEnabled is set with manual control set to true. This requires an explicit ReceiveContext.Complete operation to be performed for every message received. The service has behavior InstanceContextMode set to single. The service will only create one instance to process all available messages in the queue. 

Note that NetMessagingBinding only supports one-way communication. Therefore, OperationContract must explicitly set the attribute IsOneWay to true. The service is defined in its App.config file.



C#

                                                [ServiceContract]
                                                public interface IPingServiceContract
                                                {
                                                    [OperationContract(IsOneWay = true)]
                                                    [ReceiveContextEnabled(ManualControl = true)]
                                                    void Ping(PingData pingData);
                                                }

                                                [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode=ConcurrencyMode.Single)]
                                                public class PingService : IPingServiceContract
                                                {
                                                    [OperationBehavior]
                                                    public void Ping(PingData pingData)
                                                    {
                                                        // Get the message properties
                                                        var incomingProperties = OperationContext.Current.IncomingMessageProperties;
                                                        BrokeredMessageProperty property = (BrokeredMessageProperty)incomingProperties[BrokeredMessageProperty.Name];

                                                        // Print message
                                                        SampleManager.OutputMessageInfo("Receive", pingData);

                                                         //Complete the Message
                                                        ReceiveContext receiveContext;
                                                        if (ReceiveContext.TryGet(incomingProperties, out receiveContext))
                                                        {
                                                            receiveContext.Complete(TimeSpan.FromSeconds(10.0d));
                                                        }
                                                        else
                                                        {
                                                            throw new InvalidOperationException("Receiver is in peek lock mode but receive context is not available!");
                                                        }
                                                    }
                                                }
 
 
The service application subscribes to the faulted event. This will notify the service if any fault occurred during execution and can be handled properly. In the sample, the service is aborted on fault.



C#

                                                static void Main(string[] args)
                                                {
                                                    Console.Title = "Ping Service";
                                                    Console.WriteLine("Ready to receive messages from {0}...", SampleManager.PingQueueName);

                                                    // Creating the service host object as defined in config
                                                    ServiceHost serviceHost = new ServiceHost(typeof(PingService));

                                                    // Add ErrorServiceBehavior for handling errors encounter by servicehost during execution.
                                                    serviceHost.Description.Behaviors.Add(new ErrorServiceBehavior());

                                                    // Subscribe to the faulted event.
                                                    serviceHost.Faulted += new EventHandler(serviceHost_Faulted);

                                                    // Start service
                                                    serviceHost.Open();

                                                    Console.WriteLine("\nPress [Enter] to Close the ServiceHost.");
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
                                             Created PingQueue, Queue.RequiresSession = False                                        

                                             Launching senders and receivers...                                         

                                             Press [Enter] to exit.
 

Expected Output – Ping Client


                                            Sending 13 messages to PingQueue...
                                            Send: Message [FHZRADKBZWL] - Group 0.
                                            Send: Message [AMBALBZMY] - Group 0.
                                            Send: Message [OTAKPRFHOSHRH] - Group 0.
                                            Send: Message [IZBDPXUAXXJN] - Group 0.
                                            Send: Message [EMDFSRISFRP] - Group 0.
                                            Send: Message [TWRHTEIFGR] - Group 0.
                                            Send: Message [AVXBCOVCA] - Group 0.
                                            Send: Message [ZAVKM] - Group 0.
                                            Send: Message [AYBDHLPVAC] - Group 0.
                                            Send: Message [ETAHLNADJVPF] - Group 0.
                                            Send: Message [KPOMTW] - Group 0.
                                            Send: Message [XGPIHFNEOGBAA] - Group 0.
                                            Send: Message [QJAUOMUHDTLTX] - Group 0. 

                                            Sender complete. 

                                            Press [Enter] to exit.
 

Expected Output - Ping Service


                                            Ready to receive messages from PingQueue...

                                            Press [Enter] to exit.
                                            Receive: Message[FHZRADKBZWL]
                                            Receive: Message[AMBALBZMY]
                                            Receive: Message[OTAKPRFHOSHRH]
                                            Receive: Message[IZBDPXUAXXJN]
                                            Receive: Message[EMDFSRISFRP]
                                            Receive: Message[TWRHTEIFGR]
                                            Receive: Message[AVXBCOVCA]
                                            Receive: Message[ZAVKM]
                                            Receive: Message[AYBDHLPVAC]
                                            Receive: Message[ETAHLNADJVPF]
                                            Receive: Message[KPOMTW]
                                            Receive: Message[XGPIHFNEOGBAA]
                                            Receive: Message[QJAUOMUHDTLTX]
 
 


Did you find this information useful?  Please send your suggestions and comments about the documentation.  
 
