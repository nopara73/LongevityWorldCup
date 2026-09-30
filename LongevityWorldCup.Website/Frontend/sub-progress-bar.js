// Function to update sub progress bar based on the current sub-stage
function updateSubProgress(currentSubStage) {
    const subProgressFill = document.getElementById('subProgressFill');
    const subStages = document.querySelectorAll('.sub-progress-container .stage');

    // Update sub-progress stages
    subStages.forEach((s, index) => {
        if (index < currentSubStage - 1) {
            s.className = 'stage completed';
        } else if (index === currentSubStage - 1) {
            s.className = 'stage active';
        } else {
            s.className = 'stage';
        }
    });

    // Update sub-progress bar fill
    const subProgressPercentage = ((currentSubStage - 1) / (subStages.length - 1)) * 100;
    subProgressFill.style.width = `${subProgressPercentage}%`;

    const subProgressContainerItem = document.getElementById('subProgressContainerItem');
    if (currentSubStage == 99) {
        subProgressContainerItem.style.display = 'none';
    }
    else {
        subProgressContainerItem.style.display = 'block';
    }
}

// Example usage:
// updateSubProgress(3);
