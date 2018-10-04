package com.microsoft.azure.servicebus.samples.sasauthorization;

import org.junit.Assert;

public class SasAuthorizationTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                SasAuthorization.runApp(new String[0], (connectionString) -> {
                    SasAuthorization app = new SasAuthorization();
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