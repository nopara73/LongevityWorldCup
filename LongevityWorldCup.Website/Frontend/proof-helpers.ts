interface ProofUploadSetupOptions {
    cameraButton?: HTMLButtonElement | null;
    cameraInput?: HTMLInputElement | null;
}

interface ProofFileRejectedTrackOptions {
    component: string;
    outcome: string;
    errorCode: string;
    metadata: Required<Pick<
        LwcSiteStatisticsMetadata,
        "fileCountBucket" | "fileTypeBucket" | "fileSizeBucket"
    >>;
}

interface PdfViewport {
    width: number;
    height: number;
}

interface PdfPage {
    getViewport: (options: { scale: number }) => PdfViewport;
    render: (options: { canvasContext: CanvasRenderingContext2D; viewport: PdfViewport }) => { promise: Promise<void> };
}

interface PdfDocument {
    numPages: number;
    getPage: (pageNumber: number) => Promise<PdfPage>;
}

interface PdfJsLibrary {
    getDocument: (options: { data: ArrayBuffer }) => { promise: Promise<PdfDocument> };
    GlobalWorkerOptions?: { workerSrc: string };
}

interface ProofHelpersWindowApi {
    getMainProofInstructionsInnerHTML: () => string;
    getSubProofInstructionsInnerHTML: () => string;
    isAthletePro: (athlete: unknown) => boolean;
    getProofChecklistLabelsFromSession: () => string[];
    setupProofUploadHTML: (
        nextButton: HTMLButtonElement,
        uploadProofButton: HTMLButtonElement,
        proofPicInput: HTMLInputElement,
        proofImageContainer: HTMLElement,
        proofPics: string[],
        biomarkerChecklistContainer: HTMLElement | null,
        biomarkers: readonly string[],
        options?: ProofUploadSetupOptions
    ) => void;
    updateProofUploadButtons: (
        nextButton: HTMLButtonElement,
        uploadProofButton: HTMLButtonElement,
        cameraButton?: HTMLButtonElement | null
    ) => void;
}

declare global {
    interface Window extends ProofHelpersWindowApi {
        pdfjsLib?: PdfJsLibrary;
        __lwcPdfJsReady?: Promise<PdfJsLibrary> | null;
        customAlert: (message: string) => Promise<unknown>;
        showLoading: () => void;
        hideLoading: () => void;
    }
}

function isProofObject(value: unknown): value is object {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

window.getMainProofInstructionsInnerHTML = function (): string {
    return "Upload <strong>proofs</strong> showing each submitted biomarker, the collection date, and the lab or report source (e.g., screenshots of PDF results or photos of physical documents)";
}

window.getSubProofInstructionsInnerHTML = function (): string {
    return "These images will be <strong>public</strong>, so you're encouraged to censor any irrelevant information.";
}

// Canonical display order: matches bortz-age.html UI (card order in DOM). Pheno-age-only users see the same order for their 9 biomarkers.
var PROOF_CHECKLIST_ORDER = [
    'Wbc1000cellsuL', 'LymPc', 'NeutrophilPc', 'MonocytePc', 'Rbc10e12L', 'McvFL', 'MchPg', 'RdwPc',
    'AlbGL', 'AltUL', 'AlpUL', 'GgtUL', 'UreaMmolL', 'CreatUmolL', 'CystatinCMgL', 'GluMmolL',
    'Hba1cMmolMol', 'CholesterolMmolL', 'ApoA1GL', 'CrpMgL', 'ShbgNmolL', 'VitaminDNmolL'
] as const;

type ProofBiomarkerKey = typeof PROOF_CHECKLIST_ORDER[number];

// Labels match bortz-age.html card headers.
var PROOF_CHECKLIST_PROPERTY_TO_LABEL: Readonly<Record<ProofBiomarkerKey, string>> = {
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
var BORTZ_ONLY_BIOMARKER_KEYS: readonly ProofBiomarkerKey[] = [
    'NeutrophilPc', 'MonocytePc', 'Rbc10e12L', 'MchPg', 'UreaMmolL',
    'CystatinCMgL', 'Hba1cMmolMol', 'CholesterolMmolL', 'ApoA1GL',
    'AltUL', 'GgtUL', 'ShbgNmolL', 'VitaminDNmolL'
];

/**
 * Determine if an athlete is Pro (has any Bortz-only biomarker in latest entry).
 * @param {object} athlete
 * @returns {boolean}
 */
window.isAthletePro = function (athlete: unknown): boolean {
    if (!isProofObject(athlete)) return false;
    const biomarkers = Reflect.get(athlete, 'Biomarkers');
    if (!Array.isArray(biomarkers) || biomarkers.length === 0) return false;

    var sorted = biomarkers.slice().sort(function (a: unknown, b: unknown) {
        var aDateValue = isProofObject(a) ? Reflect.get(a, 'Date') : null;
        var bDateValue = isProofObject(b) ? Reflect.get(b, 'Date') : null;
        var aDate = aDateValue ? new Date(String(aDateValue)).getTime() : NaN;
        var bDate = bDateValue ? new Date(String(bDateValue)).getTime() : NaN;
        if (isNaN(aDate) && isNaN(bDate)) return 0;
        if (isNaN(aDate)) return 1;
        if (isNaN(bDate)) return -1;
        return bDate - aDate;
    });

    var latest: unknown = sorted[0] || biomarkers[0] || {};
    for (var i = 0; i < BORTZ_ONLY_BIOMARKER_KEYS.length; i++) {
        var key = BORTZ_ONLY_BIOMARKER_KEYS[i];
        if (key === undefined) continue;
        var val = isProofObject(latest) ? Reflect.get(latest, key) : undefined;
        if (hasFiniteBiomarkerValue(val)) return true;
    }
    return false;
};

function hasFiniteBiomarkerValue(value: unknown): boolean {
    if (value === null || value === undefined || typeof value === 'boolean') return false;
    if (typeof value === 'number') return Number.isFinite(value);
    if (typeof value === 'string') {
        var trimmed = value.trim();
        return trimmed !== '' && Number.isFinite(Number(trimmed));
    }
    return false;
}

function getProofSessionItem(key: string): string | null {
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
window.getProofChecklistLabelsFromSession = function (): string[] {
    try {
        var raw = getProofSessionItem('biomarkerData');
        if (!raw) return [];
        var data: unknown = JSON.parse(raw);
        var biomarkers = isProofObject(data) ? Reflect.get(data, 'Biomarkers') : null;
        var latest: unknown = Array.isArray(biomarkers) ? biomarkers[0] || {} : {};
        var labels: string[] = [];
        for (var i = 0; i < PROOF_CHECKLIST_ORDER.length; i++) {
            var prop = PROOF_CHECKLIST_ORDER[i];
            if (prop === undefined) continue;
            var val = isProofObject(latest) ? Reflect.get(latest, prop) : undefined;
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

function getProofFileExtension(file: File | null | undefined): string {
    const name = file && typeof file.name === 'string' ? file.name.toLowerCase() : '';
    const dotIndex = name.lastIndexOf('.');
    return dotIndex >= 0 ? name.slice(dotIndex + 1) : '';
}

function isProofPdfFile(file: File | null | undefined): boolean {
    const type = file && typeof file.type === 'string' ? file.type.toLowerCase() : '';
    return type === 'application/pdf' || getProofFileExtension(file) === 'pdf';
}

function isSupportedProofFile(file: File | null | undefined): file is File {
    if (!file) return false;

    const type = typeof file.type === 'string' ? file.type.toLowerCase() : '';
    const extension = getProofFileExtension(file);
    return type === 'application/pdf'
        || type.startsWith('image/')
        || extension === 'pdf'
        || extension === 'jpg'
        || extension === 'jpeg'
        || extension === 'png'
        || extension === 'webp'
        || extension === 'heic'
        || extension === 'heif';
}

function trackProofFileRejected(
    errorCode: string,
    files: Iterable<File> | ArrayLike<File> | null | undefined
): void {
    const stats = window.LwcSiteStats;
    if (!stats || typeof stats.track !== 'function') return;

    const rejectedFiles = Array.from(files || []);
    const first = rejectedFiles[0] || null;
    stats.track('proof_file_rejected', {
        component: 'proof_upload',
        outcome: 'rejected',
        errorCode: errorCode || 'client_rejected',
        metadata: {
            fileCountBucket: typeof stats.countBucket === 'function' ? stats.countBucket(rejectedFiles.length) : String(rejectedFiles.length),
            fileTypeBucket: typeof stats.fileTypeBucket === 'function' ? stats.fileTypeBucket(first) : 'unknown',
            fileSizeBucket: typeof stats.fileSizeBucket === 'function' ? stats.fileSizeBucket(first) : 'unknown'
        }
    } satisfies ProofFileRejectedTrackOptions);
}

const proofReviews = new WeakMap<HTMLElement, ProofReview>();
const proofProcessingButtons = new WeakSet<HTMLButtonElement>();
const maxProofImages = 37;

class ProofReview {
    readonly labels = new Map<string, string>();
    private removed: { image: string; index: number }[] = [];
    private notice = '';
    private progress: { label: string; completed: number; total: number } | null = null;
    private dialog: HTMLDialogElement | null = null;
    private viewerIndex = 0;
    private opener: HTMLElement | null = null;
    private zoomed = false;
    private container: HTMLElement;
    private images: string[];
    private changed: () => void;

    constructor(container: HTMLElement, images: string[], changed: () => void) {
        this.container = container;
        this.images = images;
        this.changed = changed;
        container.classList.add('proof-review-panel');
    }

    setProgress(progress: { label: string; completed: number; total: number } | null): void {
        this.progress = progress;
        this.render();
    }

    announce(message: string): void {
        this.notice = message;
        this.render();
    }

    render(): void {
        this.container.replaceChildren();
        this.container.hidden = !this.images.length && !this.removed.length && !this.progress && !this.notice;
        const heading = document.createElement('h3');
        heading.className = 'proof-review-heading';
        heading.textContent = `${this.images.length} proof ${this.images.length === 1 ? 'page' : 'pages'}`;
        this.container.appendChild(heading);

        if (this.progress) {
            const status = document.createElement('div');
            status.className = 'proof-preparation';
            status.setAttribute('role', 'status');
            const label = document.createElement('span');
            label.textContent = this.progress.label;
            const meter = document.createElement('progress');
            meter.max = this.progress.total;
            meter.value = this.progress.completed;
            meter.setAttribute('aria-label', 'Preparing proof files');
            status.append(label, meter);
            this.container.appendChild(status);
        }

        const grid = document.createElement('div');
        grid.className = 'proof-page-grid';
        grid.setAttribute('aria-busy', String(Boolean(this.progress)));
        this.images.forEach((source, index) => {
            const card = document.createElement('article');
            card.className = 'proof-page-card';
            const preview = this.button('', 'proof-page-preview', () => this.open(index, preview));
            preview.disabled = Boolean(this.progress);
            preview.setAttribute('aria-label', `Review proof page ${index + 1}${this.labels.has(source) ? `: ${this.labels.get(source)}` : ''}`);
            const image = document.createElement('img');
            image.src = source;
            image.alt = `Proof image ${index + 1}`;
            const previewLabel = document.createElement('span');
            previewLabel.textContent = 'Review page';
            preview.append(image, previewLabel);
            const caption = document.createElement('div');
            caption.className = 'proof-page-caption';
            const title = document.createElement('strong');
            title.textContent = `Page ${index + 1}`;
            const label = document.createElement('span');
            label.className = 'proof-page-source';
            label.textContent = this.labels.get(source) || 'Attached image';
            label.title = label.textContent;
            caption.append(title, label);
            const remove = this.button('Remove', 'proof-page-remove', () => {
                this.removed.push({ image: source, index });
                this.images.splice(index, 1);
                this.notice = `Page ${index + 1} removed.`;
                this.render();
                this.changed();
                const target = this.container.querySelectorAll<HTMLButtonElement>('.proof-page-remove')[Math.min(index, this.images.length - 1)]
                    || this.container.querySelector<HTMLButtonElement>('.proof-undo');
                target?.focus({ preventScroll: true });
            });
            remove.disabled = Boolean(this.progress);
            remove.setAttribute('aria-label', `Remove proof page ${index + 1}`);
            card.append(preview, caption, remove);
            grid.appendChild(card);
        });
        this.container.appendChild(grid);

        if (this.notice || this.removed.length) {
            const status = document.createElement('div');
            status.className = 'proof-review-feedback';
            const message = document.createElement('span');
            message.className = 'proof-upload-notice';
            message.setAttribute('role', 'status');
            message.textContent = this.notice;
            status.appendChild(message);
            if (this.removed.length) {
                const undo = this.button('Undo removal', 'proof-undo', () => {
                    const removed = this.removed.pop();
                    if (!removed) return;
                    if (!this.images.includes(removed.image)) {
                        this.images.splice(Math.min(removed.index, this.images.length), 0, removed.image);
                    }
                    this.notice = 'Page restored.';
                    this.render();
                    this.changed();
                    this.container.querySelectorAll<HTMLButtonElement>('.proof-page-preview')[this.images.indexOf(removed.image)]?.focus({ preventScroll: true });
                });
                undo.disabled = Boolean(this.progress) || this.images.length >= maxProofImages;
                status.appendChild(undo);
            }
            this.container.appendChild(status);
        }
    }

    private button(label: string, className: string, click: () => void): HTMLButtonElement {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = className;
        button.textContent = label;
        button.addEventListener('click', click);
        return button;
    }

    private open(index: number, opener: HTMLElement): void {
        this.opener = opener;
        this.viewerIndex = index;
        this.zoomed = false;
        if (!this.dialog) {
            const dialog = document.createElement('dialog');
            dialog.className = 'proof-review-dialog';
            dialog.setAttribute('aria-label', 'Review attached proofs');
            dialog.innerHTML = `<div class="proof-review-toolbar"><h2></h2><button type="button" class="proof-review-close" aria-label="Close proof review">Close <span aria-hidden="true">×</span></button></div>
                <div class="proof-review-stage" tabindex="0" role="region" aria-label="Proof page"><img alt=""></div>
                <div class="proof-review-navigation"><button type="button" class="proof-review-previous">← Previous</button><span class="proof-review-position" role="status"></span><button type="button" class="proof-review-next">Next →</button><button type="button" class="proof-review-zoom">Zoom in</button></div>`;
            dialog.querySelector('.proof-review-close')?.addEventListener('click', () => dialog.close());
            dialog.querySelector('.proof-review-previous')?.addEventListener('click', () => this.navigate(-1));
            dialog.querySelector('.proof-review-next')?.addEventListener('click', () => this.navigate(1));
            dialog.querySelector('.proof-review-zoom')?.addEventListener('click', () => { this.zoomed = !this.zoomed; this.updateViewer(); });
            dialog.addEventListener('keydown', event => {
                if (event.key === 'Tab') {
                    const controls = Array.from(dialog.querySelectorAll<HTMLElement>('button:not(:disabled), [tabindex="0"]'));
                    const first = controls[0];
                    const last = controls[controls.length - 1];
                    if ((event.shiftKey && document.activeElement === first) || (!event.shiftKey && document.activeElement === last)) {
                        event.preventDefault();
                        (event.shiftKey ? last : first)?.focus();
                    }
                }
                if (this.zoomed && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
                    event.preventDefault();
                    const stage = dialog.querySelector<HTMLElement>('.proof-review-stage')!;
                    const horizontal = event.key === 'ArrowLeft' || event.key === 'ArrowRight';
                    const distance = (event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 1) * 120;
                    stage.scrollBy({ left: horizontal ? distance : 0, top: horizontal ? 0 : distance, behavior: 'instant' });
                    return;
                }
                if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
                    event.preventDefault();
                    this.navigate(event.key === 'ArrowLeft' ? -1 : 1);
                }
            });
            dialog.addEventListener('click', event => {
                if (event.target !== dialog) return;
                const box = dialog.getBoundingClientRect();
                if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) dialog.close();
            });
            dialog.addEventListener('close', () => {
                document.body.classList.remove('proof-review-open');
                if (this.opener?.isConnected) this.opener.focus({ preventScroll: true });
            });
            this.dialog = dialog;
            document.body.appendChild(dialog);
        }
        this.updateViewer();
        document.body.classList.add('proof-review-open');
        this.dialog.showModal();
    }

    private navigate(direction: number): void {
        this.viewerIndex = Math.max(0, Math.min(this.images.length - 1, this.viewerIndex + direction));
        this.zoomed = false;
        this.updateViewer();
    }

    private updateViewer(): void {
        const dialog = this.dialog;
        const source = this.images[this.viewerIndex];
        if (!dialog || !source) return;
        dialog.querySelector('h2')!.textContent = this.labels.get(source) || `Proof page ${this.viewerIndex + 1}`;
        const image = dialog.querySelector('img')!;
        image.src = source;
        image.alt = `Proof page ${this.viewerIndex + 1} of ${this.images.length}`;
        dialog.querySelector('.proof-review-position')!.textContent = `${this.viewerIndex + 1} / ${this.images.length}`;
        dialog.querySelector<HTMLButtonElement>('.proof-review-previous')!.disabled = this.viewerIndex === 0;
        dialog.querySelector<HTMLButtonElement>('.proof-review-next')!.disabled = this.viewerIndex === this.images.length - 1;
        const zoom = dialog.querySelector<HTMLButtonElement>('.proof-review-zoom')!;
        zoom.textContent = this.zoomed ? 'Fit page' : 'Zoom in';
        zoom.setAttribute('aria-pressed', String(this.zoomed));
        dialog.classList.toggle('is-zoomed', this.zoomed);
        const stage = dialog.querySelector<HTMLElement>('.proof-review-stage')!;
        stage.scrollTo(0, 0);
    }
}

window.setupProofUploadHTML = function (
    nextButton: HTMLButtonElement,
    uploadProofButton: HTMLButtonElement,
    proofPicInput: HTMLInputElement,
    proofImageContainer: HTMLElement,
    proofPics: string[],
    biomarkerChecklistContainer: HTMLElement | null,
    biomarkers: readonly string[],
    options?: ProofUploadSetupOptions
): void {
    nextButton.disabled = true;
    const cameraButton = options && options.cameraButton;
    const cameraInput = options && options.cameraInput;
    const existingReview = proofReviews.get(proofImageContainer);
    if (existingReview) {
        proofImageContainer.style.display = 'block';
        existingReview.render();
        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
        return;
    }
    let isProofUploadProcessing = false;
    const proofOptimizationOptions = {
        maxSize: 2560,
        quality: 0.88,
        targetMaxBytes: 1.5 * 1024 * 1024
    };
    const review = new ProofReview(proofImageContainer, proofPics,
        () => checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer));
    proofReviews.set(proofImageContainer, review);
    const proofSourceImages = new Map<string, string>();
    let preferredProofCanvasContentType: Promise<'image/webp' | 'image/jpeg'> | null = null;

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

    const focusProofRetryButton: (retryButton: HTMLButtonElement | null | undefined) => void = retryButton => {
        if (retryButton && typeof retryButton.focus === 'function') {
            retryButton.focus();
        }
    };

    const showProofUploadNotice: (message: string) => void = message => {
        review.announce(message);
    };

    const handleProofFiles = async function (
        files: FileList | readonly File[] | null | undefined,
        input: HTMLInputElement | null,
        retryButton: HTMLButtonElement | null | undefined
    ): Promise<void> {
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
            trackProofFileRejected('unsupported_file_type', unsupportedFiles);
            window.customAlert('Proof files must be images or PDFs.')
                .then(() => focusProofRetryButton(retryButton));
            return;
        }

        isProofUploadProcessing = true;
        proofProcessingButtons.add(nextButton);
        uploadProofButton.disabled = true;
        proofPicInput.disabled = true;
        if (cameraButton) cameraButton.disabled = true;
        if (cameraInput) cameraInput.disabled = true;
        nextButton.disabled = true;
        review.setProgress({ label: 'Preparing proof files…', completed: 0, total: supportedFiles.length });
        try {
            // Helper to read a File or encoded canvas Blob as a data URL.
            const readDataURL: (file: Blob) => Promise<string> = file => new Promise((res, rej) => {
                const r = new FileReader();
                r.onload = () => {
                    if (typeof r.result === 'string') {
                        res(r.result);
                    } else {
                        rej(new Error('Proof file could not be read as a data URL.'));
                    }
                };
                r.onerror = () => rej(r.error ?? new Error('Proof file could not be read.'));
                r.onabort = () => rej(new Error('Proof file read was aborted.'));
                r.readAsDataURL(file);
            });

            const optimizeProofImageOrFallback: (raw: string) => Promise<string> = async raw => {
                try {
                    const { dataUrl } = await window.optimizeImageClient(raw, proofOptimizationOptions);
                    return dataUrl || raw;
                } catch (_) {
                    return raw;
                }
            };

            const encodeCanvasBlob = (
                canvas: HTMLCanvasElement,
                contentType: string,
                quality: number
            ): Promise<Blob | null> => new Promise(resolve => canvas.toBlob(resolve, contentType, quality));

            const resizeProofCanvas = (source: HTMLCanvasElement, scale: number): HTMLCanvasElement => {
                const width = Math.max(1, Math.round(source.width * scale));
                const height = Math.max(1, Math.round(source.height * scale));
                if (width === source.width && height === source.height) return source;

                const resized = document.createElement('canvas');
                resized.width = width;
                resized.height = height;
                const context = resized.getContext('2d');
                if (!context) throw new Error('Canvas context unavailable.');
                context.drawImage(source, 0, 0, width, height);
                return resized;
            };

            const getPreferredProofCanvasContentType = (): Promise<'image/webp' | 'image/jpeg'> => {
                if (preferredProofCanvasContentType) return preferredProofCanvasContentType;

                preferredProofCanvasContentType = (async () => {
                    const probe = document.createElement('canvas');
                    probe.width = 1;
                    probe.height = 1;
                    const webpBlob = await encodeCanvasBlob(
                        probe,
                        'image/webp',
                        proofOptimizationOptions.quality);
                    return webpBlob?.type.toLowerCase() === 'image/webp'
                        ? 'image/webp'
                        : 'image/jpeg';
                })();
                return preferredProofCanvasContentType;
            };

            const encodeProofCanvas: (canvas: HTMLCanvasElement) => Promise<string> = async canvas => {
                const maxDimension = Math.max(canvas.width, canvas.height);
                let workingCanvas = maxDimension > proofOptimizationOptions.maxSize
                    ? resizeProofCanvas(canvas, proofOptimizationOptions.maxSize / maxDimension)
                    : canvas;
                const contentType = await getPreferredProofCanvasContentType();
                let lastBlob: Blob | null = null;

                // Try progressively lower quality at each size, then downscale according to
                // the measured excess. This preserves the same bounds as normal image uploads
                // without round-tripping a PDF canvas through createImageBitmap.
                for (let attempt = 0; attempt < 12; attempt++) {
                    const qualityStep = attempt % 4;
                    const quality = Math.max(0.43, proofOptimizationOptions.quality - qualityStep * 0.15);
                    const blob = await encodeCanvasBlob(workingCanvas, contentType, quality);
                    if (!blob || blob.type.toLowerCase() !== contentType) {
                        throw new Error('Proof canvas could not be encoded in a bounded image format.');
                    }

                    lastBlob = blob;
                    if (blob.size <= proofOptimizationOptions.targetMaxBytes) {
                        return await readDataURL(blob);
                    }

                    if (qualityStep === 3) {
                        const measuredScale = Math.sqrt(proofOptimizationOptions.targetMaxBytes / blob.size) * 0.92;
                        const nextScale = Math.min(0.8, Math.max(0.45, measuredScale));
                        workingCanvas = resizeProofCanvas(workingCanvas, nextScale);
                    }
                }

                throw new Error(
                    `Proof canvas remained too large after bounded encoding (${lastBlob?.size ?? 0} bytes).`);
            };

            const fingerprintProofSource = async (bytes: ArrayBuffer): Promise<string> => {
                if (window.crypto?.subtle) {
                    const digest = await window.crypto.subtle.digest('SHA-256', bytes);
                    return Array.from(new Uint8Array(digest))
                        .map(value => value.toString(16).padStart(2, '0'))
                        .join('');
                }

                // Content-based fallback for older embedded browsers. This is a duplicate
                // hint, not a security boundary.
                let hash = 2166136261;
                for (const value of new Uint8Array(bytes)) {
                    hash = Math.imul(hash ^ value, 16777619);
                }
                return `${bytes.byteLength}:${(hash >>> 0).toString(16)}`;
            };

            const isKnownLiveProofSource = (sourceKey: string): boolean => {
                const existingProof = proofSourceImages.get(sourceKey);
                if (!existingProof) return false;
                if (proofPics.includes(existingProof)) return true;

                proofSourceImages.delete(sourceKey);
                return false;
            };

            let failedFiles = 0;
            const failedFileSamples: File[] = [];
            let hitImageLimit = false;
            let duplicateProofs = 0;
            const knownProofImages = new Set(proofPics);
            const addProofImage = (dataUrl: string): boolean => {
                if (knownProofImages.has(dataUrl)) {
                    duplicateProofs++;
                    return false;
                }

                knownProofImages.add(dataUrl);
                proofPics.push(dataUrl);
                return true;
            };
            // process one by one to preserve order
            for (const file of supportedFiles) {
                const fileIndex = supportedFiles.indexOf(file);
                review.setProgress({ label: `Preparing ${file.name}`, completed: fileIndex, total: supportedFiles.length });
                const proofCountBeforeFile = proofPics.length;
                try {
                    const fileBytes = await file.arrayBuffer();
                    const sourceFingerprint = await fingerprintProofSource(fileBytes);
                    if (isProofPdfFile(file)) {
                        const pdfLib = await ensurePdfJsReady();
                        // load PDF
                        const loadingTask = pdfLib.getDocument({ data: fileBytes });
                        const pdfDoc = await loadingTask.promise;
                        // render each page
                        for (let pageNum = 1; pageNum <= pdfDoc.numPages; pageNum++) {
                            review.setProgress({ label: `${file.name} · Page ${pageNum} of ${pdfDoc.numPages}`,
                                completed: fileIndex + (pageNum - 1) / pdfDoc.numPages, total: supportedFiles.length });
                            if (proofPics.length >= maxProofImages) {
                                hitImageLimit = true;
                                break;
                            }
                            const sourceKey = `${sourceFingerprint}:page:${pageNum}`;
                            if (isKnownLiveProofSource(sourceKey)) {
                                duplicateProofs++;
                                continue;
                            }
                            const page = await pdfDoc.getPage(pageNum);
                            const viewport = page.getViewport({ scale: 1.5 });
                            const canvas = document.createElement('canvas');
                            canvas.width = viewport.width;
                            canvas.height = viewport.height;
                            const context = canvas.getContext('2d');
                            if (!context) throw new Error('Canvas context unavailable.');
                            await page.render({ canvasContext: context, viewport }).promise;
                            const optimizedPage = await encodeProofCanvas(canvas);
                            if (optimizedPage) {
                                if (!review.labels.has(optimizedPage)) review.labels.set(optimizedPage, `${file.name} · Page ${pageNum}`);
                                addProofImage(optimizedPage);
                                proofSourceImages.set(sourceKey, optimizedPage);
                            }
                        }
                        review.render();
                        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        nextButton.disabled = true;
                        continue;
                    }

                    if (proofPics.length >= maxProofImages) {
                        hitImageLimit = true;
                        break;
                    }
                    const sourceKey = `${sourceFingerprint}:image`;
                    if (isKnownLiveProofSource(sourceKey)) {
                        duplicateProofs++;
                        continue;
                    }
                    const raw = await readDataURL(file);
                    const dataUrl = await optimizeProofImageOrFallback(raw);
                    if (dataUrl) {
                        if (!review.labels.has(dataUrl)) review.labels.set(dataUrl, file.name);
                        proofSourceImages.set(sourceKey, dataUrl);
                        if (addProofImage(dataUrl)) {
                            review.render();
                            checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                            nextButton.disabled = true;
                        }
                    } else {
                        failedFiles++;
                        failedFileSamples.push(file);
                    }
                } catch (_) {
                    failedFiles++;
                    failedFileSamples.push(file);
                    if (proofPics.length > proofCountBeforeFile) {
                        review.render();
                        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        nextButton.disabled = true;
                    }
                }
            }
            if (unsupportedFiles.length > 0) {
                trackProofFileRejected('unsupported_file_type', unsupportedFiles);
                window.customAlert('Some proof files were skipped because proof files must be images or PDFs.')
                    .then(() => focusProofRetryButton(retryButton));
            }
            if (failedFiles > 0) {
                trackProofFileRejected('client_processing_failed', failedFileSamples);
                window.customAlert('Some proof files could not be processed. Please try them again as images or PDFs.')
                    .then(() => focusProofRetryButton(retryButton));
            }
            const uploadNotices: string[] = [];
            if (duplicateProofs > 0) {
                uploadNotices.push('Duplicate proof images were skipped.');
            }
            if (hitImageLimit) {
                review.render();
                uploadNotices.push('Only the first ' + maxProofImages + ' proof images were kept. Remove one to add another.');
            }
            if (uploadNotices.length > 0) {
                showProofUploadNotice(uploadNotices.join(' '));
            }
        } catch (error) {
            trackProofFileRejected('proof_upload_failed', selectedFiles);
            window.customAlert('Proof upload failed. Please try again with an image or PDF file.')
                .then(() => focusProofRetryButton(retryButton));
        } finally {
            // Reset the file input's value to allow re-uploading the same file if needed.
            if (input) input.value = "";
            isProofUploadProcessing = false;
            proofProcessingButtons.delete(nextButton);
            review.setProgress(null);
            uploadProofButton.disabled = false;
            proofPicInput.disabled = false;
            if (cameraButton) cameraButton.disabled = false;
            if (cameraInput) cameraInput.disabled = false;
            checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
        }
    };

    // Handle proof uploads (without cropping)
    if (proofPicInput && !proofPicInput.hasAttribute('data-listener')) {
        proofPicInput.addEventListener('change', async function () {
            await handleProofFiles(proofPicInput.files, proofPicInput, uploadProofButton);
        });
        proofPicInput.setAttribute('data-listener', 'true');
    }

    if (cameraInput && !cameraInput.hasAttribute('data-listener')) {
        cameraInput.addEventListener('change', async function () {
            await handleProofFiles(cameraInput.files, cameraInput, cameraButton || uploadProofButton);
        });
        cameraInput.setAttribute('data-listener', 'true');
    }

    proofImageContainer.style.display = 'block';

    // Display any existing proof images
    review.render();

    generateBiomarkerChecklist(biomarkerChecklistContainer, biomarkers, nextButton, proofPics, uploadProofButton, cameraButton);

    // Check if proof images already exist
    checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
}

function ensurePdfJsReady(): Promise<PdfJsLibrary> {
    if (window.pdfjsLib && typeof window.pdfjsLib.getDocument === 'function') {
        setPdfWorker(window.pdfjsLib);
        return Promise.resolve(window.pdfjsLib);
    }

    if (window.__lwcPdfJsReady) return window.__lwcPdfJsReady;

    const readiness = new Promise<PdfJsLibrary>((resolve, reject) => {
        const pdfScript = document.createElement('script');
        pdfScript.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.8.162/pdf.min.js';
        pdfScript.async = true;
        pdfScript.dataset.lwcPdfjs = 'true';
        pdfScript.onload = () => {
            const pdfLibrary = window.pdfjsLib;
            if (!pdfLibrary || typeof pdfLibrary.getDocument !== 'function') {
                window.__lwcPdfJsReady = null;
                reject(new Error('PDF renderer failed to load.'));
                return;
            }

            setPdfWorker(pdfLibrary);
            resolve(pdfLibrary);
        };
        pdfScript.onerror = () => {
            window.__lwcPdfJsReady = null;
            reject(new Error('PDF renderer failed to load.'));
        };
        document.head.appendChild(pdfScript);
    });
    window.__lwcPdfJsReady = readiness;

    return readiness;
}

function setPdfWorker(pdfLib: PdfJsLibrary): void {
    if (!pdfLib || !pdfLib.GlobalWorkerOptions) return;
    pdfLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.8.162/pdf.worker.min.js';
}

function checkProofImages(
    nextButton: HTMLButtonElement,
    proofPics: readonly string[],
    uploadProofButton: HTMLButtonElement,
    cameraButton: HTMLButtonElement | null | undefined,
    _biomarkerChecklistContainer: HTMLElement | null
): void {
    const hasProofs = proofPics.length > 0;
    document.body?.classList.toggle('proof-upload-has-proofs', hasProofs);
    nextButton.disabled = !hasProofs || proofProcessingButtons.has(nextButton);
    window.updateProofUploadButtons(nextButton, uploadProofButton, cameraButton);
}

window.updateProofUploadButtons = function (
    nextButton: HTMLButtonElement,
    uploadProofButton: HTMLButtonElement,
    cameraButton?: HTMLButtonElement | null
): void {
    if (!nextButton || !uploadProofButton) return;

    const uploadIsRequired = nextButton.disabled && !proofProcessingButtons.has(nextButton);
    uploadProofButton.classList.toggle('green', uploadIsRequired);
    uploadProofButton.classList.toggle('grey', !uploadIsRequired);
    uploadProofButton.classList.toggle('flow-action--secondary', !uploadIsRequired);

    // Camera capture is an alternative input method, not a competing primary action.
    if (cameraButton) {
        cameraButton.classList.remove('green');
        cameraButton.classList.add('grey', 'flow-action--secondary');
    }
}

function generateBiomarkerChecklist(
    biomarkerChecklistContainer: HTMLElement | null,
    biomarkers: readonly string[],
    nextButton: HTMLButtonElement,
    proofPics: readonly string[],
    uploadProofButton: HTMLButtonElement,
    cameraButton: HTMLButtonElement | null | undefined
): void {
    if (!biomarkerChecklistContainer) return;

    // Clear any existing content
    biomarkerChecklistContainer.innerHTML = '';

    // Title
    const title = document.createElement('h4');
    title.textContent = 'Proof tracker';
    title.style.marginBottom = '4px';
    biomarkerChecklistContainer.appendChild(title);

    const instructions = document.createElement('p');
    instructions.textContent = "Check each item only when an uploaded proof shows its marker name and submitted value:";
    instructions.style.marginTop = '1px';
    instructions.style.marginBottom = '4px';
    instructions.classList.add('smaller-text');
    biomarkerChecklistContainer.appendChild(instructions);

    biomarkers.forEach((name: string) => {
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

export {};
