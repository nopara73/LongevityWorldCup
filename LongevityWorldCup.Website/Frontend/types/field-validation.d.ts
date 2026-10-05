interface LwcFieldValidationOptions {
    validate: () => boolean;
    clear: () => void;
    isRestoring?: () => boolean;
    onFeedback?: () => void;
}

interface LwcFieldValidationController {
    validate: () => boolean;
    reset: () => void;
    refresh: () => void;
    flush: () => void;
}

interface Window {
    LwcFieldValidation: {
        bind: (field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement,
            options: LwcFieldValidationOptions) => LwcFieldValidationController;
    };
}
