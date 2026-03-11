# LongevityWorldCup Agent Notes

This file documents project-specific rules that agents commonly get wrong. It is intentionally focused on domain constraints, not generic development advice.

## Ranking Logic Must Stay Aligned

- Ranking is currently calculated in both the backend and the frontend.
- Do not assume one side is only presenting data from the other. Both sides contain logic that can affect ordering and placements.
- If you change ranking logic on one side, you must review and update the other side so the logic stays identical.
- The expected outcome is that the backend and frontend produce the same ordering and the same placements for the same data.

## Ultimate League Ordering

- Inside the Ultimate League, Pro and Amateur are distinct categories.
- Pro always has priority over Amateur in lists and ordering.
- This is not an optional presentation detail. It is part of the expected sorting behavior.
- Current concrete example: Bortz is Pro, PhenoAge is Amateur.
- If you touch any sorting, ranking, or listing logic that affects Ultimate League entries, explicitly verify that Pro entries still appear before Amateur entries.

## Keep This File Updated

- If you modify behavior in any of the areas described above, update this file as part of the same change.
- If a rule here stops being accurate, do not leave the file stale. Adjust it together with the code change.
