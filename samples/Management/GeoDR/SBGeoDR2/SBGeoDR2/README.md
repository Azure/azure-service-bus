# Geo-DR with a separate alias

Follow the [shared setup and operation guide](../../README.md).
Choose `SERVICEBUS_ALIAS` distinct from both namespace names. This variant does
not supply `AlternateName` when creating the pairing.

Build here with `dotnet build`; `dotnet run -- --help` performs no Azure calls.
Use `status` for readback. All mutations require an explicit command and
`--confirm-alias <alias>`. Review the failover and message-loss limitations in
the shared guide before using a disposable test deployment.
