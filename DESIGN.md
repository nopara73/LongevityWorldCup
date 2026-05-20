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
