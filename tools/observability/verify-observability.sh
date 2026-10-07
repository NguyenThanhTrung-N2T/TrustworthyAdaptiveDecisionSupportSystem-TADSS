#!/usr/bin/env bash
# OB-2, OB-4 (O-23): kiểm tra chế độ vận hành có observability bằng Docker thật: dựng tadss-runtime + grafana (otel-lgtm) từ docker-compose.yml, không ngrok, không Neon.
#   OB-2 : Grafana nạp dashboard "TADSS Runtime" (uid tadss-runtime, thư mục TADSS) từ thư mục provisioning mount chỉ đọc (API Grafana, không đăng nhập).
#   OB-4 : sau vài request, Prometheus (qua proxy datasource của Grafana) có tadss_decisions_total > 0 trong 60 giây: tadss-runtime thật sự xuất metric tới Grafana.
# Hermetic như verify-expose.sh: script sinh MỘT FILE OVERRIDE TẠM (thư mục tạm, xóa khi thoát, không nằm trong repo) thêm PostgreSQL/Redis dùng một lần, đặt biến môi trường
# chuỗi kết nối + khóa API ngẫu nhiên (ghi đè appsettings.Development.json trong image, nên không bao giờ chạm Neon/Redis thật; script KHÔNG đọc file đó), và đưa tadss-runtime + grafana
# vào MỘT MẠNG RIÊNG của project thử (không dùng smashcourt-network, nên không đụng container của người dùng và không trùng tên DNS "grafana"); không publish cổng nào ra host.
# Không khởi động ngrok. Chạy trong project compose riêng (tadss-observability-check) nên không đụng project `tadss` của người dùng. Image build ra bị xóa khi thoát.
# Khóa API sinh ngẫu nhiên mỗi lần chạy, không bao giờ được in ra. Chạy được trên Git Bash (Windows) và Ubuntu. Lần đầu phải tải image otel-lgtm (~3,7 GB).
set -uo pipefail

# Git Bash đổi đường dẫn kiểu /dev/null thành đường dẫn Windows khi gọi docker.exe; tắt để tham số tới container nguyên vẹn.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

cd "$(dirname "$0")/../.." || exit 1

# Hermetic: biến shell của người dùng (khóa, chuỗi kết nối, COMPOSE_*) được compose ưu tiên hơn --env-file nên phải bỏ hết.
for name in $(compgen -e); do
  case "$name" in TADSS_*|NGROK_*|COMPOSE_*) unset "$name" ;; esac
done

PROJECT="tadss-observability-check"
CURL_IMAGE="curlimages/curl:8.22.0"
MIN_COMPOSE_VERSION="2.24.4"  # cần cho `!override` trong override tạm
SAMPLE="tools/expose/sample-decision-request.json"
DASHBOARD_UID="tadss-runtime"
REQUESTS=20
METRIC_DEADLINE_SECONDS=60

TMP_DIR="$(mktemp -d)" || { echo "LỖI: không tạo được thư mục tạm." >&2; exit 1; }
if command -v cygpath >/dev/null 2>&1; then TMP_DIR="$(cygpath -m "$TMP_DIR")"; fi
ENV_FILE="${TMP_DIR}/obs.env"
OVERRIDE_FILE="${TMP_DIR}/override.yml"
ERR_FILE="${TMP_DIR}/up.err"

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

dc() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f "$OVERRIDE_FILE" "$@"; }

cleanup() {
  # Chỉ dọn tài nguyên của đúng project này: container, volume, mạng, image build ra (có thể chứa appsettings.Development.json thật).
  if [ -f "$ENV_FILE" ] && [ -f "$OVERRIDE_FILE" ]; then
    dc down -v --rmi local --remove-orphans >/dev/null 2>&1
  fi
  docker image rm -f "${PROJECT}-tadss-runtime" >/dev/null 2>&1
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

# ── Điều kiện tiên quyết ──
if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
  echo "LỖI: cần Docker đang chạy." >&2; exit 1
fi
compose_version="$(docker compose version --short 2>/dev/null | sed 's/^v//; s/[^0-9.].*$//')"
if [ -z "$compose_version" ] || [ "$(printf '%s\n%s\n' "$MIN_COMPOSE_VERSION" "$compose_version" | sort -V | head -n1)" != "$MIN_COMPOSE_VERSION" ]; then
  echo "LỖI: cần docker compose >= ${MIN_COMPOSE_VERSION} (có '${compose_version:-không rõ}')." >&2; exit 1
fi
if ! command -v openssl >/dev/null 2>&1; then
  echo "LỖI: cần openssl để sinh khóa ngẫu nhiên." >&2; exit 1
fi

# ── Khóa ngẫu nhiên + biến cho project (không in khóa) ──
API_KEY="$(openssl rand -hex 32)"
API_KEY_SHA256="$(printf %s "$API_KEY" | sha256_hex)"

# ── Override tạm: PostgreSQL + Redis dùng một lần, mạng riêng, không publish cổng (KHÔNG nằm trong file compose đã commit) ──
# Compose cần NGROK_AUTHTOKEN để render service ngrok; token giả, ngrok không bao giờ được khởi động (chỉ `up tadss-runtime grafana`).
cat >"$OVERRIDE_FILE" <<'EOF'
services:
  obs-postgres:
    image: postgres:16.15-alpine
    networks:
      - obs-net
    environment:
      POSTGRES_DB: tadss
      POSTGRES_USER: tadss
      POSTGRES_PASSWORD: tadss_check
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 5s
      timeout: 3s
      retries: 20

  obs-redis:
    image: redis:7.4-alpine
    networks:
      - obs-net
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 20

  tadss-runtime:
    ports: !override []
    networks: !override
      - obs-net
    environment:
      # Biến môi trường ghi đè appsettings.Development.json trong image (có thể chứa secret Neon/Redis thật): container thử CHỈ nối postgres/redis dùng một lần ở trên
      # và chỉ nhận khóa ngẫu nhiên của lần chạy này.
      ConnectionStrings__DefaultConnection: "Host=obs-postgres;Port=5432;Database=tadss;Username=tadss;Password=tadss_check"
      Tadss__Cache__RedisConnectionString: "obs-redis:6379"
      Tadss__Security__ApiKeys__0__ClientId: "smashcourt-be"
      Tadss__Security__ApiKeys__0__KeySha256: "${CHECK_API_KEY_SHA256}"
      # CHỈ cho database dùng một lần ở trên (compose đã commit luôn là "false", không bao giờ tự migrate Neon)
      Tadss__Database__ApplyMigrationsOnStartup: "true"
      # Xuất metric sớm để kiểm trong hạn 60 giây
      Tadss__Telemetry__MetricExportIntervalMs: "2000"
    depends_on:
      obs-postgres:
        condition: service_healthy
      obs-redis:
        condition: service_healthy

  grafana:
    ports: !override []
    networks: !override
      - obs-net

networks:
  obs-net: {}
EOF
printf '%s\n' "CHECK_API_KEY_SHA256=${API_KEY_SHA256}" "NGROK_AUTHTOKEN=fake-token-for-compose-config-only" >"$ENV_FILE"

# ── Dựng stack (tadss-runtime + grafana + PostgreSQL/Redis tạm; không ngrok) ──
echo ">> Dựng project ${PROJECT} (build image + tải otel-lgtm lần đầu, có thể mất vài phút)..."
stack_up="FAIL"
if dc up -d --build --wait tadss-runtime grafana >/dev/null 2>"$ERR_FILE"; then stack_up="PASS"; fi
if [ "$stack_up" != "PASS" ]; then
  echo "LỖI: không dựng được stack:" >&2; tail -n 20 "$ERR_FILE" >&2; dc logs --tail 30 tadss-runtime >&2
fi
rm -f "$ERR_FILE"

NETWORK="${PROJECT}_obs-net"
probe() { docker run --rm --network "$NETWORK" "$CURL_IMAGE" -s -m 20 "$@"; }
code_of() { probe -o /dev/null -w '%{http_code}' "$@"; }

if [ "$stack_up" = PASS ]; then
  # Chờ Grafana sẵn sàng (otel-lgtm khởi động nhiều thành phần): tối đa 180 giây.
  grafana_ready=""
  for _ in $(seq 1 90); do
    grafana_ready="$(code_of "http://grafana:3000/api/health")"
    [ "$grafana_ready" = "200" ] && break
    sleep 2
  done

  # OB-2: dashboard được provisioning nạp (provider quét thư mục mỗi 30 giây nên chờ tối đa 90 giây).
  dash_code=""; dash_body=""
  for _ in $(seq 1 45); do
    dash_body="$(probe -w $'\n%{http_code}' "http://grafana:3000/api/dashboards/uid/${DASHBOARD_UID}")"
    dash_code="${dash_body##*$'\n'}"
    [ "$dash_code" = "200" ] && break
    sleep 2
  done
  has_uid="no"; has_folder="no"
  printf '%s' "$dash_body" | grep -q "\"uid\": *\"${DASHBOARD_UID}\"" && has_uid="yes"
  printf '%s' "$dash_body" | grep -q '"folderTitle": *"TADSS"' && has_folder="yes"
  if [ "$grafana_ready" = 200 ] && [ "$dash_code" = 200 ] && [ "$has_uid" = yes ] && [ "$has_folder" = yes ]; then
    record "OB-2" PASS "Grafana sẵn sàng; dashboard ${DASHBOARD_UID} nạp vào thư mục TADSS (HTTP 200)"
  else
    record "OB-2" FAIL "grafana=${grafana_ready:-?} dashboard=${dash_code:-?} uid=${has_uid} thư-mục-TADSS=${has_folder}"
  fi

  # Gửi các request hợp lệ (mỗi lần một đơn mới) tới tadss-runtime trong mạng riêng.
  ready=""
  for _ in $(seq 1 30); do
    ready="$(code_of "http://tadss-runtime:8080/health/ready")"
    [ "$ready" = "200" ] && break
    sleep 2
  done
  ok_count=0
  for i in $(seq 1 "$REQUESTS"); do
    hex="$(openssl rand -hex 16)"
    reservation_id="${hex:0:8}-${hex:8:4}-4${hex:13:3}-8${hex:17:3}-${hex:20:12}"
    now_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    start_utc="$(date -u -d "+7 days +${i} hours" +%Y-%m-%dT%H:00:00Z)"
    end_utc="$(date -u -d "+7 days +$((i + 2)) hours" +%Y-%m-%dT%H:00:00Z)"
    body="$(sed -e "s/__RESERVATION_ID__/${reservation_id}/" -e "s/__REQUESTED_AT__/${now_utc}/" \
                -e "s/__START_TIME__/${start_utc}/" -e "s/__END_TIME__/${end_utc}/" "$SAMPLE")"
    code="$(code_of -X POST -H 'Content-Type: application/json' -H "X-Api-Key: ${API_KEY}" -H "X-Idempotency-Key: ${reservation_id}" \
              -d "$body" "http://tadss-runtime:8080/api/v2/decisions")"
    [ "$code" = "200" ] && ok_count=$((ok_count + 1))
  done

  # OB-4: Prometheus (qua proxy datasource của Grafana) có tadss_decisions_total > 0 trong hạn.
  metric_total="0"; waited=0
  while [ "$waited" -lt "$METRIC_DEADLINE_SECONDS" ]; do
    result="$(probe -G --data-urlencode 'query=sum(tadss_decisions_total)' "http://grafana:3000/api/datasources/proxy/uid/prometheus/api/v1/query")"
    value="$(printf '%s' "$result" | grep -o '"value":\[[^]]*\]' | head -n1 | sed 's/.*,"\([^"]*\)"\].*/\1/')"
    if [ -n "$value" ] && awk -v v="$value" 'BEGIN { exit !(v + 0 > 0) }'; then metric_total="$value"; break; fi
    sleep 3; waited=$((waited + 3))
  done
  if [ "$ok_count" = "$REQUESTS" ] && awk -v v="$metric_total" 'BEGIN { exit !(v + 0 > 0) }'; then
    record "OB-4" PASS "${ok_count}/${REQUESTS} POST 200; tadss_decisions_total=${metric_total} trên Prometheus sau ${waited}s"
  else
    record "OB-4" FAIL "POST 200: ${ok_count}/${REQUESTS}; tadss_decisions_total=${metric_total} sau ${waited}s (hạn ${METRIC_DEADLINE_SECONDS}s)"
    dc logs --tail 30 tadss-runtime >&2
  fi
else
  record "OB-2" FAIL "không dựng được stack"
  record "OB-4" FAIL "không dựng được stack"
fi

# ── Bảng kết quả ──
echo
echo "Kết quả verify-observability (project ${PROJECT}):"
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
