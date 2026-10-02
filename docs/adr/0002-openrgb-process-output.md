# ADR 0002: OpenRGB process output and availability checks

Status: Accepted

Date: 2026-10-03

Updates [ADR 0001](0001-openrgb-integration-and-startup.md).

## Context

The launched OpenRGB process inherited LightSync's terminal. Its controller
lifecycle warnings, plugin warnings and SDK disconnect messages appeared as
LightSync failures even when discovery succeeded.

OpenRGB's [server implementation](https://github.com/CalcProgrammer1/OpenRGB/blob/master/NetworkServer.cpp)
logs an error when reading the next packet's magic returns zero bytes, including
an orderly client disconnect. The message closes that client's listener. Bare
TCP availability probes added extra connections without sending SDK packets.
Normal discovery connections can still produce the same upstream disconnect log.

OpenRGB must remain usable after the launcher's process handle is disposed and
after LightSync closes. Redirecting to parent-owned pipes would require continued
draining to avoid backpressure and would couple diagnostic lifetime to LightSync.

## Decision

Check availability with a bounded SDK protocol handshake. Do not enumerate
controllers or send control packets. A listening socket with no SDK response is
unavailable; malformed protocol responses remain errors rather than triggering
an automatic launch that could compete for an occupied port. Caller cancellation
propagates separately from unavailable-server results.

On Linux and macOS, launch through `/bin/sh` with a fixed wrapper:

```sh
exec "$@" >>"$LIGHTSYNC_OPENRGB_LOG" 2>&1
```

Pass the executable and all SDK arguments as separate argv entries, and pass
the log path in a child-only environment value. Do not interpolate paths, hosts
or ports into the script. `exec` preserves the launched process identity and exit
status. Validate log directory access before starting the child. Use a unique
log file per launch under LightSync's configuration `logs` directory.

Regular file descriptors belong to OpenRGB and do not need a parent reader.
Startup timeout and early-exit messages include the log path. Exit codes 126
and 127 explain installation, PATH or executable access problems. Do not change
OpenRGB's logging level, overwrite its configuration, or filter its warning text.

Windows retains a direct GUI launch with `CreateNoWindow`; the Unix wrapper and
launch logs are specific to Unix. Existing OpenRGB processes remain unchanged.

## Consequences

LightSync's terminal stays readable, while upstream warnings remain available
for diagnosis. The wrapper cannot interpret shell syntax contained in the log
path or argv values. No native file descriptor interop or background pipe-draining
service is required. Startup still needs an installed OpenRGB executable.

Each launch creates a new local log file; users can remove old files when no
longer needed. An OpenRGB process already launched with inherited output needs
to be closed and relaunched to receive the new output policy.

## Validation

Tests verify a real SDK handshake without controller enumeration or lighting
commands, stalled responses, malformed responses and cancellation. A Unix stub
process writes both output streams to a path containing spaces, quotes and shell
syntax. Tests verify literal path handling, an unchanged exit code, an empty
parent console, and continued output exceeding a pipe buffer after the launcher
disposes its process handle. These tests do not access real lighting hardware.
