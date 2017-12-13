package com.microsoft.azure.servicebus.samples.prefetch;

import org.junit.Assert;

public class PrefetchTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                Prefetch.runApp(new String[0], (connectionString) -> {
                    Prefetch app = new Prefetch();
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