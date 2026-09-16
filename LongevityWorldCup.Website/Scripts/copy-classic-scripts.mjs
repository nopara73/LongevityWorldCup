import { copyFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";

// These classic scripts were extracted from HTML without changing their global
// scope or execution order. Keep TypeScript's strict checks for typed sources.
const websiteRoot = dirname(dirname(fileURLToPath(import.meta.url)));
for (const name of readdirSync(join(websiteRoot, "Frontend")).filter(name => name.endsWith(".js"))) {
    const source = join(websiteRoot, "Frontend", name);
    execFileSync(process.execPath, ["--check", source], { stdio: "inherit" });
    copyFileSync(source, join(websiteRoot, "wwwroot", "js", name));
}
