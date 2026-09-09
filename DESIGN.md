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
- Shared interaction transitions name their visual properties explicitly. Responsive widths, flex sizing, and gaps update immediately so neighboring controls keep their space during resizing.
- First-visit game storytelling may pace existing text once; repeat visits fast-forward and reduced motion displays it immediately.
- Important outcomes may combine shared durations into bounded choreography that explains the result. Publish semantic results and actions immediately, never gate progress on `animationend`, cap decoration, and provide an immediate reduced-motion state.
- Render indefinite activities from aggregate state with bounded elements, never one image or DOM node per historical event.

## Controls and Layout

- Related controls share inherited fonts, height, modest radius, light borders, and visible focus. Aim for 44px direct-tap targets where space allows.
- Searchable timezone choices expand within the form. Keep the text cursor in the search field during arrow navigation, expose the active option to assistive technology, and let Tab or Escape leave without changing the selected timezone.
- Challenge timezone edits remain in the open page across views and background updates. Retain later edits while saving and protect unfinished work on exit. Keep Save inactive only when the selection is confirmed saved; uncertain results remain retryable even after reverting. Show feedback only for a recoverable failure; saved timezone rules take effect only after an accepted response.
- When the device and saved challenge timezones differ, offer a quiet in-page choice. Review opens the existing timezone editor; Save confirms the change. Remember a decision to keep the current zone on that device, ignore equivalent timezone aliases, and never replace an unfinished timezone edit with a suggestion.
- Fields distinguish filled (teal boundary), read-only (muted neutral), invalid (danger boundary plus nearby explanation), and disabled (readable text, non-interactive cursor) states without relying on placeholders.
- Profile field errors appear beside the field without interrupting the next edit. Submission shows all invalid fields and focuses the first; correcting or restoring a value clears its feedback while keeping the draft intact.
- Newsletter subscription and removal feedback stays beside the form. Preserve typing and focus during a request, serialize submissions, and offer retry after failures. Confirm the submitted address without a dialog, clear only that unchanged field, and remove obsolete feedback when the reader edits it.
- Profile drafts persist quietly, stay tied to the selected athlete, and clear after successful submission. Offer Reset all with Undo without a summary panel or repeated field links. Show save feedback only when storage fails and the user needs to keep the page open; protect that unsaved work when leaving.
- Informational, success, warning, and error messages share neutral surfaces, semantic leading edges, spacing, and recovery-action geometry. Blocking alerts retain dialog shells with the same palette, type, radius, and action hierarchy. Group helper, confirmation, validation, and empty-state copy in compact light panels when useful.
- Frame file, proof, and profile previews; use `object-fit: contain` for variable aspect ratios. Autocomplete uses padded floating panels with clear hover/focus rows and must avoid covering the next mobile action.
- Use the square `play-athlete-placeholder` artwork for athlete placeholders throughout the UI. The older `headshot` image is a historical champion illustration, not the default avatar.
- Challenge profile-photo uploads keep the selected preview and filename through failures, with retry and replacement beside the error. Keep the action focused while uploading; a late response must preserve other edits and focus. Publish only the returned picture and clear transient upload feedback after success.
- Challenge sign-in feedback names the submitted address and clears when it is edited. Keep typing and keyboard focus available while sending, serialize requests, and offer retry beside recoverable errors. Preserve unrelated reminder opt-out notices; confirmation copy must fit both confirmed and unconfirmed participants.
- Proof uploads show an ordered page grid with source filenames, a zoomable reader, and removal undo. Keep preparation progress beside the pages and disable submission while files are processing. Returning to the step retains the attached pages and checklist; background preparation must respect the active step's validation.
- Protect selected or processing proof files from accidental page exits. Clearing all proofs or accepting a submission removes the warning; in-page steps remain uninterrupted. Keep removal and undo quiet, and clear obsolete upload notices when another selection starts.
- Dense rows never resize text or reflow on hover. Keep empty table states compact with recovery controls matching their neighbors. Filters and segments visibly distinguish active, clearable, and unavailable states beyond tiny badges.
- Filtered leaderboards expose removable selections and the matching athlete count beside the results. Mobile filter drawers keep their results action visible while the options scroll.
- Leaderboard filter counts reflect an option's matching members after adding it to the current selection, preserving other groups and the search. Keep alternatives available when combining selections or switching competitions can produce matches, including when that changes the rank matched by a numeric search.
- When a signed-in participant has a due check-in, open the focused check-in dialog on arrival, including remembered sign-ins and reloads. Hide the participant tabs and day switcher, keep the surrounding page inactive, and return to the dashboard after the due check-ins are saved. Keep an explicit close action available.
- Check-ins retain unfinished answers, remarks, and selected photos when switching days or views. Mark work in progress, offer undo after a reset, and keep the save action visible through the form. Failed saves retain the work for retry; saving one day must not interrupt another day's draft.
- Adding check-in photos extends the current selection, shows filenames, and explains skipped files and capacity limits. Changes stay unpublished until the check-in is saved; leaving the page with unfinished work prompts before discarding it.
- Discussion replies and edits retain separate drafts across threads, paging, and views in the open page. Closing keeps the draft; discarding is explicit. Show character capacity, a way to resume, and recoverable save errors. Background updates keep the active thread on screen and preserve its composer, selection, and focus; explicit paging still navigates normally. Retries retain the same submission identity.
- Suggestions fit the available viewport and keep the keyboard-selected option visible. Enter accepts that option without submitting the form; Escape and leaving the field dismiss the list without changing the value. Arrow keys can reopen it.
- Athlete name searches accept omitted accents while displaying and storing the original name. Enter may accept a unique full-name match; ambiguous matches require an explicit choice. Show a quiet empty result only after the directory has loaded and a nonempty query finds nothing. Failed searches offer retry beside the field, preserving the form and current query; arriving results respect dismissal and keyboard focus.
- Mobile drawers/viewers need clear close targets and contrasting backdrops. Stack modal sections and prefer the main scroll over nested scrolling. Long names/labels wrap without clipping; button icons keep fixed slots.
- Closing an athlete profile returns to its calling page without adding duplicate browser visits. Back and Forward restore the matching profile or image layer. Direct profile links close safely within the site, preserving unrelated query parameters and anchors; closing animations never rewrite a later navigation.
- Profile sharing keeps copy and native-share results tied to the originating profile. Preserve keyboard focus and serialize pending actions; dismissing or reopening sharing prevents stale results from changing the current view. Failed copying offers a selectable profile link alongside retry and the existing share destinations.
- Public image readers show loading and recoverable failure states. Keep navigation and Close usable while an image loads, enable zoom only for a loaded image, and retry the selected image without changing position or zoom. Late responses must not replace the image the user has moved to.
- Keep proof navigation separate from zoom and reserve space for reading controls. Place proof galleries before asynchronous highlights so loading cannot move evidence under the pointer.
- Public proof galleries use numbered previews in the main page scroll. Long sets begin with six previews and an explicit Show all control; the reader always includes the full ordered set. Closing the reader reveals and focuses the selected proof, including pages outside the initial overview.
- Keep badges compact and stable on hover; details also appear on keyboard focus. Dense leaderboard rows show at most three badges plus bounded overflow; athlete detail views may show all.
