package com.microsoft.azure.servicebus.samples.managingentity;

import org.junit.Assert;

public class ManagingEntityTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                ManagingEntity.runApp(new String[0], (connectionString) -> {
                    ManagingEntity app = new ManagingEntity();
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