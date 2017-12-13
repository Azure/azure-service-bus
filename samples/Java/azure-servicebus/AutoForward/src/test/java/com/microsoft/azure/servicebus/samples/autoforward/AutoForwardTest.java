package com.microsoft.azure.servicebus.samples.autoforward;

import org.junit.Assert;
import org.junit.Test;

public class AutoForwardTest {

    @Test
    public void run() throws Exception {
        Assert.assertEquals(0,
                AutoForward.runApp(new String[0], (connectionString) -> {
                    AutoForward app = new AutoForward();
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