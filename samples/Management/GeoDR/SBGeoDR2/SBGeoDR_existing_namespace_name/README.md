# Geo-DR using an existing namespace name as the alias

Follow the [shared setup and operation guide](../../README.md).
Set `SERVICEBUS_ALIAS` to the original `SERVICEBUS_PRIMARY_NAMESPACE` and set
`SERVICEBUS_ALTERNATE_PRIMARY_NAME` to the alternate primary name before `pair`.
This variant passes `AlternateName` to the current ARM SDK pairing API.

Build here with `dotnet build`; `dotnet run -- --help` performs no Azure calls.
The original namespace-name endpoint becomes the alias. Check Azure's reported
namespace and alias state before subsequent operations; do not assume that a
completed API request means all metadata has replicated.

After breaking pairing without failover, do not delete the primary alias.
This variant deliberately rejects that command. After failover, review the
recovery plan before explicitly deleting the secondary alias.
