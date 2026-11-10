// Returning-athlete proof desk. Files still use the shared validation,
// conversion, duplicate detection and redaction-aware upload path.
(function () {
    const body = document.body;
    const input = document.getElementById('proofPicInput');
    const uploadButton = document.getElementById('uploadProofButton');
    const sheet = document.querySelector<HTMLElement>('.proof-upload-start');
    if (!(input instanceof HTMLInputElement) || !(uploadButton instanceof HTMLButtonElement) || !sheet) return;

    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const hasFiles = (event: DragEvent): boolean => Array.from(event.dataTransfer?.types || []).includes('Files');
    const canAcceptFiles = (): boolean => input.dataset.listener === 'true'
        && !input.disabled && !uploadButton.disabled
        && !document.documentElement.hasAttribute('data-initial-view')
        && !document.querySelector(':modal');
    let dragDepth = 0;

    function stopDragging(): void {
        dragDepth = 0;
        body.classList.remove('proof-desk-dragging');
    }

    window.addEventListener('dragenter', event => {
        if (!hasFiles(event)) return;
        event.preventDefault();
        if (!canAcceptFiles()) { stopDragging(); return; }
        dragDepth += 1;
        body.classList.add('proof-desk-dragging');
    });
    window.addEventListener('dragover', event => {
        if (!hasFiles(event)) return;
        // Always prevent file navigation, including during preparation/submission.
        event.preventDefault();
        const accepted = canAcceptFiles();
        if (event.dataTransfer) event.dataTransfer.dropEffect = accepted ? 'copy' : 'none';
        if (!accepted) stopDragging();
    });
    window.addEventListener('dragleave', event => {
        if (!hasFiles(event)) return;
        dragDepth = Math.max(0, dragDepth - 1);
        if (!dragDepth) stopDragging();
    });
    window.addEventListener('drop', event => {
        if (!hasFiles(event)) return;
        event.preventDefault();
        stopDragging();
        if (!canAcceptFiles() || !event.dataTransfer?.files.length) return;
        input.files = event.dataTransfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
    window.addEventListener('blur', stopDragging);
    window.addEventListener('pagehide', stopDragging);

    // Commit the final layout immediately. Animate only the decorative symbol:
    // moving the sheet's height or live controls races the shared action dock
    // and can move a button after the athlete starts interacting with it.
    let docked = body.classList.contains('proof-upload-has-proofs');
    let animation: Animation | undefined;
    new MutationObserver(() => {
        const nowDocked = body.classList.contains('proof-upload-has-proofs');
        if (nowDocked === docked) return;
        docked = nowDocked;
        animation?.cancel();
        if (!nowDocked || motion.matches) return;
        animation = sheet.querySelector('.proof-upload-symbol')?.animate([
            { transform: 'translateY(-4px) scale(1.08)' },
            { transform: 'none' }
        ], { duration: 380, easing: 'cubic-bezier(0.2, 0, 0, 1)' });
    }).observe(body, { attributes: true, attributeFilter: ['class'] });
    motion.addEventListener('change', () => { if (motion.matches) animation?.cancel(); });
})();
