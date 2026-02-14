window.TryGetDivisionIcon = function (division) {
    const divisionIcons = {
        "men's": "💪",
        "women's": "👠",
        "open": "🦾"
    };

    // Return the icon if it exists, or an empty string if not
    return divisionIcons[division.toLowerCase()] || '';
}

window.TryGetLeagueTrackIcon = function (leagueTrack) {
    const leagueTrackIcons = {
        "amateur": "🎓",
        "professional": "🏆"
    };
    return leagueTrackIcons[(leagueTrack || '').toLowerCase()] || '';
};

window.TryGetGenerationIcon = function (generation) {
    const generationIcons = {
        "silent generation": "📻",
        "baby boomers": "📺",
        "gen x": "🖥️",
        "millennials": "💻",
        "gen z": "📱",
        "gen alpha": "🚀"
    };

    // Return the icon if it exists, or an empty string if not
    return generationIcons[generation.toLowerCase()] || '';
}

window.TryGetDivisionFaIcon = function (division) {
    const divisionIcons = {
        "men's": "fa-mars",       // 💪
        "women's": "fa-venus", // 👠
        "open": "fa-robot"            // 🦾
    };

    return divisionIcons[division.toLowerCase()] || '';
}

window.TryGetGenerationFaIcon = function (generation) {
    const generationIcons = {
        "silent generation": "fa-radio",       // 📻
        "baby boomers": "fa-tv",               // 📺
        "gen x": "fa-desktop",                 // 🖥️
        "millennials": "fa-laptop",            // 💻
        "gen z": "fa-mobile-screen-button",    // 📱
        "gen alpha": "fa-rocket"               // 🚀
    };

    return generationIcons[generation.toLowerCase()] || '';
}