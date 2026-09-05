# LongevityWorldCup Design Decisions

Keep reusable product decisions here; omit implementation history and one-off polish notes.

## Visual System

- Make meaning clear through layout, grouping, affordances, and visual cues before adding labels or helper copy. Fix misunderstood visual patterns before explaining them with words.
- Use graphite chrome, cool neutral canvases, white task surfaces, and one teal action/data accent. Play, challenge, and athlete artwork may be expressive; controls and typography follow the shared system.
- Use shared `--space-*`, `--type-*`, `--radius-*`, `--shadow-*`, and `--duration-*` scales. Exceptions need a content or platform constraint.
- Roboto regular/bold is functional; Orbitron is only for short decorative competition marks.
- Radii are 4px, 8px, and 12px for small, standard, and large components. Circles suit icons/portraits; pills suit compact badges/chips, not full-width actions.
- Group with whitespace and neutral surface changes. Use small shadows for raised surfaces and medium shadows for active overlays. Combine tint, border, and shadow only when each conveys a distinct state.
- Strong color marks action, selection, or named status; borders stay neutral. Also convey state through text, icons, or shape. Design light/dark palettes independently, without filters; pair semantic foreground/surface colors and use on-accent tokens for action text.

## Motion

- Use shared standard easing with 140ms transitions or 220ms state transitions. Avoid generic scroll entrances, looping decoration, delayed routine text, and unbounded particles in focused tasks.
- First-visit game storytelling may pace existing text once; repeat visits fast-forward and reduced motion displays it immediately.
- Important outcomes may combine shared durations into bounded choreography that explains the result. Publish semantic results and actions immediately, never gate progress on `animationend`, cap decoration, and provide an immediate reduced-motion state.
- Render indefinite activities from aggregate state with bounded elements, never one image or DOM node per historical event.

## Controls and Layout

- Related controls share inherited fonts, height, modest radius, light borders, and visible focus. Aim for 44px direct-tap targets where space allows.
- Fields distinguish filled (teal boundary), read-only (muted neutral), invalid (danger boundary plus nearby explanation), and disabled (readable text, non-interactive cursor) states without relying on placeholders.
- Informational, success, warning, and error messages share neutral surfaces, semantic leading edges, spacing, and recovery-action geometry. Blocking alerts retain dialog shells with the same palette, type, radius, and action hierarchy. Group helper, confirmation, validation, and empty-state copy in compact light panels when useful.
- Frame file, proof, and profile previews; use `object-fit: contain` for variable aspect ratios. Autocomplete uses padded floating panels with clear hover/focus rows and must avoid covering the next mobile action.
- Dense rows never resize text or reflow on hover. Keep empty table states compact with recovery controls matching their neighbors. Filters and segments visibly distinguish active, clearable, and unavailable states beyond tiny badges.
- Suggestions fit the available viewport and keep the keyboard-selected option visible. Enter accepts that option without submitting the form; Escape and leaving the field dismiss the list without changing the value. Arrow keys can reopen it.
- Mobile drawers/viewers need clear close targets and contrasting backdrops. Stack modal sections and prefer the main scroll over nested scrolling. Long names/labels wrap without clipping; button icons keep fixed slots.
- Keep badges compact and stable on hover; details also appear on keyboard focus. Dense leaderboard rows show at most three badges plus bounded overflow; athlete detail views may show all.
