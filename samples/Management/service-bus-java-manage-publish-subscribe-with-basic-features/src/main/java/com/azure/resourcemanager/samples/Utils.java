// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

package com.azure.resourcemanager.samples;

import com.azure.resourcemanager.servicebus.models.AuthorizationKeys;
import java.io.PrintStream;

public final class Utils {
    private Utils() {
    }

    public static void print(AuthorizationKeys keys, PrintStream out) {
        // Never access key fields, even when diagnostic logging is requested.
        out.println("Authorization keys: [REDACTED]");
    }

    public static void printFailure(String operation, Throwable failure, PrintStream err) {
        // SDK exception messages and causes can contain request headers or credentials.
        err.println(operation + " failed (" + failure.getClass().getSimpleName()
            + "). Details suppressed to protect credentials.");
    }
}
