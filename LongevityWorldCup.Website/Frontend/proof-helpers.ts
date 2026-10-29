interface ProofUploadSetupOptions {
    cameraButton?: HTMLButtonElement | null;
    cameraInput?: HTMLInputElement | null;
    isActive?: () => boolean;
    hasUnsubmittedProofs?: () => boolean;
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
    destroy?: () => Promise<void>;
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
const proofStepIsActive = new WeakMap<HTMLButtonElement, () => boolean>();
const maxProofImages = 37;

const proofOptimizationOptions = {
    maxSize: 2560,
    quality: 0.88,
    targetMaxBytes: 1.5 * 1024 * 1024
};
let preferredProofCanvasContentType: Promise<'image/webp' | 'image/jpeg'> | null = null;

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


interface ProofArea { x: number; y: number; width: number; height: number; }
interface ProofEditPage {
    areas: ProofArea[];
    undo: ProofArea[][];
    selected: number;
    scale: number;
    x: number;
    y: number;
}

// Drafts contain only geometry. Applying flattens all edited pages before the
// owner atomically replaces submission bytes and its duplicate/removal caches.
class ProofRedaction {
    readonly element = document.createElement('div');
    readonly controls = document.createElement('div');
    readonly pages = new Map<number, ProofEditPage>();
    busy = false;
    private image = document.createElement('img');
    private sheet = document.createElement('div');
    private overlay = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    private page: ProofEditPage | null = null;
    private index = -1;
    private tool: 'hide' | 'move' = 'hide';
    private pointers = new Map<number, { x: number; y: number }>();
    private gesture: { kind: string; x: number; y: number; before: ProofArea[]; area: ProofArea | null; corner: string } | null = null;
    private pinch: { distance: number; scale: number; x: number; y: number } | null = null;
    private lastFit = 0;
    private observer: ResizeObserver;
    private status = document.createElement('span');
    private applyButton: HTMLButtonElement;
    private undoButton: HTMLButtonElement;
    private deleteButton: HTMLButtonElement;
    private moveButton: HTMLButtonElement;
    private fitButton: HTMLButtonElement;
    private apply: () => Promise<void>;

    constructor(apply: () => Promise<void>) {
        this.apply = apply;
        this.element.className = 'proof-edit-stage';
        this.element.setAttribute('data-lwc-no-gesture', '');
        this.element.tabIndex = 0;
        this.element.setAttribute('role', 'region');
        this.element.setAttribute('aria-label', 'Hide details on proof page');
        this.element.setAttribute('aria-description', 'Drag to hide an area. Enter adds an area. Page Up and Page Down select areas. Arrow keys move the selection; Shift and arrows resize it; Alt makes fine adjustments. Delete removes it. Escape deselects. Control Z undoes. Plus and minus zoom. With no selection, arrows pan.');
        this.image.alt = '';
        this.image.draggable = false;
        this.sheet.className = 'proof-edit-sheet';
        this.sheet.append(this.image, this.overlay);
        this.element.append(this.sheet);
        this.controls.className = 'proof-edit-controls';
        const tools = document.createElement('div');
        tools.className = 'proof-edit-tools';
        this.undoButton = this.iconButton('Undo', 'proof-edit-undo', 'M9 5 4 10l5 5M4 10h10a6 6 0 0 1 0 12', () => this.undo());
        this.moveButton = this.iconButton('Move page', 'proof-edit-move', 'M8 13V6a2 2 0 0 1 4 0v6-8a2 2 0 0 1 4 0v8-6a2 2 0 0 1 4 0v9c0 5-3 7-7 7-3 0-5-2-6-4l-3-5a2 2 0 0 1 3-2l1 2', () => {
            this.finishGesture(true);
            this.tool = this.tool === 'hide' ? 'move' : 'hide';
            if (this.tool === 'move' && this.page) this.page.selected = -1;
            this.render();
        });
        this.moveButton.hidden = true;
        this.deleteButton = this.iconButton('Remove selected area', 'proof-edit-delete', 'M4 7h16M9 7V4h6v3M6 7l1 14h10l1-14M10 11v6m4-6v6', () => {
            this.deleteSelection();
            this.element.focus({ preventScroll: true });
        });
        this.deleteButton.hidden = true;
        this.element.append(this.deleteButton);
        tools.append(this.undoButton, this.moveButton);
        const actions = document.createElement('div');
        actions.className = 'proof-edit-actions';
        const zoom = document.createElement('div');
        zoom.className = 'proof-edit-zoom';
        const out = this.button('−', 'proof-edit-zoom-out', () => this.zoom(1 / 1.4));
        out.setAttribute('aria-label', 'Zoom out');
        this.fitButton = this.button('Fit page', 'proof-edit-fit', () => this.fit());
        this.fitButton.setAttribute('aria-label', 'Fit proof page');
        const into = this.button('+', 'proof-edit-zoom-in', () => this.zoom(1.4));
        into.setAttribute('aria-label', 'Zoom in');
        zoom.append(out, this.fitButton, into);
        this.applyButton = this.button('Apply', 'proof-edit-apply', () => { void this.apply(); });
        actions.append(tools, zoom, this.applyButton);
        this.status.className = 'proof-edit-status';
        this.status.setAttribute('role', 'status');
        this.controls.append(actions, this.status);
        this.element.addEventListener('pointerdown', event => this.pointerDown(event));
        this.element.addEventListener('pointermove', event => this.pointerMove(event));
        this.element.addEventListener('pointerup', event => this.pointerEnd(event, false));
        this.element.addEventListener('pointercancel', event => this.pointerEnd(event, true));
        this.element.addEventListener('lostpointercapture', event => this.pointerEnd(event, true));
        // This surface owns touch drawing/panning. Suppress the compatibility
        // mouse gesture so the next tap can activate a toolbar control directly.
        for (const type of ['touchstart', 'touchmove']) this.element.addEventListener(type, event => {
            if (this.page && !this.busy && !this.deleteButton.contains(event.target as Node)) event.preventDefault();
        }, { passive: false });
        this.element.addEventListener('keydown', event => this.keydown(event));
        this.element.addEventListener('wheel', event => {
            // Browser zoom remains native, including Ctrl+wheel at 200%.
            if (!this.page || this.busy || event.ctrlKey) return;
            event.preventDefault();
            this.page.x -= event.deltaX;
            this.page.y -= event.deltaY;
            this.render();
        }, { passive: false });
        this.observer = new ResizeObserver(() => {
            if (!this.page || this.busy) return;
            if (Math.abs(this.page.scale - this.lastFit) < .001) this.fit();
            else this.render();
        });
        this.observer.observe(this.element);
    }

    private button(label: string, name: string, action: () => void): HTMLButtonElement {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = name;
        button.textContent = label;
        button.addEventListener('click', () => { if (!this.busy) action(); });
        return button;
    }

    private iconButton(label: string, name: string, path: string, action: () => void): HTMLButtonElement {
        const button = this.button(label, name, action);
        button.setAttribute('aria-label', label);
        button.title = label;
        const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.setAttribute('viewBox', '0 0 24 24');
        icon.setAttribute('aria-hidden', 'true');
        const drawing = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        drawing.setAttribute('d', path);
        icon.append(drawing);
        button.replaceChildren(icon);
        return button;
    }

    async show(index: number, source: string): Promise<void> {
        if (index === this.index) return;
        this.finishGesture(true);
        this.pointers.clear();
        this.pinch = null;
        this.index = index;
        this.page = null;
        this.deleteButton.hidden = true;
        this.sheet.hidden = true;
        this.image.src = source;
        this.status.textContent = 'Loading page…';
        try {
            await this.image.decode();
            if (this.index !== index || !this.element.isConnected) return;
            const existing = this.pages.get(index);
            this.page = existing || { areas: [], undo: [], selected: -1, scale: 1, x: 0, y: 0 };
            this.pages.set(index, this.page);
            this.sheet.style.width = `${this.image.naturalWidth}px`;
            this.sheet.style.height = `${this.image.naturalHeight}px`;
            this.overlay.setAttribute('viewBox', `0 0 ${this.image.naturalWidth} ${this.image.naturalHeight}`);
            this.sheet.hidden = false;
            if (existing) this.render();
            else this.fit();
        } catch (_) {
            if (this.index === index) this.status.textContent = 'This page could not be opened. Cancel and try again.';
        }
    }

    private fitScale(): number {
        return Math.min((this.element.clientWidth - 32) / this.image.naturalWidth,
            (this.element.clientHeight - 32) / this.image.naturalHeight, 1);
    }

    private fit(): void {
        if (!this.page || this.busy) return;
        this.finishGesture(true);
        this.tool = 'hide';
        this.page.scale = Math.max(.01, this.fitScale());
        this.lastFit = this.page.scale;
        this.page.x = (this.element.clientWidth - this.image.naturalWidth * this.page.scale) / 2;
        this.page.y = (this.element.clientHeight - this.image.naturalHeight * this.page.scale) / 2;
        this.render();
    }

    private zoom(factor: number): void {
        if (!this.page) return;
        this.finishGesture(true);
        const area = this.page.areas[this.page.selected];
        const x = area ? this.page.x + (area.x + area.width / 2) * this.page.scale : this.element.clientWidth / 2;
        const y = area ? this.page.y + (area.y + area.height / 2) * this.page.scale : this.element.clientHeight / 2;
        this.zoomAt(this.page.scale * factor, Math.max(0, Math.min(this.element.clientWidth, x)), Math.max(0, Math.min(this.element.clientHeight, y)));
        this.render();
    }

    private zoomAt(scale: number, x: number, y: number): void {
        if (!this.page) return;
        const next = Math.max(this.fitScale(), Math.min(8, scale));
        this.page.x = x - (x - this.page.x) * next / this.page.scale;
        this.page.y = y - (y - this.page.y) * next / this.page.scale;
        this.page.scale = next;
    }

    private point(event: PointerEvent): { x: number; y: number } {
        const bounds = this.element.getBoundingClientRect();
        return { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
    }

    private imagePoint(point: { x: number; y: number }): { x: number; y: number } {
        const page = this.page!;
        return { x: Math.max(0, Math.min(this.image.naturalWidth, (point.x - page.x) / page.scale)),
            y: Math.max(0, Math.min(this.image.naturalHeight, (point.y - page.y) / page.scale)) };
    }

    private pointerDown(event: PointerEvent): void {
        if (!this.page || this.busy || event.button > 0 || this.deleteButton.contains(event.target as Node)) return;
        if (event.pointerType !== 'touch') event.preventDefault();
        this.element.focus({ preventScroll: true });
        this.element.setPointerCapture(event.pointerId);
        const point = this.point(event);
        this.pointers.set(event.pointerId, point);
        if (this.pointers.size === 2) {
            // A second finger turns a tentative mark into a pinch, never a redaction.
            this.finishGesture(true);
            const [a, b] = [...this.pointers.values()] as [{ x: number; y: number }, { x: number; y: number }];
            const center = this.imagePoint({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
            this.pinch = { distance: Math.max(1, Math.hypot(a.x - b.x, a.y - b.y)), scale: this.page.scale, ...center };
            return;
        }
        if (this.pointers.size !== 1) return;
        const p = this.imagePoint(point);
        const target = event.target instanceof Element ? event.target.closest('[data-area]') : null;
        const index = target ? Number(target.getAttribute('data-area')) : -1;
        const area = this.page.areas[index];
        const corner = target?.getAttribute('data-corner') || '';
        const before = this.copyAreas();
        if (this.tool === 'move') {
            this.gesture = { kind: 'pan', ...point, before, area: null, corner };
        } else if (area) {
            this.page.selected = index;
            this.gesture = { kind: corner ? 'resize' : 'move', ...p, before, area: { ...area }, corner };
        } else {
            const inside = point.x >= this.page.x && point.x <= this.page.x + this.image.naturalWidth * this.page.scale
                && point.y >= this.page.y && point.y <= this.page.y + this.image.naturalHeight * this.page.scale;
            if (!inside) { this.page.selected = -1; this.render(); return; }
            this.page.selected = this.page.areas.length;
            this.page.areas.push({ ...p, width: 0, height: 0 });
            this.gesture = { kind: 'draw', ...p, before, area: null, corner };
        }
        this.render();
    }

    private pointerMove(event: PointerEvent): void {
        const page = this.page;
        if (!page || !this.pointers.has(event.pointerId)) return;
        const point = this.point(event);
        this.pointers.set(event.pointerId, point);
        if (this.pinch && this.pointers.size >= 2) {
            const [a, b] = [...this.pointers.values()] as [{ x: number; y: number }, { x: number; y: number }];
            this.zoomAt(this.pinch.scale * Math.hypot(a.x - b.x, a.y - b.y) / this.pinch.distance, 0, 0);
            page.x = (a.x + b.x) / 2 - this.pinch.x * page.scale;
            page.y = (a.y + b.y) / 2 - this.pinch.y * page.scale;
        } else if (this.gesture) {
            const g = this.gesture;
            const p = this.imagePoint(point);
            const area = page.areas[page.selected];
            if (g.kind === 'pan') {
                page.x += point.x - g.x; page.y += point.y - g.y;
                g.x = point.x; g.y = point.y;
            } else if (area) {
                if (g.kind === 'draw') {
                    Object.assign(area, { x: Math.min(g.x, p.x), y: Math.min(g.y, p.y), width: Math.abs(p.x - g.x), height: Math.abs(p.y - g.y) });
                } else if (g.area && g.kind === 'move') {
                    area.x = Math.max(0, Math.min(this.image.naturalWidth - area.width, g.area.x + p.x - g.x));
                    area.y = Math.max(0, Math.min(this.image.naturalHeight - area.height, g.area.y + p.y - g.y));
                } else if (g.area) {
                    const anchorX = g.corner.includes('w') ? g.area.x + g.area.width : g.area.x;
                    const anchorY = g.corner.includes('n') ? g.area.y + g.area.height : g.area.y;
                    Object.assign(area, { x: Math.min(anchorX, p.x), y: Math.min(anchorY, p.y), width: Math.abs(anchorX - p.x), height: Math.abs(anchorY - p.y) });
                }
            }
        }
        this.render();
    }

    private pointerEnd(event: PointerEvent, canceled: boolean): void {
        if (!this.pointers.has(event.pointerId)) return;
        this.pointers.delete(event.pointerId);
        if (this.element.hasPointerCapture(event.pointerId)) this.element.releasePointerCapture(event.pointerId);
        this.finishGesture(canceled);
        // Keep a pinch inert until both fingers lift; the remaining finger must
        // not accidentally paint or move an area.
        if (!this.pointers.size) this.pinch = null;
        this.render();
    }

    private copyAreas(): ProofArea[] { return this.page!.areas.map(area => ({ ...area })); }

    private finishGesture(cancel: boolean): void {
        if (!this.gesture || !this.page) return;
        const g = this.gesture;
        this.gesture = null;
        const area = this.page.areas[this.page.selected];
        if (g.kind === 'pan') return;
        if (cancel || !area || area.width * this.page.scale < 3 || area.height * this.page.scale < 3) {
            this.page.areas = g.before;
            this.page.selected = -1;
        } else if (JSON.stringify(g.before) !== JSON.stringify(this.page.areas)) {
            this.page.undo.push(g.before);
        }
    }

    private undo(): void {
        if (!this.page) return;
        this.finishGesture(true);
        const areas = this.page.undo.pop();
        if (areas) { this.page.areas = areas; this.page.selected = -1; }
        this.render();
    }

    private deleteSelection(): void {
        if (!this.page || this.page.selected < 0) return;
        this.page.undo.push(this.copyAreas());
        this.page.areas.splice(this.page.selected, 1);
        this.page.selected = -1;
        this.render();
    }

    private keydown(event: KeyboardEvent): void {
        if (!this.page || this.busy || event.target !== this.element) return;
        // Keep browser zoom and tab-navigation shortcuts native. Only Undo is
        // owned by the editor when a platform command modifier is held.
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() !== 'z') return;
        const page = this.page;
        const area = page.areas[page.selected];
        const arrows = ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'];
        if (!['Enter', 'Delete', 'Backspace', 'Escape', 'PageUp', 'PageDown', '+', '=', '-', 'z', 'Z', ...arrows].includes(event.key)) return;
        if (event.key.toLowerCase() === 'z' && !(event.ctrlKey || event.metaKey)) return;
        if (event.key === 'Escape' && page.selected < 0 && !this.gesture) return;
        event.preventDefault();
        event.stopPropagation();
        if (event.key.toLowerCase() === 'z') { this.undo(); return; }
        if (event.key === '+' || event.key === '=') { this.zoom(1.4); return; }
        if (event.key === '-') { this.zoom(1 / 1.4); return; }
        if (event.key === 'PageUp' || event.key === 'PageDown') {
            this.finishGesture(true);
            if (page.areas.length) {
                page.selected = page.selected < 0 ? (event.key === 'PageDown' ? 0 : page.areas.length - 1)
                    : (page.selected + (event.key === 'PageDown' ? 1 : -1) + page.areas.length) % page.areas.length;
                this.tool = 'hide';
                const selected = page.areas[page.selected]!;
                const x = page.x + selected.x * page.scale, y = page.y + selected.y * page.scale;
                if (x < 16 || y < 16 || x + selected.width * page.scale > this.element.clientWidth - 16
                    || y + selected.height * page.scale > this.element.clientHeight - 16) {
                    page.x = this.element.clientWidth / 2 - (selected.x + selected.width / 2) * page.scale;
                    page.y = this.element.clientHeight / 2 - (selected.y + selected.height / 2) * page.scale;
                }
            }
        } else if (event.key === 'Escape') { this.finishGesture(true); page.selected = -1; }
        else if (event.key === 'Delete' || event.key === 'Backspace') { this.deleteSelection(); return; }
        else if (event.key === 'Enter') {
            page.undo.push(this.copyAreas());
            const center = this.imagePoint({ x: this.element.clientWidth / 2, y: this.element.clientHeight / 2 });
            const width = Math.min(this.image.naturalWidth, this.element.clientWidth / page.scale) * .35;
            const height = Math.min(this.image.naturalHeight, 44 / page.scale);
            page.selected = page.areas.length;
            page.areas.push({ x: Math.max(0, Math.min(this.image.naturalWidth - width, center.x - width / 2)),
                y: Math.max(0, Math.min(this.image.naturalHeight - height, center.y - height / 2)), width, height });
            this.tool = 'hide';
        } else if (area && this.tool === 'hide') {
            if (!event.repeat) page.undo.push(this.copyAreas());
            const step = event.altKey ? 1 : Math.max(1, Math.round(5 / page.scale));
            const dx = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
            const dy = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
            if (event.shiftKey) {
                area.width = Math.max(1, Math.min(this.image.naturalWidth - area.x, area.width + dx));
                area.height = Math.max(1, Math.min(this.image.naturalHeight - area.y, area.height + dy));
            } else {
                area.x = Math.max(0, Math.min(this.image.naturalWidth - area.width, area.x + dx));
                area.y = Math.max(0, Math.min(this.image.naturalHeight - area.height, area.y + dy));
            }
        } else {
            page.x += event.key === 'ArrowLeft' ? 80 : event.key === 'ArrowRight' ? -80 : 0;
            page.y += event.key === 'ArrowUp' ? 80 : event.key === 'ArrowDown' ? -80 : 0;
        }
        this.render();
    }

    render(): void {
        const page = this.page;
        if (!page) return;
        const w = this.image.naturalWidth * page.scale, h = this.image.naturalHeight * page.scale;
        const canPan = page.scale > this.fitScale() + .001;
        if (!canPan) this.tool = 'hide';
        page.x = w <= this.element.clientWidth - 32 ? (this.element.clientWidth - w) / 2 : Math.max(this.element.clientWidth - w - 16, Math.min(16, page.x));
        page.y = h <= this.element.clientHeight - 32 ? (this.element.clientHeight - h) / 2 : Math.max(this.element.clientHeight - h - 16, Math.min(16, page.y));
        this.sheet.style.transform = `translate(${page.x}px, ${page.y}px) scale(${page.scale})`;
        this.element.dataset.tool = this.tool;
        let shapeIndex = 0;
        const shape = (tag: string, attributes: Record<string, string | number>): SVGElement => {
            const previous = this.overlay.children.item(shapeIndex++);
            const element = previous?.tagName === tag ? previous : document.createElementNS('http://www.w3.org/2000/svg', tag);
            for (const attribute of Array.from(element.attributes)) {
                if (!(attribute.name in attributes)) element.removeAttribute(attribute.name);
            }
            for (const [key, value] of Object.entries(attributes)) {
                if (element.getAttribute(key) !== String(value)) element.setAttribute(key, String(value));
            }
            if (element !== previous) {
                if (previous) previous.replaceWith(element);
                else this.overlay.append(element);
            }
            return element as SVGElement;
        };
        page.areas.forEach((area, index) => {
            shape('rect', { ...area, fill: '#000', 'data-area': index });
            if (index !== page.selected || this.tool === 'move') return;
            shape('rect', { ...area, fill: 'none', stroke: '#fff', 'stroke-width': 3 / page.scale, 'pointer-events': 'none' });
            shape('rect', { ...area, fill: 'none', stroke: '#087f8c', 'stroke-width': 1.5 / page.scale, 'pointer-events': 'none' });
            for (const [corner, x, y] of [['nw', area.x, area.y], ['ne', area.x + area.width, area.y], ['sw', area.x, area.y + area.height], ['se', area.x + area.width, area.y + area.height]] as const) {
                shape('circle', { cx: x, cy: y, r: 5 / page.scale, fill: '#fff', stroke: '#087f8c', 'stroke-width': 2 / page.scale, 'pointer-events': 'none' });
                shape('circle', { cx: x, cy: y, r: 22 / page.scale, fill: 'transparent', 'data-area': index, 'data-corner': corner, class: 'proof-edit-handle' });
            }
        });
        while (this.overlay.children.length > shapeIndex) this.overlay.lastElementChild?.remove();
        this.moveButton.hidden = !canPan;
        this.moveButton.setAttribute('aria-pressed', String(this.tool === 'move'));
        this.undoButton.disabled = this.busy || !page.undo.length;
        const selected = page.areas[page.selected];
        this.deleteButton.hidden = this.busy || !selected || !!this.gesture || !!this.pinch || this.tool === 'move';
        if (!this.deleteButton.hidden && selected) {
            const width = this.element.clientWidth, height = this.element.clientHeight;
            const x = page.x + selected.x * page.scale, y = page.y + selected.y * page.scale;
            const right = x + selected.width * page.scale, bottom = y + selected.height * page.scale;
            // Keep removal beside the selection, clear of its 44px corner targets.
            // Try each side before using a corner of the viewport for a very large area.
            const cx = Math.max(8, Math.min(width - 52, (x + right) / 2 - 22));
            const cy = Math.max(8, Math.min(height - 52, (y + bottom) / 2 - 22));
            const candidates: [number, number][] = [[right + 28, cy], [x - 72, cy], [cx, y - 72], [cx, bottom + 28]];
            const position = candidates.find(([left, top]) => left >= 8 && top >= 8 && left + 44 <= width - 8 && top + 44 <= height - 8)
                || [width - 52, 8];
            this.deleteButton.style.left = `${position[0]}px`;
            this.deleteButton.style.top = `${position[1]}px`;
            this.deleteButton.hidden = right < 0 || bottom < 0 || x > width || y > height;
        }
        const count = [...this.pages.values()].reduce((sum, draft) => sum + draft.areas.length, 0);
        this.applyButton.disabled = this.busy || !count;
        this.applyButton.textContent = this.busy ? 'Applying…' : 'Apply';
        this.applyButton.setAttribute('aria-label', this.busy ? 'Applying hidden areas' : `Apply${count ? ` (${count})` : ''}`);
        this.element.setAttribute('aria-label', `Hide details on proof page${page.selected >= 0 ? `, area ${page.selected + 1} of ${page.areas.length} selected` : ''}`);
        const keyboard = this.element.matches(':focus-visible');
        // Do not reflow the page underneath an active finger or pointer.
        if (!this.gesture && !this.pinch) this.status.textContent = this.busy ? 'Saving hidden areas…' : page.selected >= 0
            ? keyboard ? 'Arrows: move · Shift: resize · Delete: remove' : 'Drag to adjust'
            : keyboard ? page.areas.length ? 'Enter: add · Page ↑ / ↓: select · Arrows: pan' : 'Enter: add area · Arrows: pan · + / −: zoom'
                : this.tool === 'hide' ? 'Drag to hide' : 'Drag to move page';
        this.element.dispatchEvent(new Event('proof-edit-change', { bubbles: true }));
    }

    async flatten(images: readonly string[]): Promise<Map<string, string>> {
        this.finishGesture(false);
        const replacements = new Map<string, string>();
        for (const [index, page] of this.pages) {
            if (!page.areas.length) continue;
            const source = images[index];
            if (!source) throw new Error('Proof page no longer available.');
            const image = new Image();
            image.src = source;
            await image.decode();
            const canvas = document.createElement('canvas');
            canvas.width = image.naturalWidth;
            canvas.height = image.naturalHeight;
            try {
                const context = canvas.getContext('2d');
                if (!context) throw new Error('Canvas unavailable.');
                context.drawImage(image, 0, 0);
                context.fillStyle = '#000';
                for (const area of page.areas) {
                    // Round outward to cover the entire chosen area with opaque pixels.
                    const x = Math.floor(area.x), y = Math.floor(area.y);
                    context.fillRect(x, y, Math.ceil(area.x + area.width) - x, Math.ceil(area.y + area.height) - y);
                }
                // Encode the flattened canvas directly: no original-image fallback.
                replacements.set(source, await encodeProofCanvas(canvas));
            } finally { canvas.width = canvas.height = 0; image.src = ''; }
        }
        return replacements;
    }

    error(): void { this.status.textContent = 'Could not apply. Your selections are kept; try again.'; }

    dispose(): void {
        this.observer.disconnect();
        this.index = -1;
        this.page = null;
        this.pages.clear();
        this.image.removeAttribute('src');
        this.element.remove();
        this.controls.remove();
    }
}

class ProofReview {
    readonly labels = new Map<string, string>();
    private removed: { image: string; index: number }[] = [];
    private notice = '';
    private progress: { label: string; completed: number; total: number } | null = null;
    private dialog: HTMLDialogElement | null = null;
    private viewerIndex = 0;
    private opener: HTMLElement | null = null;
    private zoomed = false;
    private editor: ProofRedaction | null = null;
    private replaceSources: (replacements: Map<string, string>) => Promise<void>;
    // Pages already on screen; anything new is revealed as it arrives.
    private shown = new Set<string>();
    private arrivals = new Map<string, number>();
    private shownProgress = 0;
    private container: HTMLElement;
    private images: string[];
    private changed: () => void;
    private hasUnsubmittedProofs: () => boolean;
    private warnBeforeLeaving = (event: BeforeUnloadEvent): void => {
        if (!this.progress && !this.hasUnsubmittedProofs()) return;
        event.preventDefault();
        event.returnValue = '';
    };

    constructor(container: HTMLElement, images: string[], changed: () => void, hasUnsubmittedProofs: () => boolean,
        replaceSources: (replacements: Map<string, string>) => Promise<void>) {
        this.container = container;
        this.images = images;
        this.changed = changed;
        this.hasUnsubmittedProofs = hasUnsubmittedProofs;
        this.replaceSources = replaceSources;
        images.forEach(image => this.shown.add(image));
        container.classList.add('proof-review-panel');
    }

    setProgress(progress: { label: string; completed: number; total: number } | null): void {
        this.progress = progress;
        this.render();
    }

    announce(message: string): void {
        if (this.notice === message) return;
        this.notice = message;
        this.render();
    }

    render(): void {
        if (this.progress || this.hasUnsubmittedProofs()) {
            window.addEventListener('beforeunload', this.warnBeforeLeaving);
        } else {
            window.removeEventListener('beforeunload', this.warnBeforeLeaving);
        }
        this.container.replaceChildren();
        this.container.hidden = !this.images.length && !this.removed.length && !this.progress && !this.notice;
        // One document card: a header band with the page count and any preparation,
        // the page tiles, then a footer for notices and undo.
        const head = document.createElement('div');
        head.className = 'proof-review-head';
        const heading = document.createElement('h3');
        heading.className = 'proof-review-heading';
        heading.textContent = `${this.images.length} proof ${this.images.length === 1 ? 'page' : 'pages'}`;
        head.appendChild(heading);
        this.container.appendChild(head);

        if (!this.progress) this.shownProgress = 0;
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
            // The drawn bar starts where the last one stopped and creeps toward the
            // next file while it is prepared, so a single file never sits at zero.
            const ratio = this.progress.total ? Math.min(1, this.progress.completed / this.progress.total) : 0;
            const next = this.progress.total ? Math.min(1, (Math.floor(this.progress.completed) + 0.85) / this.progress.total) : 0;
            const bar = document.createElement('span');
            bar.className = 'proof-preparation-bar';
            bar.setAttribute('aria-hidden', 'true');
            bar.style.setProperty('--proof-from', String(Math.min(this.shownProgress, ratio)));
            bar.style.setProperty('--proof-to', String(ratio));
            bar.style.setProperty('--proof-creep', String(Math.max(ratio, next)));
            bar.appendChild(document.createElement('span'));
            this.shownProgress = ratio;
            status.append(label, meter, bar);
            head.appendChild(status);
        }

        const grid = document.createElement('div');
        grid.className = 'proof-page-grid';
        grid.setAttribute('aria-busy', String(Boolean(this.progress)));
        let arriving = 0;
        this.images.forEach((source, index) => {
            const card = document.createElement('article');
            card.className = 'proof-page-card';
            if (!this.shown.has(source)) {
                this.shown.add(source);
                this.arrivals.set(source, performance.now() - arriving++ * 70);
            }
            // Re-renders during an arrival continue its reveal instead of restarting it.
            const arrivedAt = this.arrivals.get(source);
            if (arrivedAt !== undefined) {
                const elapsed = performance.now() - arrivedAt;
                if (elapsed < 1000) {
                    card.classList.add('is-arriving');
                    card.style.setProperty('--proof-elapsed', `${Math.round(elapsed)}ms`);
                } else {
                    this.arrivals.delete(source);
                }
            }
            const preview = this.button('', 'proof-page-preview', () => this.open(index, preview));
            preview.disabled = Boolean(this.progress);
            preview.setAttribute('aria-label', `Review proof page ${index + 1}${this.labels.has(source) ? `: ${this.labels.get(source)}` : ''}`);
            const image = document.createElement('img');
            image.src = source;
            image.alt = `Proof image ${index + 1}`;
            const previewLabel = document.createElement('span');
            previewLabel.textContent = 'Review page';
            const hint = document.createElement('i');
            hint.className = 'fas fa-magnifying-glass-plus proof-page-zoom-hint';
            hint.setAttribute('aria-hidden', 'true');
            preview.append(image, previewLabel, hint);
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
                if (!card.isConnected || this.progress) return;
                const bounds = image.getBoundingClientRect();
                // Submission state and validation change in this click, before any
                // animation. Only an inert copy of the sheet remains on screen.
                this.removePage(source, index);
                if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || typeof card.animate !== 'function') return;
                const sheet = document.createElement('img');
                sheet.src = source;
                sheet.alt = '';
                sheet.className = 'proof-page-exit';
                sheet.setAttribute('aria-hidden', 'true');
                sheet.inert = true;
                Object.assign(sheet.style, {
                    left: `${bounds.left}px`, top: `${bounds.top}px`,
                    width: `${bounds.width}px`, height: `${bounds.height}px`
                });
                document.body.appendChild(sheet);
                const exit = sheet.animate([
                    { opacity: 1, scale: '1' },
                    { opacity: 0, scale: '0.9' }
                ], { duration: 140, easing: 'cubic-bezier(0.3, 0, 1, 1)' });
                exit.addEventListener('finish', () => sheet.remove(), { once: true });
                exit.addEventListener('cancel', () => sheet.remove(), { once: true });
            });
            remove.disabled = Boolean(this.progress);
            remove.setAttribute('aria-label', `Remove proof page ${index + 1}`);
            card.append(preview, caption, remove);
            grid.appendChild(card);
        });
        this.renderRest(grid);
    }

    private removePage(source: string, index: number): void {
        if (this.images[index] !== source) index = this.images.indexOf(source);
        if (index < 0) return;
        this.removed.push({ image: source, index });
        this.images.splice(index, 1);
        // Undo brings the page back with the same reveal as a fresh upload.
        this.shown.delete(source);
        this.arrivals.delete(source);
        this.notice = '';
        this.render();
        this.changed();
        const target = this.container.querySelectorAll<HTMLButtonElement>('.proof-page-remove')[Math.min(index, this.images.length - 1)]
            || this.container.querySelector<HTMLButtonElement>('.proof-undo');
        target?.focus({ preventScroll: true });
    }

    private renderRest(grid: HTMLElement): void {
        if (this.progress) {
            // Files still being prepared hold their place in the grid.
            const pending = Math.max(1, this.progress.total - Math.floor(this.progress.completed));
            for (let index = 0; index < Math.min(pending, maxProofImages - this.images.length); index++) {
                const placeholder = document.createElement('div');
                placeholder.className = 'proof-page-pending';
                placeholder.setAttribute('aria-hidden', 'true');
                placeholder.innerHTML = '<span class="proof-page-pending-sheet"><i class="fas fa-file-medical"></i></span><span class="proof-page-pending-line"></span><span class="proof-page-pending-line"></span>';
                grid.appendChild(placeholder);
            }
        }
        this.container.appendChild(grid);

        if (this.notice || this.removed.length) {
            const status = document.createElement('div');
            status.className = 'proof-review-feedback';
            const message = document.createElement('span');
            message.className = 'proof-upload-notice';
            message.setAttribute('role', 'status');
            message.textContent = this.notice;
            status.classList.toggle('has-notice', Boolean(this.notice));
            status.appendChild(message);
            if (this.removed.length) {
                const undo = this.button('Undo removal', 'proof-undo', () => {
                    const removed = this.removed.pop();
                    if (!removed) return;
                    // Re-uploading may already have restored this page in a new
                    // position. Keep that order and never duplicate its bytes.
                    if (!this.images.includes(removed.image)) {
                        this.images.splice(Math.min(removed.index, this.images.length), 0, removed.image);
                    }
                    this.notice = '';
                    this.render();
                    this.changed();
                    this.container.querySelectorAll<HTMLButtonElement>('.proof-page-preview')[this.images.indexOf(removed.image)]?.focus({ preventScroll: true });
                });
                undo.disabled = Boolean(this.progress) || (this.images.length >= maxProofImages
                    && !this.images.includes(this.removed[this.removed.length - 1]!.image));
                const undoIcon = document.createElement('i');
                undoIcon.className = 'fas fa-rotate-left';
                undoIcon.setAttribute('aria-hidden', 'true');
                undo.prepend(undoIcon);
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
            dialog.innerHTML = `<div class="proof-review-toolbar"><div class="proof-review-title"><span class="proof-review-position" role="status"></span><h2></h2></div><button type="button" class="proof-review-close" aria-label="Close proof review"><span class="proof-review-close-label">Close </span><span aria-hidden="true">×</span></button></div>
                <div class="proof-review-stage" tabindex="0" role="region" aria-label="Proof page"><img alt=""></div>
                <div class="proof-review-filmstrip" role="group" aria-label="Proof pages"></div>
                <div class="proof-review-navigation"><div class="proof-review-steps"><button type="button" class="proof-review-previous"><span>←</span> <span class="proof-review-step-label">Previous</span></button><button type="button" class="proof-review-next"><span class="proof-review-step-label">Next</span> <span>→</span></button></div><button type="button" class="proof-review-zoom"><span class="proof-review-zoom-label">Zoom in</span></button><button type="button" class="proof-review-hide">Hide details</button></div>`;
            dialog.querySelector('.proof-review-close')?.addEventListener('click', () => this.editor ? this.cancelEdit() : dialog.close());
            dialog.querySelector('.proof-review-hide')?.addEventListener('click', () => this.startEdit());
            dialog.querySelector('.proof-review-previous')?.addEventListener('click', () => this.navigate(-1));
            dialog.querySelector('.proof-review-next')?.addEventListener('click', () => this.navigate(1));
            dialog.querySelector('.proof-review-zoom')?.addEventListener('click', () => { this.zoomed = !this.zoomed; this.updateViewer(); });
            dialog.addEventListener('keydown', event => {
                const inFilmstrip = document.activeElement?.closest('.proof-review-filmstrip');
                if (event.key === 'Tab') {
                    const controls = Array.from(dialog.querySelectorAll<HTMLElement>('button:not(:disabled), [tabindex="0"]')).filter(control => control.getClientRects().length > 0);
                    const first = controls[0];
                    const last = controls[controls.length - 1];
                    if ((event.shiftKey && document.activeElement === first) || (!event.shiftKey && document.activeElement === last)) {
                        event.preventDefault();
                        (event.shiftKey ? last : first)?.focus();
                    }
                }
                if (this.editor && !inFilmstrip) return;
                if (this.zoomed && !inFilmstrip && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
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
                    if (inFilmstrip) dialog.querySelector<HTMLButtonElement>('.proof-review-thumb[aria-current="true"]')?.focus({ preventScroll: true });
                } else if (inFilmstrip && (event.key === 'Home' || event.key === 'End')) {
                    event.preventDefault();
                    this.viewerIndex = event.key === 'Home' ? 0 : this.images.length - 1;
                    this.zoomed = false;
                    this.updateViewer();
                    dialog.querySelector<HTMLButtonElement>('.proof-review-thumb[aria-current="true"]')?.focus({ preventScroll: true });
                }
            });
            dialog.addEventListener('cancel', event => {
                if (!this.editor) return;
                event.preventDefault();
                this.cancelEdit();
            });
            dialog.addEventListener('click', event => {
                if (event.target !== dialog) return;
                const box = dialog.getBoundingClientRect();
                if (!this.editor && (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom)) dialog.close();
            });
            dialog.addEventListener('close', () => {
                // Native close events are queued: the user may already have
                // moved focus or reopened this same dialog before delivery.
                if (dialog.open) return;
                this.endEdit();
                document.body.classList.remove('proof-review-open');
                const active = document.activeElement;
                if (!active || active === document.body || dialog.contains(active)) {
                    const opener = this.opener?.isConnected ? this.opener : this.container.querySelectorAll<HTMLButtonElement>('.proof-page-preview')[this.viewerIndex];
                    opener?.focus({ preventScroll: true });
                }
            });
            this.dialog = dialog;
            document.body.appendChild(dialog);
            const steps = dialog.querySelector<HTMLElement>('.proof-review-steps')!;
            const viewerResize = new ResizeObserver(() => this.fitNavigation());
            viewerResize.observe(steps);
            viewerResize.observe(dialog);
            viewerResize.observe(dialog.querySelector<HTMLElement>('.proof-review-toolbar')!);
            void document.fonts.ready.then(() => this.fitNavigation());
        }
        this.buildFilmstrip();
        document.body.classList.add('proof-review-open');
        this.dialog.showModal();
        // Measure the filmstrip after the dialog has a layout, including when
        // opening directly on a page at the end of a long report.
        this.updateViewer();
    }

    // Every page as a thumbnail under the viewer, so a long report can be crossed in one tap.
    private buildFilmstrip(): void {
        const strip = this.dialog?.querySelector<HTMLElement>('.proof-review-filmstrip');
        if (!strip) return;
        strip.replaceChildren();
        strip.hidden = this.images.length < 2;
        this.images.forEach((source, index) => {
            const thumb = this.button('', 'proof-review-thumb', () => {
                this.viewerIndex = index;
                this.zoomed = false;
                this.updateViewer();
            });
            thumb.setAttribute('aria-label', `Proof page ${index + 1}`);
            const sheet = document.createElement('span');
            sheet.className = 'proof-review-thumb-sheet';
            sheet.style.backgroundImage = `url("${source}")`;
            thumb.appendChild(sheet);
            strip.appendChild(thumb);
        });
    }

    private navigate(direction: number): void {
        if (this.editor?.busy) return;
        this.viewerIndex = Math.max(0, Math.min(this.images.length - 1, this.viewerIndex + direction));
        this.zoomed = false;
        this.updateViewer();
    }

    private fitNavigation(): void {
        if (!this.dialog?.open) return;
        const dialog = this.dialog;
        dialog.classList.remove('is-compact-title', 'is-compact-height');
        const title = dialog.querySelector<HTMLElement>('h2')!;
        dialog.classList.toggle('is-compact-title', title.clientWidth < parseFloat(getComputedStyle(title).fontSize) * 3);
        const toolbar = dialog.querySelector<HTMLElement>('.proof-review-toolbar')!;
        const strip = dialog.querySelector<HTMLElement>('.proof-review-filmstrip')!;
        const navigation = dialog.querySelector<HTMLElement>('.proof-review-navigation')!;
        const stage = dialog.querySelector<HTMLElement>('.proof-review-stage')!;
        const requiredHeight = toolbar.offsetHeight + strip.offsetHeight + navigation.offsetHeight
            + parseFloat(getComputedStyle(stage).minHeight);
        dialog.classList.toggle('is-compact-height', !this.editor && requiredHeight > dialog.clientHeight);
        const steps = dialog.querySelector<HTMLElement>('.proof-review-steps')!;
        steps.classList.remove('is-compact');
        const buttons = Array.from(steps.querySelectorAll('button'));
        const overflow = steps.scrollWidth > steps.clientWidth + 1
            || buttons.some(button => button.scrollWidth > button.clientWidth + 1);
        steps.classList.toggle('is-compact', overflow);
        const current = strip.querySelector<HTMLElement>('[aria-current="true"]');
        if (current && strip.scrollWidth > strip.clientWidth) {
            const left = strip.scrollLeft + current.getBoundingClientRect().left
                - strip.getBoundingClientRect().left - (strip.clientWidth - current.offsetWidth) / 2;
            strip.scrollTo({ left: Math.max(0, left), behavior: 'instant' });
        }
    }

    private updateViewer(): void {
        const dialog = this.dialog;
        const source = this.images[this.viewerIndex];
        if (!dialog || !source) return;
        const title = dialog.querySelector('h2')!;
        title.textContent = this.labels.get(source) || `Proof page ${this.viewerIndex + 1}`;
        title.title = title.textContent;
        const image = dialog.querySelector('img')!;
        image.src = source;
        image.alt = `Proof page ${this.viewerIndex + 1} of ${this.images.length}`;
        dialog.querySelector('.proof-review-position')!.textContent = `${this.viewerIndex + 1} / ${this.images.length}`;
        dialog.querySelector<HTMLButtonElement>('.proof-review-previous')!.disabled = this.viewerIndex === 0;
        dialog.querySelector<HTMLButtonElement>('.proof-review-next')!.disabled = this.viewerIndex === this.images.length - 1;
        const zoom = dialog.querySelector<HTMLButtonElement>('.proof-review-zoom')!;
        zoom.querySelector<HTMLElement>('.proof-review-zoom-label')!.textContent = this.zoomed ? 'Fit page' : 'Zoom in';
        zoom.setAttribute('aria-pressed', String(this.zoomed));
        dialog.classList.toggle('is-zoomed', this.zoomed);
        const stage = dialog.querySelector<HTMLElement>('.proof-review-stage')!;
        stage.scrollTo(0, 0);
        const strip = dialog.querySelector<HTMLElement>('.proof-review-filmstrip');
        strip?.querySelectorAll<HTMLButtonElement>('.proof-review-thumb').forEach((thumb, index) => {
            const current = index === this.viewerIndex;
            thumb.tabIndex = current ? 0 : -1;
            if (current) thumb.setAttribute('aria-current', 'true');
            else thumb.removeAttribute('aria-current');
        });
        this.fitNavigation();
        if (this.editor) void this.editor.show(this.viewerIndex, source);
    }

    private startEdit(): void {
        if (!this.dialog || this.editor) return;
        this.editor = new ProofRedaction(() => this.applyEdit());
        this.zoomed = false;
        this.editor.element.addEventListener('proof-edit-change', () => {
            this.dialog?.querySelectorAll<HTMLButtonElement>('.proof-review-thumb').forEach((thumb, index) => {
                const count = this.editor?.pages.get(index)?.areas.length || 0;
                thumb.classList.toggle('has-edits', count > 0);
                thumb.setAttribute('aria-label', `Proof page ${index + 1}${count ? `, ${count} hidden ${count === 1 ? 'area' : 'areas'} pending` : ''}`);
            });
        });
        this.dialog.classList.add('is-editing');
        this.dialog.classList.remove('is-zoomed');
        this.dialog.querySelector('.proof-review-stage')!.after(this.editor.element);
        this.dialog.append(this.editor.controls);
        this.dialog.setAttribute('aria-label', 'Hide details');
        const close = this.dialog.querySelector<HTMLButtonElement>('.proof-review-close')!;
        close.textContent = 'Cancel';
        close.setAttribute('aria-label', 'Cancel hiding details');
        this.updateViewer();
        this.editor.element.focus({ preventScroll: true });
    }

    private endEdit(): void {
        this.editor?.dispose();
        this.editor = null;
        this.dialog?.classList.remove('is-editing');
        this.dialog?.setAttribute('aria-label', 'Review attached proofs');
        this.dialog?.querySelectorAll<HTMLButtonElement>('.proof-review-thumb').forEach((thumb, index) => {
            thumb.classList.remove('has-edits');
            thumb.setAttribute('aria-label', `Proof page ${index + 1}`);
        });
        const close = this.dialog?.querySelector<HTMLButtonElement>('.proof-review-close');
        if (close) {
            close.innerHTML = '<span class="proof-review-close-label">Close </span><span aria-hidden="true">×</span>';
            close.setAttribute('aria-label', 'Close proof review');
        }
    }

    private cancelEdit(): void {
        if (this.editor?.busy) return;
        this.endEdit();
        this.updateViewer();
        this.dialog?.querySelector<HTMLButtonElement>('.proof-review-hide')?.focus();
    }

    private async applyEdit(): Promise<void> {
        const editor = this.editor;
        if (!editor || editor.busy || !this.dialog) return;
        editor.busy = true;
        editor.render();
        const buttons = Array.from(this.dialog.querySelectorAll<HTMLButtonElement>('button'));
        const disabled = buttons.map(button => button.disabled);
        buttons.forEach(button => button.disabled = true);
        editor.element.inert = true;
        let failed = false;
        try {
            const replacements = await editor.flatten(this.images);
            await this.replaceSources(replacements);
            for (const [source, replacement] of replacements) {
                for (let i = 0; i < this.images.length; i++) if (this.images[i] === source) this.images[i] = replacement;
                for (const removed of this.removed) if (removed.image === source) removed.image = replacement;
                const label = this.labels.get(source);
                this.labels.delete(source);
                if (label) this.labels.set(replacement, label);
                this.shown.delete(source);
                this.shown.add(replacement);
                this.arrivals.delete(source);
            }
            this.endEdit();
            this.render();
            this.buildFilmstrip();
            this.zoomed = false;
            this.updateViewer();
            this.changed();
            this.dialog.querySelector<HTMLElement>('.proof-review-stage')?.focus();
        } catch (_) {
            failed = true;
            editor.busy = false;
            editor.element.inert = false;
        } finally {
            buttons.forEach((button, index) => button.disabled = disabled[index]!);
            if (this.editor) this.editor.render();
            if (failed) editor.error();
        }
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
    if (options?.isActive) proofStepIsActive.set(nextButton, options.isActive);
    if (proofStepIsActive.get(nextButton)?.() === false) return;
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
    const queuedProofSelections: { files: File[]; retryButton: HTMLButtonElement | null | undefined }[] = [];
    const proofSourceImages = new Map<string, string>();
    // Keep only a fingerprint of replaced bytes. Even a differently named input
    // that optimizes to the old image resolves to the latest flattened copy.
    const redactedImages = new Map<string, string>();
    const imageFingerprint = (source: string): Promise<string> => fingerprintProofSource(new TextEncoder().encode(source).buffer);
    const resolveProofImage = async (source: string): Promise<string> => redactedImages.get(await imageFingerprint(source)) || source;
    const review = new ProofReview(proofImageContainer, proofPics,
        () => checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer),
        options?.hasUnsubmittedProofs || (() => proofPics.length > 0),
        async replacements => {
            // Complete all fallible work before changing any page or cache.
            const fingerprints = await Promise.all([...replacements].map(async ([source, replacement]) => [await imageFingerprint(source), replacement] as const));
            for (const [key, source] of proofSourceImages) proofSourceImages.set(key, replacements.get(source) || source);
            for (const [key, source] of redactedImages) redactedImages.set(key, replacements.get(source) || source);
            for (const [key, replacement] of fingerprints) redactedImages.set(key, replacement);
            // The athlete should check evidence again after hiding any part of it.
            biomarkerChecklistContainer?.querySelectorAll<HTMLInputElement>('.biomarker-checkbox').forEach(box => box.checked = false);
            if (biomarkerChecklistContainer) updateProofTrackerProgress(biomarkerChecklistContainer);
        });
    proofReviews.set(proofImageContainer, review);

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
        const selectedFiles = Array.from(files || []);
        if (selectedFiles.length === 0) {
            if (input) input.value = "";
            return;
        }
        review.announce('');

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
        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
        review.setProgress({ label: 'Preparing proof files…', completed: 0, total: supportedFiles.length });
        try {
            const optimizeProofImageOrFallback: (raw: string) => Promise<string> = async raw => {
                let candidate = raw;
                try {
                    const { dataUrl } = await window.optimizeImageClient(raw, proofOptimizationOptions);
                    candidate = dataUrl || raw;
                } catch (_) { /* Keep a decodable original when optimization fails. */ }
                // Match the server's encoded-image limit. A corrupt image or an
                // oversized fallback must not become a broken, submittable page.
                if (candidate.length > 10 * 1024 * 1024) {
                    throw new Error('Proof image exceeds the encoded-image limit.');
                }
                const image = new Image();
                image.src = candidate;
                await image.decode();
                if (!image.naturalWidth || !image.naturalHeight) throw new Error('Proof image could not be decoded.');
                return candidate;
            };

            const useCachedProofSource = (sourceKey: string): boolean => {
                const existingProof = proofSourceImages.get(sourceKey);
                if (!existingProof) return false;
                if (proofPics.includes(existingProof)) duplicateProofs++;
                else if (proofPics.length >= maxProofImages) hitImageLimit = true;
                else addProofImage(existingProof);
                // Removal and re-upload must restore the edited copy, never
                // regenerate the unredacted page from the original file.
                return true;
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
                        try {
                            // render each page
                            for (let pageNum = 1; pageNum <= pdfDoc.numPages; pageNum++) {
                                review.setProgress({ label: `${file.name} · Page ${pageNum} of ${pdfDoc.numPages}`,
                                    completed: fileIndex + (pageNum - 1) / pdfDoc.numPages, total: supportedFiles.length });
                                const sourceKey = `${sourceFingerprint}:page:${pageNum}`;
                                if (useCachedProofSource(sourceKey)) continue;
                                if (proofPics.length >= maxProofImages) {
                                    hitImageLimit = true;
                                    break;
                                }
                                const page = await pdfDoc.getPage(pageNum);
                                let viewport = page.getViewport({ scale: 1.5 });
                                const longestSide = Math.max(viewport.width, viewport.height);
                                if (!Number.isFinite(longestSide) || Math.min(viewport.width, viewport.height) <= 0) {
                                    throw new Error('Proof page has invalid dimensions.');
                                }
                                if (longestSide > proofOptimizationOptions.maxSize) {
                                    viewport = page.getViewport({ scale: 1.5 * proofOptimizationOptions.maxSize / longestSide });
                                }
                                const canvas = document.createElement('canvas');
                                canvas.width = viewport.width;
                                canvas.height = viewport.height;
                                const context = canvas.getContext('2d');
                                if (!context) throw new Error('Canvas context unavailable.');
                                let optimizedPage: string;
                                try {
                                    await page.render({ canvasContext: context, viewport }).promise;
                                    optimizedPage = await resolveProofImage(await encodeProofCanvas(canvas));
                                } finally {
                                    canvas.width = canvas.height = 0;
                                }
                                if (optimizedPage) {
                                    if (!review.labels.has(optimizedPage)) review.labels.set(optimizedPage, `${file.name} · Page ${pageNum}`);
                                    addProofImage(optimizedPage);
                                    proofSourceImages.set(sourceKey, optimizedPage);
                                }
                            }
                        } finally {
                            await pdfDoc.destroy?.();
                        }
                        review.render();
                        checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
                        continue;
                    }

                    const sourceKey = `${sourceFingerprint}:image`;
                    if (useCachedProofSource(sourceKey)) continue;
                    if (proofPics.length >= maxProofImages) {
                        hitImageLimit = true;
                        break;
                    }
                    const raw = await readDataURL(file);
                    const dataUrl = await resolveProofImage(await optimizeProofImageOrFallback(raw));
                    if (dataUrl) {
                        if (!review.labels.has(dataUrl)) review.labels.set(dataUrl, file.name);
                        proofSourceImages.set(sourceKey, dataUrl);
                        if (addProofImage(dataUrl)) {
                            review.render();
                            checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
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
        }
    };

    const handleProofSelection = async (
        files: FileList | null,
        input: HTMLInputElement,
        retryButton: HTMLButtonElement | null | undefined
    ): Promise<void> => {
        // Snapshot each selection before another change can replace the FileList.
        const selected = Array.from(files || []);
        input.value = '';
        if (!selected.length) return;
        queuedProofSelections.push({ files: selected, retryButton });
        if (isProofUploadProcessing) return;
        isProofUploadProcessing = true;
        try {
            while (queuedProofSelections.length) {
                const selection = queuedProofSelections.shift()!;
                await handleProofFiles(selection.files, null, selection.retryButton);
            }
        } finally {
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
            await handleProofSelection(proofPicInput.files, proofPicInput, uploadProofButton);
        });
        proofPicInput.setAttribute('data-listener', 'true');
    }

    if (cameraInput && !cameraInput.hasAttribute('data-listener')) {
        cameraInput.addEventListener('change', async function () {
            await handleProofSelection(cameraInput.files, cameraInput, cameraButton || uploadProofButton);
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
    // Onboarding reuses Next across steps; background file work only owns it on the proof step.
    if (proofStepIsActive.get(nextButton)?.() === false) return;
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
    biomarkerChecklistContainer.classList.add('proof-tracker');

    // Header band with a wordless meter that fills as items are confirmed.
    const head = document.createElement('div');
    head.className = 'proof-tracker-head';
    const title = document.createElement('h4');
    title.textContent = 'Proof tracker';
    const meter = document.createElement('span');
    meter.className = 'proof-tracker-meter';
    meter.setAttribute('aria-hidden', 'true');
    meter.appendChild(document.createElement('span'));
    head.append(title, meter);
    biomarkerChecklistContainer.appendChild(head);

    const instructions = document.createElement('p');
    instructions.textContent = "Check each item only when an uploaded proof shows its marker name and submitted value:";
    instructions.classList.add('smaller-text');
    biomarkerChecklistContainer.appendChild(instructions);

    // The report's date and source come first, apart from the markers themselves.
    const groups = new Map<boolean, HTMLElement>();
    const groupFor = (isContext: boolean): HTMLElement => {
        let group = groups.get(isContext);
        if (!group) {
            group = document.createElement('div');
            group.className = isContext ? 'proof-tracker-group proof-tracker-group--context' : 'proof-tracker-group';
            groups.set(isContext, group);
            biomarkerChecklistContainer.appendChild(group);
        }
        return group;
    };

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
            updateProofTrackerProgress(biomarkerChecklistContainer);
            checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);
        });

        // span with the visible name
        const span = document.createElement('span');
        span.textContent = name;

        label.appendChild(input);
        label.appendChild(span);
        itemDiv.appendChild(label);
        groupFor(PROOF_CONTEXT_CHECKLIST_LABELS.includes(name)).appendChild(itemDiv);
    });
    updateProofTrackerProgress(biomarkerChecklistContainer);
};

function updateProofTrackerProgress(container: HTMLElement): void {
    const boxes = Array.from(container.querySelectorAll<HTMLInputElement>('.biomarker-checkbox'));
    const checked = boxes.filter(box => box.checked).length;
    const fill = container.querySelector<HTMLElement>('.proof-tracker-meter > span');
    if (fill) fill.style.width = boxes.length ? `${(checked / boxes.length) * 100}%` : '0%';
    container.classList.toggle('is-complete', boxes.length > 0 && checked === boxes.length);
}

export {};
