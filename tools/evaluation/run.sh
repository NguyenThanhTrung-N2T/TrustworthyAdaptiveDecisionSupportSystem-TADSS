#!/usr/bin/env bash
# Chạy chế độ đánh giá bằng MỘT lệnh (chạy trong thư mục TADSS-Runtime/). Tự build/tải mọi image cần thiết ở lần đầu, không cần chạy gì trước.
#   bash tools/evaluation/run.sh            # bản nhanh (~25 phút)
#   bash tools/evaluation/run.sh full       # bản đầy đủ (~3 giờ)
#   bash tools/evaluation/run.sh quick --steps e,f   # chỉ chạy một vài bước (a..f, cách nhau bằng dấu phẩy; mặc định chạy đủ a..f)
#   bash tools/evaluation/run.sh quick --clean   # chạy xong thì dọn sạch: container, volume, image của chế độ đánh giá, build cache (~6 GB)
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

COMPOSE=(docker compose -p tadss-eval --profile evaluation)
EVAL_IMAGES=(tadss-evaluator:local tadss-eval-runtime:local grafana/otel-lgtm:0.34.0 postgres:16.15-alpine redis:7.4-alpine
  nginx:1.29.8-alpine ghcr.io/shopify/toxiproxy:2.12.0 testcontainers/ryuk:0.14.0)

clean() {
  echo ">> Dọn stack đánh giá, image và build cache của nó..."
  "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1
  docker rmi -f "${EVAL_IMAGES[@]}" >/dev/null 2>&1
  docker image prune -f >/dev/null 2>&1
  docker builder prune -a -f >/dev/null 2>&1
  docker system df
}

if $CLEAN_ONLY; then
  clean
  exit 0
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
