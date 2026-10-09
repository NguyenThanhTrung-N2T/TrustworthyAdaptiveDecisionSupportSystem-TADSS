#!/usr/bin/env bash
# Chạy chế độ đánh giá bằng MỘT lệnh (chạy trong thư mục TADSS-Runtime/). Tự build/tải mọi image cần thiết ở lần đầu, không cần chạy gì trước.
#   bash tools/evaluation/run.sh            # bản nhanh (~25 phút)
#   bash tools/evaluation/run.sh full       # bản đầy đủ (~3 giờ)
#   bash tools/evaluation/run.sh quick --clean   # chạy xong thì dọn sạch: container, volume, image của chế độ đánh giá, build cache (~6 GB)
# Kết quả: evidence/<run_id>/report.html. Khi không --clean, Grafana của stack đánh giá còn ở http://127.0.0.1:3002 để xem lại; dọn sau bằng --clean-only.
set -uo pipefail
cd "$(dirname "$0")/../.."
export MSYS_NO_PATHCONV=1   # Git Bash trên Windows: không đổi đường dẫn /var/run/docker.sock

MODE="quick"
CLEAN=false
CLEAN_ONLY=false
for arg in "$@"; do
  case "$arg" in
    quick|full) MODE="$arg" ;;
    --clean) CLEAN=true ;;
    --clean-only) CLEAN_ONLY=true ;;
    *) echo "Tham số không hợp lệ: $arg (dùng: quick|full [--clean] hoặc --clean-only)" >&2; exit 2 ;;
  esac
done

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

echo ">> Chế độ đánh giá: $MODE (lần đầu sẽ build/tải image, vài phút)"
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
