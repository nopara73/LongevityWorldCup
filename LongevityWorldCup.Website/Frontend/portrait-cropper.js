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
                        <cropper-grid role="presentation" bordered covered></cropper-grid>
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
                options.ready?.();
            }).catch(() => { if (!this.destroyed) options.error?.(); });
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
            const side = Math.min(this.naturalWidth, this.naturalHeight);
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

        disable() { this.disabled = true; this.canvas.disabled = true; }
        enable() { this.disabled = false; this.canvas.disabled = false; }
        toCanvas(options) { return this.selection.$toCanvas(options); }
        destroy() {
            this.destroyed = true;
            this.ready = false;
            this.cropper.destroy();
            if (this.element.cropper === this) delete this.element.cropper;
        }
    };
})();
