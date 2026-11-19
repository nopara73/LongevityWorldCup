// Regenerate the small share-card copies of bundled SVG flags after artwork changes.
// Run from any directory after building the .NET tests (which supplies Playwright):
// node LongevityWorldCup.Website/Scripts/render-share-flags.mjs [playwright-package-directory]
// No browser or SVG dependency is needed by the production server.
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const website = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = path.resolve(website, '..');
const webroot = path.join(website, 'wwwroot');
const driver = process.argv[2] || path.join(root, 'LongevityWorldCup.Tests/bin/Debug/net10.0/.playwright/package');
const { chromium } = await import(pathToFileURL(path.join(driver, 'index.mjs')).href);
const normalize = name => name.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
const catalog = new Map(Object.entries(JSON.parse(await fs.readFile(path.join(website, 'Frontend/flag-icon-codes.json'), 'utf8'))).map(([name, code]) => [normalize(name), code]));
const aliases = new Map([...((await fs.readFile(path.join(website, 'Business/FlagCanonicalizer.cs'), 'utf8')).matchAll(/\["([^"]+)"\] = "([^"]+)"/g))].map(match => [match[1], match[2]]));
const codes = new Set();
for (const entry of await fs.readdir(path.join(webroot, 'athletes'), { withFileTypes: true })) {
    if (!entry.isDirectory()) continue;
    const athlete = JSON.parse(await fs.readFile(path.join(webroot, 'athletes', entry.name, 'athlete.json'), 'utf8'));
    const key = normalize(athlete.Flag || '');
    const code = catalog.get(normalize(aliases.get(key) || key));
    if (code) codes.add(code);
}
const css = await fs.readFile(path.join(webroot, 'css/icons.css'), 'utf8');
const custom = new Map([...css.matchAll(/\.fi-([\w-]+)\s*\{\s*background-image:\s*url\("([^"?]+)(?:\?[^" ]*)?"\)/g)].map(match => [match[1], path.resolve(webroot, 'css', match[2])]));
const output = path.join(webroot, 'assets/flags/share');
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
    const page = await browser.newPage({ viewport: { width: 160, height: 120 }, deviceScaleFactor: 1 });
    const manifest = {};
    for (const code of [...codes].sort()) {
        const source = custom.get(code) || path.join(webroot, 'vendor/flag-icons/flags/4x3', code + '.svg');
        if (path.extname(source) !== '.svg') continue; // Existing custom raster art is reused directly.
        const svg = await fs.readFile(source);
        await page.setContent(`<style>html,body{margin:0;background:transparent}img{width:160px;height:120px;object-fit:contain}</style><img src="data:image/svg+xml;base64,${svg.toString('base64')}">`);
        await page.locator('img').evaluate(image => image.decode());
        await page.screenshot({ path: path.join(output, code + '.png'), omitBackground: true });
        manifest[code + '.png'] = path.relative(webroot, source).replaceAll('\\', '/');
    }
    await fs.writeFile(path.join(output, 'sources.json'), JSON.stringify(manifest, null, 2) + '\n');
    process.stdout.write(`Rendered ${Object.keys(manifest).length} active SVG flags.\n`);
} finally {
    await browser.close();
}
