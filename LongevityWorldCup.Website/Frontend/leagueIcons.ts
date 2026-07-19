const divisionFontAwesomeIcons: Readonly<Record<string, string>> = {
    "men's": "fa-mars",
    "women's": "fa-venus",
    open: "fa-robot"
};

const leagueTrackFontAwesomeIcons: Readonly<Record<string, string>> = {
    amateur: "fa-user-graduate",
    professional: "fa-trophy"
};

const generationFontAwesomeIcons: Readonly<Record<string, string>> = {
    "silent generation": "fa-radio",
    "baby boomers": "fa-tv",
    "gen x": "fa-desktop",
    millennials: "fa-laptop",
    "gen z": "fa-mobile-screen-button",
    "gen alpha": "fa-rocket"
};

function lookupIcon(icons: Readonly<Record<string, string>>, value: string): string {
    return icons[value.toLowerCase()] ?? "";
}

window.TryGetDivisionFaIcon = function (division: string): string {
    return lookupIcon(divisionFontAwesomeIcons, division);
};

window.TryGetLeagueTrackFaIcon = function (leagueTrack: string | null | undefined): string {
    return lookupIcon(leagueTrackFontAwesomeIcons, leagueTrack || "");
};

window.TryGetGenerationFaIcon = function (generation: string): string {
    return lookupIcon(generationFontAwesomeIcons, generation);
};

export {};
