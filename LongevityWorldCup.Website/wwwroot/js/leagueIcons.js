window.TryGetDivisionIcon = function (division) {
    const divisionIcons = {
        "men's": "💪",
        "women's": "👠",
        "open": "🦾"
    };

    // Return the icon if it exists, or an empty string if not
    return divisionIcons[division.toLowerCase()] || '';
}

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