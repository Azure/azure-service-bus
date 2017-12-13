package com.microsoft.azure.servicebus.samples.timetolive;

import org.junit.Assert;

public class TimeToLiveTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                TimeToLive.runApp(new String[0], (connectionString) -> {
                    TimeToLive app = new TimeToLive();
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