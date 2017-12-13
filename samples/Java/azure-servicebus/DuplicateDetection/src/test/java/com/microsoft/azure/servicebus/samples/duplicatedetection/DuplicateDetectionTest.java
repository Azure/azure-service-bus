package com.microsoft.azure.servicebus.samples.duplicatedetection;


import org.junit.Assert;

public class DuplicateDetectionTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                DuplicateDetection.runApp(new String[0], (connectionString) -> {
                    DuplicateDetection app = new DuplicateDetection();
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