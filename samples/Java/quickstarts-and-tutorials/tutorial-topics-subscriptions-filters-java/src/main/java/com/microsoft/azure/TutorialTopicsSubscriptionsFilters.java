package com.microsoft.azure;

import org.apache.commons.cli.CommandLine;
import org.apache.commons.cli.CommandLineParser;
import org.apache.commons.cli.DefaultParser;
import org.apache.commons.cli.HelpFormatter;
import org.apache.commons.cli.Option;
import org.apache.commons.cli.Options;

import com.microsoft.azure.servicebus.*;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;
import com.google.gson.Gson;

import static java.nio.charset.StandardCharsets.*;

import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.Function;

public class TutorialTopicsSubscriptionsFilters {
	
	static final Gson GSON = new Gson();
	
	public String ConnectionString = null;
    public String TopicName = null;
    static final String[] Subscriptions = {"S1","S2","S3"};
    static final String[] Store = {"Store1","Store2","Store3","Store4","Store5","Store6","Store7","Store8","Store9","Store10"};
    static final String SysField = "sys.To";
    static final String CustomField = "StoreId";    
    int NrOfMessagesPerStore = 1; // Send at least 1.

	public static void main(String[] args) {
		TutorialTopicsSubscriptionsFilters app = new TutorialTopicsSubscriptionsFilters();
        try {
            app.runApp(args);
            app.run();
        } catch (Exception e) {
            System.out.printf("%s", e.toString());
        }
        System.exit(0);
	}	
	
	public void run() throws Exception {
		// Send sample messages.
        this.sendMessagesToTopic();

        // Receive messages from subscriptions.
        this.receiveAllMessages();        
	}
	
	public void receiveAllMessages() throws Exception
	{		
		System.out.printf("\nStart Receiving Messages.\n");
		
		CompletableFuture.allOf(
				receiveAllMessageFromSubscription(Subscriptions[0]),
		        receiveAllMessageFromSubscription(Subscriptions[1]),
		        receiveAllMessageFromSubscription(Subscriptions[2]) 
				).join();
		
	}
	
	public void sendMessagesToTopic() throws Exception, ServiceBusException {
		 // Create client for the topic.
        TopicClient topicClient = new TopicClient(new ConnectionStringBuilder(ConnectionString, TopicName));

        // Create a message sender from the topic client.

        System.out.printf("\nSending orders to topic.\n");

        // Now we can start sending orders.
        CompletableFuture.allOf(
                SendOrders(topicClient,Store[0]),
                SendOrders(topicClient,Store[1]),
                SendOrders(topicClient,Store[2]),
                SendOrders(topicClient,Store[3]),
                SendOrders(topicClient,Store[4]),
                SendOrders(topicClient,Store[5]),
                SendOrders(topicClient,Store[6]),
                SendOrders(topicClient,Store[7]),
                SendOrders(topicClient,Store[8]),
                SendOrders(topicClient,Store[9])                
        ).join();

        System.out.printf("\nAll messages sent.\n");
    }

    public CompletableFuture<Void> SendOrders(TopicClient topicClient, String store) throws Exception {

        for(int i = 0;i<NrOfMessagesPerStore;i++) {
        	Random r = new Random();
        	final Item item = new Item(r.nextInt(5),r.nextInt(5),r.nextInt(5));        	
        	IMessage message = new Message(GSON.toJson(item,Item.class).getBytes(UTF_8)); 
        	// We always set the Sent to field
            message.setTo(store);    
            final String StoreId = store;
            Double priceToString = item.getPrice();
            final String priceForPut = priceToString.toString();
            message.setProperties(new HashMap<String, String>() {{
            	// Additionally we add a customer store field. In reality you would use sys.To or a customer property but not both. 
            	// This is just for demo purposes.
                put("StoreId", StoreId);
                // Adding more potential filter / rule and action able fields
                put("Price", priceForPut);
                put("Color", item.getColor());
                put("Category", item.getItemCategory());
            }});
                        
            System.out.printf("Sent order to Store %s. Price=%f, Color=%s, Category=%s\n", StoreId, item.getPrice(), item.getColor(), item.getItemCategory());            
            topicClient.sendAsync(message);
        }
               
		return new CompletableFuture().completedFuture(null);         
    }        
	
	public CompletableFuture<Void> receiveAllMessageFromSubscription(String subscription) throws Exception {
		
		int receivedMessages = 0;

        // Create subscription client.
        IMessageReceiver subscriptionClient = ClientFactory.createMessageReceiverFromConnectionStringBuilder(new ConnectionStringBuilder(ConnectionString, TopicName+"/subscriptions/"+ subscription), ReceiveMode.PEEKLOCK);

        // Create a receiver from the subscription client and receive all messages.
        System.out.printf("\nReceiving messages from subscription %s.\n\n", subscription);

        while (true)
        {
        	// This will make the connection wait for N seconds if new messages are available. 
        	// If no additional messages come we close the connection. This can also be used to realize long polling.
        	// In case of long polling you would obviously set it more to e.g. 60 seconds.
        	IMessage receivedMessage = subscriptionClient.receive(Duration.ofSeconds(1));
            if (receivedMessage != null)
            {
                if ( receivedMessage.getProperties() != null ) {                	                	                	                	
                    System.out.printf("StoreId=%s\n", receivedMessage.getProperties().get("StoreId"));                                                	                                    	
                	
                    // Show the label modified by the rule action
                    if(receivedMessage.getLabel() != null)
                		System.out.printf("Label=%s\n", receivedMessage.getLabel());   
                }
                
                byte[] body = receivedMessage.getBody();
                Item theItem = GSON.fromJson(new String(body, UTF_8), Item.class);
                System.out.printf("Item data. Price=%f, Color=%s, Category=%s\n", theItem.getPrice(), theItem.getColor(), theItem.getItemCategory());                            
                
                subscriptionClient.complete(receivedMessage.getLockToken());
                receivedMessages++;
            }
            else
            {
                // No more messages to receive.
            	subscriptionClient.close();
                break;
            }
        }
        System.out.printf("\nReceived %s messages from subscription %s.\n", receivedMessages, subscription);
		
		return new CompletableFuture().completedFuture(null); 
	}

    public void runApp(String[] args) {
    	 try {
             // parse connection string from command line
             Options options = new Options();
             options.addOption(new Option("c", true, "Connection string"));
             options.addOption(new Option("t", true, "Topic Name"));
             CommandLineParser clp = new DefaultParser();
             CommandLine cl = clp.parse(options, args);
             if (cl.getOptionValue("c") != null && cl.getOptionValue("t") != null) {
                 ConnectionString = cl.getOptionValue("c");
                 TopicName =  cl.getOptionValue("t");
             }
             else
             {
                 HelpFormatter formatter = new HelpFormatter();
                 formatter.printHelp("run jar with", "", options, "", true);
             }
         } catch (Exception e) {
             System.out.printf("%s", e.toString());
         }
    }
}
