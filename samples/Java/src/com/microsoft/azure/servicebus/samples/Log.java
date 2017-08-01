// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
package com.microsoft.azure.servicebus.samples;

import java.text.DateFormat;
import java.text.SimpleDateFormat;
import java.util.Date;

public class Log {
    public static boolean turnOffDateLogging = false;

    public static void log(String str, Object... args)
    {
        Object[] argArr = new Object[args.length + 1];

        if (!turnOffDateLogging)
        {
            DateFormat dateFormat = new SimpleDateFormat("yyyy/MM/dd HH:mm:ss");
            Date dt = new Date();
            argArr[0] = dateFormat.format(dt) + " ";
        }
        else
        {
            argArr[0] = "";
        }

        for(int i=0; i<args.length; i++)
        {
            if (args[i] instanceof Exception)
            {
                Exception ex = (Exception)args[i];
                StackTraceElement[] ste = (ex).getStackTrace();
                String toString = ex.getClass().toString() + ' ' + ex.getMessage() + '\n';
                for(StackTraceElement stee: ste)
                {
                    toString = toString.concat(stee.toString()) + '\n';
                }

                argArr[i+1] = toString;
            }
            else
            {
                argArr[i+1] = args[i];
            }
        }

        System.out.println(String.format("%s" + str, argArr));
    }
}
