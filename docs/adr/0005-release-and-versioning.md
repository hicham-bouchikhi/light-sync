# ADR 0005: Release versioning and packaging

Status: Accepted

Date: 2026-11-12

## Context

CI published the CLI as a native AOT binary and then discarded it: no uploaded
artifact, no release, no tags, and `permissions: contents: read`. The version was the
SDK default `1.0.0` with the commit sha appended, so two builds of different code
shared a version, and nothing was downloadable.

Every push to `main` should produce a new, distinguishable version. A deployment or
workflow run id is a tempting source for that number because it is unique and
increases, but it describes the build, not the code: the same commit rebuilt becomes a
different version, a cancelled run consumes a number, and the value cannot be traced
back to a revision. It also cannot be ordered against a real release.

The desktop is an Avalonia application. It was published self-contained because Native
AOT had never been attempted for it.

## Decision

Derive the version from git tags in `scripts/compute-version.sh`. The newest reachable
`vMAJOR.MINOR.PATCH` tag is the base; the commit count since that tag is the
prerelease number. A commit carrying the tag is a release, any later commit is
`<next patch>-preview.<height>`. The commit sha stays in the informational version, so
`--version` reports `1.2.1-preview.7+3fa9c21`. Downloadable versions are reproducible
from the repository alone, and SemVer precedence orders previews below the release
they precede.

Publish on every push to `main` into a single `continuous` prerelease that is deleted
and recreated, with stable asset names. A pushed `v*` tag publishes an immutable
release with generated notes. Recreating rather than editing is required because a
GitHub release is bound to a tag ref.

Ship the CLI as `light-sync-x86_64.tar.gz`. It is already one native binary; an
AppImage would add a FUSE-mounted squashfs around a file that needs no runtime. Ship
the desktop as `LightSync-x86_64.AppImage`, and publish it with Native AOT: the same
application drops from 103 MB self-contained to a 37 MB payload, which is 15 MB
compressed.

Build both on `ubuntu-22.04` so the linked glibc baseline stays at 2.35, and pin
`appimagetool` to a checksum-verified release.

## Consequences

The continuous release always shows the newest `main` build and keeps a stable asset
URL; earlier main builds are not retained as releases. Stable releases remain
immutable and are the only ones `releases/latest` reports, because it ignores
prereleases.

Packaging is not self-contained in the way the format suggests: PipeWire or
PulseAudio, `parec`, `pactl`, GStreamer with `pipewiresrc`, avahi and the screen-cast
portal are host services the image cannot carry. The AppImage removes the .NET runtime
requirement, not the system dependencies.

Native AOT for the desktop relies on Avalonia 12, compiled bindings and
source-generated JSON leaving no reflection behind; the analysers confirm this at
build time. Reflection that only executes at run time cannot be caught that way, so
CI launches the built AppImage under Xvfb and fails if it exits early. A real window is
never rendered in CI, which is the residual risk.
