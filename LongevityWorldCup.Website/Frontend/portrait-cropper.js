(() => {
    // Portrait-only geometry shared by application and profile editing. Cropper 2
    // owns pointer input and rendering; source-image coordinates survive reflow.
    window.LwcPortraitCropper ??= class {
        constructor(element, options = {}) {
            this.element = element;
            this.options = options;
            this.ready = false;
            this.disabled = false;
            this.destroyed = false;
            this.adjusting = false;
            this.width = this.height = 0;
            const handles = ['n', 'e', 's', 'w', 'ne', 'nw', 'se', 'sw']
                .map(side => `<cropper-handle action="${side}-resize"></cropper-handle>`).join('');
            this.cropper = new window.Cropper.Cropper(element, {
                template: `<cropper-canvas class="cropper-container" background data-lwc-no-gesture>
                    <cropper-image scalable translatable></cropper-image>
                    <cropper-shade></cropper-shade>
                    <cropper-handle action="move" plain></cropper-handle>
                    <cropper-selection class="cropper-crop-box" aspect-ratio="1" movable resizable precise>
                        <div class="lwc-crop-ring" aria-hidden="true"></div>
                        <cropper-grid role="presentation" covered></cropper-grid>
                        <cropper-crosshair centered></cropper-crosshair>
                        <cropper-handle class="cropper-face" action="move" theme-color="rgba(255,255,255,.1)"></cropper-handle>
                        ${handles}
                    </cropper-selection>
                </cropper-canvas>`
            });
            this.canvas = this.cropper.getCropperCanvas();
            // Cropper 2 defaults to a 200x100 minimum. The surrounding portrait
            // stage also supports narrower layouts and enlarged browser zoom.
            this.canvas.style.minWidth = '0';
            this.canvas.style.minHeight = '0';
            this.image = this.cropper.getCropperImage();
            this.image.alt = element.alt;
            this.selection = this.cropper.getCropperSelection();
            element.cropper = this;
            this.stage = element.parentElement;
            this.stageSetup();
            this.image.addEventListener('transform', event => {
                if (!this.ready || this.adjusting) return;
                const bounds = this.imageBounds(event.detail.matrix);
                const crop = this.getCropBoxData();
                if (bounds.left > crop.left + .01 || bounds.top > crop.top + .01
                    || bounds.left + bounds.width < crop.left + crop.width - .01
                    || bounds.top + bounds.height < crop.top + crop.height - .01) event.preventDefault();
            });
            this.selection.addEventListener('change', event => {
                if (!this.ready || this.adjusting) return;
                // Keep bounds in source/layout coordinates. Cropper's min-inset
                // compares viewport rectangles with layout sizes; fractional
                // browser zoom can reject even the initial full-image selection.
                const bounds = this.imageBounds();
                const { x, y, width, height } = event.detail;
                const minWidth = Math.min(options.minCropBoxWidth || 0, bounds.width, bounds.height);
                const minHeight = Math.min(options.minCropBoxHeight || 0, bounds.width, bounds.height);
                if (x < bounds.left - .01 || y < bounds.top - .01
                    || x + width > bounds.left + bounds.width + .01
                    || y + height > bounds.top + bounds.height + .01
                    || width < minWidth || height < minHeight) event.preventDefault();
            });
            this.image.$ready().then(image => {
                if (this.destroyed) return;
                this.naturalWidth = image.naturalWidth;
                this.naturalHeight = image.naturalHeight;
                this.resize();
                this.ready = true;
                this.develop();
                options.ready?.();
            }).catch(() => { if (!this.destroyed) options.error?.(); });
        }

        // Stage dressing: an ambient copy of the photo, live previews and the
        // adjusting state that shows the thirds guides.
        stageSetup() {
            const stage = this.stage;
            if (!stage) return;
            stage.setAttribute('data-lwc-crop-stage', '');
            stage.dataset.state = 'developing';
            stage.querySelector(':scope > .lwc-crop-ambient')?.remove();
            const ambient = document.createElement('img');
            ambient.className = 'lwc-crop-ambient';
            ambient.alt = '';
            ambient.setAttribute('aria-hidden', 'true');
            ambient.decoding = 'async';
            ambient.src = this.element.getAttribute('src') || '';
            stage.prepend(ambient);
            this.ambient = ambient;

            this.previews = this.options.previews || null;
            if (this.previews) {
                this.previews.dataset.state = 'developing';
                this.previews.setAttribute('aria-hidden', 'true');
                if (!this.previews.querySelector('canvas')) {
                    const sizes = (this.previews.dataset.sizes || '88 52 30').split(/\s+/).map(Number);
                    for (const size of sizes) {
                        const canvas = document.createElement('canvas');
                        canvas.style.width = canvas.style.height = `${size}px`;
                        canvas.dataset.size = String(size);
                        this.previews.append(canvas);
                    }
                }
            }

            const queue = () => this.queuePreview();
            this.image.addEventListener('transform', queue);
            this.selection.addEventListener('change', queue);
            let wheelTimer = 0;
            const setAdjusting = on => {
                for (const el of [stage, this.previews]) {
                    if (!el) continue;
                    if (on) el.setAttribute('data-adjusting', '');
                    else el.removeAttribute('data-adjusting');
                }
            };
            this.canvas.addEventListener('actionstart', () => { if (!this.disabled) setAdjusting(true); });
            this.canvas.addEventListener('actionend', () => setAdjusting(false));
            this.canvas.addEventListener('wheel', () => {
                if (this.disabled) return;
                setAdjusting(true);
                clearTimeout(wheelTimer);
                wheelTimer = setTimeout(() => setAdjusting(false), 420);
            }, { passive: true });
            this.clearAdjusting = () => { clearTimeout(wheelTimer); setAdjusting(false); };
        }

        develop() {
            this.drawPreviews();
            requestAnimationFrame(() => requestAnimationFrame(() => {
                if (this.destroyed) return;
                if (this.stage) this.stage.dataset.state = 'ready';
                if (this.previews) this.previews.dataset.state = 'ready';
            }));
        }

        queuePreview() {
            if (this.previewFrame || !this.ready) return;
            this.previewFrame = requestAnimationFrame(() => {
                this.previewFrame = 0;
                this.drawPreviews();
            });
        }

        drawPreviews() {
            if (!this.previews || this.destroyed || !this.naturalWidth) return;
            const source = this.image.$image || this.element;
            const { x, y, width, height } = this.getData();
            if (!(width > 0 && height > 0)) return;
            const ratio = Math.min(window.devicePixelRatio || 1, 3);
            for (const canvas of this.previews.querySelectorAll('canvas')) {
                const size = Math.round((Number(canvas.dataset.size) || 48) * ratio);
                if (canvas.width !== size) canvas.width = canvas.height = size;
                const context = canvas.getContext('2d');
                if (!context) continue;
                context.clearRect(0, 0, size, size);
                context.imageSmoothingQuality = 'high';
                try { context.drawImage(source, x, y, width, height, 0, 0, size, size); } catch (_) { }
            }
        }

        // Screen rectangle of the round window, for the landing flight.
        windowRect() {
            const rect = this.selection.getBoundingClientRect();
            return rect.width > 0 ? rect : null;
        }

        imageBounds(matrix = this.image.$getTransform()) {
            const [scale, , , , x, y] = matrix;
            return {
                left: (this.naturalWidth - this.naturalWidth * scale) / 2 + x,
                top: (this.naturalHeight - this.naturalHeight * scale) / 2 + y,
                width: this.naturalWidth * scale,
                height: this.naturalHeight * scale,
                scale
            };
        }

        getData() {
            const bounds = this.imageBounds();
            return {
                x: (this.selection.x - bounds.left) / bounds.scale,
                y: (this.selection.y - bounds.top) / bounds.scale,
                width: this.selection.width / bounds.scale,
                height: this.selection.height / bounds.scale
            };
        }

        setData(data) {
            const bounds = this.imageBounds();
            this.selection.$change(bounds.left + data.x * bounds.scale,
                bounds.top + data.y * bounds.scale, data.width * bounds.scale, data.height * bounds.scale);
        }

        getImageData() { return { naturalWidth: this.naturalWidth, naturalHeight: this.naturalHeight }; }
        getContainerData() { return { width: this.width, height: this.height }; }
        getCropBoxData() {
            return { left: this.selection.x, top: this.selection.y, width: this.selection.width, height: this.selection.height };
        }
        setCropBoxData({ left, top, width, height }) { this.selection.$change(left, top, width, height); }

        resize() {
            const stage = this.element.parentElement;
            const width = stage.clientWidth;
            const height = this.options.maxHeight
                ? Math.min(this.options.maxHeight, width * this.naturalHeight / this.naturalWidth)
                : stage.clientHeight;
            if (!width || !height || !this.naturalWidth) return;
            // The first window leaves a margin so its brackets sit inside the photo.
            const side = Math.min(this.naturalWidth, this.naturalHeight) * 0.9;
            // Image readiness can arrive while Back has hidden the stage. Until
            // a first layout exists there are no image coordinates to preserve.
            const hasSelection = this.ready && this.width > 0 && this.height > 0
                && this.selection.width > 0 && this.selection.height > 0;
            const selected = hasSelection ? this.getData() : {
                x: (this.naturalWidth - side) / 2, y: (this.naturalHeight - side) / 2, width: side, height: side
            };
            this.adjusting = true;
            try {
                this.width = width;
                this.height = height;
                this.canvas.style.width = '100%';
                this.canvas.style.height = `${height}px`;
                const scale = Math.min(width / this.naturalWidth, height / this.naturalHeight);
                this.image.$setTransform(scale, 0, 0, scale,
                    (width - this.naturalWidth) / 2, (height - this.naturalHeight) / 2);
                this.setData(selected);
            } finally { this.adjusting = false; }
        }

        disable() { this.disabled = true; this.canvas.disabled = true; this.clearAdjusting?.(); }
        enable() { this.disabled = false; this.canvas.disabled = false; }
        toCanvas(options) { return this.selection.$toCanvas(options); }
        destroy() {
            this.destroyed = true;
            this.ready = false;
            cancelAnimationFrame(this.previewFrame || 0);
            this.clearAdjusting?.();
            this.ambient?.remove();
            if (this.stage) {
                this.stage.removeAttribute('data-lwc-crop-stage');
                delete this.stage.dataset.state;
            }
            if (this.previews) {
                this.previews.replaceChildren();
                delete this.previews.dataset.state;
            }
            this.cropper.destroy();
            if (this.element.cropper === this) delete this.element.cropper;
        }
    };

    // Flies the cropped circle from the crop window into the page portrait,
    // then settles the portrait. Reduced motion or an off-screen target just
    // updates the portrait in place.
    window.LwcPortraitCropper.land ??= (from, src, target) => {
        if (!target || !src) return;
        const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
        const settle = () => {
            target.classList.remove('lwc-crop-landed');
            void target.offsetWidth;
            target.classList.add('lwc-crop-landed');
            target.addEventListener('animationend', () => target.classList.remove('lwc-crop-landed'), { once: true });
        };
        const to = target.getBoundingClientRect();
        const visible = to.width > 0 && to.bottom > 0 && to.top < window.innerHeight;
        if (reduce) return;
        if (!from || !visible || !document.body.animate) { settle(); return; }
        const ghost = document.createElement('div');
        ghost.className = 'lwc-crop-flight';
        ghost.setAttribute('aria-hidden', 'true');
        ghost.style.backgroundImage = `url("${src}")`;
        Object.assign(ghost.style, {
            left: `${to.left}px`, top: `${to.top}px`, width: `${to.width}px`, height: `${to.height}px`
        });
        document.body.append(ghost);
        const scale = from.width / to.width;
        const dx = from.left + from.width / 2 - (to.left + to.width / 2);
        const dy = from.top + from.height / 2 - (to.top + to.height / 2);
        // A slight arc: the midpoint lifts above the straight path.
        const lift = Math.min(80, Math.hypot(dx, dy) * 0.18);
        const previous = target.style.opacity;
        target.style.opacity = '0';
        const flight = ghost.animate([
            { transform: `translate(${dx}px, ${dy}px) scale(${scale})` },
            { transform: `translate(${dx * 0.42}px, ${dy * 0.42 - lift}px) scale(${1 + (scale - 1) * 0.35})`, offset: 0.55 },
            { transform: 'none' }
        ], { duration: 620, easing: 'cubic-bezier(0.45, 0, 0.15, 1)' });
        const finish = () => {
            ghost.remove();
            target.style.opacity = previous;
            settle();
        };
        flight.finished.then(finish, finish);
    };
})();
