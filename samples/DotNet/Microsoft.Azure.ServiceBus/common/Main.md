#Messaging Samples Main.cs

All Messaging samples include a shared C# file, [Main.cs](Main.cs) that resides in the 
"common" repository folder. It implements a class <code>AppEntryPoint</code> with 
a static <code>Main</code> method that the CLR picks up as the main entry point for
the app.

The reason for having a shared entry point to simplify and unify the 
handling of the required configuration information for Service Bus that is created 
by the [setup script](../README.md) and held in the user profile directory.

The implementation assumes that the sample implements a class <code>Program</code>
with an instance method <code>Run()</code> and will call that method after handling 
the configuration data. The specific, invoked <code>Run()</code> method will differ
by the kind of sample and is therefore defined by an interface that <code>Program</code>
is expected to implement.

The entry point can also be used for Windows Forms apps and other apps requiring the 
<code>[STAThread]</code> declaration by defining the "STA" symbol in the project settings.  

The code first reads the settings from the "azure-msg-config.properties" file that
it expects in the root of the user's profile folder. The expected format for the text file 
are lines holding {property}={value}, separated by line-breaks. 

The settings can also be set (or overridden) with environment variables set before 
the program is invoked:

| Property               |  Description                                                         |
|------------------------|----------------------------------------------------------------------|
| SERVICEBUS_NAMESPACE   | Service Bus namespace name, without domain suffix                    |
| SERVICEBUS_ENTITY_PATH | Name of the configured entity to use for this sample                 |
| SERVICEBUS_FQDN_SUFFIX | Service Bus host name suffix, typically "servicebus.windows.net"     |
| SERVICEBUS_SEND_KEY    | SAS key value of the "samplesend" and "rootsamplesend" SAS rules     |
| SERVICEBUS_LISTEN_KEY  | SAS key value of the "samplelisten" and "rootsamplelisten" SAS rules |
| SERVICEBUS_MANAGE_KEY  | SAS key value of the "samplemanage" and "rootsamplemanage" SAS rules |


