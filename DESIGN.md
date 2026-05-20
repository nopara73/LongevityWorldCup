# LongevityWorldCup Design Decisions

This file records durable UI decisions that have proven useful across repeated Longevity World Cup surfaces. Keep it short. Add to it only when a design choice is solid enough to guide future UI work; do not use it for workflow rules, business constraints, or broad taste preferences.

## Decisions

- Mobile and dense interactive controls should generally provide a 44px touch target when they are meant to be tapped directly. This has been applied to footer links, modal close controls, carousel dots, and shared onboarding form controls.

- Shared onboarding form controls in `bioageform` use a 44px minimum height, 1rem text, and a visible cyan focus ring. This keeps convergence, Bortz Age, and PhenoAge forms consistent after the shared form-control polish.

- `bioageform` selects use a consistent custom chevron treatment, while text inputs remain plain. This avoids mixing native and custom select arrows inside the same onboarding flow.

- `bioageform` fieldsets use a subtle light surface, soft border, and modest radius so grouped form sections look intentional next to the site’s card-like UI.

- `bioageform` legends use a small surface-label treatment so section titles sit clearly on grouped controls instead of competing with the fieldset border.

- Onboarding fieldsets that represent a distinct step section should use a short legend when neighboring step sections do, so multi-step forms keep a consistent scanning rhythm.

- Bioage biomarker accordion cards use white card surfaces, soft slate borders, a distinct active header/content treatment, and a compact circular toggle affordance so dense lab groups scan clearly inside the lighter fieldsets.

- Bioage biomarker input/unit rows rely on row gaps instead of per-control margins, with numeric inputs allowed to shrink before unit selects wrap on narrow screens.

- Bioage CRP negative toggles sit in a compact helper panel with a 44px label target so the optional switch reads as part of the opened biomarker card.

- Bioage final calculate CTAs are centered with an explicit max width instead of inline full-width styles so step actions stay visually consistent across Bortz and Pheno.

- Proof upload previews use a centered card with a clearly tappable remove control and a compact checklist panel. This keeps Convergence proof submission aligned with the standalone proof-upload surface that uses the same helper.

- Proof upload images sit in a bounded `object-fit: contain` preview frame so unusual receipt, lab-report, or generated image ratios do not dominate the mobile flow.

- Convergence upload steps use constrained full-width action blocks and compact guidance panels instead of narrow one-off upload buttons, keeping profile and proof upload steps aligned.

- Convergence profile crop previews use the same centered card treatment as proof previews so selected images feel anchored before the crop/save action.

- Convergence helper and confirmation copy should sit in light bordered panels when it explains an adjacent form field, making the form easier to scan than loose paragraphs between inputs.

- Character counters should sit outside text inputs as compact status pills when space allows, so they do not cover typed content or resize handles.

- Convergence stage-intro copy should use the same light bordered panel language as field helper copy when it sits under the stage image, so narrative text remains distinct from form controls.

- Convergence field validation errors should appear as compact alert panels only when populated, rather than reserving empty space or showing bare red text.

- Convergence autocomplete menus use a padded floating panel with rounded rows and an explicit inline match highlight, matching the form-control and helper-panel polish without changing selection behavior.

- Sub-progress rails should read as light bordered panels with a subtle track and distinct completed/current dot states, so step position feels integrated with the surrounding onboarding cards.

- Decision cards with substantially different content heights should size to their own content instead of stretching to match the tallest card when stretching creates large empty space before the call to action.

- End-of-flow confirmation/status copy should be grouped in a light bordered panel when it is the primary message below an illustration, so the page reads as a clear result state instead of loose supporting text.

- Autocomplete menus that open above nearby action buttons should reserve vertical space while open, so suggestions do not cover the next available control on mobile or short desktop viewports.

- Poster-like onboarding/play visuals should use the same modest border, radius, and shadow language as adjacent image surfaces when they sit directly above stacked action buttons.

- Helper or discount panels that appear inside a stacked action block should align to the same width as the surrounding actions unless there is a clear reason to make them narrower.

- Edit-profile text fields, selects, and textareas should share the same inherited font, light border, modest radius, and cyan focus ring so editable profile details do not switch visual languages mid-form.

- Edit-profile autocomplete menus should use the same light floating-panel treatment as other polished autocomplete surfaces and reserve vertical space while open.

- Inline restore controls in edit-profile rows should match the 44px field height so pending-change affordances stay tappable without stretching the row.

- Profile cropper modals should keep the selected image in a bounded, lightly framed preview so unusual image proportions remain inspectable before saving.

- Proof-upload instruction copy should read as a compact light panel above the upload action, matching the checklist and helper-panel treatment used later in the same flow.

- Proof tracker checklist rows should be label-sized tap targets with accent-colored checkboxes, keeping long biomarker lists scannable without reverting to raw browser defaults.

- Uploaded proof success messages should appear as compact success panels directly above the preview card, so the confirmed upload state is grouped with the evidence being reviewed.

- Multiple uploaded proof preview cards should keep a visible vertical gap between cards so each evidence image remains individually scannable on mobile and desktop.

- Proof upload action shadows should follow the current button state: green depth while upload is the primary required action, neutral depth once it becomes a secondary add-more-proofs action.

- Dashboard titles that render athlete-provided names should use a tight line-height and responsive mobile sizing so long display names stay readable without pushing the action stack too far down.

- Dashboard action buttons should wrap generated text in a label span and keep icons in fixed edge slots, so long action names stay centered when they wrap on narrow screens.

- Animated text-reveal headings should size against the rendered font, not just the character count, so bold display text does not clip its final characters.

- Leaderboard toolbar controls should share a 44px control rhythm across view switches, search fields, and filter buttons; on mobile, view switches should span the toolbar width so the control stack feels aligned.
