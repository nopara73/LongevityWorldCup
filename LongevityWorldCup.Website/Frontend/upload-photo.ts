// Shared by the four upload screens. The decoder URL is versioned by the page renderer.
const LwcUploadPhoto = (() => {
    const decoderUrl = (document.currentScript as HTMLScriptElement).dataset.decoder!;
    const recovery = 'This photo could not be opened. Export it as a JPEG or PNG and choose it again.';
    function isHeic(file: File): boolean {
        return /\.(heic|heif)$/i.test(file.name) || /^image\/hei[cf](-sequence)?$/i.test(file.type);
    }

    async function decode(file: File): Promise<HTMLImageElement | ImageBitmap> {
        // Even the HEIF file type box alone needs 16 bytes.
        if (file.size < 16) throw new Error(recovery);
        const url = URL.createObjectURL(file);
        try {
            const image = new Image();
            image.src = url;
            await image.decode();
            return image;
        } catch {
            // Native HEIF support varies by browser and image codec.
        } finally {
            URL.revokeObjectURL(url);
        }

        const { heicTo } = await import(decoderUrl) as {
            heicTo: (options: { blob: Blob; type: 'bitmap' }) => Promise<ImageBitmap>;
        };
        return heicTo({ blob: file, type: 'bitmap' });
    }

    // A phone photo can contain tens of millions of pixels. Release its decoded
    // image and the caller's canvas before starting the next HEIC conversion.
    let pendingHeic = Promise.resolve();
    function withHeicImage<T>(file: File, consume: (image: HTMLImageElement | ImageBitmap) => Promise<T>): Promise<T> {
        const next = pendingHeic.then(async () => {
            const image = await decode(file);
            try {
                return await consume(image);
            } finally {
                if ('close' in image) image.close();
                else image.removeAttribute('src');
            }
        });
        pendingHeic = next.then(() => {}, () => {});
        return next;
    }

    function prepareBlob(file: File, maxDimension: number): Promise<Blob> {
        if (!isHeic(file)) return Promise.resolve(file);
        return withHeicImage(file, async image => {
            const canvas = document.createElement('canvas');
            try {
                const scale = Math.min(1, maxDimension / Math.max(image.width, image.height));
                canvas.width = Math.max(1, Math.round(image.width * scale));
                canvas.height = Math.max(1, Math.round(image.height * scale));
                canvas.getContext('2d')!.drawImage(image, 0, 0, canvas.width, canvas.height);
                return await new Promise<Blob>((resolve, reject) => canvas.toBlob(
                    result => result ? resolve(result) : reject(new Error(recovery)), 'image/png'));
            } finally {
                canvas.width = canvas.height = 0;
            }
        });
    }

    // Both portrait croppers export at most 1024px. Keep room for a 4x crop.
    function prepareCrop(file: File): Promise<Blob> { return prepareBlob(file, 4096); }

    function setCropImage(image: HTMLImageElement, blob: Blob | null): void {
        const previous = image.getAttribute('src');
        if (blob) image.src = URL.createObjectURL(blob);
        else image.removeAttribute('src');
        if (previous?.startsWith('blob:')) URL.revokeObjectURL(previous);
    }

    async function read(file: File, maxDimension: number): Promise<string> {
        const blob = await prepareBlob(file, maxDimension);
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = event => {
                const result = event.target?.result;
                if (typeof result === 'string') resolve(result);
                else reject(new Error(recovery));
            };
            reader.onerror = reader.onabort = () => reject(reader.error);
            reader.readAsDataURL(blob);
        });
    }

    return { isHeic, withHeicImage, prepareCrop, setCropImage, read, recovery };
})();
