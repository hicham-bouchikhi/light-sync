# CLI and desktop feature parity

Both frontends use `config.json` and `devices.json` in the LightSync configuration
directory. If profiles do not exist, the legacy `device` section is imported once.
CLI import persists the ID so commands can select it reliably. `--config PATH`
uses `devices.json` beside that file; `--profiles PATH` overrides the profile store.
Custom configurations also keep their PID file beside the configuration, so use
the same `--config` when running `stop` from another terminal.

`run` synchronizes every enabled profile by default. Select particular outputs
with `--device ID` or an exact, unique profile name; repeat the option for multiple
outputs. Explicit selection also allows a disabled profile. Inspection, pairing,
brightness and LED tests require one profile; use `--device` when several exist.

## Controls

| Desktop feature | CLI equivalent |
|---|---|
| Device list / settings / add / edit / remove | `devices list`, `devices show`, `devices add`, `devices update`, `devices remove` |
| Audio sync membership | `devices enable` / `devices disable`; `devices update --enabled true\|false` |
| Connection settings / LED address order | `devices update --setting KEY=VALUE`; repeat for host, port, ledMapping, controllerName, serial, location, tokenEnvironmentVariable, etc. |
| Clear a connection setting | `devices update --unset-setting KEY` |
| Discover and add Nanoleaf | `discover --save` |
| Discover and add PC components | `discover --adapter openrgb --save` |
| Pair and save a profile token | `pair --device ID --save` |
| Connect / inspect | `test-device --device ID` |
| Disconnect / black out | `stop` for a running CLI session; `blackout` for enabled or explicitly selected outputs while idle |
| Device master brightness | `brightness 75 --device ID` (apply and save); `devices update --brightness 75` (save only) |
| RGB presets / exact RGB | `test-color red`; `test-color '#1180FF'`; `test-color 17,128,255` |
| Apply to selected LED | `test-color red --zone 3 --background white` |
| Isolate selected LED | `test-color red --zone 3 --isolate` |
| Set several LEDs independently | `test-color black --led 1=red --led 2=17,128,255` |
| Chase / verify order | `test-chase --colour white` (700 ms per LED by default) |
| Playback source list / refresh | `list-audio-sources` |
| Start audio / multiple devices | `run`; `run --device ID1 --device ID2` |
| Audio response, gain, brightness, motion, smoothing, silence threshold, colour | `config set audio.KEY VALUE` or `run --set audio.KEY=VALUE` |
| Audio visualizer | Live terminal LED strip, spectrum bars, level, bass, mids and treble; `run --preview` needs no device |
| Lighting mode: hold a colour, pulse or rainbow | `run --source ambient`; `config set lighting.mode static\|pulse\|rainbow` |
| Lighting colour and output brightness | `config set lighting.color 128,64,255` (also `#RRGGBB`, `black`); `config set lighting.brightness 0.8` |
| Lighting speed and rainbow direction | `config set lighting.speed 1.5`, `lighting.direction left-to-right\|right-to-left` |
| Lighting preview without devices | `run --source ambient --preview`; combine with `--seconds` for a timed check |
| Choose a new screen / region | `run --source screen --select-area`; `select-area` saves a selection without running sync |
| Use saved capture area | `run --source screen` |
| Demo source | `run --source screen --demo` |
| Screen preview only | `run --source screen --preview` or `run --source screen --demo --preview` |
| Screen output selection | Enabled profiles by default, or repeat `--device` for selected outputs |
| Preview sampling zones / columns / rows / reverse order | `config set mapping.zoneCount 24`, `mapping.layout vertical\|horizontal`, `mapping.direction left-to-right\|top-to-bottom`, `mapping.reverse true\|false` |
| Frame rate | `config set capture.fps 30` |
| Screen brightness / audio intensity / playback source | `config set screen.brightness 0.75`, `screen.audioIntensityEnabled true\|false`, `screen.audioSource SOURCE` |
| Screen colour smoothing / saturation / averaging | `config set processing.smoothing 0.2`, `processing.saturation 1`, `processing.averaging luminance-weighted\|mean\|colour-weighted` |
| Stop and black out | `stop`, Ctrl+C, or `run --seconds 10` |

Graphical image previews, clickable overlays and navigation remain desktop UI.
The CLI exposes LED colours and audio measurements in the terminal. LED numbers
in test commands start at **1**. A selected-LED test starts the other LEDs at the
specified background (black by default); `--isolate` always clears them.
Repeat `--led NUMBER=COLOUR` to edit several LEDs in the same requested frame
while preserving every other channel.
RGB tests refresh exact channel values at 10 Hz for 10 seconds by default; use
`--seconds 0` to hold until stopped. Ending a test blacks out the device.

## Example workflow

```bash
light-sync devices list
light-sync discover --adapter openrgb --save
light-sync devices add --name Lamp --adapter nanoleaf \
  --setting host=192.168.1.24 --setting port=16021
light-sync pair --device Lamp --save
light-sync test-device --device Lamp
light-sync brightness 100 --device Lamp
light-sync test-color 17,128,255 --device Lamp --zone 3 --isolate --seconds 5
light-sync test-chase --device Lamp

light-sync config set audio.mode rainbow
light-sync config set audio.gain 15
light-sync config set audio.motion 1.5
light-sync config set audio.smoothing 0.35
light-sync run

light-sync config set lighting.mode static
light-sync config set lighting.color 128,64,255
light-sync config set lighting.brightness 0.8
light-sync run --source ambient
light-sync run --source ambient --preview
```

Settings also accept exact audio colours (`audio.color 128,64,255`), lighting
colours (`lighting.color 128,64,255`, `black`) and JSON orders
(`mapping.customOrder '[2,1,0]'`). Values use the same units as the JSON
configuration: brightness and smoothing are fractions, master brightness is an
integer percentage. Unknown keys, invalid values and ambiguous profile names fail
with an error. Profile settings name token environment variables or secret files;
tokens belong in those files or variables, outside configuration and profiles.
Pairing saves a separate owner-only token file and environment-variable name per
profile, matching the desktop.

## Live settings and output behavior

By default the CLI reloads saved settings every half second. During audio sync,
audio tuning applies live. Changing playback source, enabled profiles, connection
settings or master brightness briefly restarts capture and releases old devices.
Removing or disabling the last output stops sync. Explicit `--device` selection
takes precedence over enabled flags; removing that profile still releases it.

During screen sync, steady brightness and the audio-intensity toggle apply live.
Changing device profiles stops the screen session. Stop and restart to apply
capture, layout, colour processing or playback-source changes. `--watch false`
disables reloading. Repeatable `--set KEY=VALUE` overrides are temporary and remain
in force during reload; `config set` saves tuning for both frontends. Preview and
`--dry-run` are read-only: they never save a capture selection or take the session
PID lock.

Ambient lighting needs neither audio nor screen capture. `run --source ambient`
holds the configured effect on every enabled profile; `--preview` shows the strip
without connecting devices. Mode, colour, brightness, speed and direction apply
live while the session runs. Changing the selected profiles restarts the effect
for the new selection. Device master brightness is held at 100% during the effect
and restored from each profile before the final blackout, so lighting brightness
is applied once. Black is a valid static colour and switches the lights off.

Audio and screen sync use each output's full LED count independently, including
mixed sizes. `mapping.zoneCount` controls preview-only output. A custom mapping
order must match the device's zone count; hardware address order can instead be
set separately in each profile. Screen output uses the same 75% baseline,
optional playback boost, post-smoothing RGB scaling, capture recovery and bounded
multi-device pipeline as the desktop. `processing.brightness` is replaced by
`screen.brightness` for screen sync to avoid multiplying brightness twice.

Master brightness is set to 100% during screen sync, then restored from profiles
before final blackout. Missing playback capture falls back to steady brightness
and reports the error while screen output continues. Local OpenRGB discovery and
connections automatically start its SDK server when needed; remote servers must
already be available. Profiles without per-LED streaming remain excluded when
discovered, as in the desktop.

Pairing, inspection, brightness application and LED tests require stopping a
running CLI session first. Saved profile changes and live tuning can be made from
another terminal. Run either frontend on a set of lights at a time; the CLI's PID
file controls CLI sessions.

## Verification

`tests/LightSync.Cli.Tests` covers command parsing, shared profile persistence,
selection, exact RGB, LED isolation, chase pacing, multi-device screen output,
master restoration before blackout, playback failure, preview without connections,
live audio tuning and removal of active outputs. Ambient lighting is covered from
mapper maths through CLI lifecycle: static hold, pulse bounds and period, rainbow
direction and live option changes. It uses temporary configurations,
generated capture and recording adapters, with no real credentials or hardware.
The existing Core and adapter suites cover shared effects, capture recovery and
protocol bytes. Native AOT publication is checked separately.

Verified for this change: Release build with zero warnings, **487 tests passed**
with no skips, Native AOT publish, and native CLI smoke tests for configuration,
profiles, demo preview, multi-device output, RGB/chase tests, background shutdown
and PID cleanup. Ambient lighting adds exact-colour, pulse-bound and rainbow
travel/direction tests plus CLI parity for `lighting.*`, static hold, live tuning,
profile-change restart, master restoration, zero-zone errors and preview without
a device. Physical device output and live portal selection were not exercised in
these checks.
