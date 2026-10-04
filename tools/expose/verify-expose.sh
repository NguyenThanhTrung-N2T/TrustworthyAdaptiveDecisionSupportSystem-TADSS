#!/usr/bin/env bash
# T5.30 / NG-1…NG-5, SEC-6: kiểm tra cấu hình expose của TADSS bằng Docker thật (không mở tunnel ngrok, không kết nối database ngoài).
#   NG-1  : docker compose config hợp lệ cho ngrok (chế độ A) và ngrok + neon (chế độ B), biến giả.
#   NG-2  : TADSS khởi động với khóa; Staging không có khóa thì KHÔNG khởi động (thoát != 0 trong 30 giây, log nêu Tadss:Security, không lộ chuỗi kết nối).
#   NG-3  : /health/ready 200, POST /api/v2/decisions không khóa 401 và có khóa 200, GET theo decisionId 200, swagger 404, DB có bản ghi.
#   NG-5  : mọi cổng host chỉ bind 127.0.0.1.
#   SEC-6 : image chạy bằng user non-root.
# Chạy trong project compose riêng (tadss-expose-check) và cổng host riêng nên không đụng stack đang chạy của người dùng.
# Khóa API sinh ngẫu nhiên mỗi lần chạy, không bao giờ được in ra. Chạy được trên Git Bash (Windows) và Ubuntu.
set -uo pipefail

# Git Bash đổi đường dẫn kiểu /dev/null thành đường dẫn Windows khi gọi docker.exe; tắt để tham số tới container nguyên vẹn.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

cd "$(dirname "$0")/../.." || exit 1

PROJECT="tadss-expose-check"
NETWORK="${PROJECT}_default"
CURL_IMAGE="curlimages/curl:8.22.0"
MIN_COMPOSE_VERSION="2.24.4"
ENV_FILE=".env.expose-check"  # đường dẫn tương đối: docker compose.exe trên Windows không hiểu /tmp của Git Bash
NOKEY_CONTAINER="${PROJECT}-nokey"
SAMPLE="tools/expose/sample-decision-request.json"

# Cổng host riêng (khác mặc định 8081/5433/6380/8474/3001 của stack E-LOCAL).
HOST_PORT=18081

declare -a RESULT_IDS=() RESULT_STATES=() RESULT_NOTES=()
failures=0

record() { # $1 id, $2 PASS|FAIL, $3 ghi chú
  RESULT_IDS+=("$1"); RESULT_STATES+=("$2"); RESULT_NOTES+=("$3")
  if [ "$2" != "PASS" ]; then failures=$((failures + 1)); fi
}

sha256_hex() { # đọc stdin, in hex SHA-256
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | cut -d' ' -f1
  else
    openssl dgst -sha256 | sed 's/^.* //'
  fi
}

dc() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.ngrok.yml "$@"; }

cleanup() {
  # Chỉ dọn tài nguyên của đúng project này: container, volume, network, image build ra, và container thăm dò NG-2.
  docker rm -f "$NOKEY_CONTAINER" >/dev/null 2>&1
  if [ -f "$ENV_FILE" ]; then
    dc down -v --rmi local --remove-orphans >/dev/null 2>&1
  else
    docker compose -p "$PROJECT" -f docker-compose.yml down -v --rmi local --remove-orphans >/dev/null 2>&1
  fi
  rm -f "$ENV_FILE"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ── Điều kiện tiên quyết ──
if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  echo "LỖI: cần Docker đang chạy." >&2; exit 1
fi
compose_version="$(docker compose version --short 2>/dev/null | sed 's/^v//; s/[^0-9.].*$//')"
if [ -z "$compose_version" ] || [ "$(printf '%s\n%s\n' "$MIN_COMPOSE_VERSION" "$compose_version" | sort -V | head -n1)" != "$MIN_COMPOSE_VERSION" ]; then
  echo "LỖI: cần docker compose >= ${MIN_COMPOSE_VERSION} (có '${compose_version:-không rõ}'); các file override dùng !override/!reset." >&2; exit 1
fi
if ! command -v openssl >/dev/null 2>&1; then
  echo "LỖI: cần openssl để sinh khóa ngẫu nhiên." >&2; exit 1
fi

# ── Khóa ngẫu nhiên + biến cho project (không in khóa) ──
API_KEY="$(openssl rand -hex 32)"
API_KEY_SHA256="$(printf %s "$API_KEY" | sha256_hex)"
cat >"$ENV_FILE" <<EOF
TADSS_API_KEY_SHA256=${API_KEY_SHA256}
NGROK_AUTHTOKEN=fake-token-for-compose-config-only
TADSS_PORT=${HOST_PORT}
TADSS_PG_PORT=15433
TADSS_REDIS_PORT=16380
TADSS_TOXIPROXY_PORT=18474
TADSS_GRAFANA_PORT=13001
EOF

# ── NG-1: compose config ──
cfg_a="FAIL"; cfg_b="FAIL"
if docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.ngrok.yml config --quiet >/dev/null 2>&1; then cfg_a="PASS"; fi
if TADSS_DB_CONNECTION="Host=fake.invalid;Port=5432;Database=fake;Username=fake;Password=fake" \
   docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.ngrok.yml -f docker-compose.neon.yml config --quiet >/dev/null 2>&1; then cfg_b="PASS"; fi
# Thiếu biến bắt buộc phải bị từ chối: neon không có TADSS_DB_CONNECTION, ngrok không có TADSS_API_KEY_SHA256.
neg_db="FAIL"; neg_key="FAIL"
if ! docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.ngrok.yml -f docker-compose.neon.yml config --quiet >/dev/null 2>&1; then neg_db="PASS"; fi
: >"${ENV_FILE}.nokey"
if ! NGROK_AUTHTOKEN=fake docker compose -p "$PROJECT" --env-file "${ENV_FILE}.nokey" -f docker-compose.yml -f docker-compose.ngrok.yml config --quiet >/dev/null 2>&1; then neg_key="PASS"; fi
rm -f "${ENV_FILE}.nokey"
if [ "$cfg_a" = PASS ] && [ "$cfg_b" = PASS ] && [ "$neg_db" = PASS ] && [ "$neg_key" = PASS ]; then
  record "NG-1" PASS "config hợp lệ cho ngrok và ngrok+neon; thiếu TADSS_DB_CONNECTION/TADSS_API_KEY_SHA256 bị từ chối"
else
  record "NG-1" FAIL "ngrok=${cfg_a} ngrok+neon=${cfg_b} thiếu-DB-bị-từ-chối=${neg_db} thiếu-khóa-bị-từ-chối=${neg_key}"
fi

# ── Chế độ A: dựng stack (không bật profile ngrok) ──
echo ">> Dựng project ${PROJECT} (build image, có thể mất vài phút)..."
stack_up="FAIL"
if dc up -d --build --wait tadss-runtime >/dev/null 2>"${ENV_FILE}.err"; then stack_up="PASS"; fi
if [ "$stack_up" != "PASS" ]; then
  echo "LỖI: không dựng được stack:" >&2; tail -n 20 "${ENV_FILE}.err" >&2; dc logs --tail 30 tadss-runtime >&2
fi
rm -f "${ENV_FILE}.err"

probe() { docker run --rm --network "$NETWORK" "$CURL_IMAGE" -s -m 20 "$@"; }
code_of() { probe -o /dev/null -w '%{http_code}' "$@"; }
BASE_URL="http://tadss-runtime:8080"

if [ "$stack_up" = PASS ]; then
  # /health/ready cần PostgreSQL qua Toxiproxy; chờ tối đa 60 giây.
  ready=""
  for _ in $(seq 1 30); do
    ready="$(code_of "${BASE_URL}/health/ready")"
    [ "$ready" = "200" ] && break
    sleep 2
  done

  # Body mẫu: thay chỗ giữ chỗ bằng giá trị tính lúc chạy để mẫu luôn hợp lệ theo thời gian và mỗi lần chạy là một đơn mới.
  hex="$(openssl rand -hex 16)"
  reservation_id="${hex:0:8}-${hex:8:4}-4${hex:13:3}-8${hex:17:3}-${hex:20:12}"
  now_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  start_utc="$(date -u -d '+7 days' +%Y-%m-%dT%H:00:00Z)"
  end_utc="$(date -u -d '+7 days +2 hours' +%Y-%m-%dT%H:00:00Z)"
  body="$(sed -e "s/__RESERVATION_ID__/${reservation_id}/" -e "s/__REQUESTED_AT__/${now_utc}/" \
              -e "s/__START_TIME__/${start_utc}/" -e "s/__END_TIME__/${end_utc}/" "$SAMPLE")"

  no_key="$(code_of -X POST -H 'Content-Type: application/json' -H "X-Idempotency-Key: ${reservation_id}" -d "$body" "${BASE_URL}/api/v2/decisions")"
  with_key_out="$(probe -X POST -H 'Content-Type: application/json' -H "X-Api-Key: ${API_KEY}" -H "X-Idempotency-Key: ${reservation_id}" \
                    -H "X-Correlation-ID: expose-check-${hex:0:8}" -d "$body" -w $'\n%{http_code}' "${BASE_URL}/api/v2/decisions")"
  with_key="${with_key_out##*$'\n'}"
  post_body="${with_key_out%$'\n'*}"
  decision_id="$(printf '%s' "$post_body" | grep -o '"decisionId" *: *"[^"]*"' | head -n1 | sed 's/.*"\([^"]*\)"$/\1/')"
  get_out="$(probe -H "X-Api-Key: ${API_KEY}" -w $'\n%{http_code}' "${BASE_URL}/api/v2/decisions/${decision_id}")"
  get_code="${get_out##*$'\n'}"
  get_id="$(printf '%s' "${get_out%$'\n'*}" | grep -o '"decisionId" *: *"[^"]*"' | head -n1 | sed 's/.*"\([^"]*\)"$/\1/')"
  swagger="$(code_of "${BASE_URL}/swagger/index.html")"
  rows="$(dc exec -T tadss-postgres psql -U "${TADSS_PG_USER:-tadss}" -d tadss -tAc 'select count(*) from tadss.decision_executions' 2>/dev/null | tr -d '[:space:]')"

  detail="ready=${ready} không-khóa=${no_key} có-khóa=${with_key} GET=${get_code} swagger=${swagger} bản-ghi=${rows:-?}"
  if [ "$ready" = 200 ] && [ "$no_key" = 401 ] && [ "$with_key" = 200 ] && [ -n "$decision_id" ] \
     && [ "$get_code" = 200 ] && [ "$get_id" = "$decision_id" ] && [ "$swagger" = 404 ] && [ "${rows:-0}" -ge 1 ] 2>/dev/null; then
    record "NG-3" PASS "$detail"
  else
    record "NG-3" FAIL "$detail"
    dc logs --tail 30 tadss-runtime >&2
  fi

  # ── NG-5: mọi cổng publish chỉ ở 127.0.0.1 ──
  ps_json="$(dc ps --format json 2>/dev/null)"
  urls="$(printf '%s' "$ps_json" | grep -o '"URL" *: *"[^"]*"' | sed 's/.*"\([^"]*\)"$/\1/' | sort -u | tr '\n' ' ')"
  published="$(printf '%s' "$ps_json" | grep -o '"PublishedPort" *: *[1-9][0-9]*' | wc -l | tr -d '[:space:]')"
  bad="$(printf '%s' "$urls" | tr ' ' '\n' | grep -v -e '^$' -e '^127\.0\.0\.1$' | tr '\n' ' ')"
  if [ "${published:-0}" -ge 1 ] && [ -z "$bad" ]; then
    record "NG-5" PASS "${published} cổng publish; URL: ${urls}"
  else
    record "NG-5" FAIL "published=${published:-0} URL không phải loopback: '${bad}'"
  fi
else
  record "NG-3" FAIL "không dựng được stack"
  record "NG-5" FAIL "không dựng được stack"
fi

# ── NG-2 (không khởi động): Staging không có khóa ──
IMAGE="$(docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f docker-compose.ngrok.yml images tadss-runtime --format json 2>/dev/null \
          | grep -o '"Repository" *: *"[^"]*"' | head -n1 | sed 's/.*"\([^"]*\)"$/\1/')"
IMAGE="${IMAGE:-${PROJECT}-tadss-runtime}"
if [ "$stack_up" = PASS ] && docker image inspect "$IMAGE" >/dev/null 2>&1; then
  docker rm -f "$NOKEY_CONTAINER" >/dev/null 2>&1
  docker run -d --name "$NOKEY_CONTAINER" -e ASPNETCORE_ENVIRONMENT=Staging \
    -e "ConnectionStrings__DefaultConnection=Host=expose-check-db.invalid;Port=5432;Database=x;Username=u;Password=SENTINEL_NOT_A_SECRET" \
    "$IMAGE" >/dev/null 2>&1
  state=""; waited=0
  while [ "$waited" -lt 30 ]; do
    state="$(docker inspect -f '{{.State.Status}}' "$NOKEY_CONTAINER" 2>/dev/null)"
    [ "$state" = "exited" ] && break
    sleep 1; waited=$((waited + 1))
  done
  exit_code="$(docker inspect -f '{{.State.ExitCode}}' "$NOKEY_CONTAINER" 2>/dev/null)"
  logs="$(docker logs "$NOKEY_CONTAINER" 2>&1)"
  has_section="no"; leaks="no"
  printf '%s' "$logs" | grep -q 'Tadss:Security' && has_section="yes"
  printf '%s' "$logs" | grep -q -e 'SENTINEL_NOT_A_SECRET' -e 'expose-check-db.invalid' && leaks="yes"
  docker rm -f "$NOKEY_CONTAINER" >/dev/null 2>&1
  if [ "$state" = exited ] && [ "${exit_code:-0}" != 0 ] && [ "$has_section" = yes ] && [ "$leaks" = no ]; then
    record "NG-2" PASS "thoát mã ${exit_code} sau ${waited}s; log nêu Tadss:Security, không lộ chuỗi kết nối"
  else
    record "NG-2" FAIL "trạng thái=${state:-?} mã=${exit_code:-?} sau ${waited}s; nêu-Tadss:Security=${has_section} lộ-chuỗi-kết-nối=${leaks}"
  fi

  # ── SEC-6: user non-root ──
  user="$(docker inspect --format '{{.Config.User}}' "$IMAGE" 2>/dev/null)"
  case "$user" in
    ""|root|0|0:*|root:*) record "SEC-6" FAIL "Config.User='${user}'" ;;
    *) record "SEC-6" PASS "Config.User='${user}'" ;;
  esac
else
  record "NG-2" FAIL "không có image tadss-runtime để thử"
  record "SEC-6" FAIL "không có image tadss-runtime để kiểm tra"
fi

# ── Bảng kết quả ──
echo
echo "Kết quả verify-expose (project ${PROJECT}):"
printf '%-7s %-5s %s\n' "Code" "State" "Detail"
for i in "${!RESULT_IDS[@]}"; do
  printf '%-7s %-5s %s\n' "${RESULT_IDS[$i]}" "${RESULT_STATES[$i]}" "${RESULT_NOTES[$i]}"
done
echo
if [ "$failures" -gt 0 ]; then
  echo "THẤT BẠI: ${failures} mã không đạt."
  exit 1
fi
echo "TẤT CẢ ĐẠT."
