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
