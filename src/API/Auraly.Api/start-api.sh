#!/bin/sh
set -eu
cd /home/site/wwwroot
export PLAYWRIGHT_BROWSERS_PATH=/home/auraly-pdf-browsers
# Chromium's revision is pinned by the published Microsoft.Playwright package.
# /home caches the browser; container OS libraries must be present on each boot.
chmod +x .playwright/node/linux-x64/node
.playwright/node/linux-x64/node .playwright/package/cli.js install --with-deps --only-shell chromium
# Fail deployment visibly if this App Service image cannot actually render a PDF.
.playwright/node/linux-x64/node <<'JS'
const { chromium } = require('./.playwright/package');
(async () => {
  const browser = await chromium.launch({ headless: true, timeout: 30000 });
  try {
    const page = await browser.newPage();
    await page.setContent('<html><body>Auraly PDF runtime</body></html>', { timeout: 10000 });
    const pdf = await page.pdf({ format: 'Letter' });
    if (pdf.subarray(0, 5).toString() !== '%PDF-') throw new Error('Invalid PDF output');
    console.log('Auraly PDF runtime ready');
  } finally { await browser.close(); }
})().catch(error => { console.error('Auraly PDF runtime failed:', error.message); process.exitCode = 1; });
JS
exec dotnet Auraly.Api.dll
