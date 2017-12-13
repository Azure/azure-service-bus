package com.microsoft.azure.servicebus.samples.topicsgettingstarted;

import org.junit.Assert;

public class TopicsGettingStartedTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                TopicsGettingStarted.runApp(new String[0], (connectionString) -> {
                    TopicsGettingStarted app = new TopicsGettingStarted();
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