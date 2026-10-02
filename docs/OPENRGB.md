# Internal PC lighting with OpenRGB

LightSync can control individual RAM, GPU, motherboard and fan controllers exposed
by OpenRGB. The component must provide a **Direct** mode with per-LED colours.
Check your exact hardware in the [OpenRGB device list](https://openrgb.org/devices.html).
This controls RGB lighting; fan speed and temperature monitoring are separate features.

## Start the SDK server

Install OpenRGB. The desktop application opens it with its SDK server enabled
when local discovery or a device connection needs it. For CLI use, enable the
SDK server in OpenRGB, or run:

```bash
openrgb --server
```

Keep OpenRGB running while using LightSync. The default endpoint is
`127.0.0.1:6742`. For a different host or port, configure the same endpoint in both
applications. OpenRGB handles hardware access; LightSync connects over TCP.

## Desktop

1. Open **Devices** and click **Discover PC components**. LightSync checks the SDK
   server first. If local OpenRGB is not running, it opens the OpenRGB window with
   its SDK server enabled and waits up to 30 seconds for hardware detection.
   The server can accept connections before its controllers are ready; LightSync
   waits for detection completion on SDK 6, or a stable nonempty list on older SDKs.
2. Each component gets a saved profile with its controller name, serial and
   location. Existing profiles are retained. Components without usable Direct
   mode are added with sync disabled and cannot connect through this adapter.
3. Select a profile and click **Connect / inspect** in **Settings & pairing**.
4. Use RGB presets, selected-LED tests and the chase to check its lighting.
5. Select its profile under **Audio sync** or **Screen sync** to synchronize it.

For a remote server, add a profile, select the **OpenRGB** adapter and enter the
server host and port before clicking **Discover PC components**. OpenRGB does not
use Nanoleaf pairing, tokens or panel IDs.

OpenRGB must be installed on PATH, alongside LightSync on Windows, or in the
standard Program Files / macOS Applications folder. LightSync reuses a running
server. If OpenRGB is already open but its SDK server is disabled, enable
**SDK Server** in that window; LightSync waits without opening a second copy.
Automatic launching applies to loopback addresses (`localhost`, `127.0.0.1`,
`::1`); remote SDK servers must be started on their own computer. OpenRGB remains
running when LightSync closes.

On Linux and macOS, automatically launched OpenRGB writes stdout and stderr to
`~/.config/light-sync/logs/openrgb-*.log` (under `XDG_CONFIG_HOME` when set).
Startup errors include the specific log path. The child owns the file handles,
so it can keep logging after LightSync closes. Windows uses a GUI launch without
a new console window; its own OpenRGB diagnostics remain available there.

OpenRGB can log `recv_select failed receiving magic, closing listener` when an
SDK client disconnects normally. This closes that client's listener, rather
than shutting down the SDK server. LightSync's availability checks exchange SDK
packets and do not enumerate or change lights. OpenRGB's controller lifecycle
and plugin metadata warnings are retained in the launch log; investigate those
in OpenRGB if hardware is missing or does not respond.

Discovery shows progress and results beneath the buttons, including when
OpenRGB is opening or its server is still starting. A connection error
identifies the endpoint and explains how to enable the SDK server. If discovery
connects but finds no components, check OpenRGB's own device list first: LightSync
can only enumerate the controllers that server exposes. Do not start the server
with `--nodetect` when discovering local hardware. Previously saved controllers
are counted in the result and keep their existing profiles.

Controller name and serial match exactly, including case and spaces. Location
distinguishes controllers with the same identity, such as two RAM modules without
serial numbers. Linux device paths can change after a restart: if the saved
location no longer matches, LightSync accepts the controller only when its name
and serial identify exactly one device. A serial mismatch is never ignored.
Location-only selectors still require an exact location. If the server exposes
only one controller, selectors can be empty. The location editor accepts multiple
lines because OpenRGB can include line breaks in its metadata.

If connection reports no matching controller, discover PC components again and
check the selected profile's name and serial against OpenRGB's current device
list. For identical components with stale locations, choose the freshly discovered
profile; LightSync refuses to guess which component to control. An empty server
reports hardware detection guidance separately from a missing profile.

Master device brightness is not implemented for OpenRGB. Audio and screen
brightness processing remain available. Exact RGB tests preserve channel bytes.

## CLI

```bash
light-sync discover --adapter openrgb
light-sync discover --adapter openrgb --host 192.168.1.20 --port 6742
```

Discovery prints a configuration for each controller. Copy its `device` section
into your configuration, for example:

```json
{
  "device": {
    "adapter": "openrgb",
    "settings": {
      "host": "127.0.0.1",
      "port": "6742",
      "controllerName": "Your exact controller name",
      "serial": "Your controller serial",
      "location": "Your controller location"
    }
  }
}
```

Omit selector fields the controller does not supply. `timeoutMilliseconds` is
optional and defaults to 3000; allowed values are 100–60000.

```bash
light-sync test-device
light-sync test-color red
light-sync run
```

For `test-stream` and screen sync, set `mapping.zoneCount` to the LED count
reported by `test-device`. Shorter frames black out the remaining LEDs.

## Connection and protocol behavior

The adapter uses managed .NET sockets without P/Invoke or native marshalling.
[OpenRGB.NET](https://github.com/diogotr7/OpenRGB.NET) is an available third-party
C# client, but its synchronous request API does not expose cancellation. LightSync
uses a small async client to bound requests and cancel stalled operations.

The client negotiates [SDK protocol](https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md)
versions 1–4. Servers supporting newer versions negotiate down to 4. Protocol 0
is unsupported. A separate startup-only connection can negotiate up to SDK 6
to observe hardware detection; it does not change the metadata or colour protocol.
Older SDKs do not report detection completion, so automatic startup waits for a
nonempty list unchanged for one second. This is a heuristic: a long pause in an
older server's scan can expose a partial list, and an empty older server reaches
the startup timeout. Retry discovery once OpenRGB has finished scanning.
Each connected profile owns a separate TCP connection, so
disconnecting one component does not close another component's connection.

Profiles save controller metadata rather than a device-list index. Every connect
enumerates and resolves that metadata again. A device-list change invalidates an
established connection's indices: disconnect and reconnect before sending more
colours. A timeout or broken TCP connection likewise requires reconnecting.
The adapter does not automatically resize zones or change hardware configuration.

Tests use an in-process SDK server and a recorded protocol 4 controller response.
Hardware compatibility and physical RGB output still require verification on
the actual component.

## Validation

On 2026-10-03, local OpenRGB 1.0 exposed SDK 6. Starting with OpenRGB stopped,
the actual Application launcher opened its GUI and SDK server, waited for
hardware detection, and discovered six controllers: two Corsair Vengeance RGB
Pro DDR4 modules, an ASUS RTX 4070 Ti, an ASUS TUF GAMING X570-PRO motherboard,
a Logitech G502 mouse and a Yeti GX microphone. A second startup call reused
the server without another launch. Enumeration reported per-LED Direct support
for all six; this check did not send colours or validate physical LED output.

Machine-independent tests cover server reuse, one launch across concurrent
requests, loopback and remote endpoints, executable arguments, process exit,
startup failure, cancellation, timeout, detection completion with an empty or
partial initial list, malformed notifications and older SDK startup behavior.
See [ADR 0001](adr/0001-openrgb-integration-and-startup.md) for the design record.
Launch diagnostics and controller resolution are documented in
[ADR 0002](adr/0002-openrgb-process-output.md) and
[ADR 0003](adr/0003-openrgb-controller-identity.md).

A subsequent launch found all six controllers and confirmed that saved USB
locations can differ between runs. Automated tests cover reconnection after
location changes, routing to fresh indices, exact name and serial matching,
duplicate RAM identities, multiline locations and refusal to send control
commands when the current location cannot disambiguate a profile.
