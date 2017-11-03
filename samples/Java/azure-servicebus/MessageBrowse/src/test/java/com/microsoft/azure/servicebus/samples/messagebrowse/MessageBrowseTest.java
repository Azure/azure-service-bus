package com.microsoft.azure.servicebus.samples.messagebrowse;

import org.junit.Assert;

public class MessageBrowseTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                MessageBrowse.runApp(new String[0], (connectionString) -> {
                    MessageBrowse app = new MessageBrowse();
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