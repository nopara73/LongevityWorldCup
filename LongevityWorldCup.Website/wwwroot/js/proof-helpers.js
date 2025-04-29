window.getMainProofInstructionsInnerHTML = function () {
    return "Upload <strong>image proofs</strong> of your biomarkers (e.g., screenshots of PDF results or photos of physical documents)";
}

window.getSubProofInstructionsInnerHTML = function () {
    return "These images will be <strong>public</strong>, so you're encouraged to censor any irrelevant information.";
}

window.setupProofUploadHTML = function (nextButton, uploadProofButton, proofPicInput, proofImageContainer, proofPics) {
    nextButton.disabled = true;

    // Attach event listener to the Upload Proof button
    if (uploadProofButton && !uploadProofButton.hasAttribute('data-listener')) {
        uploadProofButton.addEventListener('click', function () {
            proofPicInput.click();
        });
        uploadProofButton.setAttribute('data-listener', 'true');
    }

    // Handle the image upload (without cropping)
    if (proofPicInput && !proofPicInput.hasAttribute('data-listener')) {
        proofPicInput.addEventListener('change', function (event) {
            const files = event.target.files;
            if (files.length > 0) {
                // Limit the total number of images to 9
                if (proofPics.length + files.length > 9) {
                    customAlert('You can upload a maximum of 9 images.');
                    return;
                }

                for (let i = 0; i < files.length; i++) {
                    const file = files[i];
                    const reader = new FileReader();
                    reader.onload = function (e) {
                        proofPics.push(e.target.result);
                        // Update the display
                        updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton);
                        // Check proof images
                        checkProofImages(nextButton, proofPics, uploadProofButton);
                    };
                    reader.readAsDataURL(file);
                }
            }
            // Reset the file input's value to allow re-uploading the same file if needed
            proofPicInput.value = "";
        });
        proofPicInput.setAttribute('data-listener', 'true');
    }

    proofImageContainer.style.display = 'block';

    // Display any existing proof images
    updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton);

    // Check if proof images already exist
    checkProofImages(nextButton, proofPics, uploadProofButton);
}

function updateProofImageContainer(container, nextButton, proofPics, uploadProofButton) {
    container.innerHTML = '';
    // Use the global `proofPics` variable directly
    if (proofPics.length > 0) {
        container.innerHTML = '<p><strong>Proof uploaded successfully.</strong></p>';
        for (let i = 0; i < proofPics.length; i++) {
            let imgContainer = document.createElement('div');
            imgContainer.style = 'position: relative; display: inline-block; margin: 0.5rem;';

            let img = document.createElement('img');
            img.src = proofPics[i];
            img.alt = 'Proof Image ' + (i + 1);
            img.style = 'max-width: 100%; border: 2px solid var(--dark-text-color); border-radius: 8px;';

            let removeButton = document.createElement('button');
            removeButton.textContent = 'Remove';
            removeButton.style = 'position: absolute; top: 5px; right: 5px; background-color: rgba(255, 0, 0, 0.7); color: var(--light-text-color); border: none; border-radius: 3px; cursor: pointer;';
            removeButton.addEventListener('click', function () {
                proofPics.splice(i, 1);
                updateProofImageContainer(container, nextButton, proofPics, uploadProofButton);
                // Check proof images
                checkProofImages(nextButton, proofPics, uploadProofButton);
            });

            imgContainer.appendChild(img);
            imgContainer.appendChild(removeButton);
            container.appendChild(imgContainer);
        }
    }
}

function checkProofImages(nextButton, proofPics, uploadProofButton) {
    if (proofPics.length > 0) {
        nextButton.disabled = false;
        updateProofUploadButtons(nextButton, uploadProofButton);
    } else {
        nextButton.disabled = true;
        updateProofUploadButtons(nextButton, uploadProofButton);
    }
}

window.updateProofUploadButtons = function (nextButton, uploadProofButton) {
    // Toggle "already-have" class for "Upload Profile Picture" button
    if (nextButton && nextButton.disabled) {
        uploadProofButton.classList.add('already-have');
    } else {
        uploadProofButton.classList.remove('already-have');
    }
}