import { existsSync, readdirSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const websiteRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const sourceRoot = join(websiteRoot, "Frontend");
const outputRoot = join(websiteRoot, "wwwroot", "js");

function listFiles(root, baseRoot = root) {
    if (!existsSync(root)) return [];

    return readdirSync(root, { withFileTypes: true })
        .flatMap(entry => {
            const path = join(root, entry.name);
            return entry.isDirectory() ? listFiles(path, baseRoot) : [relative(baseRoot, path)];
        })
        .map(path => path.split(sep).join("/"))
        .sort();
}

const sourceFiles = listFiles(sourceRoot);
const runtimeSources = sourceFiles
    .filter(path => path.endsWith(".ts") && !path.endsWith(".d.ts"));
const nestedRuntimeSources = runtimeSources.filter(path => path.includes("/"));
const expectedOutputs = runtimeSources
    .filter(path => !path.includes("/"))
    .map(path => path.replace(/\.ts$/, ".js"))
    .sort();

const actualOutputs = existsSync(outputRoot)
    ? listFiles(outputRoot).filter(path => path.endsWith(".js"))
    : [];

if (nestedRuntimeSources.length) {
    console.error(`Runtime TypeScript entry points must stay directly under Frontend: ${nestedRuntimeSources.join(", ")}`);
    process.exitCode = 1;
}

if (JSON.stringify(actualOutputs) !== JSON.stringify(expectedOutputs)) {
    const missing = expectedOutputs.filter(file => !actualOutputs.includes(file));
    const unexpected = actualOutputs.filter(file => !expectedOutputs.includes(file));
    if (missing.length) console.error(`Missing generated frontend assets: ${missing.join(", ")}`);
    if (unexpected.length) console.error(`Unexpected generated frontend assets: ${unexpected.join(", ")}`);
    process.exitCode = 1;
}
