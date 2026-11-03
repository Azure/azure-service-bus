package com.microsoft.azure.servicebus.samples.jmstopicquickstart;

import org.junit.Assert;

public class JmsTopicQuickstartTest {
    @org.junit.Test
    public void parsesNamespaceWithoutConnecting() {
        String configured = System.getenv("SB_SAMPLES_NAMESPACE");
        Assert.assertEquals(0, JmsTopicQuickstart.runApp(new String[] {"-n", "example.servicebus.windows.net"}, host -> {
            Assert.assertEquals(configured == null ? "example.servicebus.windows.net" : configured, host);
            return 0;
        }));
    }

    @org.junit.Test
    public void rejectsUnknownArgumentsWithoutConnecting() {
        Assert.assertEquals(3, JmsTopicQuickstart.runApp(new String[] {"--unknown"}, host -> {
            Assert.fail("Must reject invalid arguments before invoking the sample.");
            return 0;
        }));
    }

    @org.junit.Test
    public void runApp() throws Exception {
        org.junit.Assume.assumeTrue("Live test requires -Dservicebus.integration=true",
                Boolean.getBoolean("servicebus.integration"));
        Assert.assertEquals(0,
                JmsTopicQuickstart.runApp(new String[0], (connectionString) -> {
                    JmsTopicQuickstart app = new JmsTopicQuickstart();
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