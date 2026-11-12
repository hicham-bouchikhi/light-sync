#!/usr/bin/env bash
# Derive the release version from git tags.
#
# The newest reachable vMAJOR.MINOR.PATCH tag is the release base. A commit that carries
# the tag is a release; every later commit is a prerelease of the next patch, numbered by
# the commit count since that tag. The count is what makes each push a distinct, still
# increasing version, without inventing numbers that cannot be traced back to code.
#
# Prints `key=value` lines, ready to append to $GITHUB_OUTPUT:
#
#   version=1.4.0            is_release=true     (tag v1.4.0 on HEAD)
#   version=1.4.1-preview.5  is_release=false    (5 commits after v1.4.0)
#
# Requires full history: actions/checkout with fetch-depth: 0.
set -euo pipefail

if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
    echo "error: shallow clone; fetch full history first" >&2
    exit 1
fi

# Highest release tag reachable from HEAD. Sorted by version, not by date, so tagging an
# older commit cannot make the version go backwards. Prerelease tags are not release bases
# and are skipped rather than rejected: only vMAJOR.MINOR.PATCH counts.
base=""
while IFS= read -r tag; do
    if [[ "${tag}" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        base="${tag}"
        break
    fi
done < <(git tag --merged HEAD --list 'v*' --sort=-v:refname)

if [ -z "${base}" ]; then
    # No release yet: the first tag becomes v1.0.0, so previews start at 1.0.1.
    base_version="1.0.0"
    height=$(git rev-list --count HEAD)
else
    base_version="${base#v}"
    height=$(git rev-list --count "${base}..HEAD")
fi

IFS='.' read -r major minor patch <<<"${base_version}"

if [ "${height}" -eq 0 ]; then
    version="${base_version}"
    is_release="true"
else
    version="${major}.${minor}.$((patch + 1))-preview.${height}"
    is_release="false"
fi

echo "version=${version}"
echo "is_release=${is_release}"
echo "base=${base:-none}"
echo "height=${height}"

# Human-readable summary on stderr so the values above stay machine-parsable.
echo "version ${version} (base ${base:-none}, ${height} commit(s) since)" >&2
