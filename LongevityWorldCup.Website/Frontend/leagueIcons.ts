declare const EMBEDDED_LEAGUE_ICON_CODES: Readonly<Record<"division" | "track" | "generation" | "exclusive", Readonly<Record<string, string>>>>;

const leagueIcons = EMBEDDED_LEAGUE_ICON_CODES;

function lookupIcon(icons: Readonly<Record<string, string>>, value: string): string {
    return Object.hasOwn(icons, value.toLowerCase()) ? icons[value.toLowerCase()] ?? "" : "";
}

window.TryGetDivisionFaIcon = division => lookupIcon(leagueIcons.division, division);
window.TryGetLeagueTrackFaIcon = track => lookupIcon(leagueIcons.track, track || "");
window.TryGetGenerationFaIcon = generation => lookupIcon(leagueIcons.generation, generation);
window.TryGetExclusiveLeagueFaIcon = league => lookupIcon(leagueIcons.exclusive, league);

export {};
