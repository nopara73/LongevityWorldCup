import { readFileSync, writeFileSync } from "node:fs";

// Share the server's catalog through the existing versioned flags.js request.
const catalog = new URL("../Frontend/flag-icon-codes.json", import.meta.url);
const output = new URL("../wwwroot/js/flags.js", import.meta.url);
const source = readFileSync(output, "utf8");
const marker = "EMBEDDED_FLAG_ICON_CODES";
if (source.split(marker).length !== 2) {
    throw new Error("Expected one flag icon catalog reference in flags.js.");
}

const codes = JSON.parse(readFileSync(catalog, "utf8"));
writeFileSync(output, source.replace(marker, () => JSON.stringify(codes)));
