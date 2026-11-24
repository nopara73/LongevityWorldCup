import { readFileSync, writeFileSync } from "node:fs";

// Embed the same catalog the server uses in the existing versioned script.
function embed(catalogName, scriptName, marker) {
    const catalog = new URL(`../Frontend/${catalogName}.json`, import.meta.url);
    const output = new URL(`../wwwroot/js/${scriptName}.js`, import.meta.url);
    const source = readFileSync(output, "utf8");
    if (source.split(marker).length !== 2) throw new Error(`Expected one ${marker} in ${scriptName}.js.`);
    const codes = JSON.parse(readFileSync(catalog, "utf8"));
    writeFileSync(output, source.replace(marker, () => JSON.stringify(codes)));
}

embed("flag-icon-codes", "flags", "EMBEDDED_FLAG_ICON_CODES");
embed("league-icon-codes", "leagueIcons", "EMBEDDED_LEAGUE_ICON_CODES");
