using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using System;
using System.Text;

namespace ManagedServiceIdentityWebApp
{
    public partial class _Default : System.Web.UI.Page
    {
        protected void btnSend_Click(object sender, EventArgs e)
        {
            // create a parameter object for the messaging factory that configures
            // the MSI token provider for Service Bus and use of the AMQP protocol:
            MessagingFactorySettings messagingFactorySettings = new MessagingFactorySettings
            {
                TokenProvider = TokenProvider.CreateManagedServiceIdentityTokenProvider(ServiceAudience.ServiceBusAudience),
                TransportType = TransportType.Amqp
            };

            // create the messaging factory using the namespace endpoint name supplied by the user
            MessagingFactory messagingFactory = MessagingFactory.Create($"sb://{txtNamespace.Text}/",
                messagingFactorySettings);

            // create a queue client using the queue name supplied by the user
            QueueClient queueClient = messagingFactory.CreateQueueClient(txtQueueName.Text);
            // send a message using the input text
            queueClient.Send(new BrokeredMessage(Encoding.UTF8.GetBytes(txtData.Text)));

            queueClient.Close();
            messagingFactory.Close();
        }

        protected void btnReceive_Click(object sender, EventArgs e)
        {
            // create a parameter object for the messaging factory that configures
            // the MSI token provider for Service Bus and use of the AMQP protocol:
            MessagingFactorySettings messagingFactorySettings = new MessagingFactorySettings
            {
                TokenProvider = TokenProvider.CreateManagedServiceIdentityTokenProvider(ServiceAudience.ServiceBusAudience),
                TransportType = TransportType.Amqp
            };

            // TODO - Remove after backend is patched with the AuthComponent open fix
            // https://github.com/Azure/azure-service-bus/issues/136
            messagingFactorySettings.AmqpTransportSettings.EnableLinkRedirect = false;

            // create the messaging factory using the namespace endpoint name supplied by the user
            MessagingFactory messagingFactory = MessagingFactory.Create($"sb://{txtNamespace.Text}/",
                messagingFactorySettings);

            // create a queue client using the queue name supplied by the user
            QueueClient queueClient = messagingFactory.CreateQueueClient(txtQueueName.Text, ReceiveMode.ReceiveAndDelete);
            // request a readily available message (with a very short wait) 
            BrokeredMessage msg = queueClient.Receive(TimeSpan.FromSeconds(1));
            if (msg != null)
            {
                // if we got a message, show its contents.
                txtReceivedData.Text += $"Seq#:{msg.SequenceNumber} data:{Encoding.UTF8.GetString(msg.GetBody<byte[]>())}{Environment.NewLine}";
            }
            queueClient.Close();
            messagingFactory.Close();
        }
    }
}
