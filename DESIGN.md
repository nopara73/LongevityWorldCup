# LongevityWorldCup Design Standards

This is a living record of durable UI decisions for Longevity World Cup. Update it when a UI polish pass produces a broadly useful rule, especially when the rule prevents one-off fixes from making the site less consistent.

## Design Principles

- Prefer sitewide consistency over isolated local polish. If a component pattern exists in more than one place, improve the shared pattern or verify the local exception is intentional.
- Keep visual changes separate from business logic, ranking logic, data flow, APIs, routes, authentication, validation, and backend code.
- Make the first screen feel like the actual product, not a landing page detour. Core game, leaderboard, event, merch, newsletter, and athlete surfaces should stay immediately usable.
- Preserve the playful longevity-sport tone while keeping operational UI quiet, scannable, and fast to understand.

## Layout

- Use existing content widths before inventing new ones: homepage sections generally align to the leaderboard/card rhythm, with generous but not ornamental spacing.
- Cards should feel related across the site: light surfaces, modest shadow, rounded corners, and compact internal spacing. Avoid nesting cards inside other cards.
- On mobile, controls and key content should stack predictably without horizontal overflow. Always verify at a narrow viewport after spacing changes.
- Sticky elements must not cover anchor targets or primary actions. Preserve scroll padding and reserved space around sticky headers.

## Typography

- Keep heading sizes proportional to their context. Use hero-scale type only for true hero/header moments; use tighter type inside cards, panels, modals, and tables.
- Body copy should remain readable and calm: moderate line height, no negative letter spacing, and enough width limits to avoid long text lines.
- Avoid viewport-width-only font scaling for important UI labels. Prefer bounded `clamp(...)` values or fixed sizes that remain legible.

## Interaction

- Interactive controls should generally meet a 44px minimum touch target on mobile and in dense modal/footer contexts.
- Hover and focus states should be visually related. A focus state must be visible without relying only on color.
- Do not add animated scale or shadow effects that cause layout shift or make compact controls feel jumpy.
- Disabled controls should look intentionally inactive across the site, not like a one-off local override.

## Visual Language

- Use the existing cyan/pink/green accents deliberately: cyan for links and secondary UI, pink for athlete/media accent moments, green for primary play/success actions.
- Avoid turning a whole section into a single-hue theme. Pair accent colors with neutral surfaces and readable dark text.
- Gradient buttons are already part of the site language, but they should have stable sizing, readable labels, and consistent hover/focus treatment.
- Product and athlete images should stay inspectable: avoid dark, blurred, overly cropped, or purely atmospheric presentation when the image communicates the object/person.

## Verification

- For UI polish PR updates, capture before-and-after screenshots at the viewport where the issue is visible.
- Include a quick reviewer note that says what changed, what did not change, and which visual metrics were checked.
- For responsive fixes, verify horizontal overflow is `0` at the target mobile width.
- Run the smallest relevant build/check that confirms the changed files still integrate.
