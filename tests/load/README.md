# Kịch bản tải k6 (LT-01, LT-02, LT-05)

Kịch bản tải của chế độ đánh giá (D-12, bước **b**). Evaluator chạy chúng trên instance 1 của stack đánh giá; có thể chạy riêng để thử (xem cuối file). T5.26 là việc của SV2: có thể chỉnh kịch bản nhưng **giữ giao diện** dưới đây để evaluator chạy được.

## Giao diện

| Biến (`--env`) | Ý nghĩa |
| :--- | :--- |
| `BASE_URL` | Địa chỉ TADSS, mặc định `http://eval-tadss-1:8080` |
| `API_KEY` | Giá trị header `X-Api-Key` |
| `RUN_ID`, `SCENARIO` | Định danh lượt chạy và kịch bản (ghi vào mỗi dòng D2) |
| `MODE` | `quick` (mặc định) hoặc `full`; chọn khối trong `config/modes.json` |
| `SUMMARY_PATH` | Nơi `handleSummary` ghi `summary.json` |
| `SAMPLE_ONLY=N` | Chỉ in `N` payload (không gửi) để kiểm schema |

- **Đầu ra D2:** mỗi request một dòng JSON qua `console.log` (`ts`, `scenario`, `phase`, `http_status`, `duration_ms`, `decision_status`, `execution_type`, `correlation_id`, `idempotency_key`, `decision_id`). Chạy với `--console-output <file>`; k6 ghi file dạng logfmt (`time=… level=info msg="…"`), evaluator đổi thành `requests.jsonl` sạch.
- **Warm-up:** scenario `warmup` (tag `phase=warmup`) chạy trước scenario `main`; dòng warm-up bị loại khỏi thống kê ở bước (e).
- **Metric:** `--out opentelemetry` với `K6_OTEL_GRPC_EXPORTER_ENDPOINT`, `K6_OTEL_GRPC_EXPORTER_INSECURE=true`, `K6_OTEL_METRIC_PREFIX=k6_` (spike S-3). Không gắn tag theo từng request (tag `name` cố định) để tránh bùng nổ nhãn.

## Hồ sơ tải (`config/modes.json`)

| Kịch bản | Bản nhanh | Bản đầy đủ |
| :--- | :--- | :--- |
| LT-01 | `constant-arrival-rate` 50 RPS, warm-up 30 s + 2 phút, 1 lần | warm-up 1 phút + 10 phút, 3 lần lặp |
| LT-02 | `ramping-arrival-rate` 10 → 200 RPS theo 6 bậc × 30 s | 10 → 400 RPS theo 17 bậc × 50 s (~15 phút) |
| LT-05 | không chạy | 50 RPS, warm-up 1 phút + 60 phút |

`preAllocatedVUs`/`maxVUs` đủ lớn để k6 không bỏ lượt; số `dropped_iterations` có trong `summary.json` và được báo cáo.

## Payload (`lib/payload.js`)

Bảng trộn khai báo ngay trong file: chi nhánh cố định; giờ cao điểm/thấp điểm theo giờ chi nhánh (`Asia/Ho_Chi_Minh`); khách vãng lai và 5 hạng thành viên; giá dưới/trên ngưỡng giá trị lớn (2.000.000 VND); 1–3 item. Thời gian chơi luôn ở tương lai (7–30 ngày) để cửa sổ duyệt còn tồn tại. Payload hợp lệ schema v2 và bất biến domain: **response 400 nghĩa là bộ sinh sai**. Kiểm bằng `tools/evaluation/tests/test_payload_samples.py` (500 mẫu).

## Chạy thử riêng

```bash
# In 5 payload mẫu (không gửi)
docker run --rm -v "$PWD/tests/load:/load:ro" -v "$PWD:/out" -w /load grafana/k6:2.3.0 \
  run -e SAMPLE_ONLY=5 --console-output /out/samples.log --quiet scenarios/lt01.js
```

Chạy thật cần stack đánh giá (`docker compose -p tadss-eval --profile evaluation run --rm --build -e EVAL_STEPS=b evaluator`).
