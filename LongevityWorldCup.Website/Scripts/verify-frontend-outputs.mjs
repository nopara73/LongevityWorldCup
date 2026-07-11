import { existsSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const websiteRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const sourceRoot = join(websiteRoot, "Frontend");
const outputRoot = join(websiteRoot, "wwwroot", "js");

const expectedOutputs = readdirSync(sourceRoot, { withFileTypes: true })
    .filter(entry => entry.isFile() && entry.name.endsWith(".ts") && !entry.name.endsWith(".d.ts"))
    .map(entry => entry.name.replace(/\.ts$/, ".js"))
    .sort();

const actualOutputs = existsSync(outputRoot)
    ? readdirSync(outputRoot, { withFileTypes: true })
        .filter(entry => entry.isFile() && entry.name.endsWith(".js"))
        .map(entry => entry.name)
        .sort()
    : [];

if (JSON.stringify(actualOutputs) !== JSON.stringify(expectedOutputs)) {
    const missing = expectedOutputs.filter(file => !actualOutputs.includes(file));
    const unexpected = actualOutputs.filter(file => !expectedOutputs.includes(file));
    if (missing.length) console.error(`Missing generated frontend assets: ${missing.join(", ")}`);
    if (unexpected.length) console.error(`Unexpected generated frontend assets: ${unexpected.join(", ")}`);
    process.exitCode = 1;
}
