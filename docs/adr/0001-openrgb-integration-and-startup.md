# ADR 0001: OpenRGB integration and local startup

Status: Accepted

Date: 2026-10-03

## Context

LightSync needs to manage internal RGB lighting alongside existing devices
without introducing hardware or vendor concerns into capture and colour
processing. OpenRGB already handles hardware access and exposes controllers
through its [TCP SDK](https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md).
The existing `ILightDevice` contract can represent each controller.

Desktop discovery initially depended on a manually started SDK server. With
OpenRGB installed but stopped, clicking discovery returned no profiles. Moving
feedback beside the discovery buttons made the connection failure visible,
but the desktop should also open the local dependency when it is needed.

A real startup test revealed another boundary: the SDK socket can become
reachable before hardware detection finishes. Immediate enumeration then
reports zero controllers even when OpenRGB will shortly detect supported hardware.

## Decision

Use a managed, asynchronous TCP adapter in `LightSync.Devices.OpenRgb`.
Keep bounded requests, cancellation, packet validation and reusable frame buffers
in that project. Native marshalling is unnecessary. A community .NET client
exists, but the synchronous request API reviewed for the initial integration did
not provide the cancellation behavior required by LightSync's owned connections.

Represent one controller as one lighting profile. Persist exact name, serial and
location selectors, then resolve fresh runtime indices on each connection.
Normal enumeration and colour output negotiate SDK versions 1–4. Invalidate
runtime indices on device-list changes and require reconnection. Require per-LED
Direct mode; keep unsupported controllers visible with sync disabled.

Put local application startup in `OpenRgbServerLauncher` in Application.
Desktop discovery and profile connection use it before enumeration or output.
Neither Core, the adapter, nor CLI discovery starts local processes.

The startup policy is:

1. Probe the configured TCP endpoint with a bounded, cancellable connection.
   Reuse a reachable endpoint without process inspection or another launch.
2. If unreachable and remote, report the endpoint and the need to start its
   SDK server on that computer. Automatic launch is limited to loopback hosts.
3. If local OpenRGB is already running, wait for its server and show guidance
   to enable SDK Server in that window. Do not open another hardware owner.
4. Otherwise locate the installed executable and launch its GUI with the SDK
   server, configured host and port, and hardware detection enabled. Pass argv
   entries through `ProcessStartInfo.ArgumentList` without a shell.
5. Wait for the socket and initial hardware scan within a 30-second timeout.
   A separate startup connection negotiates up to SDK 6 to receive detection
   notifications. It reads controller counts but never sends colours or uses
   version 6 IDs as lighting addresses. SDK 1–5 fall back to a nonempty list
   unchanged for one second because they lack completion notifications.
6. Continue normal discovery or connection once ready. Report launch failure,
   process exit, timeout and missing installation through the existing UI.

Serialize startup checks within one LightSync process. Cancellation stops
waiting, and releasing the process handle does not stop OpenRGB. OpenRGB remains
available to other clients after LightSync closes.

## Consequences and limits

Users can discover local PC components without first starting OpenRGB manually.
Startup and discovery feedback stay beside the buttons, which share one row
above the device list. Already saved devices remain counted in discovery results.

OpenRGB must already be installed on PATH or at a supported standard executable
location. LightSync does not install it, elevate privileges, change hardware
permissions, rewrite its settings or enable a disabled SDK server in an existing
window. Custom ports must match the selected profile; remote servers remain
managed on their own computer.

Older SDK readiness is a compatibility heuristic. A scan paused longer than
one second after finding some controllers can expose a partial list; a server
with no controllers can reach the startup timeout. The user can retry discovery
after scanning completes. Startup serialization applies within one application
process, not across independently launched LightSync instances.

Per-profile SDK ownership, optional brightness capabilities and flat colour
frames remain unchanged. Fan speed, temperature monitoring, hardware topology
and shared SDK transport require separate decisions if they become necessary.

## Alternatives considered

- Manual SDK startup only: simple for CLI use, but leaves desktop discovery
  incomplete for an installed local dependency.
- Launch OpenRGB on every click: opens duplicate applications and can create
  competing hardware owners.
- Treat a successful TCP connection as full readiness: the live test returned
  zero controllers before the scan completed.
- Launch from `ILightDevice` or Core: hides process side effects inside lighting
  operations and applies desktop policy to every consumer.
- Fully migrate colour output to SDK 6 for startup notifications: unnecessary;
  startup observation can use a separate connection while the tested output
  protocol remains unchanged.

## Validation

The automated suite uses injected process and endpoint behavior plus a real
in-process TCP peer, without local hardware or credentials. It covers reuse,
concurrent requests, remote hosts, loopback hosts, arguments, process exit,
launch errors, timeout, cancellation, malformed detection packets and SDK
completion with an initially empty or partial controller list. Existing byte-level
colour, selector, reconnection and device-list invalidation tests still apply.

On 2026-10-03, the real launcher opened local OpenRGB 1.0 from a stopped state.
After SDK 6 detection completion, normal enumeration found all six controllers.
A second call reused the server. See the [validation record](../OPENRGB.md#validation).
Physical colour output and device timing remain separate hardware validation.
