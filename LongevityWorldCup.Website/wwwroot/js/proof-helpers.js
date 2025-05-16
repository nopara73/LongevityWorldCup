window.getMainProofInstructionsInnerHTML = function () {
    return "Upload <strong>proofs</strong> of your biomarkers (e.g., screenshots of PDF results or photos of physical documents)";
}

window.getSubProofInstructionsInnerHTML = function () {
    return "These images will be <strong>public</strong>, so you're encouraged to censor any irrelevant information.";
}

window.setupProofUploadHTML = function (nextButton, uploadProofButton, proofPicInput, proofImageContainer, proofPics, biomarkerChecklistContainer, biomarkers) {
    nextButton.disabled = true;

    // ——— Load PDF.js if not already loaded ———
    if (!window.pdfjsLib) {
        const pdfScript = document.createElement('script');
        pdfScript.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.8.162/pdf.min.js';
        pdfScript.onload = () => {
            // point to the worker
            pdfjsLib.GlobalWorkerOptions.workerSrc =
                'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.8.162/pdf.worker.min.js';
        };
        document.head.appendChild(pdfScript);
    }

    // Attach event listener to the Upload Proof button
    if (uploadProofButton && !uploadProofButton.hasAttribute('data-listener')) {
        uploadProofButton.addEventListener('click', function () {
            proofPicInput.click();
        });
        uploadProofButton.setAttribute('data-listener', 'true');
    }

    // Handle the image upload (without cropping)
    if (proofPicInput && !proofPicInput.hasAttribute('data-listener')) {
        proofPicInput.addEventListener('change', async function (event) {
            const files = event.target.files;
            if (files.length > 0) {
                // Limit the total number of images to 9
                if (proofPics.length + files.length > 9) {
                    customAlert('You can upload a maximum of 9 images.');
                    return;
                }

                // helper to read a File as dataURL
                const readDataURL = file => new Promise((res, rej) => {
                    const r = new FileReader();
                    r.onload = e => res(e.target.result);
                    r.onerror = rej;
                    r.readAsDataURL(file);
                });

                // process one by one to preserve order
                for (const file of Array.from(files)) {
                    const raw = await readDataURL(file);
                    if (file.type === 'application/pdf') {
                        // read file as arrayBuffer
                        const arrayBuffer = await file.arrayBuffer();
                        // load PDF
                        const loadingTask = pdfjsLib.getDocument({ data: arrayBuffer });
                        const pdfDoc = await loadingTask.promise;
                        // render each page
                        for (let pageNum = 1; pageNum <= pdfDoc.numPages; pageNum++) {
                            const page = await pdfDoc.getPage(pageNum);
                            const viewport = page.getViewport({ scale: 1 });
                            const canvas = document.createElement('canvas');
                            canvas.width = viewport.width;
                            canvas.height = viewport.height;
                            const context = canvas.getContext('2d');
                            await page.render({ canvasContext: context, viewport }).promise;
                            const rawPage = canvas.toDataURL();
                            const { dataUrl: optimizedPage } = await window.optimizeImageClient(rawPage);
                            proofPics.push(optimizedPage);
                        }
                        updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton);
                        checkProofImages(nextButton, proofPics, uploadProofButton);
                        continue;
                    }

                    const { dataUrl } = await window.optimizeImageClient(raw);
                    if (dataUrl) {
                        proofPics.push(dataUrl);
                        updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton);
                        checkProofImages(nextButton, proofPics, uploadProofButton);
                    }
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

    generateBiomarkerChecklist(biomarkerChecklistContainer, biomarkers);

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
    // Toggle "green" class for "Upload Profile Picture" button
    if (!nextButton || !uploadProofButton) return;
    // second argument to toggle is a boolean: add if true, remove if false
    uploadProofButton.classList.toggle('green', nextButton.disabled);
}

function generateBiomarkerChecklist(biomarkerChecklistContainer, biomarkers) {
    if (!biomarkerChecklistContainer) return;

    // Clear any existing content
    biomarkerChecklistContainer.innerHTML = '';

    // Title
    const title = document.createElement('h4');
    title.textContent = 'Proof Tracker';
    title.style.marginBottom = '4px';
    biomarkerChecklistContainer.appendChild(title);

    const instructions = document.createElement('p');
    instructions.textContent = "Check each biomarker you've already uploaded proof for:";
    instructions.style.marginTop = '1px';
    instructions.style.marginBottom = '4px';
    instructions.classList.add('smaller-text');
    biomarkerChecklistContainer.appendChild(instructions);

    biomarkers.forEach(name => {
        // wrapper div
        const itemDiv = document.createElement('div');

        // label.biomerker-item
        const label = document.createElement('label');
        label.className = 'biomarker-item';

        // input[type=checkbox]
        const input = document.createElement('input');
        input.type = 'checkbox';
        input.className = 'biomarker-checkbox';
        // generate an ID like "biomarker-Albumin" or "biomarker-CReactiveProtein"
        input.id = 'biomarker-' + name.replace(/[^a-z0-9]/gi, '');

        // span with the visible name
        const span = document.createElement('span');
        span.textContent = name;

        label.appendChild(input);
        label.appendChild(span);
        itemDiv.appendChild(label);
        biomarkerChecklistContainer.appendChild(itemDiv);
    });
};