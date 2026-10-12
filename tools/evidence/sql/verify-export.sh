#!/usr/bin/env bash
# Kiểm truy vấn xuất SmashCourt trên PostgreSQL dùng một lần (chạy trong bất kỳ thư mục nào):
#   bash tools/evidence/sql/verify-export.sh
# Dựng postgres:16.15-alpine (--rm), nạp docs/database/schema_smashcourt.sql rồi tests/fixtures/smashcourt_seed.sql, chạy
# export_smashcourt_bookings.sql đúng như người dùng chạy bằng psql, so CSV với tests/fixtures/smashcourt_expected_export.csv từng ô.
# Không mở cổng, không mount thư mục, không chạm Neon; container (và volume ẩn danh của nó) luôn bị xóa khi kết thúc, kể cả lỗi hoặc Ctrl+C.
# Thoát 0 khi CSV khớp; 1 khi lệch hoặc vi phạm kiểm tra; 2 khi lỗi môi trường (Docker, nạp schema).
set -uo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash trên Windows: không đổi đường dẫn bên trong container

HERE="$(cd "$(dirname "$0")" && pwd)"
EVIDENCE="$(cd "$HERE/.." && pwd)"
REPO="$(cd "$EVIDENCE/../.." && pwd)"
SCHEMA="$REPO/docs/database/schema_smashcourt.sql"
SEED="$EVIDENCE/tests/fixtures/smashcourt_seed.sql"
EXPORT_SQL="$HERE/export_smashcourt_bookings.sql"
EXPECTED="$EVIDENCE/tests/fixtures/smashcourt_expected_export.csv"

IMAGE="postgres:16.15-alpine"
NAME="tadss-verify-export-$$"
DB="smashcourt_verify"
WORK="$(mktemp -d)"

cleanup() {
  docker rm -f -v "$NAME" >/dev/null 2>&1
  rm -rf "$WORK"
}
trap cleanup EXIT
trap 'exit 130' INT TERM

fail() { echo "THẤT BẠI: $*" >&2; exit 1; }
env_fail() { echo "LỖI MÔI TRƯỜNG: $*" >&2; exit 2; }

# 1. Kiểm tĩnh: file xuất chỉ đọc và không chọn cột cá nhân.
CODE="$(sed -e 's/--.*$//' "$EXPORT_SQL")"
if grep -qiE '\b(insert|update|delete|drop|alter|create|truncate|grant)\b' <<<"$CODE"; then
  fail "export_smashcourt_bookings.sql chứa lệnh ghi (INSERT/UPDATE/DELETE/DROP/ALTER/CREATE/TRUNCATE/GRANT)"
fi
grep -qi 'BEGIN TRANSACTION READ ONLY' <<<"$CODE" || fail "export_smashcourt_bookings.sql thiếu BEGIN TRANSACTION READ ONLY"
HEADER="$(head -n 1 "$EXPECTED" | tr -d '\r')"
if grep -qiE '(^|,)[a-z_]*(name|phone|email|note|booking_code|token|address)[a-z_]*(,|$)' <<<"$HEADER"; then
  fail "tiêu đề CSV có cột nghi là dữ liệu cá nhân: $HEADER"
fi
echo ">> Kiểm tĩnh đạt: chỉ đọc, không cột cá nhân."

# 2. Dựng PostgreSQL dùng một lần.
docker info >/dev/null 2>&1 || env_fail "Docker chưa chạy"
docker image inspect "$IMAGE" >/dev/null 2>&1 || docker pull "$IMAGE" >/dev/null || env_fail "không có image $IMAGE"
docker run -d --rm --name "$NAME" -e POSTGRES_PASSWORD=verify -e POSTGRES_DB="$DB" "$IMAGE" >/dev/null || env_fail "không khởi động được container"
echo ">> Container $NAME khởi động; chờ PostgreSQL sẵn sàng..."
for _ in $(seq 1 60); do
  if docker logs "$NAME" 2>&1 | grep -q 'init process complete' && docker exec "$NAME" pg_isready -U postgres -d "$DB" >/dev/null 2>&1; then READY=1; break; fi
  sleep 1
done
[ "${READY:-0}" = 1 ] || env_fail "PostgreSQL không sẵn sàng sau 60 giây"

PSQL=(docker exec -i "$NAME" psql -U postgres -d "$DB" -X)

# 3. Nạp schema. KHÔNG dùng ON_ERROR_STOP: bản dump của Neon (PG17) có `SET transaction_timeout` mà PG16 không biết, lỗi vô hại.
#    Tạo vai trò neondb_owner cho các dòng OWNER TO; bỏ BOM đầu file. Thành công được xác nhận bằng các bảng cần dùng.
"${PSQL[@]}" -q -c 'CREATE ROLE neondb_owner' >/dev/null || env_fail "không tạo được vai trò neondb_owner"
sed '1s/^\xEF\xBB\xBF//' "$SCHEMA" | "${PSQL[@]}" -q -f - >"$WORK/schema.out" 2>"$WORK/schema.err"
echo ">> Nạp schema xong; lỗi psql: $(grep -c 'ERROR' "$WORK/schema.err") (kỳ vọng chỉ là SET transaction_timeout của PG17):"
grep 'ERROR' "$WORK/schema.err" | sort | uniq -c | sed 's/^/     /'
UNEXPECTED="$(grep 'ERROR' "$WORK/schema.err" | grep -v 'transaction_timeout' || true)"
[ -z "$UNEXPECTED" ] || { echo "$UNEXPECTED" >&2; env_fail "nạp schema có lỗi ngoài SET transaction_timeout"; }
for table in bookings booking_courts booking_price_items invoices payments refunds users customer_loyalty loyalty_tiers courts; do
  FOUND="$("${PSQL[@]}" -t -A -c "SELECT to_regclass('public.$table') IS NOT NULL")"
  [ "$FOUND" = "t" ] || env_fail "thiếu bảng public.$table sau khi nạp schema"
done

# 4. Nạp dữ liệu giả (dừng ngay nếu sai).
"${PSQL[@]}" -q -v ON_ERROR_STOP=1 -f - <"$SEED" >/dev/null || env_fail "nạp smashcourt_seed.sql thất bại"
BOOKINGS="$("${PSQL[@]}" -t -A -c 'SELECT count(*) FROM bookings')"
echo ">> Đã nạp dữ liệu giả: $BOOKINGS booking."

# 5. Chạy truy vấn xuất đúng như người dùng (cùng cờ psql), đọc từ stdin để không cần mount.
"${PSQL[@]}" -q --csv -v ON_ERROR_STOP=1 -f - <"$EXPORT_SQL" >"$WORK/actual.csv" || fail "truy vấn xuất báo lỗi"
ROWS=$(($(wc -l <"$WORK/actual.csv") - 1))
echo ">> Truy vấn xuất trả $ROWS dòng dữ liệu."
[ "$ROWS" = "$BOOKINGS" ] || fail "số dòng xuất ($ROWS) khác số booking ($BOOKINGS): mỗi booking phải đúng một dòng"

# 6. So với kỳ vọng từng ô (bỏ CR do autocrlf).
tr -d '\r' <"$EXPECTED" >"$WORK/expected.csv"
tr -d '\r' <"$WORK/actual.csv" >"$WORK/actual.lf.csv"
if ! diff -u "$WORK/expected.csv" "$WORK/actual.lf.csv" >"$WORK/diff.txt"; then
  KEEP="${TMPDIR:-/tmp}/smashcourt_actual_export.csv"
  cp "$WORK/actual.lf.csv" "$KEEP"
  cat "$WORK/diff.txt" >&2
  fail "CSV khác kỳ vọng (bản thực tế lưu ở $KEEP)"
fi
echo ">> CSV khớp smashcourt_expected_export.csv ($ROWS dòng)."

# 7. Dọn container rồi xác nhận đã xóa.
docker rm -f -v "$NAME" >/dev/null 2>&1
if docker ps -a --filter "name=$NAME" --format '{{.Names}}' | grep -q .; then fail "container $NAME vẫn còn"; fi
echo ">> Container $NAME đã bị xóa."
echo "ĐẠT: verify-export"
