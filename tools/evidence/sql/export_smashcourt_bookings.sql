-- ============================================================================
-- Xuất booking SmashCourt để replay qua TADSS (RP-01, gói 5.6). CHỈ ĐỌC.
--
-- Mục đích: mỗi dòng CSV là một booking với đủ dữ liệu để evaluator dựng request v2
-- (INTEGRATION_SPEC §4.2): sân đang hoạt động, tổng sau giảm giá, hạng khách, ngày tạo tài khoản và lịch sử
-- của khách TẠI THỜI ĐIỂM đặt. Evaluator ẩn danh các định danh (HMAC khóa ngẫu nhiên của lượt chạy) trước khi
-- ghi bất kỳ file nào vào Evidence Pack; file CSV thô chỉ nằm ở data/private/ (gitignore), không vào repo.
--
-- Cách chạy (người dùng tự chạy; biến môi trường do người dùng đặt, file này không chứa thông tin kết nối):
--   psql "$SMASHCOURT_DATABASE_URL" -X -q --csv -v ON_ERROR_STOP=1 \
--        -f tools/evidence/sql/export_smashcourt_bookings.sql -o data/private/smashcourt_bookings.csv
--   (-q bắt buộc để psql không ghi thẻ lệnh BEGIN/SET/COMMIT vào CSV; không dùng -t để giữ dòng tiêu đề)
--
-- Cam kết dữ liệu cá nhân: KHÔNG chọn tên, số điện thoại, email, ghi chú, mã booking, token hủy hay địa chỉ.
-- Số điện thoại khách vãng lai chỉ dùng BÊN TRONG truy vấn (CTE booking_party) để ghép lịch sử, không có trong kết quả.
-- Truy vấn chạy trong giao dịch READ ONLY và đặt múi giờ phiên là UTC để mốc thời gian xuất không phụ thuộc cấu hình máy chủ.
--
-- Cột xuất (một dòng một booking, sắp theo created_at, booking_id):
--   booking_id, branch_id, customer_id (rỗng nếu vãng lai)   UUID gốc; chỉ được băm ở evaluator
--   source, status                                           bookings.source / bookings.status (nhãn cuối của môi trường phát triển)
--   created_at                                               ISO 8601 UTC, micro giây (-> requestedAt)
--   items                                                    JSON một dòng: các sân của booking, mỗi phần tử {court_id, court_type_id, date, start_time, end_time, price};
--                                                            price = tổng booking_price_items.unit_price của sân đó.
--                                                            Booking không còn sân nào hoạt động (đã kết thúc: BE tắt is_active của MỌI sân khi hủy,
--                                                            NO_SHOW, COMPLETED, PENDING_PAYMENT) hoặc đã hủy: xuất TẤT CẢ sân, vì replay cần request
--                                                            như lúc tạo đơn. Booking còn sân hoạt động: chỉ sân ĐANG HOẠT ĐỘNG (sân giải phóng sớm bị bỏ)
--   final_total                                              invoices.final_total (rỗng nếu không có hóa đơn)
--   tier_rank                                                thứ hạng của hạng HIỆN TẠI theo loyalty_tiers.min_points tăng dần (1…n); rỗng nếu chưa có hạng
--   member_since                                             users.created_at, ISO 8601 UTC (rỗng nếu vãng lai)
--   history_*                                                số booking ĐÃ KẾT THÚC trước created_at của booking này (xem quy tắc dưới)
--
-- Quy tắc lịch sử (INTEGRATION_SPEC §3.3: "hủy do nhân viên không tính"):
--   Khách có tài khoản ghép theo customer_id; khách vãng lai (customer_id rỗng) ghép theo số điện thoại đã chuẩn hóa
--   (bỏ ký tự không phải số; 84xxxxxxxxx thành 0xxxxxxxxx). Không có số điện thoại, hoặc số sau chuẩn hóa ngắn hơn 6 chữ số (rác như '0', '1'),
--   thì lịch sử bằng 0 (không dùng làm khóa ghép để tránh gom những người khác nhau).
--   Mốc kết thúc: COMPLETED/NO_SHOW là giờ kết thúc muộn nhất của các sân (giờ Asia/Ho_Chi_Minh); các trạng thái hủy là cancelled_at.
--   Chỉ tính booking có mốc kết thúc < created_at của booking đang xét (và khác chính nó).
--
--   status                                        cancel_source        loại lịch sử
--   COMPLETED                                     -                    completed
--   NO_SHOW                                       -                    no_show
--   CANCELLED | CANCELLED_PENDING_REFUND |
--   CANCELLED_REFUNDED                            SYSTEM               payment_expired (BE hủy do hết hạn hoặc thanh toán thất bại; hóa đơn EXPIRED)
--                                                 CUSTOMER | LINK      khách tự hủy (app hoặc link hủy): late_cancelled nếu ĐÃ THANH TOÁN
--                                                                      (có payment SUCCESS/REFUNDED) mà tỷ lệ hoàn lớn nhất trong refunds < 100
--                                                                      (kể cả không có bản ghi hoàn = 0 %), ngược lại cancelled
--                                                 STAFF | rỗng         không tính
--   Các trạng thái khác (PENDING, CONFIRMED, PAID_ONLINE, IN_PROGRESS, PENDING_PAYMENT) không tính.
--   Phân loại dựa vào tỷ lệ hoàn thực (refunds.refund_percent), KHÔNG dựa vào tên trạng thái, vì BE giữ CANCELLED khi chính sách hủy cho hoàn 0 %
--   và chỉ chuyển CANCELLED_PENDING_REFUND -> CANCELLED_REFUNDED khi có tiền hoàn. Khách tự hủy khi chưa trả tiền không mất gì nên tính là cancelled.
-- ============================================================================

BEGIN TRANSACTION READ ONLY;

SET LOCAL TIME ZONE 'UTC';

WITH booking_party AS (
    -- Khóa ghép lịch sử: tài khoản theo customer_id, vãng lai theo số điện thoại chuẩn hóa. Số điện thoại KHÔNG ra khỏi CTE này.
    SELECT b.id,
           b.status,
           b.cancel_source,
           b.cancelled_at,
           b.created_at,
           CASE
               WHEN b.customer_id IS NOT NULL THEN 'U:' || b.customer_id::text
               WHEN length(phone.normalized) >= 6 THEN 'P:' || phone.normalized
           END AS party_key
    FROM bookings b
    CROSS JOIN LATERAL (SELECT regexp_replace(coalesce(b.guest_phone, ''), '[^0-9]', '', 'g') AS digits) AS raw_phone
    CROSS JOIN LATERAL (
        SELECT CASE WHEN raw_phone.digits ~ '^84[0-9]{9}$' THEN '0' || substr(raw_phone.digits, 3) ELSE raw_phone.digits END AS normalized
    ) AS phone
),
history_event AS MATERIALIZED (
    SELECT bp.id,
           bp.party_key,
           CASE
               WHEN bp.status IN ('COMPLETED', 'NO_SHOW') THEN play.ended_at
               ELSE bp.cancelled_at
           END AS ended_at,
           CASE
               WHEN bp.status = 'COMPLETED' THEN 'completed'
               WHEN bp.status = 'NO_SHOW' THEN 'no_show'
               WHEN bp.status IN ('CANCELLED', 'CANCELLED_PENDING_REFUND', 'CANCELLED_REFUNDED') THEN
                   CASE
                       WHEN bp.cancel_source = 'SYSTEM' THEN 'payment_expired'
                       WHEN bp.cancel_source IN ('CUSTOMER', 'LINK') THEN
                           CASE
                               WHEN EXISTS (
                                        SELECT 1
                                        FROM invoices i
                                        JOIN payments p ON p.invoice_id = i.id
                                        WHERE i.booking_id = bp.id AND p.status IN ('SUCCESS', 'REFUNDED'))
                                    AND coalesce((
                                        SELECT max(r.refund_percent)
                                        FROM invoices i
                                        JOIN payments p ON p.invoice_id = i.id
                                        JOIN refunds r ON r.payment_id = p.id
                                        WHERE i.booking_id = bp.id), 0) < 100
                               THEN 'late_cancelled'
                               ELSE 'cancelled'
                           END
                   END
           END AS category
    FROM booking_party bp
    LEFT JOIN LATERAL (
        SELECT max(bc.date + bc.end_time) AT TIME ZONE 'Asia/Ho_Chi_Minh' AS ended_at
        FROM booking_courts bc
        WHERE bc.booking_id = bp.id
    ) AS play ON true
    WHERE bp.party_key IS NOT NULL
)
SELECT b.id AS booking_id,
       b.branch_id,
       b.customer_id,
       b.source,
       b.status,
       to_char(b.created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS created_at,
       coalesce((
           SELECT jsonb_agg(
                      jsonb_build_object(
                          'court_id', bc.court_id,
                          'court_type_id', c.court_type_id,
                          'date', bc.date,
                          'start_time', bc.start_time,
                          'end_time', bc.end_time,
                          'price', coalesce((SELECT sum(pi.unit_price) FROM booking_price_items pi WHERE pi.booking_court_id = bc.id), 0))
                      ORDER BY bc.date, bc.start_time, bc.court_id)
           FROM booking_courts bc
           JOIN courts c ON c.id = bc.court_id
           WHERE bc.booking_id = b.id
             AND (bc.is_active
                  OR b.status IN ('CANCELLED', 'CANCELLED_PENDING_REFUND', 'CANCELLED_REFUNDED')
                  OR NOT EXISTS (SELECT 1 FROM booking_courts active WHERE active.booking_id = b.id AND active.is_active))
       ), '[]'::jsonb)::text AS items,
       inv.final_total,
       CASE
           WHEN lt.id IS NULL THEN NULL
           ELSE (SELECT count(*) FROM loyalty_tiers t2 WHERE t2.min_points <= lt.min_points)
       END AS tier_rank,
       to_char(u.created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"') AS member_since,
       hist.completed AS history_completed,
       hist.no_show AS history_no_show,
       hist.cancelled AS history_cancelled,
       hist.late_cancelled AS history_late_cancelled,
       hist.payment_expired AS history_payment_expired
FROM bookings b
JOIN booking_party bp ON bp.id = b.id
LEFT JOIN invoices inv ON inv.booking_id = b.id
LEFT JOIN users u ON u.id = b.customer_id
LEFT JOIN customer_loyalty cl ON cl.user_id = b.customer_id
LEFT JOIN loyalty_tiers lt ON lt.id = cl.tier_id
CROSS JOIN LATERAL (
    SELECT count(*) FILTER (WHERE h.category = 'completed') AS completed,
           count(*) FILTER (WHERE h.category = 'no_show') AS no_show,
           count(*) FILTER (WHERE h.category = 'cancelled') AS cancelled,
           count(*) FILTER (WHERE h.category = 'late_cancelled') AS late_cancelled,
           count(*) FILTER (WHERE h.category = 'payment_expired') AS payment_expired
    FROM history_event h
    WHERE bp.party_key IS NOT NULL
      AND h.party_key = bp.party_key
      AND h.id <> b.id
      AND h.category IS NOT NULL
      AND h.ended_at < b.created_at
) AS hist
ORDER BY b.created_at, b.id;

COMMIT;
