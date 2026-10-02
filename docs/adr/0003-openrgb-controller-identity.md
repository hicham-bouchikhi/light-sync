# ADR 0003: OpenRGB controller identity across location changes

Status: Accepted

Date: 2026-10-03

## Context

Discovery saves the controller name, serial and location. Requiring all three
to match exactly causes connection failures when Linux changes a USB device's
`/dev/hidraw*` path. Observed controller locations also contain line breaks.
Two RAM modules can have the same name and no serial, so accepting a name match
without checking uniqueness could control the wrong component.

## Decision

Resolve fresh SDK controller metadata on every connection:

1. Require exact matches for every supplied name and serial.
2. If supplied, use an exact location match to select among those candidates.
3. When the location is stale, accept only one candidate identified by a supplied
   name or serial. Location-only selectors remain exact.
4. Reject absent or ambiguous identities before selecting Direct mode or writing
   colours. Never fall back from a mismatched name or serial, or use a saved index.

Report an empty server separately, with hardware detection guidance. A missing
profile error identifies the endpoint, saved controller name and available count.
Let the desktop location editor accept multiline input without trimming metadata.

## Consequences and limits

Unique controllers reconnect after their OS device path changes. Identical
controllers still require a current location; rediscovery provides a new profile
without overwriting saved user choices. A unique name without a serial identifies
the model currently present, rather than proving that it is the same physical unit.
Users requiring physical identity should supply a serial or use a location-only
selector. Locations remain opaque: no parsing or guessing of HID or I2C paths.

This refines the exact location policy in [ADR 0001](0001-openrgb-integration-and-startup.md).
Runtime device-list invalidation and Direct mode requirements remain in force.

## Validation

Machine-independent tests cover changed USB paths, fresh indices, exact name
and serial requirements, duplicate RAM identities, stale ambiguous locations,
location-only selectors, empty servers and CRLF metadata round trips. An SDK peer
verifies that ambiguity sends no control commands. Live discovery found six
controllers; the diagnostic did not change lighting.
