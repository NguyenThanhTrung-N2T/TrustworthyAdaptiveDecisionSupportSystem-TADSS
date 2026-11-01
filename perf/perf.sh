#!/usr/bin/env bash
# Một lệnh cho môi trường đo hiệu năng (Epic 6C, gói 6C-4). Chạy trong TADSS-Runtime/ (Git Bash trên Windows cũng được).
#   bash perf/perf.sh up [--be TAG] [--tadss TAG] [--ai TAG] [--tadss-off] [--trace]
#       bật bộ quan sát kiểm thử (Grafana http://127.0.0.1:3003) + môi trường perf, chờ mọi service healthy.
#       Tag mặc định = image sha đang chạy ở bản deploy (đo đúng bản đang chạy thật); chưa có bản deploy ⇒ develop/develop/master.
#       --tadss-off: BE không gọi TADSS (cặp đối chứng SP-06). --trace: bật trace 100 % (lượt phân tích, không dùng để đo chính thức).
#   bash perf/perf.sh seed [--seed 42] [--customers 2000]   dựng dữ liệu benchmark + database mẫu (một lần sau up)
#   bash perf/perf.sh reset      khôi phục hai database từ mẫu + xóa Redis + kiểm checksum (trước mỗi lượt đo, P6-46)
#   bash perf/perf.sh status     tag/commit từng service đang chạy
#   bash perf/perf.sh down       tắt môi trường perf và xóa dữ liệu của nó (bộ quan sát giữ nguyên để xem lại kết quả)
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
stack="$root/stack/docker-compose.yml"
obs="$root/docker-compose.yml"
command -v cygpath >/dev/null 2>&1 && { stack="$(cygpath -m "$stack")"; obs="$(cygpath -m "$obs")"; }
services=(be tadss ai)
declare -A deploy_container=([be]=smashcourt-be [tadss]=tadss-runtime [ai]=smashcourt-ai)
declare -A fallback_tag=([be]=develop [tadss]=develop [ai]=master)

deployed_tag() {
  local image
  image="$(docker inspect --format '{{.Config.Image}}' "${deploy_container[$1]}" 2>/dev/null || true)"
  [ -n "$image" ] && echo "${image##*:}" || echo "${fallback_tag[$1]}"
}

show_status() {
  for s in "${services[@]}"; do
    local c="perf-$s" image revision
    image="$(docker inspect --format '{{.Config.Image}}' "$c" 2>/dev/null)" || { echo "  $s: không chạy"; continue; }
    revision="$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$c")"
    echo "  $s: ${image##*:} (commit ${revision:0:7}, $(docker inspect --format '{{.State.Health.Status}}' "$c"))"
  done
}

# MSYS_NO_PATHCONV: Git Bash không đổi đường dẫn trong container (/docker-entrypoint-initdb.d/...) thành đường dẫn Windows
psql_sc() { MSYS_NO_PATHCONV=1 docker exec -i -e PGOPTIONS=--client-min-messages=warning perf-postgres psql -q -v ON_ERROR_STOP=1 -U smashcourt "$@"; }
psql_tadss() { MSYS_NO_PATHCONV=1 docker exec -i -e PGOPTIONS=--client-min-messages=warning perf-tadss-postgres psql -q -v ON_ERROR_STOP=1 -U tadss "$@"; }
# Không dùng database đích làm kết nối khi xóa/tạo lại nó
recreate_db() {  # <psql func> <db> [template]
  "$1" -d postgres -c "DROP DATABASE IF EXISTS $2 WITH (FORCE)"
  "$1" -d postgres -c "CREATE DATABASE $2${3:+ TEMPLATE $3}"
}
# Checksum nội dung các bảng nghiệp vụ (không gồm schema hangfire): cùng dữ liệu ⇒ cùng chuỗi, độc lập thứ tự vật lý
db_checksum() {
  psql_sc -d smashcourt -tA <<'SQL'
SELECT md5(string_agg(t.table_name || ':' || t.digest, ',' ORDER BY t.table_name)) FROM (
  SELECT c.relname AS table_name,
         coalesce((xpath('/table/row/d/text()', query_to_xml(format('SELECT coalesce(md5(string_agg(md5(x::text), '''' ORDER BY md5(x::text))), '''') AS d FROM public.%I x', c.relname), false, false, '')))[1]::text, '') AS digest
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'public' AND c.relkind = 'r') t;
SQL
}
wait_healthy() {  # <container>, tối đa 120 giây
  for _ in $(seq 1 120); do
    [ "$(docker inspect --format '{{.State.Health.Status}}' "$1" 2>/dev/null)" = healthy ] && return 0
    sleep 1
  done
  echo "perf.sh: $1 không healthy sau 120 giây (docker logs $1)" >&2; return 1
}
stop_apps() { docker stop perf-be perf-tadss >/dev/null; }
start_apps() { docker start perf-tadss >/dev/null; wait_healthy perf-tadss; docker start perf-be >/dev/null; wait_healthy perf-be; }
seed_meta="$root/results/perf-seed.json"

cmd="${1:-}"; shift || true
case "$cmd" in
  up)
    BE_TAG="$(deployed_tag be)"; TADSS_TAG="$(deployed_tag tadss)"; AI_TAG="$(deployed_tag ai)"
    PERF_TADSS_DECISION=true; PERF_TRACE_RATIO=0
    while [ $# -gt 0 ]; do
      case "$1" in
        --be) BE_TAG="${2:?--be cần tag}"; shift 2 ;;
        --tadss) TADSS_TAG="${2:?--tadss cần tag}"; shift 2 ;;
        --ai) AI_TAG="${2:?--ai cần tag}"; shift 2 ;;
        --tadss-off) PERF_TADSS_DECISION=false; shift ;;
        --trace) PERF_TRACE_RATIO=1; shift ;;
        *) echo "perf.sh: không hiểu '$1'" >&2; exit 2 ;;
      esac
    done
    export BE_TAG TADSS_TAG AI_TAG PERF_TADSS_DECISION PERF_TRACE_RATIO
    echo "perf.sh: bộ quan sát kiểm thử"
    docker compose -f "$obs" up -d --wait
    echo "perf.sh: môi trường perf (be ${BE_TAG}, tadss ${TADSS_TAG}, ai ${AI_TAG}, TADSS $([ "$PERF_TADSS_DECISION" = true ] && echo bật || echo tắt), trace ${PERF_TRACE_RATIO})"
    docker compose -f "$stack" pull --quiet be tadss ai
    docker compose -f "$stack" up -d --wait
    show_status
    echo "perf.sh: BE http://127.0.0.1:18080 · Mailpit http://127.0.0.1:18025 · Grafana \"Kiểm thử\" http://127.0.0.1:3003"
    ;;
  seed)
    # Dựng database mẫu một lần (sau `up`): SmashCourt = schema production + dữ liệu benchmark; TADSS = database đã migrate, chưa có quyết định
    # Mặc định phân bố tham chiếu rút từ 215 booking (task 2.17); placeholder chỉ khi chưa có file đó
    distribution="$root/stack/distribution.reference.json"; [ -f "$distribution" ] || distribution="$root/stack/distribution.placeholder.json"
    seed=42; customers=2000
    while [ $# -gt 0 ]; do
      case "$1" in
        --seed) seed="${2:?}"; shift 2 ;;
        --customers) customers="${2:?}"; shift 2 ;;
        --distribution) distribution="${2:?}"; shift 2 ;;
        *) echo "perf.sh: không hiểu '$1'" >&2; exit 2 ;;
      esac
    done
    mkdir -p "$root/results"
    sql="$root/results/perf-seed-${seed}.sql"
    python "$root/stack/datagen.py" --seed "$seed" --customers "$customers" --distribution "$distribution" > "$sql"
    sql_sha="$(sha256sum "$sql" | cut -d' ' -f1)"
    stop_apps
    echo "perf.sh: nạp schema + dữ liệu (seed ${seed}, ${customers} khách)"
    recreate_db psql_sc smashcourt
    psql_sc -d smashcourt -f /docker-entrypoint-initdb.d/10-schema.sql >/dev/null
    psql_sc -d smashcourt -f /docker-entrypoint-initdb.d/20-drop-hangfire.sql >/dev/null
    psql_sc -d smashcourt < "$sql"
    checksum="$(db_checksum)"
    recreate_db psql_sc smashcourt_seed smashcourt
    # TADSS: database trống ⇒ TADSS tự migrate khi khởi động ⇒ dừng ⇒ chụp làm mẫu
    recreate_db psql_tadss tadss
    docker start perf-tadss >/dev/null; wait_healthy perf-tadss; docker stop perf-tadss >/dev/null
    recreate_db psql_tadss tadss_seed tadss
    printf '{"seed": %s, "customers": %s, "distribution": "%s", "sqlSha256": "%s", "dbChecksum": "%s", "anchor": "%s"}\n' \
      "$seed" "$customers" "$(basename "$distribution")" "$sql_sha" "$checksum" "$(head -2 "$sql" | grep -o 'anchor=[0-9-]*' | cut -d= -f2)" > "$seed_meta"
    docker exec perf-redis redis-cli FLUSHALL >/dev/null
    start_apps
    echo "perf.sh: xong; checksum ${checksum} (ghi ở perf/results/perf-seed.json)"
    ;;
  reset)
    # Trước mỗi lượt đo (P6-46): chép lại hai database từ mẫu, xóa Redis, kiểm checksum khớp lúc seed
    [ -f "$seed_meta" ] || { echo "perf.sh: chưa seed (bash perf/perf.sh seed)" >&2; exit 1; }
    started=$(date +%s)
    stop_apps
    recreate_db psql_sc smashcourt smashcourt_seed
    recreate_db psql_tadss tadss tadss_seed
    docker exec perf-redis redis-cli FLUSHALL >/dev/null
    expected="$(grep -o '"dbChecksum": "[^"]*"' "$seed_meta" | cut -d'"' -f4)"
    actual="$(db_checksum)"
    [ "$actual" = "$expected" ] || { echo "perf.sh: checksum lệch (mẫu ${expected}, sau khôi phục ${actual})" >&2; exit 1; }
    start_apps
    echo "perf.sh: đã khôi phục trong $(( $(date +%s) - started )) giây, checksum khớp ${actual}"
    ;;
  run)
    # Một lượt k6 (task 2.15; kịch bản SP ở gói 6C-5): khôi phục dữ liệu ⇒ ghi metadata ⇒ k6 ⇒ (có --code) nạp lên dashboard "Kiểm thử"
    profile="${1:?Cách dùng: perf.sh run <smoke|load|stress|spike|soak|breakpoint|closed> [--rate N] [--duration 15m] [--vus N] [--code SP-01] [--limited LÝ_DO] [--no-reset]}"; shift
    code=""; limited=""; do_reset=1; k6_env=(-e "PROFILE=$profile")
    while [ $# -gt 0 ]; do
      case "$1" in
        --rate) k6_env+=(-e "RATE=${2:?}"); shift 2 ;;
        --max-rate) k6_env+=(-e "MAX_RATE=${2:?}"); shift 2 ;;
        --duration) k6_env+=(-e "DURATION=${2:?}"); shift 2 ;;
        --warmup) k6_env+=(-e "WARMUP=${2:?}"); shift 2 ;;
        --vus) k6_env+=(-e "VUS=${2:?}"); shift 2 ;;
        --max-vus) k6_env+=(-e "MAX_VUS=${2:?}"); shift 2 ;;
        --think) k6_env+=(-e "THINK_SCALE=${2:?}"); shift 2 ;;
        --seed) k6_env+=(-e "SEED=${2:?}"); shift 2 ;;
        --code) code="${2:?}"; shift 2 ;;
        --limited) limited="${2:?}"; shift 2 ;;
        --no-reset) do_reset=0; shift ;;
        *) echo "perf.sh: không hiểu '$1'" >&2; exit 2 ;;
      esac
    done
    [ "$profile" = smoke ] && k6_env+=(-e "THINK_SCALE=${THINK_SCALE:-0.05}")
    [ "$do_reset" = 1 ] && "$0" reset
    run_id="$(date +%Y%m%d-%H%M%S)-${profile}${code:+-$code}"
    out="$root/results/k6/$run_id"; mkdir -p "$out"
    customers="$(grep -o '"customers": [0-9]*' "$seed_meta" | grep -o '[0-9]*$')"
    {
      echo "{"
      echo "  \"runId\": \"$run_id\", \"profile\": \"$profile\", \"code\": \"${code}\", \"args\": \"${k6_env[*]}\","
      for s in "${services[@]}"; do
        echo "  \"$s\": {\"image\": \"$(docker inspect --format '{{.Config.Image}}' "perf-$s")\", \"revision\": \"$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "perf-$s")\"},"
      done
      echo "  \"tadssDecision\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | sed -n 's/^TadssConfig__EnableTadssDecision=//p')\","
      echo "  \"traceRatio\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | sed -n 's/^Telemetry__TraceSampleRatio=//p')\","
      echo "  \"seed\": $(cat "$seed_meta"),"
      echo "  \"host\": {\"cpus\": $(docker info --format '{{.NCPU}}'), \"memoryBytes\": $(docker info --format '{{.MemTotal}}')},"
      echo "  \"startedAt\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\""
      echo "}"
    } > "$out/metadata.json"
    echo "perf.sh: lượt ${run_id} (kết quả: perf/results/k6/${run_id}/)"
    set +e
    BE_TAG=- TADSS_TAG=- AI_TAG=- MSYS_NO_PATHCONV=1 docker compose -f "$stack" --profile k6 run --rm --no-deps "${k6_env[@]}" -e "RUN_ID=$run_id" \
      -e "CUSTOMERS=${customers:-2000}" -e "DISTRIBUTION=$(grep -o '"distribution": "[^"]*"' "$seed_meta" | cut -d'"' -f4)" k6 run --quiet --out opentelemetry /scripts/mix.js
    k6_exit=$?
    set -e
    echo "perf.sh: k6 thoát ${k6_exit} (0 = mọi ngưỡng đạt, 99 = có ngưỡng hỏng)"
    # Bảng tài nguyên theo phút + resources.json (đợi lần đẩy số liệu cuối 5 giây của app/k6 tới Prometheus)
    sleep 10
    [ -f "$out/summary.json" ] && { python "$root/stack/analyze.py" "$out" || echo "perf.sh: không phân tích được lượt (Prometheus?)" >&2; }
    if [ -n "$code" ]; then
      python "$root/board.py" k6 "$code" "$out/summary.json" ${limited:+--limited "$limited"}
    fi
    exit "$k6_exit"
    ;;
  status) show_status ;;
  # Tag chỉ cần cho up; compose vẫn đòi biến khi đọc file
  down) BE_TAG=- TADSS_TAG=- AI_TAG=- docker compose -f "$stack" down -v --remove-orphans ;;
  *) sed -n '2,11p' "$0"; exit 2 ;;
esac
