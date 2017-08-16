# Get started sending and receiving messages from Service Bus queues on Universal Windows Platform 

In order to run the sample in this directory, replace the following bracketed values in the `MainPage.xaml.cs` file.

```csharp
// Connection String for the namespace can be obtained from the Azure portal under the 
// `Shared Access policies` section.
const string ServiceBusConnectionString = "{Service Bus connection string}";
const string QueueName = "{Queue Name}";
```

## The Sample Program
The program is built to be run as a sample in the Context of Visual Studio and thus not cover aspects such as pushing application
to the Windows store. The sample has been tested to run fine when deployed and run using either the `Simulator` or `LocalMachine`
VisualStudio Configuration.

Also to keep things reasonably simple, the sample program keeps send and receive code within a single hosting application.
Typically in real world applications these roles are often spread across applications, services, or at least across 
independently deployed and run tiers of applications or services. For clarity, the send and receive activities are kept as 
separate methods as if they were different apps.

For further information on how to create this sample on your own, follow the rest of the tutorial.

## Running the Program

## What will be accomplished
In this tutorial, we will write a console application to send and receive messages to a ServiceBus queue using UWP.

## Prerequisites
1. [UWP Workload Installation](https://docs.microsoft.com/en-us/windows/uwp/get-started/get-set-up)
2. When installing the UWP Workload, also install the Optional `Windows 10 SDK (10.0.10586.0)`
2. An Azure subscription.
3. [A ServiceBus namespace](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-create-namespace-portal) 
4. [A ServiceBus queue](https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dotnet-get-started-with-queues#2-create-a-queue-using-the-azure-portal)

### Create a console application

- Create a new UWP application. Check out [this link](https://docs.microsoft.com/en-us/windows/uwp/get-started/create-a-hello-world-app-xaml-universal) for help to create a new application on your operating system.

### Add the ServiceBus client reference

1. Add the following to your csproj, to make sure that the solution references the `Microsoft.Azure.ServiceBus` project.

    ```json
    "Microsoft.Azure.ServiceBus": "1.0.1"
    ```

### Add the following under the `Grid` Xml Tag in `MainPage.Xaml` page. These define the UI elements of Sample app:
		<Button x:Name="sendMessageButton" Content="Send Messages" HorizontalAlignment="Left" Margin="406,208,0,0" VerticalAlignment="Top" Click="sendMessageButton_Click"/>
        <Button x:Name="receiveMessageButton" Content="ReceiveMessages" HorizontalAlignment="Left" Margin="655,208,0,0" VerticalAlignment="Top" Click="receiveMessageButton_Click"/>
        <TextBox x:Name="headingTextBox" HorizontalAlignment="Left" Margin="503,10,0,0" TextWrapping="Wrap" Text="UWP Send Receive Sample" VerticalAlignment="Top"/>
        <Button x:Name="exitButton" Content="Press this Button To Exit" HorizontalAlignment="Left" Margin="523,364,0,0" VerticalAlignment="Top" Click="exitButton_Click"/>
        <TextBox x:Name="resultTextBox" HorizontalAlignment="Left" Margin="406,467,0,0" TextWrapping="Wrap" AcceptsReturn="True" MaxHeight="172" Width="500" ScrollViewer.VerticalScrollBarVisibility="Auto" IsReadOnly="True" Header="Output" VerticalAlignment="Top"/>

### Write some code to send and receive messages from the queue
1. Add the following using statement to the top of the MainPage.Xaml.cs file.
   
    ```csharp
    using Microsoft.Azure.ServiceBus;
	using Microsoft.Azure.ServiceBus.Core;
    ```

1. Add the following variables to the `MainPage` class, and replace the placeholder values:
    
    ```csharp
    const string ServiceBusConnectionString = "{Service Bus connection string}";
    const string QueueName = "{Queue Name}";
	const int NumberOfMessagesToSend = 10;
    ```

1. Create a new async method called `receiveMessageButton_Click` that knows how to handle received messages with the following code:

	```csharp
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
	```
1. Create a new async method called `sendMessageButton_Click` with the following code:

    ```csharp
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
    ```
1. Create a new method called `exitButton_Click` with the following code:
	```csharp
	void exitButton_Click(object sender, RoutedEventArgs e)
    {
		Debug.WriteLine("Exiting ServiceBus UWP Send/Receive sample...");
        CoreApplication.Exit();
    }
	```

1. Create a new method called `Log` to output the sample programs logs:
	```
	void Log(string message)
    {
            this.resultTextBox.Text += message;
            this.resultTextBox.Text += "\r\n";
    }
	```

Congratulations! You have now sent and received messages to a ServiceBus queue using a Universal Windows Platform application.
