const divisionIcons = {
    "men's": "💪",
    "women's": "👠",
    open: "🦾"
};
const leagueTrackIcons = {
    amateur: "🎓",
    professional: "🏆"
};
const generationIcons = {
    "silent generation": "📻",
    "baby boomers": "📺",
    "gen x": "🖥️",
    millennials: "💻",
    "gen z": "📱",
    "gen alpha": "🚀"
};
const divisionFontAwesomeIcons = {
    "men's": "fa-mars",
    "women's": "fa-venus",
    open: "fa-robot"
};
const generationFontAwesomeIcons = {
    "silent generation": "fa-radio",
    "baby boomers": "fa-tv",
    "gen x": "fa-desktop",
    millennials: "fa-laptop",
    "gen z": "fa-mobile-screen-button",
    "gen alpha": "fa-rocket"
};
function lookupIcon(icons, value) {
    return icons[value.toLowerCase()] ?? "";
}
window.TryGetDivisionIcon = function (division) {
    return lookupIcon(divisionIcons, division);
};
window.TryGetLeagueTrackIcon = function (leagueTrack) {
    return lookupIcon(leagueTrackIcons, leagueTrack || "");
};
window.TryGetGenerationIcon = function (generation) {
    return lookupIcon(generationIcons, generation);
};
window.TryGetDivisionFaIcon = function (division) {
    return lookupIcon(divisionFontAwesomeIcons, division);
};
window.TryGetGenerationFaIcon = function (generation) {
    return lookupIcon(generationFontAwesomeIcons, generation);
};
export {};
