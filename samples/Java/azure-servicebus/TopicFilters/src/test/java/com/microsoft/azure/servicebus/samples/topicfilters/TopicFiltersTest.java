package com.microsoft.azure.servicebus.samples.topicfilters;

import org.junit.Assert;

public class TopicFiltersTest {
    @org.junit.Test
    public void runApp() throws Exception {
        Assert.assertEquals(0,
                TopicFilters.runApp(new String[0], (connectionString) -> {
                    TopicFilters app = new TopicFilters();
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