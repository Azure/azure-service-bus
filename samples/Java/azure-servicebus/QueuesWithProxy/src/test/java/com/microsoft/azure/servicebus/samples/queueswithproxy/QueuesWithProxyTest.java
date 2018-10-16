package com.microsoft.azure.servicebus.samples.queueswithproxy;

import org.junit.Assert;


public class QueuesWithProxyTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                QueuesWithProxy.runApp(new String[0], (connectionString) -> {
                    QueuesWithProxy app = new QueuesWithProxy();
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