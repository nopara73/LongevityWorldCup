// The homepage's yearly newsletter sign-up as one posted letter.
//
// - A complete address readies the form: the paper plane tips up and the
//   field's edge lights, so the next step is obvious.
// - On success the address lifts off the field as a slip, slides into an
//   envelope whose flap closes, and the envelope flies in an arc up into the
//   "Yearly newsletter" heading, whose words flash the accent while an
//   accent line runs along the strip. Only then does the confirmation unfold,
//   postmarked.
// - A refused address is returned to sender: the field shakes and the reason
//   unfolds under it.
// - The panel grows and shrinks smoothly whenever the confirmation or the
//   reason comes or goes, so the footer below never jumps.
//
// newsletter-post.css owns the look. homepage.js keeps every behaviour
// (validation, the request, the status words); this classic script only
// watches its form and status line. Nothing here adds words. The status line
// stays in the accessibility tree while it waits for the envelope, so it is
// announced at once. Reduced motion keeps the readiness and the postmark and
// drops the flight, the shake and the resizing.

(() => {
    const section = document.getElementById("newsletter");
    const panel = section?.querySelector<HTMLElement>(".newsletter-panel") ?? null;
    const form = document.getElementById("newsletter-form") as HTMLFormElement | null;
    const input = document.getElementById("emailInput") as HTMLInputElement | null;
    const status = document.getElementById("newsletterStatus");
    const heading = document.getElementById("newsletter-title");
    if (!section || !panel || !form || !input || !status || !heading) return;

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    section.dataset.newsletterPost = "";

    // ------------------------------------------------ an addressed form is ready to send
    const syncReady = (): void => {
        form.classList.toggle("is-addressed", input.value.trim() !== "" && input.checkValidity());
    };
    input.addEventListener("input", syncReady);
    syncReady();

    // ------------------------------------------------ the panel resizes smoothly
    let lastHeight = panel.offsetHeight;
    let armedUntil = 0;
    let sizing: Animation | null = null;
    const arm = (): void => { armedUntil = performance.now() + 120; };

    function resize(from: number, to: number): void {
        sizing?.cancel();
        panel!.classList.add("is-sizing");
        sizing = panel!.animate(
            [{ height: `${from}px` }, { height: `${to}px` }],
            { duration: 380, easing: "cubic-bezier(0.22, 1, 0.36, 1)" });
        const done = (): void => {
            panel!.classList.remove("is-sizing");
            sizing = null;
            lastHeight = panel!.offsetHeight;
        };
        sizing.onfinish = done;
        sizing.oncancel = done;
    }

    if (typeof ResizeObserver === "function") {
        new ResizeObserver(() => {
            if (sizing) return;
            const height = panel!.offsetHeight;
            // Only status changes animate; width changes (rotation, resize) just follow.
            if (!reduced.matches && performance.now() < armedUntil && Math.abs(height - lastHeight) > 1) {
                resize(lastHeight, height);
                return;
            }
            lastHeight = height;
        }).observe(panel);
    }

    // ------------------------------------------------ the letter
    type Pending = { readonly address: string; readonly field: DOMRect; readonly font: string; readonly padStart: string };
    let pending: Pending | null = null;
    let holdTimer = 0;

    function el(tag: string, cls: string, parent: HTMLElement): HTMLElement {
        const node = document.createElement(tag);
        node.className = cls;
        node.setAttribute("aria-hidden", "true");
        parent.appendChild(node);
        return node;
    }

    function hold(): void {
        panel!.classList.add("is-holding");
        window.clearTimeout(holdTimer);
        // Never keep the confirmation back for long, whatever happens to the envelope.
        holdTimer = window.setTimeout(release, 1800);
    }

    function release(): void {
        window.clearTimeout(holdTimer);
        if (!panel!.classList.contains("is-holding")) return;
        arm();
        panel!.classList.remove("is-holding");
        stamp();
    }

    function stamp(): void {
        status!.classList.remove("is-postmarked");
        void status!.offsetWidth;
        status!.classList.add("is-postmarked");
    }

    function catchIt(): void {
        heading!.classList.remove("is-catching");
        void heading!.offsetWidth;
        heading!.classList.add("is-catching");
    }

    // The heading's words (its icon stays hidden by the site's design).
    function landing(): DOMRect {
        const words = Array.from(heading!.childNodes).find(node => node.nodeType === Node.TEXT_NODE && (node.textContent || "").trim());
        if (words) {
            const range = document.createRange();
            range.selectNodeContents(words);
            const rect = Array.from(range.getClientRects()).find(r => r.width > 0);
            if (rect) return rect;
        }
        return heading!.getBoundingClientRect();
    }

    function post(letter: Pending): void {
        const field = letter.field;
        const words = landing();
        // Aim at the start of the words, where a letter would be filed.
        const target = new DOMRect(words.left, words.top, Math.min(words.width, 28), words.height);
        const cx = field.left + field.width / 2;
        const cy = field.top + field.height / 2;
        const envW = 62;
        const envH = 40;

        // The slip is the field's own face, carrying the address.
        const slip = el("div", "newsletter-post-slip", document.body);
        slip.style.left = `${field.left}px`;
        slip.style.top = `${field.top}px`;
        slip.style.width = `${field.width}px`;
        slip.style.height = `${field.height}px`;
        slip.style.font = letter.font;
        slip.style.paddingInlineStart = letter.padStart;
        slip.textContent = letter.address;

        const envelope = el("div", "newsletter-post-envelope", document.body);
        envelope.style.left = `${cx - envW / 2}px`;
        envelope.style.top = `${cy - envH / 2}px`;
        envelope.style.width = `${envW}px`;
        envelope.style.height = `${envH}px`;
        const flap = el("span", "newsletter-post-flap", envelope);

        // 1. The slip lifts and folds down to letter size over the field's centre.
        const fold = Math.min(1, (envW - 8) / Math.max(1, field.width));
        const dx = cx - (field.left + field.width / 2);
        slip.animate([
            { transform: "translate(0, 0) scale(1)", opacity: 1, boxShadow: "0 0 0 rgba(0,0,0,0)" },
            { transform: `translate(${dx}px, -8px) scale(1.02)`, opacity: 1, boxShadow: "0 10px 22px rgba(0,0,0,.2)", offset: 0.3 },
            { transform: `translate(${dx}px, -4px) scale(${fold}, ${Math.max(fold, 0.55)})`, opacity: 1, offset: 0.72 },
            { transform: `translate(${dx}px, 10px) scale(${fold}, ${Math.max(fold, 0.55)})`, opacity: 0 }
        ], { duration: 460, easing: "cubic-bezier(0.4, 0, 0.2, 1)", fill: "forwards" });

        // 2. The envelope opens under it, takes the slip and closes its flap.
        envelope.animate([
            { opacity: 0, transform: "scale(0.86)" },
            { opacity: 1, transform: "scale(1)" }
        ], { duration: 200, delay: 220, easing: "cubic-bezier(0.22, 1, 0.36, 1)", fill: "backwards" });
        flap.animate([
            { transform: "rotateX(180deg)" },
            { transform: "rotateX(180deg)", offset: 0.55 },
            { transform: "rotateX(0deg)" }
        ], { duration: 560, delay: 0, easing: "cubic-bezier(0.3, 1.35, 0.45, 1)", fill: "both" });

        // 3. It flies in an arc up into the heading, which takes it.
        const tx = target.left + target.width / 2 - cx;
        const ty = target.top + target.height / 2 - cy;
        const lift = Math.min(-60, ty * 0.35 - 40);
        const flight = envelope.animate([
            { transform: "translate(0, 0) rotate(0deg) scale(1)", opacity: 1 },
            { transform: `translate(${tx * 0.45}px, ${ty * 0.45 + lift}px) rotate(-12deg) scale(0.8)`, opacity: 1, offset: 0.5 },
            { transform: `translate(${tx}px, ${ty}px) rotate(-4deg) scale(0.34)`, opacity: 0.9, offset: 0.92 },
            { transform: `translate(${tx}px, ${ty}px) rotate(-4deg) scale(0.2)`, opacity: 0 }
        ], { duration: 680, delay: 600, easing: "cubic-bezier(0.45, 0, 0.25, 1)", fill: "both" });

        const finish = (): void => {
            slip.remove();
            envelope.remove();
        };
        window.setTimeout(catchIt, 600 + 600);
        window.setTimeout(release, 600 + 680);
        flight.onfinish = finish;
        flight.oncancel = finish;
        // Scrolling away mid-flight still cleans up.
        window.setTimeout(finish, 2000);
    }

    function returnToSender(): void {
        if (reduced.matches) return;
        form!.classList.remove("is-returned");
        void form!.offsetWidth;
        form!.classList.add("is-returned");
    }

    // homepage.js trims and validates synchronously, then sends. Its listener runs
    // first, so by now the field holds exactly what was sent and is marked if invalid.
    form.addEventListener("submit", () => {
        const address = input.value.trim();
        if (!address || input.getAttribute("aria-invalid") === "true") {
            pending = null;
            return;
        }
        const computed = getComputedStyle(input);
        pending = {
            address,
            field: input.getBoundingClientRect(),
            font: computed.font,
            padStart: computed.paddingInlineStart
        };
    });

    // React to what the status says, not to this script's own postmark class.
    let said = "";
    new MutationObserver(() => {
        const text = (status.textContent || "").trim();
        const now = `${status.classList.contains("is-error") ? "!" : ""}${text}`;
        if (now === said) return;
        said = now;
        arm();
        syncReady();
        if (!text) {
            status.classList.remove("is-postmarked");
            return;
        }
        if (status.classList.contains("is-error")) {
            pending = null;
            returnToSender();
            return;
        }
        // A confirmation for something just sent: the letter goes first.
        const letter = pending;
        pending = null;
        if (!letter || reduced.matches) {
            stamp();
            return;
        }
        hold();
        post(letter);
    }).observe(status, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ["class"] });

    form.addEventListener("animationend", event => {
        if (event.animationName === "newsletter-post-return") form.classList.remove("is-returned");
    });
    heading.addEventListener("animationend", event => {
        if (event.animationName === "newsletter-post-take") heading.classList.remove("is-catching");
    });
})();
