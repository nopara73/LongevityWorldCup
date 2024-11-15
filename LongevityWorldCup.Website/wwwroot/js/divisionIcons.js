window.GetDivisionIcon = function (division) {
    const divisionIcons = {
        "men's": "💪",
        "women's": "👠",
        "open": "🦾"
    };

    // Return the icon if it exists, or an empty string if not
    return divisionIcons[division.toLowerCase()] || '';
}