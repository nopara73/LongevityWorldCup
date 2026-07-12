const divisionIcons: Readonly<Record<string, string>> = {
    "men's": "💪",
    "women's": "👠",
    open: "🦾"
};

const leagueTrackIcons: Readonly<Record<string, string>> = {
    amateur: "🎓",
    professional: "🏆"
};

const generationIcons: Readonly<Record<string, string>> = {
    "silent generation": "📻",
    "baby boomers": "📺",
    "gen x": "🖥️",
    millennials: "💻",
    "gen z": "📱",
    "gen alpha": "🚀"
};

const divisionFontAwesomeIcons: Readonly<Record<string, string>> = {
    "men's": "fa-mars",
    "women's": "fa-venus",
    open: "fa-robot"
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

window.TryGetDivisionIcon = function (division: string): string {
    return lookupIcon(divisionIcons, division);
};

window.TryGetLeagueTrackIcon = function (leagueTrack: string | null | undefined): string {
    return lookupIcon(leagueTrackIcons, leagueTrack || "");
};

window.TryGetGenerationIcon = function (generation: string): string {
    return lookupIcon(generationIcons, generation);
};

window.TryGetDivisionFaIcon = function (division: string): string {
    return lookupIcon(divisionFontAwesomeIcons, division);
};

window.TryGetGenerationFaIcon = function (generation: string): string {
    return lookupIcon(generationFontAwesomeIcons, generation);
};

export {};
