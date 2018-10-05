package com.microsoft.azure.servicebus.samples.sasauthorization;

import com.microsoft.azure.servicebus.ClientFactory;
import com.microsoft.azure.servicebus.IMessage;
import com.microsoft.azure.servicebus.IMessageReceiver;
import com.microsoft.azure.servicebus.IMessageSender;
import com.microsoft.azure.servicebus.Message;
import com.microsoft.azure.servicebus.management.AccessRights;
import com.microsoft.azure.servicebus.management.AuthorizationRule;
import com.microsoft.azure.servicebus.management.EntityNameHelper;
import com.microsoft.azure.servicebus.management.ManagementClientAsync;
import com.microsoft.azure.servicebus.management.SharedAccessAuthorizationRule;
import com.microsoft.azure.servicebus.management.TopicDescription;
import com.microsoft.azure.servicebus.primitives.AuthorizationFailedException;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.MessagingFactory;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;
import com.microsoft.azure.servicebus.security.SharedAccessSignatureTokenProvider;

import java.net.URI;
import java.util.Arrays;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class SasAuthorization {
    // For asynchronous operations, use ManagementClientAsync.
    // For use cases where operations are always synchronous, you could use ManagementClient. 
    // Both provide same functionality.
    ManagementClientAsync managementClient;

    public void run(String connectionString) throws Exception {
        this.managementClient = new ManagementClientAsync(new ConnectionStringBuilder(connectionString));
        
        String topicName = UUID.randomUUID().toString().substring(0, 8);
        String subscriptionName1 = UUID.randomUUID().toString().substring(0, 8);
        String subscriptionName2 = UUID.randomUUID().toString().substring(0, 8);

        System.out.println("Creating a new Topic with name - " + topicName + " with 2 subscriptions - " + subscriptionName1 + " and " + subscriptionName2);
        this.managementClient.createTopicAsync(topicName).get();
        this.managementClient.createSubscriptionAsync(topicName, subscriptionName1).get();
        this.managementClient.createSubscriptionAsync(topicName, subscriptionName2).get();

        ConnectionStringBuilder originalCsBuilder = new ConnectionStringBuilder(connectionString);

        // We are trying to create an authorization rule for the topic.
        // We will be adding a new SAS policy which has Send-only claims
        // i.e., using that messages can only be sent to entity, but not received.
        // The access is defined using {SASKeyName, SASKey} combination.
        System.out.println("\nCreating a send-only rule for topic");
        SharedAccessAuthorizationRule sendOnlyAuthRule = this.createAuthRuleForTopicAsync(topicName, AccessRights.Send);

        // We will try to send and receive using a send-only SAS rule (using sasKeyName and sasKey).
        // Send should succeed below and receive should fail.
        System.out.println("Trying to send and receive using the send-only rule");
        ConnectionStringBuilder csBuilder = new ConnectionStringBuilder(
            originalCsBuilder.getEndpoint(), 
            topicName,
            sendOnlyAuthRule.getKeyName(),
            sendOnlyAuthRule.getPrimaryKey());
        this.performSendAndReceiveOperation(topicName, subscriptionName1, csBuilder);

        // Given a SAS policy, we can also create SAS tokens which already have authentication information.
        // Lets try SASToken based authentication for subscription1.
        System.out.println("\nCreating a receive-only authentication token for " + subscriptionName1);
        SharedAccessAuthorizationRule receiveOnlyAuthRule = this.createAuthRuleForTopicAsync(topicName, AccessRights.Listen);
        SharedAccessSignatureTokenProvider sasTokenProvider = new SharedAccessSignatureTokenProvider(receiveOnlyAuthRule.getKeyName(), receiveOnlyAuthRule.getPrimaryKey(), 600);
        String tokenAudience = originalCsBuilder.getEndpoint().resolve(EntityNameHelper.formatSubscriptionPath(topicName, subscriptionName1)).toString();
        String sasToken = sasTokenProvider.getSecurityTokenAsync(tokenAudience).get().getTokenValue();
        
        // We will try to send and receive using a receive-only SAS rule (using sasToken generated for subscriptionName1)
        // Receive should succeed below and send should fail.
        System.out.println("\nTrying to send and receive using the receive-only token for " + subscriptionName1);
        csBuilder = new ConnectionStringBuilder(
            originalCsBuilder.getEndpoint(), 
            EntityNameHelper.formatSubscriptionPath(topicName, subscriptionName1), 
            sasToken);
        this.performSendAndReceiveOperation(topicName, subscriptionName1, csBuilder);
    
        // We will try to send and receive using the same receive-only SAS rule created above.
        // Both operations should fail as the token was generated for subscriptionName1 and not subscriptionName2
        System.out.println("\nTrying to send and receive using the receive-only token ofr " + subscriptionName2);
        this.performSendAndReceiveOperation(topicName, subscriptionName2, csBuilder);

        // Delete resources
        this.managementClient.deleteTopicAsync(topicName).get();
        this.managementClient.close();
    }

    private SharedAccessAuthorizationRule createAuthRuleForTopicAsync(String topicName, AccessRights accessRights) throws InterruptedException, ExecutionException {
        TopicDescription topicDescription = this.managementClient.getTopicAsync(topicName).get();
        AuthorizationRule rule = new SharedAccessAuthorizationRule("ruleWith" + accessRights.toString(), Arrays.asList(accessRights));
        topicDescription.setAuthorizationRules(Arrays.asList(rule));
        TopicDescription updatedDescription = this.managementClient.updateTopicAsync(topicDescription).get();
        return (SharedAccessAuthorizationRule)updatedDescription.getAuthorizationRules().get(0);
    }

    private void performSendAndReceiveOperation(String topicName, String subscriptionName, ConnectionStringBuilder csBuilder) throws InterruptedException, ExecutionException, ServiceBusException {
        MessagingFactory factory = MessagingFactory.createFromConnectionStringBuilder(csBuilder);

        try {
            IMessageSender sender = ClientFactory.createMessageSenderFromEntityPath(factory, topicName);
            sender.send(new Message("msg"));
            System.out.println("Sent message successfully");
            sender.close();
        } catch (ServiceBusException e) {
            System.out.println("Could not send message due to authorization failure");
        }

        try {
            IMessageReceiver receiver = ClientFactory.createMessageReceiverFromEntityPath(factory, EntityNameHelper.formatSubscriptionPath(topicName, subscriptionName));
            IMessage msg = receiver.receive();
            System.out.println("Received message successfully");
            receiver.close();
        } catch (ServiceBusException e) {
            System.out.println("Could not receive message due to authorization failure");
        }

        factory.close();
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            SasAuthorization app = new SasAuthorization();
            try {
                app.run(connectionString);
                return 0;
            } catch (Exception e) {
                System.out.printf("%s", e.toString());
                return 1;
            }
        }));
    }

    static final String SB_SAMPLES_CONNECTIONSTRING = "SB_SAMPLES_CONNECTIONSTRING";

    public static int runApp(String[] args, Function<String, Integer> run) {
        try {

            String connectionString = null;

            // parse connection string from command line
            Options options = new Options();
            options.addOption(new Option("c", true, "Connection string"));
            CommandLineParser clp = new DefaultParser();
            CommandLine cl = clp.parse(options, args);
            if (cl.getOptionValue("c") != null) {
                connectionString = cl.getOptionValue("c");
            }

            // get overrides from the environment
            String env = System.getenv(SB_SAMPLES_CONNECTIONSTRING);
            if (env != null) {
                connectionString = env;
            }
            
            if (connectionString == null) {
                HelpFormatter formatter = new HelpFormatter();
                formatter.printHelp("run jar with", "", options, "", true);
                return 2;
            }
            return run.apply(connectionString);
        } catch (Exception e) {
            System.out.printf("%s", e.toString());
            return 3;
        }
    }
}
