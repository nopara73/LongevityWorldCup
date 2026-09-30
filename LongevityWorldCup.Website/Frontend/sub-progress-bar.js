// Function to update sub progress bar based on the current sub-stage
function updateSubProgress(currentSubStage) {
    const subProgressFill = document.getElementById('subProgressFill');
    const allStages = Array.from(document.querySelectorAll('.sub-progress-container .stage'));
    const requestedCount = Number(document.body.dataset.subProgressSteps);
    const stageCount = Number.isInteger(requestedCount) && requestedCount > 0
        ? Math.min(requestedCount, allStages.length) : allStages.length;
    allStages.forEach((stage, index) => { stage.style.display = index < stageCount ? '' : 'none'; });
    const subStages = allStages.slice(0, stageCount);

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
    const subProgressPercentage = subStages.length > 1
        ? Math.max(0, Math.min(100, ((currentSubStage - 1) / (subStages.length - 1)) * 100)) : 100;
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
