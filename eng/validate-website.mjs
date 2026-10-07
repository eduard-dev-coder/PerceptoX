import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const site = path.join(root, 'website');
const html = fs.readFileSync(path.join(site, 'index.html'), 'utf8');
const script = fs.readFileSync(path.join(site, 'app.js'), 'utf8');
const css = fs.readFileSync(path.join(site, 'styles.css'), 'utf8');
assert.match(html, /<html lang="en">/);
assert.match(html, /<meta name="viewport"/);
assert.match(css, /prefers-reduced-motion/);
for (const match of html.matchAll(/(?:src|href)="([^"#]+)"/g)) {
  const value = match[1];
  if (/^https?:/.test(value)) continue;
  assert(fs.existsSync(path.join(site, value)), `Missing website asset: ${value}`);
}
for (const page of ['identify', 'comparison', 'groups', 'settings', 'scan']) {
  for (const theme of ['light', 'dark']) {
    const imagePath = path.join(site, 'assets', `${page}-${theme}-en.webp`);
    const webp = fs.readFileSync(imagePath);
    assert.equal(webp.toString('ascii', 0, 4), 'RIFF');
    assert.equal(webp.toString('ascii', 8, 12), 'WEBP');
    assert(webp.length > 1000);
  }
}

function element(dataset = {}) {
  const listeners = new Map();
  return { dataset, attrs: {}, classList: { toggle() {} }, textContent: '', href: 'releases',
    setAttribute(key, value) { this.attrs[key] = value; },
    addEventListener(event, handler) { listeners.set(event, handler); },
    click() { listeners.get('click')?.(); } };
}
async function run(releases, fail = false) {
  const shots = ['comparison', 'groups', 'settings'].map(shot => element({ shot }));
  const elements = new Map(['detail-shot', 'preview-theme', 'installer-link', 'zip-link', 'release-status'].map(id => [id, element()]));
  let requests = 0;
  const context = vm.createContext({ document: { getElementById: id => elements.get(id), querySelectorAll: () => shots },
    AbortController, setTimeout, clearTimeout, fetch: async () => {
      requests++;
      if (fail) throw new Error('Offline');
      return { ok: true, json: async () => releases };
    } });
  vm.runInContext(script, context);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(requests, 1);
  shots[1].click();
  assert.equal(elements.get('detail-shot').src, 'assets/groups-light-en.webp');
  elements.get('preview-theme').click();
  assert.equal(elements.get('detail-shot').src, 'assets/groups-dark-en.webp');
  assert.equal(elements.get('preview-theme').attrs['aria-pressed'], 'true');
  return elements;
}
const downloadPrefix = 'https://github.com/eduard-dev-coder/PerceptoX/releases/download/v1/';
const release = { tag_name: 'v1', prerelease: true, draft: false, assets: [
  { name: 'PerceptoX-1.0.0-Setup-win-x64.exe', browser_download_url: downloadPrefix + 'setup.exe' },
  { name: 'PerceptoX-win-x64.zip', browser_download_url: downloadPrefix + 'portable.zip' }
] };
assert.equal((await run([])).get('installer-link').href, 'releases');
assert.equal((await run(null, true)).get('installer-link').href, 'releases');
assert.equal((await run([{ ...release, draft: true }])).get('installer-link').href, 'releases');
assert.equal((await run([{ ...release, assets: release.assets.slice(0, 1) }])).get('installer-link').href, 'releases');
assert.equal((await run([release])).get('installer-link').href, downloadPrefix + 'setup.exe');
const hostile = { ...release, assets: release.assets.map(asset => ({ ...asset, browser_download_url: 'https://example.invalid/payload.exe' })) };
assert.equal((await run([hostile])).get('installer-link').href, 'releases');
console.log('Website validation passed: local links, 10 real WebP screenshots, screenshot controls, one bounded request, offline/empty/draft/partial/valid/foreign-release cases. Not a visual browser certification.');
