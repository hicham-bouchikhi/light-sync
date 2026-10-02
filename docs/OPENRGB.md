# Internal PC lighting with OpenRGB

LightSync can control individual RAM, GPU, motherboard and fan controllers exposed
by OpenRGB. The component must provide a **Direct** mode with per-LED colours.
Check your exact hardware in the [OpenRGB device list](https://openrgb.org/devices.html).
This controls RGB lighting; fan speed and temperature monitoring are separate features.

## Start the SDK server

Install OpenRGB and enable its SDK server in the application, or run:

```bash
openrgb --server
```

Keep OpenRGB running while using LightSync. The default endpoint is
`127.0.0.1:6742`. For a different host or port, configure the same endpoint in both
applications. OpenRGB handles hardware access; LightSync connects over TCP.

## Desktop

1. Open **Devices** and click **Discover PC components**.
2. Each component gets a saved profile with its controller name, serial and
   location. Existing profiles are retained. Components without usable Direct
   mode are added with sync disabled and cannot connect through this adapter.
3. Select a profile and click **Connect / inspect** in **Settings & pairing**.
4. Use RGB presets, selected-LED tests and the chase to check its lighting.
5. Select its profile under **Audio sync** or **Screen sync** to synchronize it.

For a remote server, add a profile, select the **OpenRGB** adapter and enter the
server host and port before clicking **Discover PC components**. OpenRGB does not
use Nanoleaf pairing, tokens or panel IDs.

Discovery shows progress and results beneath the buttons. A connection error
identifies the endpoint and explains how to enable the SDK server. If discovery
connects but finds no components, check OpenRGB's own device list first: LightSync
can only enumerate the controllers that server exposes. Do not start the server
with `--nodetect` when discovering local hardware. Previously saved controllers
are counted in the result and keep their existing profiles.

Controller selectors match exactly, including case and spaces. If multiple
controllers match, connection fails until the name, serial or location selects
exactly one. If the server exposes only one controller, selectors can be empty.

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
is unsupported. Each connected profile owns a separate TCP connection, so
disconnecting one component does not close another component's connection.

Profiles save controller metadata rather than a device-list index. Every connect
enumerates and resolves that metadata again. A device-list change invalidates an
established connection's indices: disconnect and reconnect before sending more
colours. A timeout or broken TCP connection likewise requires reconnecting.
The adapter does not automatically resize zones or change hardware configuration.

Tests use an in-process SDK server and a recorded protocol 4 controller response.
Hardware compatibility and physical RGB output still require verification on
the actual component.
