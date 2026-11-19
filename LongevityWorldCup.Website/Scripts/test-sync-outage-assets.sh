#!/bin/sh
set -eu
script_root="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
nginx_bin="$(command -v nginx)"
test_root="$(mktemp -d)"
cleanup() {
  if [ -f "$test_root/nginx.pid" ]; then "$nginx_bin" -p "$test_root" -c nginx.conf -s quit; fi
  rm -rf "$test_root"
}
trap cleanup EXIT
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

# Run the installed snippet against an unavailable upstream on a private Unix socket.
mkdir -p "$test_root/logs" "$test_root/wwwroot/athletes/sample"
proof="$test_root/wwwroot/athletes/sample/proof_1.webp"
printf 'published proof bytes' > "$proof"
printf '{}' > "$test_root/wwwroot/athletes/sample/athlete.json"
sed -e "s|/var/www/LongevityWorldCup/publish/wwwroot|$test_root/wwwroot|g" \
    -e "s|/var/www/longevityworldcup-errors|$test_root/longevityworldcup-errors|g" \
    "$test_root/snippets/lwc-error-page.conf" > "$test_root/outage.conf"
cat > "$test_root/nginx.conf" <<EOF
worker_processes 1;
pid $test_root/nginx.pid;
error_log $test_root/error.log;
events { worker_connections 32; }
http {
  access_log off;
  types { image/webp webp; text/html html; }
  client_body_temp_path $test_root/body;
  proxy_temp_path $test_root/proxy;
  fastcgi_temp_path $test_root/fastcgi;
  scgi_temp_path $test_root/scgi;
  uwsgi_temp_path $test_root/uwsgi;
  server {
    listen unix:$test_root/public.sock;
    include $test_root/outage.conf;
    location / { proxy_pass http://unix:$test_root/stopped.sock; }
  }
}
EOF
"$nginx_bin" -p "$test_root" -c nginx.conf
check_response() {
  expected_status=$1 expected_file=$2 path=$3
  shift 3
  status="$(curl -sS --max-time 5 --unix-socket "$test_root/public.sock" -D "$test_root/headers" \
    -o "$test_root/response" -w '%{http_code}' "$@" "http://localhost$path")"
  if [ "$status" != "$expected_status" ]; then
    echo "$path returned $status, expected $expected_status" >&2
    exit 1
  fi
  cmp "$expected_file" "$test_root/response"
}
outage_page="$test_root/longevityworldcup-errors/502.html"
check_response 200 "$proof" '/athletes/sample/proof_1.webp?v=123'
grep -iq '^Content-Type: image/webp' "$test_root/headers"
check_response 502 "$outage_page" /api/proof-previews/sample/proof_1.webp
check_response 502 "$outage_page" /athletes/sample/proof_2.webp
check_response 502 "$outage_page" /athletes/sample/athlete.json
check_response 502 "$outage_page" /athletes/sample/proof_1.webp -X POST
echo 'Published proof fallback and gateway error responses passed.'
