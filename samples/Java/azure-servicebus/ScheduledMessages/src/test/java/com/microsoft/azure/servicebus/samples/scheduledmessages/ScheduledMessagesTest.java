package com.microsoft.azure.servicebus.samples.scheduledmessages;

import org.junit.Assert;

public class ScheduledMessagesTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                ScheduledMessages.runApp(new String[0], (connectionString) -> {
                    ScheduledMessages app = new ScheduledMessages();
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