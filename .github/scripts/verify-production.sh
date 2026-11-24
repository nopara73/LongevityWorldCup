#!/bin/sh
set -eu

verified_sha="$1"
work="$2"
publish_output="$3"
deploy_source="$4"

health_url="https://www.longevityworldcup.com/health"
health_body="$work/health.json"
for attempt in $(seq 1 24); do
  if curl -fsS --max-time 10 "$health_url" -o "$health_body"; then
    break
  fi

  if [ "$attempt" -eq 24 ]; then
    echo "Production health check failed: $health_url"
    sudo systemctl status longevityworldcup.service --no-pager -l || true
    exit 1
  fi

  sleep 5
done

if ! grep -q '"status":"Healthy"' "$health_body"; then
  echo "Production health check returned unexpected content."
  cat "$health_body"
  exit 1
fi
rm -f "$health_body"

crawl_headers="$work/crawl-headers.txt"
normalized_crawl_headers="$work/crawl-headers.normalized.txt"
crawl_body="$work/crawl-body.html"
for missing_path in /missing-public-page-deploy-verification /athlete/missing-athlete-deploy-verification /league/missing-league-deploy-verification /flag/missing-flag-deploy-verification /error/404.html; do
  for method in GET HEAD; do
    if [ "$method" = HEAD ]; then
      crawl_status="$(curl -sS --max-time 10 --head -D "$crawl_headers" -o /dev/null -w '%{http_code}' "https://longevityworldcup.com$missing_path")"
    else
      crawl_status="$(curl -sS --max-time 10 -D "$crawl_headers" -o "$crawl_body" -w '%{http_code}' "https://longevityworldcup.com$missing_path")"
    fi
    tr -d '\r' < "$crawl_headers" > "$normalized_crawl_headers"
    if [ "$crawl_status" != 404 ] ||
       grep -Eiq '^location:' "$normalized_crawl_headers" ||
       ! grep -Eiq '^x-robots-tag:.*noindex' "$normalized_crawl_headers" ||
       ! grep -Eiq '^cache-control:.*no-store' "$normalized_crawl_headers"; then
      echo "Incorrect public 404 response for $method $missing_path (HTTP $crawl_status)." >&2
      cat "$normalized_crawl_headers" >&2
      exit 1
    fi
    if [ "$method" = GET ] && ! grep -Fq '<title>404 Not Found - Longevity World Cup</title>' "$crawl_body"; then
      echo "The proxy replaced the application 404 page for $missing_path." >&2
      exit 1
    fi
  done
done

cors_headers="$work/cors-headers.txt"
normalized_cors_headers="$work/cors-headers.normalized.txt"
assert_single_public_cors_origin() {
  origin_count="$(grep -Eic '^access-control-allow-origin:' "$normalized_cors_headers" || true)"
  wildcard_origin_count="$(grep -Eic '^access-control-allow-origin:[[:space:]]*\*[[:space:]]*$' "$normalized_cors_headers" || true)"
  if [ "$origin_count" -ne 1 ] || [ "$wildcard_origin_count" -ne 1 ]; then
    echo "Expected exactly one Access-Control-Allow-Origin header with value '*'; found $origin_count total and $wildcard_origin_count wildcard." >&2
    cat "$normalized_cors_headers" >&2
    exit 1
  fi
}

for public_path in /api/data/flags /api/events; do
  curl -fsS --max-time 10 \
    -H "Origin: https://public-api-client.example" \
    -D "$cors_headers" \
    -o /dev/null \
    "https://www.longevityworldcup.com$public_path"
  tr -d '\r' < "$cors_headers" > "$normalized_cors_headers"
  assert_single_public_cors_origin
done

curl -fsS --max-time 10 \
  -X OPTIONS \
  -H "Origin: https://public-api-client.example" \
  -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: content-type" \
  -D "$cors_headers" \
  -o /dev/null \
  "https://www.longevityworldcup.com/api/data/pheno-age"
tr -d '\r' < "$cors_headers" > "$normalized_cors_headers"
assert_single_public_cors_origin
grep -Eiq '^access-control-allow-methods:.*POST' "$normalized_cors_headers"
grep -Eiq '^access-control-allow-headers:.*content-type' "$normalized_cors_headers"

curl -fsS --max-time 10 \
  -X OPTIONS \
  -H "Origin: https://public-api-client.example" \
  -H "Access-Control-Request-Method: GET" \
  -H "Access-Control-Request-Headers: content-type" \
  -D "$cors_headers" \
  -o /dev/null \
  "https://www.longevityworldcup.com/api/events"
tr -d '\r' < "$cors_headers" > "$normalized_cors_headers"
assert_single_public_cors_origin
grep -Eiq '^access-control-allow-methods:.*GET' "$normalized_cors_headers"
grep -Eiq '^access-control-allow-headers:.*content-type' "$normalized_cors_headers"

curl -fsS --max-time 10 \
  -H "Origin: https://public-api-client.example" \
  -D "$cors_headers" \
  -o /dev/null \
  "$health_url"
tr -d '\r' < "$cors_headers" > "$normalized_cors_headers"
if grep -Eiq '^access-control-allow-origin:' "$normalized_cors_headers"; then
  echo "A non-public endpoint unexpectedly allowed an arbitrary CORS origin." >&2
  cat "$normalized_cors_headers" >&2
  exit 1
fi

downloaded_script="$work/deployed-script.js"
for script_path in "$publish_output"/wwwroot/js/*.js; do
  script_name="$(basename "$script_path")"
  # Retry a temporary transfer failure before judging the deployed bytes.
  if ! script_status="$(curl -fsS --max-time 10 --retry 1 --retry-delay 1 --retry-max-time 12 --write-out '%{http_code}' \
    "https://www.longevityworldcup.com/js/$script_name?v=$verified_sha" \
    -o "$downloaded_script")" || [ "$script_status" != 200 ]; then
    echo "Could not download deployed script: $script_name (HTTP $script_status)" >&2
    exit 1
  fi
  expected_hash="$(sha256sum "$script_path")"
  expected_hash="${expected_hash%% *}"
  actual_hash="$(sha256sum "$downloaded_script")"
  actual_hash="${actual_hash%% *}"
  if [ "$expected_hash" != "$actual_hash" ]; then
    echo "Deployed script content differs from the verified artifact: $script_name" >&2
    exit 1
  fi
done
rm -f "$downloaded_script"

if [ -n "$(git status --short)" ]; then
  echo "Server checkout was dirtied during deploy."
  git status --short
  exit 1
fi

sudo systemctl status longevityworldcup.service --no-pager
sudo sh "$deploy_source/LongevityWorldCup.Website/Scripts/sync-outage-assets.sh" \
  "$publish_output/wwwroot" /var/www/longevityworldcup-errors /etc/nginx/snippets/lwc-error-page.conf
