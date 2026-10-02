# ADR 0004: Screen audio intensity and shared brightness

Status: Accepted

Date: 2026-10-03

## Context

Screen sync needs a common 75% brightness for all lights, with an optional
sound-intensity response during louder playback. The preview must show the same
requested colours as the outputs. Some adapters have master brightness and
OpenRGB does not, so per-device brightness commands cannot implement the shared
effect consistently. Audio colour sync already has independent tuning.

## Decision

Call the feature **Audio intensity** and expose its live toggle beside steady
brightness in **Screen sync & preview**. Use 75% as the default steady level,
boosting toward 100% during loud playback. Enable the effect by default; existing
configurations receive these defaults through a separate `screen` section.
Persist the selected playback source and keep audio colour tuning independent.

Compute RMS from interleaved digital samples and convert it with
`20 * log10(RMS)` to dBFS, bounded below at −120 dBFS. Map −45 to −6 dBFS onto
the headroom between the steady level and 100%. Smooth with an 80 ms attack and
350 ms release. A zero steady level stays off; full brightness stays bounded.
Use one shared envelope and snapshot it once per captured screen frame.

Apply the scale to RGB after screen colour smoothing, preserving the screen's
palette. Use unit brightness in the inner colour processor and 100% device master
brightness during the screen session to avoid multiplying separate brightness
levels. The preview records the same scaled RGB values. On shutdown,
restore each profile's saved master brightness for subsequent streams, then black
out all outputs. Blackout comes last because master commands can power lamps on.

Own playback capture in `ScreenAudioMonitor`, using `IAudioCapture`. Serialize
toggle and disposal transitions, cancel blocked reads and await capture cleanup
before opening a replacement. Disabled effects do not capture audio. Capture
failure restores steady brightness, reports the error and leaves screen sync
running. Toggling off and on retries capture. Normal screen stop or window close
releases both capture sources. CLI screen mode retains its existing policy.

## Consequences and limits

Every selected device uses the same intensity, including devices with different
LED counts or without master brightness. Brightness and the effect toggle apply
live; source selection requires stopping. Changes persist across sessions.
The desktop effect depends on Linux PulseAudio/PipeWire playback capture and
`parec`. Digital dBFS is not calibrated acoustic sound pressure. The fixed range
responds to recording and playback level; different mixes may need different
steady brightness. RGB scaling describes requested output, not measured light.

## Validation

Tests cover configuration defaults and round trips, dB measurement, stereo power,
louder/quiet responses, attack/release, invalid inputs, intensity bounds, live
options, frame-consistent multi-device colours, failure fallback, capture reuse,
toggle cleanup, cancellation and concurrent shutdown. Headless desktop review
uses generated screen frames, simulated devices and synthetic playback helpers;
it accesses no real lighting hardware or credentials.
