#!/usr/bin/env bash
# T5.30 / NG-1…NG-5, SEC-6: kiểm tra cấu hình expose của docker-compose.yml bằng Docker thật (không mở tunnel ngrok, không khởi động Grafana, không kết nối database ngoài).
#   NG-1  : docker compose config hợp lệ (với NGROK_AUTHTOKEN giả) và `config --services` liệt kê đủ tadss-runtime, ngrok, grafana (không profile nào);
#           service tadss-runtime: môi trường Development (giống SmashCourt-BE), migration "false", không mount file, không biến ConnectionStrings/RedisConnectionString/ApiKeys,
#           gửi telemetry tới grafana; service grafana: đúng hai mount, đều chỉ đọc (dashboard + provisioning).
#   NG-2a : (danh mục NG-2) /health/ready 200, POST /api/v2/decisions không khóa 401 và có khóa 200 (Swagger được phục vụ ở Development nên không kiểm 404).
#   NG-2b : (danh mục NG-2) môi trường Staging không có khóa thì KHÔNG khởi động (thoát != 0 trong 30 giây, log nêu Tadss:Security, không lộ chuỗi kết nối).
#   NG-3  : truy cập database: GET theo decisionId trả đúng quyết định vừa ghi, và psql đếm được bản ghi.
#   NG-5  : mọi cổng host của file compose đã commit chỉ bind 127.0.0.1 (cấu hình render ra, và cổng đang publish thật).
#   SEC-6 : image chạy bằng user non-root.
# Hermetic: image dựng ra có thể chứa appsettings.Development.json thật của người dùng (secret Neon/Redis; file này được copy vào image như SmashCourt-BE) nhưng script
# KHÔNG đọc nó. Script sinh MỘT FILE OVERRIDE TẠM (thư mục tạm, xóa khi thoát, không bao giờ nằm trong repo) thêm postgres/redis dùng một lần và đặt biến môi trường
# ConnectionStrings__DefaultConnection, Tadss__Cache__RedisConnectionString, Tadss__Security__ApiKeys__0__ClientId/KeySha256 (mã băm của khóa ngẫu nhiên)
# cho tadss-runtime: biến môi trường ghi đè file json nên container thử không bao giờ chạm Neon/Redis thật. Cổng publish đổi sang một cổng trống
# (`ports: !override`) để không tranh cổng 8081 với stack đang chạy. Phần còn lại của file compose đã commit được thử nguyên trạng.
# Không bao giờ khởi động service ngrok hay grafana (chỉ `up tadss-runtime`; hai service đó không phải dependency của tadss-runtime nên compose không tự kéo lên). Chạy trong project compose riêng (tadss-expose-check) nên không đụng project `tadss` của người dùng.
# Image build ra bị xóa khi thoát (có thể chứa secret). Mạng external smashcourt-network chỉ được tạo nếu chưa có và chỉ bị xóa khi thoát nếu chính script này tạo ra.
# Khóa API sinh ngẫu nhiên mỗi lần chạy, không bao giờ được in ra. Chạy được trên Git Bash (Windows) và Ubuntu.
set -uo pipefail

# Git Bash đổi đường dẫn kiểu /dev/null thành đường dẫn Windows khi gọi docker.exe; tắt để tham số tới container nguyên vẹn.
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

cd "$(dirname "$0")/../.." || exit 1

# Hermetic: biến shell của người dùng (khóa, chuỗi kết nối, COMPOSE_*) được compose ưu tiên hơn --env-file nên phải bỏ hết,
# nếu không project kiểm tra có thể dùng nhầm khóa/chuỗi kết nối thật.
for name in $(compgen -e); do
  case "$name" in TADSS_*|NGROK_*|COMPOSE_*) unset "$name" ;; esac
done

PROJECT="tadss-expose-check"
NETWORK="smashcourt-network"  # mạng external cố định của compose; chỉ xóa khi script này tự tạo (NETWORK_CREATED=yes)
NETWORK_CREATED="no"
CURL_IMAGE="curlimages/curl:8.22.0"
MIN_COMPOSE_VERSION="2.24.4"  # cần cho `ports: !override` trong override tạm
SAMPLE="tools/expose/sample-decision-request.json"
NOKEY_CONTAINER="${PROJECT}-nokey"

# Thư mục tạm chứa env + override. docker compose.exe trên Windows không hiểu /tmp của Git Bash nên đổi sang đường dẫn gốc (cygpath -m) khi có.
TMP_DIR="$(mktemp -d)" || { echo "LỖI: không tạo được thư mục tạm." >&2; exit 1; }
if command -v cygpath >/dev/null 2>&1; then TMP_DIR="$(cygpath -m "$TMP_DIR")"; fi
ENV_FILE="${TMP_DIR}/runtime.env"
FAKE_ENV_FILE="${TMP_DIR}/fake.env"
OVERRIDE_FILE="${TMP_DIR}/override.yml"
ERR_FILE="${TMP_DIR}/up.err"

# Cổng host riêng cho override tạm: lấy cổng trống đầu tiên từ 18081 (khác 8081 cố định trong file compose đã commit).
HOST_PORT=18081
while (exec 3<>"/dev/tcp/127.0.0.1/${HOST_PORT}") 2>/dev/null; do HOST_PORT=$((HOST_PORT + 1)); done

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

# Chạy stack thử: file compose đã commit + override tạm.
dc() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" -f docker-compose.yml -f "$OVERRIDE_FILE" "$@"; }
# Chỉ file compose đã commit, với env-file ($1).
dc_main() { local env_file="$1"; shift; docker compose -p "$PROJECT" --env-file "$env_file" -f docker-compose.yml "$@"; }

cleanup() {
  # Chỉ dọn tài nguyên của đúng project này: container, volume, image build ra, và container thăm dò NG-2b.
  docker rm -f "$NOKEY_CONTAINER" >/dev/null 2>&1
  if [ -f "$ENV_FILE" ] && [ -f "$OVERRIDE_FILE" ]; then
    dc down -v --rmi local --remove-orphans >/dev/null 2>&1
  fi
  # Image build ra có thể chứa appsettings.Development.json thật (secret): xóa chắc chắn, kể cả khi `down` không tìm thấy (build dở, env-file chưa ghi).
  docker image rm -f "${PROJECT}-tadss-runtime" >/dev/null 2>&1
  # Mạng external không do compose quản lý; chỉ xóa nếu script này đã tạo (mạng có sẵn của người dùng được giữ nguyên).
  if [ "$NETWORK_CREATED" = yes ]; then docker network rm "$NETWORK" >/dev/null 2>&1; fi
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

# Mạng external smashcourt-network phải tồn tại trước khi up: dùng mạng có sẵn nếu có, nếu không thì tạo và nhớ để xóa khi thoát.
if ! docker network inspect "$NETWORK" >/dev/null 2>&1; then
  if ! docker network create "$NETWORK" >/dev/null 2>&1; then
    echo "LỖI: không tạo được mạng ${NETWORK}." >&2; exit 1
  fi
  NETWORK_CREATED="yes"
fi

# ── Khóa ngẫu nhiên + biến cho project (không in khóa) ──
API_KEY="$(openssl rand -hex 32)"
API_KEY_SHA256="$(printf %s "$API_KEY" | sha256_hex)"
FAKE_NGROK="fake-token-for-compose-config-only"  # compose cần NGROK_AUTHTOKEN để render service ngrok; chỉ dùng thử cấu hình, không bao giờ khởi động ngrok

# ── NG-1: compose config của file đã commit ──
# NGROK_AUTHTOKEN là biến duy nhất file compose cần (ngrok); env-file giả chỉ chứa token giả, đồng thời cô lập khỏi .env của người dùng.
printf '%s\n' "NGROK_AUTHTOKEN=${FAKE_NGROK}" >"$FAKE_ENV_FILE"
# Một lệnh `up` khởi động cả ba service (không profile): config phải hợp lệ và liệt kê đủ tadss-runtime, ngrok, grafana.
cfg="FAIL"
if dc_main "$FAKE_ENV_FILE" config --quiet >/dev/null 2>&1; then cfg="PASS"; fi
services="$(dc_main "$FAKE_ENV_FILE" config --services 2>/dev/null | tr -d '\r' | sort | tr '\n' ' ')"
svc="FAIL"
if [ "$services" = "grafana ngrok tadss-runtime " ]; then svc="PASS"; fi
# Ràng buộc bảo mật áp cho RIÊNG service tadss-runtime (đọc từ cấu hình render dạng YAML, tách khối theo service): môi trường Development (giống SmashCourt-BE),
# migration "false", không mount file, không biến chuỗi kết nối/Redis/ApiKeys. Grafana được mount dashboard chỉ đọc (không chứa secret) và tadss-runtime gửi telemetry tới nó.
rendered_main="$(dc_main "$FAKE_ENV_FILE" config --format json 2>/dev/null)"
service_block() { # $1 tên service; đọc YAML render từ stdin, in các dòng thuộc service đó
  awk -v svc="$1" '$0 == "  " svc ":" { in_block = 1; next } /^  [^ ]/ { in_block = 0 } /^[^ ]/ { in_block = 0 } in_block'
}
rendered_yaml="$(dc_main "$FAKE_ENV_FILE" config 2>/dev/null | tr -d '\r')"
runtime_block="$(printf '%s\n' "$rendered_yaml" | service_block tadss-runtime)"
grafana_block="$(printf '%s\n' "$rendered_yaml" | service_block grafana)"
env_bad=""
printf '%s' "$runtime_block" | grep -q 'ASPNETCORE_ENVIRONMENT: Development' || env_bad="${env_bad}Development "
printf '%s' "$runtime_block" | grep -q 'Tadss__Database__ApplyMigrationsOnStartup: "false"' || env_bad="${env_bad}migration "
if printf '%s' "$runtime_block" | grep -q -e '^    volumes:' -e 'appsettings\.'; then env_bad="${env_bad}có-mount "; fi
if printf '%s' "$runtime_block" | grep -q -e 'ConnectionStrings__' -e 'RedisConnectionString' -e 'ApiKeys'; then env_bad="${env_bad}có-biến-secret "; fi
printf '%s' "$runtime_block" | grep -q 'Tadss__Telemetry__OtlpEndpoint: http://grafana:4317' || env_bad="${env_bad}không-gửi-telemetry-tới-grafana "
# Grafana: đúng hai mount, đều chỉ đọc
grafana_mounts="$(printf '%s\n' "$grafana_block" | grep -c 'type: bind')"
grafana_readonly="$(printf '%s\n' "$grafana_block" | grep -c 'read_only: true')"
if [ "$grafana_mounts" != 2 ] || [ "$grafana_readonly" != 2 ]; then env_bad="${env_bad}grafana-mount(${grafana_mounts}/${grafana_readonly} chỉ-đọc) "; fi
if [ -z "$runtime_block" ] || [ -z "$grafana_block" ]; then env_bad="${env_bad}không-tách-được-khối-service "; fi
if [ "$cfg" = PASS ] && [ "$svc" = PASS ] && [ -z "$env_bad" ]; then
  record "NG-1" PASS "config hợp lệ (NGROK_AUTHTOKEN giả); services: ${services}; tadss-runtime: Development, migration false, không mount, không biến secret, telemetry → grafana; grafana: 2 mount chỉ đọc"
else
  record "NG-1" FAIL "config=${cfg} services=${svc} (${services}); môi-trường-sai: ${env_bad:-không}"
fi

# ── NG-5 (tĩnh): mọi cổng trong cấu hình render ra đều bind 127.0.0.1 ──
rendered="$rendered_main"  # gồm cổng 4040 của ngrok và cổng Grafana/OTLP
cfg_published="$(printf '%s' "$rendered" | grep -c '"published" *:')"
cfg_loopback="$(printf '%s' "$rendered" | grep -c '"host_ip" *: *"127\.0\.0\.1"')"
cfg_ports="FAIL"
if [ "${cfg_published:-0}" -ge 1 ] && [ "$cfg_published" = "$cfg_loopback" ]; then cfg_ports="PASS"; fi

# ── Override tạm: PostgreSQL + Redis dùng một lần (chỉ để chạy thử, KHÔNG nằm trong file compose đã commit) ──
# Tên service có tiền tố expose- để không trùng tên DNS với container khác trên smashcourt-network của người dùng.
cat >"$OVERRIDE_FILE" <<'EOF'
services:
  expose-postgres:
    image: postgres:16.15-alpine
    networks:
      - smashcourt-network
    environment:
      POSTGRES_DB: tadss
      POSTGRES_USER: tadss
      POSTGRES_PASSWORD: tadss_check
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER} -d $${POSTGRES_DB}"]
      interval: 5s
      timeout: 3s
      retries: 20

  expose-redis:
    image: redis:7.4-alpine
    networks:
      - smashcourt-network
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 20

  tadss-runtime:
    # Cổng host trống thay cho 8081 cố định của file đã commit (không tranh cổng với TADSS đang chạy của người dùng); vẫn chỉ bind loopback.
    ports: !override
      - "127.0.0.1:${CHECK_HOST_PORT}:8080"
    environment:
      # Biến môi trường ghi đè appsettings.Development.json trong image (có thể chứa secret Neon/Redis thật): container thử CHỈ nối postgres/redis dùng một lần ở trên
      # và chỉ nhận khóa ngẫu nhiên của lần chạy này (ClientId smashcourt-be + mã băm, không phải khóa thật).
      ConnectionStrings__DefaultConnection: "Host=expose-postgres;Port=5432;Database=tadss;Username=tadss;Password=tadss_check"
      Tadss__Cache__RedisConnectionString: "expose-redis:6379"
      Tadss__Security__ApiKeys__0__ClientId: "smashcourt-be"
      Tadss__Security__ApiKeys__0__KeySha256: "${CHECK_API_KEY_SHA256}"
      # CHỈ cho database dùng một lần ở trên (compose đã commit luôn là "false", không bao giờ tự migrate Neon): cần bật để có schema mà thử.
      Tadss__Database__ApplyMigrationsOnStartup: "true"
    depends_on:
      expose-postgres:
        condition: service_healthy
      expose-redis:
        condition: service_healthy
EOF
# NGROK_AUTHTOKEN giả chỉ để compose nội suy được biến của service ngrok (service này không bao giờ được khởi động).
printf '%s\n' "CHECK_HOST_PORT=${HOST_PORT}" "CHECK_API_KEY_SHA256=${API_KEY_SHA256}" "NGROK_AUTHTOKEN=${FAKE_NGROK}" >"$ENV_FILE"

# ── Dựng stack (chỉ tadss-runtime và postgres/redis tạm; không khởi động ngrok/grafana) ──
echo ">> Dựng project ${PROJECT} (build image, có thể mất vài phút)..."
stack_up="FAIL"
if dc up -d --build --wait tadss-runtime >/dev/null 2>"$ERR_FILE"; then stack_up="PASS"; fi
if [ "$stack_up" != "PASS" ]; then
  echo "LỖI: không dựng được stack:" >&2; tail -n 20 "$ERR_FILE" >&2; dc logs --tail 30 tadss-runtime >&2
fi
rm -f "$ERR_FILE"

probe() { docker run --rm --network "$NETWORK" "$CURL_IMAGE" -s -m 20 "$@"; }
code_of() { probe -o /dev/null -w '%{http_code}' "$@"; }
BASE_URL=""

if [ "$stack_up" = PASS ]; then
  # Gọi thẳng IP của container trên smashcourt-network: tên DNS tadss-runtime có thể trùng với stack `tadss` đang chạy của người dùng.
  rt_ip="$(docker inspect -f '{{(index .NetworkSettings.Networks "smashcourt-network").IPAddress}}' "$(dc ps -q tadss-runtime)" 2>/dev/null)"
  BASE_URL="http://${rt_ip}:8080"

  # /health/ready cần PostgreSQL và Redis; chờ tối đa 60 giây.
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
  rows="$(dc exec -T expose-postgres psql -U tadss -d tadss -tAc 'select count(*) from tadss.decision_executions' 2>/dev/null | tr -d '[:space:]')"

  detail="ready=${ready} không-khóa=${no_key} có-khóa=${with_key}"
  if [ "$ready" = 200 ] && [ "$no_key" = 401 ] && [ "$with_key" = 200 ]; then
    record "NG-2a" PASS "$detail"
  else
    record "NG-2a" FAIL "$detail"
    dc logs --tail 30 tadss-runtime >&2
  fi

  detail="GET=${get_code} bản-ghi=${rows:-?}"
  if [ -n "$decision_id" ] && [ "$get_code" = 200 ] && [ "$get_id" = "$decision_id" ] && [ "${rows:-0}" -ge 1 ] 2>/dev/null; then
    record "NG-3" PASS "$detail"
  else
    record "NG-3" FAIL "$detail"
    dc logs --tail 30 tadss-runtime >&2
  fi

  # ── NG-5 (động): mọi cổng đang publish chỉ ở 127.0.0.1 ──
  ps_json="$(dc ps --format json 2>/dev/null)"
  urls="$(printf '%s' "$ps_json" | grep -o '"URL" *: *"[^"]*"' | sed 's/.*"\([^"]*\)"$/\1/' | sort -u | tr '\n' ' ')"
  published="$(printf '%s' "$ps_json" | grep -o '"PublishedPort" *: *[1-9][0-9]*' | wc -l | tr -d '[:space:]')"
  bad="$(printf '%s' "$urls" | tr ' ' '\n' | grep -v -e '^$' -e '^127\.0\.0\.1$' | tr '\n' ' ')"
  if [ "$cfg_ports" = PASS ] && [ "${published:-0}" -ge 1 ] && [ -z "$bad" ]; then
    record "NG-5" PASS "cấu hình: ${cfg_published} cổng đều 127.0.0.1; đang chạy: ${published} cổng publish, URL: ${urls}"
  else
    record "NG-5" FAIL "cấu hình=${cfg_ports} (${cfg_loopback}/${cfg_published} loopback); published=${published:-0} URL không phải loopback: '${bad}'"
  fi
else
  record "NG-2a" FAIL "không dựng được stack"
  record "NG-3" FAIL "không dựng được stack"
  record "NG-5" FAIL "không dựng được stack (cấu hình tĩnh: ${cfg_ports})"
fi

# ── NG-2b (không khởi động): Staging không có khóa; chuỗi kết nối DB/Redis là host chết nên không bao giờ chạm Neon/Redis thật (image có thể chứa appsettings.Development.json) ──
IMAGE="$(dc images tadss-runtime --format json 2>/dev/null \
          | grep -o '"Repository" *: *"[^"]*"' | head -n1 | sed 's/.*"\([^"]*\)"$/\1/')"
IMAGE="${IMAGE:-${PROJECT}-tadss-runtime}"
if [ "$stack_up" = PASS ] && docker image inspect "$IMAGE" >/dev/null 2>&1; then
  docker rm -f "$NOKEY_CONTAINER" >/dev/null 2>&1
  docker run -d --name "$NOKEY_CONTAINER" -e ASPNETCORE_ENVIRONMENT=Staging \
    -e "ConnectionStrings__DefaultConnection=Host=expose-check-db.invalid;Port=5432;Database=x;Username=u;Password=SENTINEL_NOT_A_SECRET" \
    -e "Tadss__Cache__RedisConnectionString=expose-check-redis.invalid:6379" \
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
    record "NG-2b" PASS "thoát mã ${exit_code} sau ${waited}s; log nêu Tadss:Security, không lộ chuỗi kết nối"
  else
    record "NG-2b" FAIL "trạng thái=${state:-?} mã=${exit_code:-?} sau ${waited}s; nêu-Tadss:Security=${has_section} lộ-chuỗi-kết-nối=${leaks}"
  fi

  # ── SEC-6: user non-root ──
  user="$(docker inspect --format '{{.Config.User}}' "$IMAGE" 2>/dev/null)"
  case "$user" in
    ""|root|0|0:*|root:*) record "SEC-6" FAIL "Config.User='${user}'" ;;
    *) record "SEC-6" PASS "Config.User='${user}'" ;;
  esac
else
  record "NG-2b" FAIL "không có image tadss-runtime để thử"
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
