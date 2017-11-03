package com.microsoft.azure.servicebus.samples.partitionedqueues;

import org.junit.Assert;

public class PartitionedQueuesTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                PartitionedQueues.runApp(new String[0], (connectionString) -> {
                    PartitionedQueues app = new PartitionedQueues();
                    try {
                        app.run(connectionString);
                        return 0;
                    } catch (Exception e) {
                        System.out.printf("%s", e.toString());
                        return 1;
                    }
                }));
    }

}