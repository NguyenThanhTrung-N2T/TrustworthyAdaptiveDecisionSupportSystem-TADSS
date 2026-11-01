-- Bản dump có bảng của schema hangfire nhưng không có dòng phiên bản (hangfire.schema) ⇒ Hangfire cài lại từ đầu và đụng cột đã có.
-- Như seed-db.sh của bản deploy: bỏ schema hangfire, BE tự tạo đúng phiên bản lúc khởi động.
DROP SCHEMA hangfire CASCADE;
