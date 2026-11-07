#!/usr/bin/env bash
# Một lệnh cho môi trường đo hiệu năng (Epic 6C, gói 6C-4). Chạy trong TADSS-Runtime/ (Git Bash trên Windows cũng được).
#   bash perf/perf.sh up [--be TAG] [--tadss TAG] [--ai TAG] [--tadss-off] [--trace] [--pg-locks on|off] [--db-pool N] [--db-prepare N]
#       [--cpu-layout physical|legacy] [--be-cpus N] [--telemetry-off] [--llm-delay MS] [--llm-jitter MS] [--faults]
#       bật bộ quan sát kiểm thử (Grafana http://127.0.0.1:3003) + môi trường perf, chờ mọi service healthy.
#       Tag mặc định = image sha đang chạy ở bản deploy (đo đúng bản đang chạy thật); chưa có bản deploy ⇒ develop/develop/master.
#       --tadss-off: BE không gọi TADSS (cặp đối chứng SP-06). --trace: bật trace 100 % (lượt phân tích, không dùng để đo chính thức).
#       --pg-locks on: bật lại collector pg_locks của Alloy (mặc định tắt từ 03/11, chỉ cho lượt đối chứng E1). --db-pool N: Maximum Pool Size
#       của BE (mặc định 100; thí nghiệm E3). --db-prepare N: Database:MaxAutoPrepare của BE (P6-54; mặc định 0 = như deploy, lượt đánh giá 50).
#       --cpu-layout: cpuset theo core vật lý (mặc định, 05/11) hoặc legacy 0-4 / 5-7 (so với lượt cũ). --be-cpus N: trần CPU của BE (mặc định 2).
#       --telemetry-off: tắt OpenTelemetry của BE, TADSS, AI (cặp đối chứng SP-07). --llm-delay / --llm-jitter: độ trễ LLM giả
#       (mặc định 800 / 200 ms; SP-10A dùng 0 / 0). --faults: BE gọi TADSS qua Toxiproxy (SP-09; run --fault stop-tadss|latency|reset).
#       Mỗi lần up đặt lại mọi núm về giá trị đã cho hoặc mặc định.
#   bash perf/perf.sh seed [--seed 42] [--customers 2000]   dựng dữ liệu benchmark + database mẫu (một lần sau up)
#   bash perf/perf.sh reset      khôi phục hai database từ mẫu + xóa Redis + kiểm checksum (trước mỗi lượt đo, P6-46)
#   bash perf/perf.sh run <profile> [--rate N] [--duration 15m] ... [--code SP-xx] [--selftest] [--cooldown GIÂY] [--allow-battery]
#       một lượt k6: khôi phục dữ liệu ⇒ k6 (mẫu thô raw.csv.gz) ⇒ analyze.py (resources.json + verdict.json theo SLO đã chốt, task 2.18).
#       --selftest: ngưỡng p95 Đọc 1 ms để kiểm k6 + verdict báo KHÔNG ĐẠT đúng (chỉ load/soak/closed, không nạp lên dashboard)
#       --pg-sample activity|locks: lấy mẫu PostgreSQL suốt lượt (stack/pgsampler.py; locks CHỈ cho lượt chẩn đoán). --tag TÊN: hậu tố mã lượt
#       (diag, E1, E2, E3…). Trước lượt: hostcheck preflight (cắm sạc, RAM VM Docker, đĩa; dừng nếu không an toàn; --allow-battery bỏ qua sạc),
#       --cooldown: nghỉ trước k6 (laptop nguội). Trong lượt: xung nhịp CPU Windows mỗi 5 s ⇒ cpu-clock.json.
#   bash perf/perf.sh pool <lượt> <lượt> <lượt>   p95/p99 trên mẫu thô ghép của các lượt cùng cấu hình (SP-01 ×3)
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
# B.7.2 + 05/11: k6 chạy cùng máy ⇒ tách CPU của VM Docker THEO CORE VẬT LÝ (perf/stack/hostcheck.py): k6 + bộ quan sát nhận core cuối (đủ hai luồng),
# hệ thống nhận các core còn lại (máy đo 4 core × 2 luồng ⇒ 0-5 / 6-7). --cpu-layout legacy giữ bố cục cũ 0-4 / 5-7 (luồng 4 và 5 cùng một core)
# để so với các lượt trước 05/11 (cặp trước/sau của P6-54). Compose đọc PERF_CPUSET_*.
set_cpusets() {
  local n layout; n="$(docker info --format '{{.NCPU}}')"
  if [ "$n" -ge 6 ]; then
    layout="$(python "$root/stack/hostcheck.py" layout "${1:-physical}")"
    PERF_CPUSET_SYSTEM="${layout% *}"; PERF_CPUSET_TOOLS="${layout#* }"
  else
    PERF_CPUSET_SYSTEM="0-$((n - 1))"; PERF_CPUSET_TOOLS="$PERF_CPUSET_SYSTEM"
    echo "perf.sh: VM Docker chỉ có $n CPU, không tách được CPU hệ thống / k6 (ghi ở metadata)" >&2
  fi
  export PERF_CPUSET_SYSTEM PERF_CPUSET_TOOLS
}
# run dùng đúng bố cục up đã đặt: đọc từ container đang chạy, không tính lại
read_cpusets() {
  PERF_CPUSET_SYSTEM="$(docker inspect --format '{{.HostConfig.CpusetCpus}}' perf-be)"
  PERF_CPUSET_TOOLS="$(docker inspect --format '{{.HostConfig.CpusetCpus}}' smashcourt-test-prometheus)"
  export PERF_CPUSET_SYSTEM PERF_CPUSET_TOOLS
}
count_cpus() { python -c "import sys; print(sum(int(b) - int(a) + 1 for a, _, b in (p.partition('-') if '-' in p else (p, '', p) for p in sys.argv[1].split(','))))" "$1"; }
system_containers=(perf-be perf-tadss perf-ai perf-postgres perf-tadss-postgres perf-redis perf-mailpit perf-fake-llm)
stop_apps() { docker stop perf-be perf-tadss >/dev/null; }
start_apps() { docker start perf-tadss >/dev/null; wait_healthy perf-tadss; docker start perf-be >/dev/null; wait_healthy perf-be; }
seed_meta="$root/results/perf-seed.json"

cmd="${1:-}"; shift || true
case "$cmd" in
  up)
    BE_TAG="$(deployed_tag be)"; TADSS_TAG="$(deployed_tag tadss)"; AI_TAG="$(deployed_tag ai)"
    PERF_TADSS_DECISION=true; PERF_TRACE_RATIO=0; PERF_PG_EXTRA_COLLECTORS='[]'; PERF_DB_POOL=100; PERF_DB_AUTO_PREPARE=0; PERF_BE_CPUS=2; cpu_layout=physical
    PERF_TELEMETRY=true; FAKE_LLM_DELAY_MS=800; FAKE_LLM_JITTER_MS=200; PERF_TADSS_URL=http://tadss:8080; faults_profile=()
    while [ $# -gt 0 ]; do
      case "$1" in
        --be) BE_TAG="${2:?--be cần tag}"; shift 2 ;;
        --tadss) TADSS_TAG="${2:?--tadss cần tag}"; shift 2 ;;
        --ai) AI_TAG="${2:?--ai cần tag}"; shift 2 ;;
        --tadss-off) PERF_TADSS_DECISION=false; shift ;;
        --trace) PERF_TRACE_RATIO=1; shift ;;
        --pg-locks)
          case "${2:-}" in on) PERF_PG_EXTRA_COLLECTORS='["locks"]' ;; off) PERF_PG_EXTRA_COLLECTORS='[]' ;;
            *) echo "perf.sh: --pg-locks on|off" >&2; exit 2 ;; esac; shift 2 ;;
        --db-pool) PERF_DB_POOL="${2:?--db-pool cần số}"; shift 2 ;;
        --db-prepare) PERF_DB_AUTO_PREPARE="${2:?--db-prepare cần số}"; shift 2 ;;
        --be-cpus) PERF_BE_CPUS="${2:?--be-cpus cần số}"; shift 2 ;;
        --telemetry-off) PERF_TELEMETRY=false; shift ;;
        --faults) PERF_TADSS_URL=http://toxiproxy:8666; faults_profile=(--profile faults); shift ;;
        --llm-delay) FAKE_LLM_DELAY_MS="${2:?--llm-delay cần số ms}"; shift 2 ;;
        --llm-jitter) FAKE_LLM_JITTER_MS="${2:?--llm-jitter cần số ms}"; shift 2 ;;
        --cpu-layout)
          case "${2:-}" in physical|legacy) cpu_layout="$2" ;; *) echo "perf.sh: --cpu-layout physical|legacy" >&2; exit 2 ;; esac; shift 2 ;;
        *) echo "perf.sh: không hiểu '$1'" >&2; exit 2 ;;
      esac
    done
    export BE_TAG TADSS_TAG AI_TAG PERF_TADSS_DECISION PERF_TRACE_RATIO PERF_PG_EXTRA_COLLECTORS PERF_DB_POOL PERF_DB_AUTO_PREPARE PERF_BE_CPUS
    export PERF_TELEMETRY FAKE_LLM_DELAY_MS FAKE_LLM_JITTER_MS PERF_TADSS_URL
    set_cpusets "$cpu_layout"
    # Trần CPU của BE không được vượt số luồng của nhóm hệ thống; sát trần thì PostgreSQL, TADSS, AI phải tranh CPU ⇒ đọc kết quả thận trọng
    system_threads="$(count_cpus "$PERF_CPUSET_SYSTEM")"
    python -c "import sys; v = float(sys.argv[1]); sys.exit(0 if 0 < v <= int(sys.argv[2]) else 1)" "$PERF_BE_CPUS" "$system_threads"       || { echo "perf.sh: --be-cpus phải trong (0, ${system_threads}] (số luồng CPU hệ thống ${PERF_CPUSET_SYSTEM})" >&2; exit 2; }
    python -c "import sys; sys.exit(0 if float(sys.argv[1]) <= int(sys.argv[2]) - 2 else 1)" "$PERF_BE_CPUS" "$system_threads"       || echo "perf.sh: CẢNH BÁO BE ${PERF_BE_CPUS} CPU trên ${system_threads} luồng hệ thống: PostgreSQL, TADSS, AI tranh CPU, kết quả cần đọc thận trọng" >&2
    echo "perf.sh: bộ quan sát kiểm thử (CPU ${PERF_CPUSET_TOOLS}, dùng chung với k6; collector pg_locks $([ "$PERF_PG_EXTRA_COLLECTORS" = '[]' ] && echo tắt || echo bật))"
    docker compose -f "$obs" up -d --wait
    # Bộ quan sát dùng cho cả dashboard ngoài lượt đo nên compose của nó không ghim CPU; ghim lúc bật cùng perf
    docker update --cpuset-cpus "$PERF_CPUSET_TOOLS" $(docker compose -f "$obs" ps -q) >/dev/null
    echo "perf.sh: môi trường perf (be ${BE_TAG}, tadss ${TADSS_TAG}, ai ${AI_TAG}, TADSS $([ "$PERF_TADSS_DECISION" = true ] && echo bật || echo tắt), trace ${PERF_TRACE_RATIO}, pool ${PERF_DB_POOL}, auto prepare ${PERF_DB_AUTO_PREPARE}, BE ${PERF_BE_CPUS} CPU, telemetry $([ "$PERF_TELEMETRY" = true ] && echo bật || echo tắt), LLM giả ${FAKE_LLM_DELAY_MS}±${FAKE_LLM_JITTER_MS} ms, CPU hệ thống ${PERF_CPUSET_SYSTEM} / công cụ ${PERF_CPUSET_TOOLS} (${cpu_layout}))"
    docker compose -f "$stack" pull --quiet be tadss ai
    docker compose -f "$stack" "${faults_profile[@]}" up -d --wait
    [ "${#faults_profile[@]}" = 0 ] && docker stop perf-toxiproxy >/dev/null 2>&1 || true   # không --faults: BE gọi thẳng TADSS
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
    profile="${1:?Cách dùng: perf.sh run <smoke|load|stress|spike|soak|breakpoint|closed|ai> [--rate N] [--duration 15m] [--vus N] [--code SP-01] [--limited LÝ_DO] [--no-reset] [--selftest]}"; shift
    code=""; limited=""; do_reset=1; selftest=0; pg_sample=""; tag=""; cooldown=0; allow_battery=""; fault=""; k6_env=(-e "PROFILE=$profile")
    while [ $# -gt 0 ]; do
      case "$1" in
        --rate) k6_env+=(-e "RATE=${2:?}"); shift 2 ;;
        --max-rate) k6_env+=(-e "MAX_RATE=${2:?}"); shift 2 ;;
        --duration) k6_env+=(-e "DURATION=${2:?}"); shift 2 ;;
        --warmup) k6_env+=(-e "WARMUP=${2:?}"); shift 2 ;;
        --vus) k6_env+=(-e "VUS=${2:?}"); shift 2 ;;
        --max-vus) k6_env+=(-e "MAX_VUS=${2:?}"); shift 2 ;;
        --start-rate) k6_env+=(-e "START_RATE=${2:?}"); shift 2 ;;
        --step-rate) k6_env+=(-e "STEP_RATE=${2:?}"); shift 2 ;;
        --step) k6_env+=(-e "STEP=${2:?}"); shift 2 ;;
        --journey-seconds) k6_env+=(-e "JOURNEY_SECONDS=${2:?}"); shift 2 ;;
        --selftest) selftest=1; k6_env+=(-e "SLO_SELFTEST=1"); shift ;;
        --think) k6_env+=(-e "THINK_SCALE=${2:?}"); shift 2 ;;
        --seed) k6_env+=(-e "SEED=${2:?}"); shift 2 ;;
        --code) code="${2:?}"; shift 2 ;;
        --limited) limited="${2:?}"; shift 2 ;;
        --no-reset) do_reset=0; shift ;;
        --pg-sample)
          case "${2:-}" in activity|locks) pg_sample="$2" ;; *) echo "perf.sh: --pg-sample activity|locks" >&2; exit 2 ;; esac; shift 2 ;;
        --tag) tag="${2:?--tag cần tên}"; shift 2 ;;
        --cooldown) cooldown="${2:?--cooldown cần số giây}"; shift 2 ;;
        --allow-battery) allow_battery="--allow-battery"; shift ;;
        --fault)
          case "${2:-}" in stop-tadss|latency|reset) fault="$2" ;; *) echo "perf.sh: --fault stop-tadss|latency|reset" >&2; exit 2 ;; esac; shift 2 ;;
        *) echo "perf.sh: không hiểu '$1'" >&2; exit 2 ;;
      esac
    done
    # Đọc pg_locks khóa chính bộ quản lý khóa (plan 6C 2C): không bao giờ trong lượt nạp dashboard
    [ "$pg_sample" = locks ] && [ -n "$code" ] && { echo "perf.sh: --pg-sample locks chỉ cho lượt chẩn đoán, bỏ --code" >&2; exit 2; }
    if [ "$selftest" = 1 ]; then
      [ -z "$code" ] || { echo "perf.sh: lượt --selftest không nạp lên dashboard, bỏ --code" >&2; exit 2; }
      case "$profile" in load|soak|closed) ;; *) echo "perf.sh: --selftest chỉ cho load/soak/closed (profile có threshold SLO)" >&2; exit 2 ;; esac
    fi
    [ "$profile" = smoke ] && k6_env+=(-e "THINK_SCALE=${THINK_SCALE:-0.05}")
    # SP-09: lỗi tiêm qua Toxiproxy cần BE đang gọi TADSS qua proxy (up --faults); stop-tadss cũng chạy sau proxy cho cùng đường đi
    if [ -n "$fault" ]; then
      [ "$profile" = load ] || { echo "perf.sh: --fault chỉ cho profile load (SP-09)" >&2; exit 2; }
      docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | grep -q '^TadssConfig__BaseUrl=http://toxiproxy' \
        || { echo "perf.sh: --fault cần perf.sh up --faults trước" >&2; exit 2; }
    fi
    # Máy đo là laptop (05/11): chạy pin, thiếu RAM VM Docker (bản deploy còn chạy) hoặc thiếu đĩa thì dừng trước khi động vào dữ liệu
    host_check="$(python "$root/stack/hostcheck.py" preflight $allow_battery)" || { echo "perf.sh: máy chưa sẵn sàng để đo (xem DỪNG ở trên)" >&2; exit 1; }
    [ "$do_reset" = 1 ] && "$0" reset
    read_cpusets
    if [ "$cooldown" != 0 ]; then echo "perf.sh: nghỉ ${cooldown} giây cho máy nguội trước k6"; sleep "$cooldown"; fi
    suffix="${tag:+-$tag}"; [ "$selftest" = 1 ] && suffix="${suffix}-selftest"
    run_id="$(date +%Y%m%d-%H%M%S)-${profile}${code:+-$code}${suffix}"
    out="$root/results/k6/$run_id"; mkdir -p "$out"
    customers="$(grep -o '"customers": [0-9]*' "$seed_meta" | grep -o '[0-9]*$')"
    repo="$(cd "$root/.." && pwd)"
    {
      echo "{"
      echo "  \"runId\": \"$run_id\", \"profile\": \"$profile\", \"code\": \"${code}\", \"selftest\": $([ "$selftest" = 1 ] && echo true || echo false), \"args\": \"${k6_env[*]}\","
      for s in "${services[@]}"; do
        echo "  \"$s\": {\"image\": \"$(docker inspect --format '{{.Config.Image}}' "perf-$s")\", \"revision\": \"$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "perf-$s")\"},"
      done
      # Tái lập (B.7.2): commit + trạng thái sửa dở của script đo, SHA-256 script k6 / slo.json / compose, CPU và trần từng container
      echo "  \"tadssRuntime\": {\"commit\": \"$(git -C "$repo" rev-parse HEAD 2>/dev/null)\", \"dirty\": $([ -n "$(git -C "$repo" status --porcelain -- perf 2>/dev/null)" ] && echo true || echo false)},"
      echo "  \"sha256\": {\"k6\": \"$(cat "$root"/k6/mix.js "$root"/k6/lib/*.js "$root"/k6/slo.json | sha256sum | cut -d' ' -f1)\", \"sloJson\": \"$(sha256sum "$root/k6/slo.json" | cut -d' ' -f1)\", \"compose\": \"$(sha256sum "$root/stack/docker-compose.yml" | cut -d' ' -f1)\"},"
      echo "  \"cpuset\": {\"system\": \"$PERF_CPUSET_SYSTEM\", \"tools\": \"$PERF_CPUSET_TOOLS\"},"
      # Luồng → core vật lý như VM Docker thấy (WSL2 không ghim vCPU vào luồng thật của Windows); nguồn điện, chế độ nguồn, RAM, đĩa lúc bắt đầu
      echo "  \"cpuTopology\": $(python "$root/stack/hostcheck.py" topology), \"cpuTopologyNote\": \"cấu trúc trong VM Docker; WSL2 không ghim vCPU vào luồng vật lý\","
      echo "  \"beCpus\": $(docker inspect --format '{{.HostConfig.NanoCpus}}' perf-be | awk '{print $1 / 1e9}'), \"hostCheck\": ${host_check}, \"cooldownSeconds\": ${cooldown},"
      echo "  \"containers\": {$(for c in "${system_containers[@]}"; do docker inspect --format "\"$c\": {\"cpuset\": \"{{.HostConfig.CpusetCpus}}\", \"nanoCpus\": {{.HostConfig.NanoCpus}}, \"memoryBytes\": {{.HostConfig.Memory}}}" "$c"; done | paste -sd, -)},"
      echo "  \"tadssDecision\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | sed -n 's/^TadssConfig__EnableTadssDecision=//p')\","
      echo "  \"telemetry\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | sed -n 's/^Telemetry__Enabled=//p')\","
      echo "  \"fakeLlmDelayMs\": $(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-fake-llm | sed -n 's/^FAKE_LLM_DELAY_MS=//p'), \"fakeLlmJitterMs\": $(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-fake-llm | sed -n 's/^FAKE_LLM_JITTER_MS=//p'),"
      echo "  \"traceRatio\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | sed -n 's/^Telemetry__TraceSampleRatio=//p')\","
      # Yếu tố của thí nghiệm E1–E4 (plan 6C 2C): đọc từ container đang chạy, không từ tham số
      echo "  \"tag\": \"${tag}\", \"pgSample\": \"${pg_sample:-none}\", \"fault\": \"${fault}\","
      echo "  \"dbPool\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | grep -o 'Maximum Pool Size=[0-9]*' | cut -d= -f2)\","
      echo "  \"dbAutoPrepare\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' perf-be | sed -n 's/^Database__MaxAutoPrepare=//p')\","
      echo "  \"pgLocksCollector\": \"$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' smashcourt-test-alloy | grep -q '^PERF_PG_EXTRA_COLLECTORS=.*locks' && echo on || echo off)\","
      echo "  \"seed\": $(cat "$seed_meta"),"
      echo "  \"host\": {\"cpus\": $(docker info --format '{{.NCPU}}'), \"memoryBytes\": $(docker info --format '{{.MemTotal}}')},"
      echo "  \"startedAt\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\""
      echo "}"
    } > "$out/metadata.json"
    echo "perf.sh: lượt ${run_id} (kết quả: perf/results/k6/${run_id}/)"
    docker rm -f perf-k6 >/dev/null 2>&1 || true   # container còn lại của lượt bị ngắt trước đó
    sampler=""
    if [ -n "$pg_sample" ]; then
      python "$root/stack/pgsampler.py" "$out" --mode "$pg_sample" --cpuset "$PERF_CPUSET_TOOLS" &
      sampler=$!
      for _ in $(seq 1 60); do [ -f "$out/.pgsampler-ready" ] && break; sleep 1; done
      [ -f "$out/.pgsampler-ready" ] || { echo "perf.sh: pgsampler không khởi động được" >&2; exit 1; }
    fi
    python "$root/stack/hostcheck.py" clock-start "$out"
    injector=""
    if [ -n "$fault" ]; then   # warm-up 120 s + chuyển 10 s + settle 60 s rồi 5 phút bình thường ⇒ lỗi 5 phút ⇒ hồi phục (mốc thật ở faults.json)
      python "$root/stack/faults.py" inject "$out" --kind "$fault" --start-after 490 --duration 300 &
      injector=$!
    fi
    set +e
    # --name perf-k6: cAdvisor ghi CPU/RAM của k6 theo tên cố định (kiểm k6 không thiếu tài nguyên, B.7.2); csv = mẫu thô để chấm SLO chính xác
    BE_TAG=- TADSS_TAG=- AI_TAG=- MSYS_NO_PATHCONV=1 docker compose -f "$stack" --profile k6 run --rm --no-deps --name perf-k6 "${k6_env[@]}" \
      -e "RUN_ID=$run_id" -e "CUSTOMERS=${customers:-2000}" -e "DISTRIBUTION=$(grep -o '"distribution": "[^"]*"' "$seed_meta" | cut -d'"' -f4)" \
      k6 run --quiet --out opentelemetry --out "csv=/results/$run_id/raw.csv.gz" /scripts/mix.js
    k6_exit=$?
    if [ -n "$sampler" ]; then   # dừng bằng file (kill từ Git Bash không gửi tín hiệu êm cho tiến trình Windows)
      touch "$out/.stop-pgsampler"; wait "$sampler" || echo "perf.sh: pgsampler thoát lỗi (xem pg-samples.jsonl)" >&2
    fi
    if [ -n "$injector" ]; then
      wait "$injector" || echo "perf.sh: faults.py inject thoát lỗi (xem faults.json)" >&2
      python "$root/stack/faults.py" check "$out" || echo "perf.sh: kiểm DB sau lỗi không đạt (xem faults.json)" >&2
    fi
    set -e
    echo "$k6_exit" > "$out/k6-exit"
    python "$root/stack/hostcheck.py" clock-summary "$out" > /dev/null || echo "perf.sh: không tóm tắt được xung nhịp CPU" >&2
    echo "perf.sh: k6 thoát ${k6_exit} (0 = mọi ngưỡng đạt, 99 = có ngưỡng hỏng, khác = dừng giữa chừng ⇒ INCOMPLETE)"
    # Bảng tài nguyên theo phút + resources.json, rồi verdict.json (đợi lần đẩy số liệu cuối 5 giây của app/k6 tới Prometheus)
    sleep 10
    [ -f "$out/summary.json" ] && { python "$root/stack/analyze.py" "$out" || echo "perf.sh: không phân tích được lượt (Prometheus?)" >&2; }
    if [ -n "$code" ]; then
      python "$root/board.py" k6 "$code" "$out/summary.json" ${limited:+--limited "$limited"}
    fi
    exit "$k6_exit"
    ;;
  pool)
    # Mẫu thô ghép của các lượt cùng cấu hình (B.7.2): nhận mã lượt hoặc đường dẫn
    runs=()
    for run in "$@"; do [ -d "$run" ] && runs+=("$run") || runs+=("$root/results/k6/$run"); done
    [ "${#runs[@]}" -ge 2 ] || { echo "perf.sh: pool cần ít nhất 2 lượt" >&2; exit 2; }
    python "$root/stack/analyze.py" pool "${runs[@]}"
    ;;
  status) show_status ;;
  # Tag chỉ cần cho up; compose vẫn đòi biến khi đọc file
  down) BE_TAG=- TADSS_TAG=- AI_TAG=- docker compose -f "$stack" down -v --remove-orphans ;;
  *) sed -n '2,24p' "$0"; exit 2 ;;
esac
