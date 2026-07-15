# LongevityWorldCup Design Decisions

This file records durable UI decisions for Longevity World Cup. Keep it short: add only reusable product-design guidance, not implementation history, workflow rules, or one-off polish notes.

## Principles

- Text should earn its place. Do not add explanatory UI copy, labels, hints, subtitles, or helper text unless the concept cannot be made clear through layout, affordance, grouping, placement, or a more self-evident visual cue. When a symbol, badge, color, or marker is misunderstood, first improve or replace the visual pattern itself; add words only after the non-text options fail.

## Decisions

- Direct-tap controls in mobile, toolbar, footer, and modal contexts should generally be easy to hit, with about a 44px target when space allows.

- Controls that belong to the same flow should share the same visual language: inherited font, consistent height, modest radius, light border, and a visible focus state.

- Helper, confirmation, validation, and empty-state copy should be grouped in compact light panels when grouping improves scanning or anchors the message to nearby controls.

- File, proof, and profile-image previews should stay inside bounded frames with `object-fit: contain` when unusual aspect ratios are likely.

- Autocomplete and suggestion menus should appear as padded floating panels with clear row hover/focus states; on constrained viewports, avoid covering the next likely action.

- Dense table and list hovers should not change font size or reflow neighboring content. Prefer color, underline, surface, edge, shadow, or slight lift for feedback.

- Empty states inside data tables should be compact and include an obvious recovery action whose shape matches nearby controls.

- Filter and segmented controls should make active, clearable, and unavailable states visually distinct without relying on a tiny badge alone.

- Mobile foreground surfaces such as drawers and full-screen viewers should have clear close targets and enough backdrop contrast that the active surface is unambiguous.

- Long user-generated labels, names, and button text should wrap without clipping; icons in action buttons should stay in fixed slots when labels wrap.

- Badges are compact visual tokens. Keep badge groups predictable and avoid hover behavior that turns them into unstable or overly tall content.

- Mobile modal content should prefer readable stacked sections over preserving desktop columns; avoid nested scrolling when stacked content can use the modal's main scroll.

- Long-running progress visuals should consume aggregate state and render a bounded number of elements. Do not create one image or DOM node per historical event when a challenge or activity can continue indefinitely.
