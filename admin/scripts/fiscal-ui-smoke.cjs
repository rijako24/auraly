const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");
const WebSocket = require("next/dist/compiled/ws");

const baseUrl = process.env.AURALY_UI_URL || "http://127.0.0.1:3000";
const edgePath = process.env.EDGE_PATH || "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const debugPort = Number(process.env.AURALY_UI_DEBUG_PORT || 9334);
const profile = fs.mkdtempSync(path.join(os.tmpdir(), "auraly-fiscal-ui-"));
const edge = spawn(edgePath, [
  "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
  `--remote-debugging-port=${debugPort}`, `--user-data-dir=${profile}`, "about:blank",
], { stdio: "ignore" });

const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

async function retry(action, timeout = 90_000, interval = 200) {
  const limit = Date.now() + timeout;
  let lastError;
  while (Date.now() < limit) {
    try {
      const result = await action();
      if (result) return result;
    } catch (error) {
      lastError = error;
    }
    await sleep(interval);
  }
  throw lastError || new Error("UI condition timed out");
}

class CdpClient {
  constructor(url) {
    this.url = url;
    this.sequence = 0;
    this.pending = new Map();
  }
  async open() {
    this.socket = new WebSocket(this.url);
    await new Promise((resolve, reject) => {
      this.socket.once("open", resolve);
      this.socket.once("error", reject);
    });
    this.socket.on("message", (data) => {
      const message = JSON.parse(data.toString());
      if (!message.id) return;
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message));
      else pending.resolve(message.result);
    });
  }
  send(method, params = {}) {
    const id = ++this.sequence;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }
  async evaluate(expression) {
    const result = await this.send("Runtime.evaluate", {
      expression, returnByValue: true, awaitPromise: true,
    });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text || "Browser evaluation failed");
    return result.result.value;
  }
  async navigate(url) {
    await this.send("Page.navigate", { url });
    await retry(() => this.evaluate("document.readyState === 'complete'"));
  }
  close() { this.socket.close(); }
}

function setInput(selector, value) {
  return `(() => { const input=document.querySelector(${JSON.stringify(selector)}); if(!input)return false; const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; setter.call(input,${JSON.stringify(value)}); input.dispatchEvent(new Event('input',{bubbles:true})); input.dispatchEvent(new Event('change',{bubbles:true})); return true; })()`;
}

function clickButton(label) {
  return `(() => { const button=[...document.querySelectorAll('button')].find((item)=>item.textContent.trim().includes(${JSON.stringify(label)})); if(!button)return false; button.click(); return true; })()`;
}

async function main() {
  const version = await retry(async () => {
    const response = await fetch(`http://127.0.0.1:${debugPort}/json/version`);
    return response.ok ? response.json() : null;
  });
  assert.ok(version.webSocketDebuggerUrl);
  const response = await fetch(`http://127.0.0.1:${debugPort}/json/new?${encodeURIComponent(`${baseUrl}/login`)}`, { method: "PUT" });
  const target = await response.json();
  const cdp = new CdpClient(target.webSocketDebuggerUrl);
  await cdp.open();
  await cdp.send("Page.enable");
  await cdp.send("Runtime.enable");
  await cdp.navigate(`${baseUrl}/login`);
  await retry(() => cdp.evaluate("Boolean(document.querySelector('#username'))"));
  await cdp.evaluate(`localStorage.setItem('selected_tenant_id','2368D79B-FE40-465F-B43D-E69332EE979C'); localStorage.setItem('selected_business_id','22222222-2222-2222-2222-222222222222'); true`);
  assert.equal(await cdp.evaluate(setInput("#username", "admin")), true);
  assert.equal(await cdp.evaluate(setInput("#password", "Admin123!")), true);
  await cdp.evaluate("document.querySelector('button[type=submit]').click(); true");
  await retry(() => cdp.evaluate("location.pathname.startsWith('/dashboard')"));
  await cdp.evaluate(`localStorage.setItem('selected_tenant_id','2368D79B-FE40-465F-B43D-E69332EE979C'); localStorage.setItem('selected_business_id','22222222-2222-2222-2222-222222222222'); location.href='/dashboard/settings/masters'; true`);
  await retry(() => cdp.evaluate("document.body.innerText.includes('Maestros de Auraly')"));
  assert.equal(await cdp.evaluate(clickButton("Fiscal")), true);
  await retry(() => cdp.evaluate("document.body.innerText.includes('AURALY-VISUAL-2026')"));
  assert.equal(await cdp.evaluate(clickButton("Editar resoluci\u00f3n")), true);
  await retry(() => cdp.evaluate("Boolean(document.querySelector('input[value=\"AURALY-VISUAL-2026\"]'))"));
  const actual = await cdp.evaluate(`(() => {
    const inputs=[...document.querySelectorAll('input')];
    const values=inputs.map((input)=>input.value);
    const numberValues=inputs.filter((input)=>input.type==='number').map((input)=>input.value);
    return {
      authorization: values.includes('AURALY-VISUAL-2026'),
      prefix: values.includes('FE'),
      taxId: values.includes('900123456'),
      hasAuthorizedRange: numberValues.length >= 3,
      hasFirstDianConsecutive: document.body.innerText.includes('Primer consecutivo DIAN'),
      hasTwoDates: inputs.filter((input)=>input.type==='date' && input.value).length === 2,
      environment: document.body.innerText.includes('Habilitaci\u00f3n'),
      technicalKeySafe: inputs.some((input)=>input.type==='password' && input.value===''),
      qrUrl: inputs.some((input)=>input.type==='url' && input.value.includes('document/searchqr')),
    };
  })()`);
  assert.deepEqual(actual, {
    authorization: true, prefix: true, taxId: true, hasAuthorizedRange: true,
    hasFirstDianConsecutive: true, hasTwoDates: true, environment: true,
    technicalKeySafe: true, qrUrl: true,
  });
  console.log(JSON.stringify({ fiscalResolutionUi: "passed", ...actual }));
  cdp.close();
}

main().catch((error) => {
  console.error(error.stack || error);
  process.exitCode = 1;
}).finally(async () => {
  edge.kill();
  await sleep(500);
  for (let attempt = 0; attempt < 5; attempt += 1) {
    try { fs.rmSync(profile, { recursive: true, force: true }); break; }
    catch (error) { if (error.code !== "EBUSY" || attempt === 4) break; await sleep(250); }
  }
});
