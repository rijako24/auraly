#!/bin/sh
set -eu
cd /home/site/wwwroot
export PLAYWRIGHT_BROWSERS_PATH=/home/auraly-pdf-browsers
# Chromium's revision is pinned by the published Microsoft.Playwright package.
# /home caches the browser; container OS libraries must be present on each boot.
chmod +x .playwright/node/linux-x64/node
.playwright/node/linux-x64/node .playwright/package/cli.js install --with-deps --only-shell chromium
exec dotnet Auraly.Api.dll
