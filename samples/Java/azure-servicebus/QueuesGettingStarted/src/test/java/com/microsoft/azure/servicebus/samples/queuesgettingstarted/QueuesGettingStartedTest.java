package com.microsoft.azure.servicebus.samples.queuesgettingstarted;

import org.junit.Assert;


public class QueuesGettingStartedTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                QueuesGettingStarted.runApp(new String[0], (connectionString) -> {
                    QueuesGettingStarted app = new QueuesGettingStarted();
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