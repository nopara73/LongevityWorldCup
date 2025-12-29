/**
 * Age Visualization Module
 * Generates a circular visualization showing the difference between biological age and chronological age
 */

function generateAgeVisualization(bioAge, chronoAge) {
    // Try to find the element, with a retry mechanism and fallback creation
    const findElement = () => {
        return document.getElementById('targetShootingVisualization') || 
               document.querySelector('#ageVisualization #targetShootingVisualization');
    };
    
    let visualizationContainer = findElement();
    if (!visualizationContainer) {
        // Wait a bit and try again (element might not be in DOM yet)
        let retries = 0;
        const maxRetries = 10;
        const checkElement = () => {
            visualizationContainer = findElement();
            if (visualizationContainer) {
                generateAgeVisualizationInternal(visualizationContainer, bioAge, chronoAge);
            } else if (retries < maxRetries) {
                retries++;
                setTimeout(checkElement, 50);
            } else {
                // Last resort: create the element if it doesn't exist (fallback if partial wasn't injected)
                const ageVizContainer = document.getElementById('ageVisualization');
                if (!ageVizContainer) {
                    // Create the age visualization container
                    const athleteBio = document.getElementById('athleteBio');
                    if (athleteBio && athleteBio.nextElementSibling) {
                        const newContainer = document.createElement('div');
                        newContainer.className = 'age-visualization';
                        newContainer.id = 'ageVisualization';
                        newContainer.style.cssText = 'margin-top: 2rem; text-align: center;';
                        athleteBio.parentNode.insertBefore(newContainer, athleteBio.nextElementSibling);
                    }
                }
                const container = document.getElementById('ageVisualization');
                if (container) {
                    const newViz = document.createElement('div');
                    newViz.id = 'targetShootingVisualization';
                    newViz.style.cssText = 'width: 150px; height: 150px; margin: 0 auto; animation: expandCircle 1s ease-in-out;';
                    container.appendChild(newViz);
                    visualizationContainer = newViz;
                    generateAgeVisualizationInternal(visualizationContainer, bioAge, chronoAge);
                } else {
                    console.error('Could not create age visualization element');
                }
            }
        };
        setTimeout(checkElement, 50);
        return;
    }
    generateAgeVisualizationInternal(visualizationContainer, bioAge, chronoAge);
}

function generateAgeVisualizationInternal(visualizationContainer, bioAge, chronoAge) {

    // Define the maximum age for scaling. Adjust if needed.
    const maxAge = 100;

    // Calculate percentages relative to maxAge
    const bioPercentage = Math.min((bioAge / maxAge) * 100, 100);
    const chronoPercentage = Math.min((chronoAge / maxAge) * 100, 100);

    let gradient;

    if (bioAge === chronoAge) {
        // Entire circle is blue
        gradient = `radial-gradient(circle, #3498db 100%, #3498db 100%)`;
    } else if (bioAge < chronoAge) {
        // Inner blue up to bioAge%, outer green up to chronoAge%, rest gray
        gradient = `radial-gradient(circle, #3498db ${bioPercentage}%, #2ecc71 ${chronoPercentage}%, #ccc 100%)`;
    } else { // bioAge > chronoAge
        // Inner blue up to chronoAge%, outer red up to bioAge%, rest gray
        gradient = `radial-gradient(circle, #3498db ${chronoPercentage}%, #e74c3c ${bioPercentage}%, #ccc 100%)`;
    }

    // Apply the gradient to the visualization container
    visualizationContainer.style.background = gradient;
    visualizationContainer.style.borderRadius = '50%';
    visualizationContainer.style.position = 'relative';
    visualizationContainer.style.border = '2px solid #ccc'; // Optional: Adds a border to the circle

    // Clear any existing content
    visualizationContainer.innerHTML = '';

    var difference = (bioAge - chronoAge).toFixed(1);
    var circleText = `${difference > 0 ? '+' : ''}${difference} yrs`;

    // Add labels based on the scenario
    visualizationContainer.innerHTML = `
        <span style="
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            color: var(--light-text-color);
            font-weight: bold;">
            ${circleText}
        </span>
    `;
}

// Export functions to window for global access
window.generateAgeVisualization = generateAgeVisualization;
window.generateAgeVisualizationInternal = generateAgeVisualizationInternal;
