// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

package com.microsoft.azure.servicebus.samples.managingentity;

import com.microsoft.azure.servicebus.management.ManagementClientAsync;
import com.microsoft.azure.servicebus.management.ManagementClientConstants;
import com.microsoft.azure.servicebus.management.QueueDescription;
import com.microsoft.azure.servicebus.management.QueueRuntimeInfo;
import com.microsoft.azure.servicebus.primitives.ConnectionStringBuilder;
import com.microsoft.azure.servicebus.primitives.ServiceBusException;

import java.time.Duration;
import java.util.UUID;
import java.util.concurrent.ExecutionException;
import java.util.function.Function;

import org.apache.commons.cli.*;

public class ManagingEntity {

    // For asynchronous operations, use ManagementClientAsync.
    // For use cases where operations are always synchronous, you could use ManagementClient. 
    // Both provide same functionality.
    ManagementClientAsync managementClient;

    public void run(String connectionString) throws Exception {

        this.managementClient = new ManagementClientAsync(new ConnectionStringBuilder(connectionString));
        String queueName = UUID.randomUUID().toString();

        System.out.println("Creating a new Queue with name - " + queueName);
        this.createQueue(queueName);

        System.out.println("Retrieving the created queue");
        QueueDescription getQueue = this.getQueue(queueName);

        System.out.println("Updating few properties of the queue");
        this.updateQueue(getQueue);

        System.out.println("Retrieving runtime information of the queue");
        this.getQueueRuntimeInfo(queueName);

        System.out.println("Deleting the queue");
        this.deleteQueue(queueName);

        this.managementClient.close();
    }

    // Creates a new Queue using the managementClient with the name provided.
    private void createQueue(String queueName) {
        
        // Name of the queue is a required parameter.
        // All other queue properties have defaults, and hence optional.
        QueueDescription queueDescription = new QueueDescription(queueName);
        
        // The duration of a peek lock; that is, the amount of time that a message is locked from other receivers.
        queueDescription.setLockDuration(Duration.ofSeconds(45));

        // Size of the Queue. For non-partitioned entity, this would be the size of the queue.
        // For partitioned entity, this would be the size of each partition.
        queueDescription.setMaxSizeInMB(2048);

        // This value indicates if the queue requires guard against duplicate messages.
        // Find out more in DuplicateDetection sample.
        queueDescription.setRequiresDuplicateDetection(false);

        // Since RequiresDuplicateDetection is false, the following need not be specified and will be ignored.
        // queueDescription.setDuplicationDetectionHistoryTimeWindow(Duration.ofMinutes(2));

        // This indicates whether the queue supports the concept of session.
        queueDescription.setRequiresSession(false);

        // The default time to live value for the messages.
        // Find out more in "TimeToLive" sample.
        queueDescription.setDefaultMessageTimeToLive(Duration.ofDays(7));

        // Duration of idle interval after which the queue is automatically deleted.
        queueDescription.setAutoDeleteOnIdle(ManagementClientConstants.MAX_DURATION);

        // Decides whether an expired message due to TTL should be dead-lettered.
        // Find out more in "TimeToLive" sample.
        queueDescription.setEnableDeadLetteringOnMessageExpiration(false);

        // The maximum delivery count of a message before it is dead-lettered.
        // Find out more in "DeadletterQueue" sample.
        queueDescription.setMaxDeliveryCount(8);

        // Creating only one partition.
        // Find out more in PartitionedQueues sample.
        queueDescription.setEnablePartitioning(false);

        try {
            this.managementClient.createQueueAsync(queueDescription).get();
        } catch (InterruptedException e) {
            System.out.println("Encountered exception while creating Queue - \n" + e.toString());
        } catch (ExecutionException e) {
            if (e.getCause() instanceof ServiceBusException) {
                System.out.println("Encountered ServiceBusException while creating Queue - \n" + e.toString());
            }
            System.out.println("Encountered exception while creating Queue - \n" + e.toString());
		}
    }

    // Retrieves a queue and its properties
    private QueueDescription getQueue(String queueName) throws InterruptedException, ExecutionException {
        QueueDescription getQueue = this.managementClient.getQueueAsync(queueName).get();
        return getQueue;
    }

    // Note - the following properties cannot be updated once created -
    // - Path
    // - RequiresSession
    // - EnablePartitioning
    private void updateQueue(QueueDescription queueDescription) throws InterruptedException, ExecutionException {
        System.out.println("Before updated - MaxDeliveryCount:" + queueDescription.getMaxDeliveryCount() + "; LockDuration:" + queueDescription.getLockDuration().toString());

        // Updating the properties of the queue.
        queueDescription.setMaxDeliveryCount(15);
        queueDescription.setLockDuration(Duration.ofMinutes(5));

        // Performing the actual update
        QueueDescription updatQueueDescription = this.managementClient.updateQueueAsync(queueDescription).get();

        System.out.println("After updated - MaxDeliveryCount:" + updatQueueDescription.getMaxDeliveryCount() + "; LockDuration:" + updatQueueDescription.getLockDuration().toString());
    }

    // Get runtime information of the queue like message count, size etc.
    private void getQueueRuntimeInfo(String queueName) throws InterruptedException, ExecutionException {
        QueueRuntimeInfo runtimeInfo = this.managementClient.getQueueRuntimeInfoAsync(queueName).get();
        System.out.println("Retrieved runtime information of queue\n " +
            "Active_messages: " + runtimeInfo.getMessageCountDetails().getActiveMessageCount() +
            "\nSize of queue: " + runtimeInfo.getSizeInBytes() +
            "\nQueue Creation time: " + runtimeInfo.getCreatedAt().toString() +
            "\nQueue last updation time: " + runtimeInfo.getUpdatedAt().toString());
    }

    // Delete the queue.
    private void deleteQueue(String queueName) throws InterruptedException, ExecutionException {
        this.managementClient.deleteQueueAsync(queueName).get();
    }

    public static void main(String[] args) {

        System.exit(runApp(args, (connectionString) -> {
            ManagingEntity app = new ManagingEntity();
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
