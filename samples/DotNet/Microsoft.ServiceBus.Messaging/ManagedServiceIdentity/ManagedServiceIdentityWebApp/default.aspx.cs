using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace ManagedServiceIdentityWebApp
{
    public partial class Default : System.Web.UI.Page
    {
        protected void btnSend_Click(object sender, EventArgs e)
        {
            MessagingFactorySettings messagingFactorySettings = new MessagingFactorySettings
            {
                //TokenProvider = TokenProvider.CreateManagedServiceIdentityTokenProvider(ServiceAudience.ServiceBusAudience),
                TransportType = TransportType.Amqp
            };

            // TODO - Remove after backend is patched with the AuthComponent open fix
            messagingFactorySettings.AmqpTransportSettings.EnableLinkRedirect = false;

            MessagingFactory messagingFactory = MessagingFactory.Create($"sb://{txtNamespace.Text}.servicebus.windows.net/",
                messagingFactorySettings);

            QueueClient queueClient = messagingFactory.CreateQueueClient(txtQueueName.Text);
            queueClient.Send(new BrokeredMessage(Encoding.UTF8.GetBytes(txtData.Text)));
            queueClient.Close();
            messagingFactory.Close();
        }

        protected void btnReceive_Click(object sender, EventArgs e)
        {
            MessagingFactorySettings messagingFactorySettings = new MessagingFactorySettings
            {
                //TokenProvider = TokenProvider.CreateManagedServiceIdentityTokenProvider(ServiceAudience.ServiceBusAudience),
                TransportType = TransportType.Amqp
            };

            messagingFactorySettings.AmqpTransportSettings.EnableLinkRedirect = false;

            MessagingFactory messagingFactory = MessagingFactory.Create($"sb://{txtNamespace.Text}.servicebus.windows.net/",
                messagingFactorySettings);

            QueueClient queueClient = messagingFactory.CreateQueueClient(txtQueueName.Text);
            BrokeredMessage msg = queueClient.Receive(TimeSpan.FromSeconds(1));
            if (msg != null)
            {
                txtReceivedData.Text += $"Seq#:{msg.SequenceNumber} data:{Encoding.UTF8.GetString(msg.GetBody<byte[]>())}{Environment.NewLine}";
            }
            queueClient.Close();
            messagingFactory.Close();
        }
    }
}