# nettrace

a minimalist, passive network monitor for windows, built with winui 3 and .net 10.

- **overview**: network status, tcp/udp counts, processes, interfaces, realtime upload/download graph, recently detected connections
- **connections**: live tcp connections and udp endpoints through the ip helper api (`GetExtendedTcpTable` / `GetExtendedUdpTable`), refreshed every 2 seconds, searchable and sortable
- **processes**: per-process endpoints, executable path where windows allows access
- **dns lookup**: forward and reverse lookups, ipv4 and ipv6, cached results
- **history**: every observed endpoint recorded locally in sqlite, with search, filters and csv/json export
- **settings**: refresh and sampling intervals, interface, retention, theme, startup page

traffic figures are system-wide interface counters. windows does not expose per-process bandwidth to unprivileged apps, so none is shown.

## privacy

nettrace only reads system tables. no telemetry, no packet capture, no firewall changes. settings and history stay in `%LOCALAPPDATA%\nettrace`.

## download

get `nettrace.exe` from the [releases page](https://github.com/slynxxx/nettrace/releases). it is a single self-contained x64 executable: no installer, no admin rights, no .net runtime required.

## credits

built by [slynxxx](https://github.com/slynxxx). [claude](https://claude.ai) helped fix bugs.

## build

requires the .net 10 sdk on windows 10 1809 or later.

```
dotnet run --project nettrace/nettrace.csproj -p:Platform=x64
```

single-file publish:

```
dotnet publish nettrace/nettrace.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
```
