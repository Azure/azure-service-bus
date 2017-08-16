// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace BasicUwpSendReceiveSample
{
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Core;
    using System;
    using System.Diagnostics;
    using System.Text;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.ApplicationModel.Core;

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // Connection String for the namespace can be obtained from the Azure portal under the 
        // 'Shared Access policies' section.
        const string ServiceBusConnectionString = "{Service Bus connection string}";
        const string QueueName = "{Queue Name}";
        const int NumberOfMessagesToSend = 10;

        public MainPage()
        {
            this.InitializeComponent();
        }

        async void sendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            var messageSender = new MessageSender(ServiceBusConnectionString, QueueName);
            try
            {
                for (var i = 0; i < NumberOfMessagesToSend; i++)
                {
                    // Create a new message to send to the queue
                    string messageBody = $"Message {i}";
                    var message = new Message(Encoding.UTF8.GetBytes(messageBody));

                    // Write the body of the message to the console
                    this.Log($"Sending message: {messageBody}");

                    // Send the message to the queue
                    await messageSender.SendAsync(message);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }
            finally
            {
                await messageSender.CloseAsync();
            }
        }

        async void receiveMessageButton_Click(object sender, RoutedEventArgs e)
        {
            int numberOfMessagesToReceive = NumberOfMessagesToSend;
            var messageReceiver = new MessageReceiver(ServiceBusConnectionString, QueueName);

            try
            {
                while (numberOfMessagesToReceive-- > 0)
                {
                    // Receive the message
                    Message message = await messageReceiver.ReceiveAsync();

                    // Process the message
                    this.resultTextBox.Focus(FocusState.Programmatic);
                    this.Log($"Received message: SequenceNumber:{message.SystemProperties.SequenceNumber} Body:{Encoding.UTF8.GetString(message.Body)}");

                    // Complete the message so that it is not received again.
                    // This can be done only if the MessageReceiver is created in ReceiveMode.PeekLock mode (which is default).
                    await messageReceiver.CompleteAsync(message.SystemProperties.LockToken);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
            }
            finally
            {
                await messageReceiver.CloseAsync();
            }
        }

        void exitButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Exiting ServiceBus UWP Send/Receive sample...");
            CoreApplication.Exit();
        }

        void Log(string message)
        {
            this.resultTextBox.Text += message;
            this.resultTextBox.Text += "\r\n";
        }
    }
}
