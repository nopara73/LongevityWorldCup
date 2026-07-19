# LongevityWorldCup Design Decisions

This file records durable UI decisions for Longevity World Cup. Keep it short: add only reusable product-design guidance, not implementation history, workflow rules, or one-off polish notes.

## Principles

- Text should earn its place. Do not add explanatory UI copy, labels, hints, subtitles, or helper text unless the concept cannot be made clear through layout, affordance, grouping, placement, or a more self-evident visual cue. When a symbol, badge, color, or marker is misunderstood, first improve or replace the visual pattern itself; add words only after the non-text options fail.

- The shared visual direction is precision sport: graphite chrome, cool neutral canvases, white task surfaces, and one teal action/data accent. Feature artwork may make the Play entry, challenge hero, and athlete photography more expressive, but their controls, type hierarchy, states, and geometry still use the shared system.

## Decisions

- Shared UI uses the `--space-*`, `--type-*`, `--radius-*`, `--shadow-*`, and `--duration-*` scales. New one-off values need a content or platform constraint that the existing scale cannot express.

- Roboto is the functional typeface with regular and bold as the normal weights. Orbitron is reserved for short decorative competition marks, never form labels, filters, metrics, or body copy.

- Use 4px, 8px, and 12px corner radii for small, standard, and large components. Reserve circles for circular icons or portraits and pills for compact badges or chips, not full-width actions.

- Prefer whitespace and a neutral surface change for grouping. Use the small shadow for quiet raised surfaces and the medium shadow only for the active overlay; do not combine a tinted fill, border, and shadow unless each communicates a distinct state.

- The default transition is 140ms and the longer state transition is 220ms, using the shared standard easing. Motion should explain continuity or state change; focused tasks do not use scroll entrances, looping decoration, delayed typewriter reveals, or unbounded celebration particles.

- Strong color communicates an action, selection, or named status. Structural borders stay neutral, and state meaning must also be available through text, iconography, or shape. Light and dark palettes are designed independently rather than produced with filters.

- Inline informational, success, warning, and error feedback uses the same neutral message surface, semantic leading edge, spacing, and recovery-action geometry. A blocking alert keeps the dialog shell because it is a focused interruption, but reuses the same palette, type, radius, and action hierarchy.

- Direct-tap controls in mobile, toolbar, footer, and modal contexts should generally be easy to hit, with about a 44px target when space allows.

- Controls that belong to the same flow should share the same visual language: inherited font, consistent height, modest radius, light border, and a visible focus state.

- Form states must remain distinguishable without placeholder text: filled fields use the teal boundary, read-only fields use a neutral muted surface, invalid fields use a danger boundary plus nearby language, and disabled fields retain readable text with a non-interactive cursor.

- Helper, confirmation, validation, and empty-state copy should be grouped in compact light panels when grouping improves scanning or anchors the message to nearby controls.

- File, proof, and profile-image previews should stay inside bounded frames with `object-fit: contain` when unusual aspect ratios are likely.

- Autocomplete and suggestion menus should appear as padded floating panels with clear row hover/focus states; on constrained viewports, avoid covering the next likely action.

- Dense table and list hovers should not change font size or reflow neighboring content. Prefer color, underline, surface, edge, shadow, or slight lift for feedback.

- Empty states inside data tables should be compact and include an obvious recovery action whose shape matches nearby controls.

- Filter and segmented controls should make active, clearable, and unavailable states visually distinct without relying on a tiny badge alone.

- Mobile foreground surfaces such as drawers and full-screen viewers should have clear close targets and enough backdrop contrast that the active surface is unambiguous.

- Long user-generated labels, names, and button text should wrap without clipping; icons in action buttons should stay in fixed slots when labels wrap.

- Badges are compact visual tokens. Keep badge groups predictable and avoid hover behavior that turns them into unstable or overly tall content.

- Badge detail that appears on pointer hover must also appear on keyboard focus. Keep at most three badges plus a bounded overflow count in dense leaderboard rows; the athlete detail view may show the complete set.

- Mobile modal content should prefer readable stacked sections over preserving desktop columns; avoid nested scrolling when stacked content can use the modal's main scroll.

- Long-running progress visuals should consume aggregate state and render a bounded number of elements. Do not create one image or DOM node per historical event when a challenge or activity can continue indefinitely.
