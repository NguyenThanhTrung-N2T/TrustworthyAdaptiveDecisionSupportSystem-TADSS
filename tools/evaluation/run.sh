#!/usr/bin/env bash
# Cách chính (D-14): `docker compose up -d --build` chạy app + ngrok + Grafana VÀ tự chạy một lượt đánh giá a…f (xem docker-compose.yml).
# Script này dành cho việc phụ: chạy lại một lượt/một vài bước mà không khởi động lại app, và dọn riêng stack đánh giá (chạy trong thư mục TADSS-Runtime/).
#   bash tools/evaluation/run.sh            # bản nhanh (~25 phút)
#   bash tools/evaluation/run.sh full       # bản đầy đủ (~3 giờ)
#   bash tools/evaluation/run.sh quick --steps e,f   # chỉ chạy một vài bước (a..f, cách nhau bằng dấu phẩy; mặc định chạy đủ a..f)
#   bash tools/evaluation/run.sh quick --clean   # chạy xong thì dọn sạch phần đánh giá: container, volume, image, build cache (~6 GB); app/ngrok/Grafana vận hành không bị đụng
# Kết quả: evidence/<run_id>/report.html. Khi không --clean, Grafana của stack đánh giá còn ở http://127.0.0.1:3002 để xem lại; dọn sau bằng --clean-only.
# Hướng dẫn từng bước (chuẩn bị máy, đọc kết quả, chạy lại một bước, sự cố thường gặp): docs/guides/EVALUATION_RUN_GUIDE.md
set -uo pipefail
cd "$(dirname "$0")/../.."
export MSYS_NO_PATHCONV=1   # Git Bash trên Windows: không đổi đường dẫn /var/run/docker.sock

MODE="quick"
CLEAN=false
CLEAN_ONLY=false
USAGE="dùng: [quick|full] [--steps a,b,c,d,e,f] [--clean] hoặc --clean-only"
STEPS=""
STEPS_GIVEN=false
while [ $# -gt 0 ]; do
  case "$1" in
    quick|full) MODE="$1" ;;
    --clean) CLEAN=true ;;
    --clean-only) CLEAN_ONLY=true ;;
    --steps)
      # Thiếu giá trị (hết tham số, hoặc tham số kế là một cờ khác) là lỗi, không đoán ý người dùng
      if [ $# -lt 2 ] || [[ "$2" == --* ]]; then
        echo "Thiếu giá trị cho --steps (ví dụ: --steps e,f). $USAGE" >&2
        exit 2
      fi
      STEPS="$2"
      STEPS_GIVEN=true
      shift
      ;;
    *) echo "Tham số không hợp lệ: $1 ($USAGE)" >&2; exit 2 ;;
  esac
  shift
done

if $STEPS_GIVEN; then
  NORMALIZED="$(printf '%s' "$STEPS" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
  if ! [[ "$NORMALIZED" =~ ^[a-f](,[a-f])*$ ]]; then
    echo "Giá trị --steps không hợp lệ: '$STEPS'. Chỉ nhận các chữ a, b, c, d, e, f cách nhau bằng dấu phẩy (ví dụ: --steps e,f)." >&2
    exit 2
  fi
  export EVAL_STEPS="$NORMALIZED"
fi

# Cùng project `tadss` (khai báo `name:` trong docker-compose.yml) với app/ngrok/Grafana vận hành: chỉ dọn đúng phần đánh giá, không `down` cả project.
COMPOSE=(docker compose)
EVAL_SERVICES=(evaluator eval-proxy eval-tadss-2 eval-tadss-1 eval-tadss-portability eval-toxiproxy eval-postgres eval-redis eval-grafana)
EVAL_VOLUMES=(tadss_eval-nuget tadss_eval-faults tadss_eval-datasets)
EVAL_IMAGES=(tadss-evaluator:local tadss-eval-runtime:local postgres:16.15-alpine redis:7.4-alpine
  nginx:1.29.8-alpine ghcr.io/shopify/toxiproxy:2.12.0 testcontainers/ryuk:0.14.0)
SHARED_IMAGE=grafana/otel-lgtm:0.34.0   # Grafana vận hành cũng dùng: chỉ xóa khi không còn container nào dùng

clean() {
  echo ">> Dọn stack đánh giá, image và build cache của nó (app/ngrok/Grafana vận hành giữ nguyên)..."
  "${COMPOSE[@]}" rm -s -f -v "${EVAL_SERVICES[@]}" >/dev/null 2>&1
  docker volume rm "${EVAL_VOLUMES[@]}" >/dev/null 2>&1
  docker network rm tadss_default >/dev/null 2>&1   # mạng riêng của stack đánh giá (vận hành dùng smashcourt-network)
  docker rmi -f "${EVAL_IMAGES[@]}" >/dev/null 2>&1
  [ -z "$(docker ps -aq --filter "ancestor=$SHARED_IMAGE")" ] && docker rmi -f "$SHARED_IMAGE" >/dev/null 2>&1
  docker image prune -f >/dev/null 2>&1
  docker builder prune -a -f >/dev/null 2>&1
  docker system df
}

if $CLEAN_ONLY; then
  clean
  exit 0
fi

# Lượt do `docker compose up` khởi động còn đang chạy thì không chạy chồng lượt thứ hai lên cùng stack
if [ -n "$("${COMPOSE[@]}" ps -q --status running evaluator 2>/dev/null)" ]; then
  echo "Evaluator do 'docker compose up' khởi động vẫn đang chạy một lượt đánh giá. Chờ xong (docker compose logs -f evaluator) rồi chạy lại." >&2
  exit 2
fi

echo ">> Chế độ đánh giá: $MODE${EVAL_STEPS:+, các bước: $EVAL_STEPS} (lần đầu sẽ build/tải image, vài phút)"
EVAL_MODE="$MODE" "${COMPOSE[@]}" run --rm --build -e EVAL_MODE="$MODE" evaluator
CODE=$?
LATEST=$(ls -1td evidence/EV-* 2>/dev/null | head -1)
echo
echo ">> Mã thoát: $CODE (0 đạt hết, 1 có tiêu chí không đạt, 2 lỗi hạ tầng)"
[ -n "$LATEST" ] && echo ">> Báo cáo: $LATEST/report.html"

if $CLEAN; then
  clean
else
  echo ">> Stack đánh giá vẫn chạy (Grafana http://127.0.0.1:3002). Dọn: bash tools/evaluation/run.sh --clean-only"
fi
exit $CODE
