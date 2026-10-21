#!/bin/sh
set -eu
script_root="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
test_root="$(mktemp -d)"
trap 'rm -rf "$test_root"' EXIT
mkdir -p "$test_root/wwwroot/error" "$test_root/wwwroot/css" "$test_root/wwwroot/assets/fonts" "$test_root/bin" "$test_root/snippets"
export OUTAGE_TEST_ROOT="$test_root"
export PATH="$test_root/bin:$PATH"
cat > "$test_root/bin/nginx" <<'EOF'
#!/bin/sh
echo nginx-test >> "$OUTAGE_TEST_ROOT/calls"
test ! -f "$OUTAGE_TEST_ROOT/fail-validation"
EOF
cat > "$test_root/bin/systemctl" <<'EOF'
#!/bin/sh
echo "$*" >> "$OUTAGE_TEST_ROOT/calls"
if [ "$1" = reload ] && [ -f "$OUTAGE_TEST_ROOT/fail-reload" ]; then
  rm "$OUTAGE_TEST_ROOT/fail-reload"
  exit 1
fi
EOF
chmod +x "$test_root/bin/"*
for code in 502 503 504; do
  printf 'new HTML %s\n' "$code" > "$test_root/wwwroot/error/$code.html"
done
for file in error/herold.png assets/favicon-128x128.png css/error-system.css assets/fonts/Roboto-Regular.woff2 assets/fonts/Roboto-Bold.woff2; do
  printf 'new asset %s\n' "$file" > "$test_root/wwwroot/$file"
done
seed_previous() {
  mkdir -p "$test_root/longevityworldcup-errors"
  printf 'previous release\n' > "$test_root/longevityworldcup-errors/503.html"
  printf 'previous snippet\n' > "$test_root/snippets/lwc-error-page.conf"
}
run_sync() {
  sh "$script_root/sync-outage-assets.sh" "$test_root/wwwroot" "$test_root/longevityworldcup-errors" "$test_root/snippets/lwc-error-page.conf"
}
assert_previous() {
  test "$(cat "$test_root/longevityworldcup-errors/503.html")" = 'previous release'
  test "$(cat "$test_root/snippets/lwc-error-page.conf")" = 'previous snippet'
  test ! -f "$test_root/longevityworldcup-errors/error-system.css"
}
seed_previous
touch "$test_root/fail-validation"
if run_sync; then echo 'Invalid nginx configuration was accepted.' >&2; exit 1; fi
assert_previous
rm "$test_root/fail-validation"
touch "$test_root/fail-reload"
if run_sync; then echo 'Failed nginx reload was accepted.' >&2; exit 1; fi
assert_previous
run_sync
cmp "$script_root/lwc-error-page.conf" "$test_root/snippets/lwc-error-page.conf"
for code in 502 503 504; do cmp "$test_root/wwwroot/error/$code.html" "$test_root/longevityworldcup-errors/$code.html"; done
for file in error/herold.png assets/favicon-128x128.png css/error-system.css assets/fonts/Roboto-Regular.woff2 assets/fonts/Roboto-Bold.woff2; do
  cmp "$test_root/wwwroot/$file" "$test_root/longevityworldcup-errors/$(basename "$file")"
done
test "$(find "$test_root/longevityworldcup-errors" -type f | wc -l)" -eq 8
test "$(stat -c %a "$test_root/longevityworldcup-errors")" = 755
test -z "$(find "$test_root" -maxdepth 1 -name '*.stage.*' -print)"
echo 'Outage assets, validation rollback and reload rollback passed.'
