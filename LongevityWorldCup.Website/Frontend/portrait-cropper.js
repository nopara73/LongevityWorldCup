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

        // An ambient copy of the photo and the adjusting state for thirds guides.
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

            let adjustingTimer = 0;
            const setAdjusting = on => stage.toggleAttribute('data-adjusting', on);
            const brieflyAdjust = () => {
                setAdjusting(true);
                clearTimeout(adjustingTimer);
                adjustingTimer = setTimeout(() => setAdjusting(false), 420);
            };
            this.canvas.tabIndex = 0;
            this.canvas.setAttribute('role', 'group');
            this.canvas.setAttribute('aria-label', this.element.alt);
            this.canvas.setAttribute('aria-description', 'Use arrow keys to move the crop, Shift for larger steps, and plus or minus to zoom.');
            this.canvas.addEventListener('keydown', event => {
                if (event.target !== this.canvas || !this.ready || this.disabled
                    || event.altKey || event.ctrlKey || event.metaKey) return;
                const step = event.shiftKey ? 10 : 1;
                switch (event.key) {
                    case 'ArrowLeft': this.selection.$move(-step, 0); break;
                    case 'ArrowRight': this.selection.$move(step, 0); break;
                    case 'ArrowUp': this.selection.$move(0, -step); break;
                    case 'ArrowDown': this.selection.$move(0, step); break;
                    case '+': case '=': this.image.$zoom(0.1); break;
                    case '-': this.image.$zoom(-0.1); break;
                    default: return;
                }
                event.preventDefault();
                brieflyAdjust();
            });
            this.canvas.addEventListener('actionstart', () => { if (!this.disabled) setAdjusting(true); });
            this.canvas.addEventListener('actionend', () => setAdjusting(false));
            this.canvas.addEventListener('wheel', () => {
                if (this.disabled) return;
                brieflyAdjust();
            }, { passive: true });
            this.clearAdjusting = () => { clearTimeout(adjustingTimer); setAdjusting(false); };
        }

        develop() {
            requestAnimationFrame(() => requestAnimationFrame(() => {
                if (this.destroyed) return;
                if (this.stage) this.stage.dataset.state = 'ready';
            }));
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
            const preferredHeight = this.options.maxHeight
                ? Math.min(this.options.maxHeight, width * this.naturalHeight / this.naturalWidth)
                : stage.clientHeight;
            const cssMaxHeight = parseFloat(getComputedStyle(stage).maxHeight);
            const height = Math.min(preferredHeight, Number.isFinite(cssMaxHeight) ? cssMaxHeight : Infinity);
            if (!width || !height || !this.naturalWidth) return;
            if (this.ready && width === this.width && height === this.height) return;
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
            this.clearAdjusting?.();
            this.ambient?.remove();
            if (this.stage) {
                this.stage.removeAttribute('data-lwc-crop-stage');
                delete this.stage.dataset.state;
            }
            this.cropper.destroy();
            if (this.element.cropper === this) delete this.element.cropper;
        }
    };

    // Flies the cropped circle from the crop window into the page portrait,
    // then settles the portrait. Reduced motion or an off-screen target just
    // updates the portrait in place.
    const landings = new WeakMap();
    window.LwcPortraitCropper.land ??= (from, src, target) => {
        if (!target) return;
        landings.get(target)?.(false);
        if (!src) return;
        const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
        const settle = () => {
            target.classList.remove('lwc-crop-landed');
            void target.offsetWidth;
            target.classList.add('lwc-crop-landed');
            target.addEventListener('animationend', () => target.classList.remove('lwc-crop-landed'), { once: true });
        };
        const to = target.getBoundingClientRect();
        const visible = to.width > 0 && to.height > 0 && to.bottom > 0 && to.top < window.innerHeight
            && to.right > 0 && to.left < window.innerWidth;
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
        let finished = false;
        let flight;
        const finish = (landed = false) => {
            if (finished) return;
            finished = true;
            observer.disconnect();
            flight?.cancel();
            ghost.remove();
            target.style.opacity = previous;
            landings.delete(target);
            if (landed && target.isConnected) settle();
        };
        const observer = new MutationObserver(() => finish(false));
        observer.observe(target, { attributes: true, attributeFilter: ['src', 'srcset'] });
        landings.set(target, finish);
        try {
            flight = ghost.animate([
                { transform: `translate(${dx}px, ${dy}px) scale(${scale})` },
                { transform: `translate(${dx * 0.42}px, ${dy * 0.42 - lift}px) scale(${1 + (scale - 1) * 0.35})`, offset: 0.55 },
                { transform: 'none' }
            ], { duration: 620, easing: 'cubic-bezier(0.45, 0, 0.15, 1)' });
            flight.finished.then(() => finish(true), () => finish(false));
        } catch (_) {
            // Optional motion must never turn a saved portrait into an error.
            finish(false);
        }
    };
})();
