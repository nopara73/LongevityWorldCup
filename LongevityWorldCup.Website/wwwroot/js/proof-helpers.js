window.getMainProofInstructionsInnerHTML = function () {
    return "Upload <strong>proofs</strong> showing each submitted biomarker, the collection date, and the lab or report source (e.g., screenshots of PDF results or photos of physical documents)";
}

window.getSubProofInstructionsInnerHTML = function () {
    return "These images will be <strong>public</strong>, so you're encouraged to censor any irrelevant information.";
}

// Canonical display order: matches bortz-age.html UI (card order in DOM). Pheno-age-only users see the same order for their 9 biomarkers.
var PROOF_CHECKLIST_ORDER = [
    'Wbc1000cellsuL', 'LymPc', 'NeutrophilPc', 'MonocytePc', 'Rbc10e12L', 'McvFL', 'MchPg', 'RdwPc',
    'AlbGL', 'AltUL', 'AlpUL', 'GgtUL', 'UreaMmolL', 'CreatUmolL', 'CystatinCMgL', 'GluMmolL',
    'Hba1cMmolMol', 'CholesterolMmolL', 'ApoA1GL', 'CrpMgL', 'ShbgNmolL', 'VitaminDNmolL'
];

// Labels match bortz-age.html card headers.
var PROOF_CHECKLIST_PROPERTY_TO_LABEL = {
    Wbc1000cellsuL: 'White blood cell count (WBC)',
    LymPc: 'Lymphocytes',
    NeutrophilPc: 'Neutrophils',
    MonocytePc: 'Monocytes',
    Rbc10e12L: 'Red blood cell count (RBC)',
    McvFL: 'Mean corpuscular volume (MCV)',
    MchPg: 'Mean corpuscular hemoglobin (MCH)',
    RdwPc: 'Red cell distribution width (RDW)',
    AlbGL: 'Albumin',
    AltUL: 'Alanine aminotransferase (ALT)',
    AlpUL: 'Alkaline phosphatase (ALP)',
    GgtUL: 'GGT',
    UreaMmolL: 'Urea',
    CreatUmolL: 'Creatinine',
    CystatinCMgL: 'Cystatin C',
    GluMmolL: 'Glucose',
    Hba1cMmolMol: 'Hemoglobin A1c (HbA1c)',
    CholesterolMmolL: 'Total cholesterol',
    ApoA1GL: 'Apolipoprotein A1 (ApoA1)',
    CrpMgL: 'C-reactive protein (CRP)',
    ShbgNmolL: 'Sex hormone-binding globulin (SHBG)',
    VitaminDNmolL: 'Vitamin D (25-OH)'
};

var PROOF_CONTEXT_CHECKLIST_LABELS = ['Collection date', 'Lab/report source'];

// Bortz-only biomarkers (not required for PhenoAge).
var BORTZ_ONLY_BIOMARKER_KEYS = [
    'NeutrophilPc', 'MonocytePc', 'Rbc10e12L', 'MchPg', 'UreaMmolL',
    'CystatinCMgL', 'Hba1cMmolMol', 'CholesterolMmolL', 'ApoA1GL',
    'AltUL', 'GgtUL', 'ShbgNmolL', 'VitaminDNmolL'
];

/**
 * Determine if an athlete is Pro (has any Bortz-only biomarker in latest entry).
 * @param {object} athlete
 * @returns {boolean}
 */
window.isAthletePro = function (athlete) {
    if (!athlete || !Array.isArray(athlete.Biomarkers) || athlete.Biomarkers.length === 0) return false;

    var sorted = athlete.Biomarkers.slice().sort(function (a, b) {
        var aDate = a && a.Date ? new Date(a.Date).getTime() : NaN;
        var bDate = b && b.Date ? new Date(b.Date).getTime() : NaN;
        if (isNaN(aDate) && isNaN(bDate)) return 0;
        if (isNaN(aDate)) return 1;
        if (isNaN(bDate)) return -1;
        return bDate - aDate;
    });

    var latest = sorted[0] || athlete.Biomarkers[0] || {};
    for (var i = 0; i < BORTZ_ONLY_BIOMARKER_KEYS.length; i++) {
        var key = BORTZ_ONLY_BIOMARKER_KEYS[i];
        var val = latest[key];
        if (hasFiniteBiomarkerValue(val)) return true;
    }
    return false;
};

function hasFiniteBiomarkerValue(value) {
    if (value === null || value === undefined || typeof value === 'boolean') return false;
    if (typeof value === 'number') return Number.isFinite(value);
    if (typeof value === 'string') {
        var trimmed = value.trim();
        return trimmed !== '' && Number.isFinite(Number(trimmed));
    }
    return false;
}

function getProofSessionItem(key) {
    try {
        return window.sessionStorage.getItem(key);
    } catch (_) {
        return null;
    }
}

/**
 * Build Proof Tracker checklist labels from sessionStorage.biomarkerData.
 * Only includes biomarkers present in the latest entry (valid number).
 * Order follows bortz-age.html UI (card order in DOM).
 * @returns {string[]} Array of display labels in canonical order.
 */
window.getProofChecklistLabelsFromSession = function () {
    try {
        var raw = getProofSessionItem('biomarkerData');
        if (!raw) return [];
        var data = JSON.parse(raw);
        var latest = (data.Biomarkers && data.Biomarkers[0]) || {};
        var labels = [];
        for (var i = 0; i < PROOF_CHECKLIST_ORDER.length; i++) {
            var prop = PROOF_CHECKLIST_ORDER[i];
            var val = latest[prop];
            if (hasFiniteBiomarkerValue(val)) {
                var label = PROOF_CHECKLIST_PROPERTY_TO_LABEL[prop];
                if (label) labels.push(label);
            }
        }
        return labels.length > 0 ? PROOF_CONTEXT_CHECKLIST_LABELS.concat(labels) : labels;
    } catch (e) {
        return [];
    }
};

function getProofFileExtension(file) {
    const name = file && typeof file.name === 'string' ? file.name.toLowerCase() : '';
    const dotIndex = name.lastIndexOf('.');
    return dotIndex >= 0 ? name.slice(dotIndex + 1) : '';
}

function isProofPdfFile(file) {
    const type = file && typeof file.type === 'string' ? file.type.toLowerCase() : '';
    return type === 'application/pdf' || getProofFileExtension(file) === 'pdf';
}

function isSupportedProofFile(file) {
    if (!file) return false;

    const type = typeof file.type === 'string' ? file.type.toLowerCase() : '';
    const extension = getProofFileExtension(file);
    return type === 'application/pdf'
        || type === 'image/jpeg'
        || type === 'image/png'
        || type === 'image/webp'
        || extension === 'pdf'
        || extension === 'jpg'
        || extension === 'jpeg'
        || extension === 'png'
        || extension === 'webp';
}

window.setupProofUploadHTML = function (nextButton, uploadProofButton, proofPicInput, proofImageContainer, proofPics, biomarkerChecklistContainer, biomarkers, options) {
    nextButton.disabled = true;
    const cameraButton = options && options.cameraButton;
    const cameraInput = options && options.cameraInput;
    let isProofUploadProcessing = false;
    const proofOptimizationOptions = {
        maxSize: 2560,
        quality: 0.88,
        targetMaxBytes: 1.5 * 1024 * 1024
    };

    ensurePdfJsReady().catch(() => {});

    // Attach event listener to the Upload Proof button
    if (uploadProofButton && !uploadProofButton.hasAttribute('data-listener')) {
        uploadProofButton.addEventListener('click', function () {
            if (isProofUploadProcessing) return;
            proofPicInput.click();
        });
        uploadProofButton.setAttribute('data-listener', 'true');
    }

    if (cameraButton && cameraInput && !cameraButton.hasAttribute('data-listener')) {
        cameraButton.addEventListener('click', function () {
            if (isProofUploadProcessing) return;
            cameraInput.click();
        });
        cameraButton.setAttribute('data-listener', 'true');
    }

    const handleProofFiles = async function (files, input) {
        if (isProofUploadProcessing) {
            if (input) input.value = "";
            return;
        }

        const selectedFiles = Array.from(files || []);
        if (selectedFiles.length === 0) {
            if (input) input.value = "";
            return;
        }

        const unsupportedFiles = selectedFiles.filter(file => !isSupportedProofFile(file));
        const supportedFiles = selectedFiles.filter(file => isSupportedProofFile(file));
        if (supportedFiles.length === 0) {
            if (input) input.value = "";
            customAlert('Proof files must be JPG, PNG, WebP, or PDF.');
            return;
        }

        isProofUploadProcessing = true;
        uploadProofButton.disabled = true;
        proofPicInput.disabled = true;
        if (cameraButton) cameraButton.disabled = true;
        if (cameraInput) cameraInput.disabled = true;
        nextButton.disabled = true;
        showLoading();
        try {
            // helper to read a File as dataURL
            const readDataURL = file => new Promise((res, rej) => {
                const r = new FileReader();
                r.onload = e => res(e.target.result);
                r.onerror = rej;
                r.onabort = rej;
                r.readAsDataURL(file);
            });

            const optimizeProofImageOrFallback = async raw => {
                try {
                    const { dataUrl } = await window.optimizeImageClient(raw, proofOptimizationOptions);
                    return dataUrl || raw;
                } catch (_) {
                    return raw;
                }
            };

            let failedFiles = 0;
            // process one by one to preserve order
            for (const file of supportedFiles) {
                const proofCountBeforeFile = proofPics.length;
                try {
                    if (isProofPdfFile(file)) {
                        const pdfLib = await ensurePdfJsReady();
                        // read file as arrayBuffer
                        const arrayBuffer = await file.arrayBuffer();
                        // load PDF
                        const loadingTask = pdfLib.getDocument({ data: arrayBuffer });
                        const pdfDoc = await loadingTask.promise;
                        // render each page
                        for (let pageNum = 1; pageNum <= pdfDoc.numPages; pageNum++) {
                            if (proofPics.length >= 9) {
                                customAlert('You can upload a maximum of 9 images.');
                                break;
                            }
                            const page = await pdfDoc.getPage(pageNum);
                            const viewport = page.getViewport({ scale: 1.5 });
                            const canvas = document.createElement('canvas');
                            canvas.width = viewport.width;
                            canvas.height = viewport.height;
                            const context = canvas.getContext('2d');
                            if (!context) throw new Error('Canvas context unavailable.');
                            await page.render({ canvasContext: context, viewport }).promise;
                            const rawPage = canvas.toDataURL();
                            const optimizedPage = await optimizeProofImageOrFallback(rawPage);
                            if (optimizedPage) {
                                proofPics.push(optimizedPage);
                            }
                        }
                        updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        nextButton.disabled = true;
                        continue;
                    }

                    if (proofPics.length >= 9) {
                        customAlert('You can upload a maximum of 9 images.');
                        break;
                    }
                    const raw = await readDataURL(file);
                    const dataUrl = await optimizeProofImageOrFallback(raw);
                    if (dataUrl) {
                        proofPics.push(dataUrl);
                        updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        nextButton.disabled = true;
                    } else {
                        failedFiles++;
                    }
                } catch (_) {
                    failedFiles++;
                    if (proofPics.length > proofCountBeforeFile) {
                        updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        nextButton.disabled = true;
                    }
                }
            }
            if (unsupportedFiles.length > 0) {
                customAlert('Some proof files were skipped because proof files must be JPG, PNG, WebP, or PDF.');
            }
            if (failedFiles > 0) {
                customAlert('Some proof files could not be processed. Please try them again as JPG, PNG, WebP, or PDF.');
            }
        } catch (error) {
            customAlert('Proof upload failed. Please try again with a JPG, PNG, WebP, or PDF file.');
        } finally {
            // Reset the file input's value to allow re-uploading the same file if needed.
            if (input) input.value = "";
            hideLoading();
            isProofUploadProcessing = false;
            uploadProofButton.disabled = false;
            proofPicInput.disabled = false;
            if (cameraButton) cameraButton.disabled = false;
            if (cameraInput) cameraInput.disabled = false;
            checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
        }
    };

    // Handle proof uploads (without cropping)
    if (proofPicInput && !proofPicInput.hasAttribute('data-listener')) {
        proofPicInput.addEventListener('change', async function (event) {
            await handleProofFiles(event.target.files, proofPicInput);
        });
        proofPicInput.setAttribute('data-listener', 'true');
    }

    if (cameraInput && !cameraInput.hasAttribute('data-listener')) {
        cameraInput.addEventListener('change', async function (event) {
            await handleProofFiles(event.target.files, cameraInput);
        });
        cameraInput.setAttribute('data-listener', 'true');
    }

    proofImageContainer.style.display = 'block';

    // Display any existing proof images
    updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);

    generateBiomarkerChecklist(biomarkerChecklistContainer, biomarkers, nextButton, proofPics, uploadProofButton, cameraButton);

    // Check if proof images already exist
    checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
}

function ensurePdfJsReady() {
    if (window.pdfjsLib && typeof window.pdfjsLib.getDocument === 'function') {
        setPdfWorker(window.pdfjsLib);
        return Promise.resolve(window.pdfjsLib);
    }

    if (window.__lwcPdfJsReady) return window.__lwcPdfJsReady;

    window.__lwcPdfJsReady = new Promise((resolve, reject) => {
        const pdfScript = document.createElement('script');
        pdfScript.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.8.162/pdf.min.js';
        pdfScript.async = true;
        pdfScript.dataset.lwcPdfjs = 'true';
        pdfScript.onload = () => {
            if (!window.pdfjsLib || typeof window.pdfjsLib.getDocument !== 'function') {
                window.__lwcPdfJsReady = null;
                reject(new Error('PDF renderer failed to load.'));
                return;
            }

            setPdfWorker(window.pdfjsLib);
            resolve(window.pdfjsLib);
        };
        pdfScript.onerror = () => {
            window.__lwcPdfJsReady = null;
            reject(new Error('PDF renderer failed to load.'));
        };
        document.head.appendChild(pdfScript);
    });

    return window.__lwcPdfJsReady;
}

function setPdfWorker(pdfLib) {
    if (!pdfLib || !pdfLib.GlobalWorkerOptions) return;
    pdfLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.8.162/pdf.worker.min.js';
}

function updateProofImageContainer(container, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer) {
    container.innerHTML = '';
    // Use the global `proofPics` variable directly
    if (proofPics.length > 0) {
        container.innerHTML = '<p><strong>Proof uploaded successfully.</strong></p>';
        for (let i = 0; i < proofPics.length; i++) {
            let imgContainer = document.createElement('div');
            imgContainer.style = 'position: relative; display: inline-block; margin: 0.5rem;';

            let img = document.createElement('img');
            img.src = proofPics[i];
            img.alt = 'Proof image ' + (i + 1);
            img.style = 'max-width: 100%; border: 2px solid var(--dark-text-color); border-radius: 8px;';

            let removeButton = document.createElement('button');
            removeButton.textContent = 'Remove';
            removeButton.style = 'position: absolute; top: 5px; right: 5px; background-color: rgba(255, 0, 0, 0.7); color: var(--light-text-color); border: none; border-radius: 3px; cursor: pointer;';
            removeButton.addEventListener('click', function () {
                proofPics.splice(i, 1);
                updateProofImageContainer(container, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                // Check proof images
                checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
            });

            imgContainer.appendChild(img);
            imgContainer.appendChild(removeButton);
            container.appendChild(imgContainer);
        }
    }
}

function checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer) {
    const hasProofs = proofPics.length > 0;
    const checklistComplete = areRequiredProofChecklistItemsChecked(biomarkerChecklistContainer);
    nextButton.disabled = !(hasProofs && checklistComplete);
    updateProofUploadButtons(nextButton, uploadProofButton, cameraButton);
}

window.updateProofUploadButtons = function (nextButton, uploadProofButton, cameraButton) {
    if (!nextButton || !uploadProofButton) return;
    // second argument to toggle is a boolean: add if true, remove if false
    uploadProofButton.classList.toggle('green', nextButton.disabled);
    if (cameraButton) cameraButton.classList.toggle('green', nextButton.disabled);
}

function areRequiredProofChecklistItemsChecked(biomarkerChecklistContainer) {
    if (!biomarkerChecklistContainer) return true;
    const checkboxes = Array.from(biomarkerChecklistContainer.querySelectorAll('.biomarker-checkbox'));
    return checkboxes.length === 0 || checkboxes.every(input => input.checked);
}

function generateBiomarkerChecklist(biomarkerChecklistContainer, biomarkers, nextButton, proofPics, uploadProofButton, cameraButton) {
    if (!biomarkerChecklistContainer) return;

    // Clear any existing content
    biomarkerChecklistContainer.innerHTML = '';

    // Title
    const title = document.createElement('h4');
    title.textContent = 'Proof tracker';
    title.style.marginBottom = '4px';
    biomarkerChecklistContainer.appendChild(title);

    const instructions = document.createElement('p');
    instructions.textContent = "Check each item once your proof shows it:";
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
        input.addEventListener('change', function () {
            checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
        });

        // span with the visible name
        const span = document.createElement('span');
        span.textContent = name;

        label.appendChild(input);
        label.appendChild(span);
        itemDiv.appendChild(label);
        biomarkerChecklistContainer.appendChild(itemDiv);
    });
};
