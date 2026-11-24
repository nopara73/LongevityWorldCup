# Font Awesome Free 6.7.2

This directory contains the pinned web-font distribution from
`@fortawesome/fontawesome-free@6.7.2`. It is served locally so shared site
iconography does not disappear when a third-party CDN is blocked or unavailable.

The upstream license is preserved in `LICENSE.txt`. The vendored files are:

- `css/all.min.css`
- the `webfonts/fa-*.woff2` and `webfonts/fa-*.ttf` files referenced by that CSS

The `webfonts/lwc-*.woff2` files are LWC subsets of those fonts, distributed
under the same SIL Open Font License. Rebuild them from the original files
with `Scripts/prepare-icon-fonts.py`. The LWC rules at the end of `css/all.min.css`
pair them with the complete upstream faces for other characters. Both are
registered together to avoid downloading the full fonts while styles load.

Source: https://www.npmjs.com/package/@fortawesome/fontawesome-free/v/6.7.2
