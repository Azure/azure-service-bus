package com.microsoft.azure.servicebus.samples.receiveloop;

import org.junit.Assert;

public class ReceiveLoopTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                ReceiveLoop.runApp(new String[0], (connectionString) -> {
                    ReceiveLoop app = new ReceiveLoop();
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