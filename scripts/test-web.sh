#!/usr/bin/env bash
# Unit-test the viewer's pure logic (wwwroot/core) with node's built-in runner.
# Runs in a container so no host Node install is needed.
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker run --rm -v "$PWD:/src" -w /src/src/Sintro.ResultViewer.Api/wwwroot \
    node:24-alpine node --test "tests/*.test.js"
