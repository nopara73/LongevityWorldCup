# Page weight

Public pages reference versioned shared JavaScript and CSS instead of repeating
application code inside each HTML response. This makes the document smaller and
lets subsequent pages reuse browser-cached assets. A first visit still downloads
the code required by that page; do not report the HTML reduction as an equivalent
reduction in the entire cold page load.

## Loading contracts

- Runtime source stays in `Frontend`. Existing TypeScript keeps strict checks;
  extracted classic JavaScript is syntax-checked and copied without transformation.
  Both source kinds participate in exact output checks and Node-free publish validation.
- `HtmlAssetPlaceholders` resolves all script, stylesheet, and configured image URLs
  using `AssetVersionProvider`. Versioned assets retain the static-file immutable
  cache policy. Changed bytes produce a new version URL.
- External classic scripts stay in the original inline scripts' document positions,
  without `async`, `defer`, or module conversion. Early head bootstraps stay inline.
- Image URLs formerly embedded in JavaScript are passed through versioned `data-*`
  attributes on the script tag and captured synchronously during evaluation.
- Shared font-face declarations remain inline to preserve versioned font URLs.
- The .NET page generator derives the three embedded athlete-dialog stylesheets
  from the corresponding shared CSS during every build, including Node-free publish.
  Their `@scope` boundary and `:scope` variables preserve the existing isolation
  on calculators, About, History, Highlights, and Challenge pages. Generated CSS
  is explicitly included in build and publish output and must not be edited directly.

## Verification

`PageWeightTests` checks document size budgets, versioned asset reuse, cache
validators, and generated scoped CSS. Browser checks cover cold and cached
navigation, profile dialogs, proof readers, Guess My Age, highlights, calculators,
onboarding, and desktop/mobile layout. Re-measure actual compressed transfer sizes
when changing these assets; smaller HTML alone does not prove faster cold rendering.
