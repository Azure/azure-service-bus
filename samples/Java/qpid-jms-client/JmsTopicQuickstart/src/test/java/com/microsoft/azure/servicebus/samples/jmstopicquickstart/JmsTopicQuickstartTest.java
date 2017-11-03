package com.microsoft.azure.servicebus.samples.jmstopicquickstart;

import org.junit.Assert;

public class JmsTopicQuickstartTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                JmsTopicQuickstart.runApp(new String[0], (connectionString) -> {
                    JmsTopicQuickstart app = new JmsTopicQuickstart();
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