package com.microsoft.azure.servicebus.samples.jmsqueuequickstart;

import org.junit.Assert;

public class JmsQueueQuickstartTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                JmsQueueQuickstart.runApp(new String[0], (connectionString) -> {
                    JmsQueueQuickstart app = new JmsQueueQuickstart();
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