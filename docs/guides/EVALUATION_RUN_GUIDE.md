# Hướng dẫn chạy chế độ đánh giá TADSS

Tài liệu này giúp bạn tự chạy, đọc và chạy lại chế độ đánh giá trên máy Windows (Git Bash + Docker Desktop), không cần biết evaluator bên trong.
Mọi lệnh chạy nguyên văn trong Git Bash (lệnh `docker compose` chạy được cả trong PowerShell), **từ thư mục `TADSS-Runtime/`**. Đây là nơi duy nhất mô tả quy trình từng bước; README chỉ giữ lệnh ngắn.

## 1. Ba cách chạy TADSS

Chọn cách chạy theo việc bạn cần làm.

| Cách | Lệnh | Dùng khi |
| :--- | :--- | :--- |
| Dev hằng ngày | `dotnet run --project src/TADSS.Runtime.Api` | Viết mã, thử API bằng Swagger. Dùng Neon + Redis Cloud của bạn. |
| **Vận hành + đánh giá** | `docker compose up -d --build` | Cho SmashCourt-BE gọi qua ngrok, xem Grafana `http://localhost:3001`, **đồng thời** tự chạy một lượt đánh giá (bản nhanh) ra `report.html` cho hội đồng. |
| Chạy lại đánh giá | `bash tools/evaluation/run.sh` | Chạy lại một lượt hoặc vài bước mà không khởi động lại app (mục 7). |

Stack đánh giá chạy cô lập (mạng riêng, PostgreSQL/Redis dùng một lần) nên **không chạm Neon**, dù khởi động cùng app (quyết định D-14).

## 2. Chuẩn bị máy (một lần)

Mục này đảm bảo máy đủ tài nguyên để số đo không bị méo.

1. Cài và bật **Docker Desktop** (chờ biểu tượng báo "Engine running"). Kiểm tra: `docker info`.
2. Cấp cho Docker **RAM ≥ 8 GB** (Settings > Resources, hoặc `memory=8GB` trong `%UserProfile%\.wslconfig`) và để trống **≥ 15 GB** ổ đĩa.
3. **Tắt chế độ ngủ** của Windows khi cắm điện (Settings > System > Power). Máy ngủ giữa lượt làm đồng hồ nhảy và số đo sai.
4. Đóng các ứng dụng nặng (IDE, trình duyệt nhiều tab) trong lúc chạy: tải k6 và TADSS cùng tranh CPU của một máy.
5. Vì đánh giá khởi động cùng app, ba thứ của chế độ vận hành phải có trước (giống SmashCourt-BE, xem README mục "Chạy bằng Docker và expose qua ngrok"):
   mạng `docker network create smashcourt-network` (thiếu mạng thì `docker compose up` dừng ngay, không chạy gì), `.env` có `NGROK_AUTHTOKEN`, và `appsettings.Development.json` có API key thật.
   Thiếu `NGROK_AUTHTOKEN` hoặc API key thì chỉ phần vận hành lỗi, lượt đánh giá vẫn chạy.
6. Mở Git Bash ở thư mục `TADSS-Runtime/`. Lần chạy đầu tự build và tải image (khoảng 6-8 GB), mất vài phút đến vài chục phút tùy mạng.

## 3. Chạy bản nhanh

Bản nhanh chạy đủ sáu bước (a)…(f), tự động khi bật Docker của TADSS:

```bash
docker compose up -d --build
docker compose logs -f evaluator
```

**Thấy gì là đúng:**
- Lệnh đầu build/tải image (lần đầu dài), khởi động app + ngrok + Grafana và stack đánh giá, rồi trả lại dấu nhắc; evaluator chạy nền.
- Lệnh thứ hai theo dõi evaluator: màn hình **im lặng** cho tới khi xong (evaluator chỉ in bảng tóm tắt ở cuối). Im lặng 20+ phút là bình thường. Thoát theo dõi bằng `Ctrl+C` (không dừng evaluator).
- Cuối cùng là bảng tóm tắt, dòng `KẾT QUẢ: ...` và `Báo cáo trên máy: evidence/<run_id>/report.html (mã thoát ...)`; container `tadss-evaluator-1` chuyển sang `Exited`.
- Docker Desktop cũng hiện được log này: mở project `tadss` > container `evaluator`.
- Mỗi lần `docker compose up` lại là chạy lại một lượt (tạo thư mục `evidence/` mới). Lượt đang chạy dở mà `up` lại thì evaluator bị khởi động lại từ đầu.

Không muốn bật app/ngrok mà chỉ chạy đánh giá: `bash tools/evaluation/run.sh` (chỉ khởi động stack đánh giá; in `>> Mã thoát` và `>> Báo cáo` khi xong).

**Thời lượng đo thật** (máy của tác giả, bản nhanh, lượt `EV-20261011-EVAL-quick-r1` chạy bằng `docker compose up -d --build`, đạt mọi tiêu chí):

| Bước | Việc | Thời lượng |
| :--- | :--- | :--- |
| (a) | 3 script verify + test gộp + pytest + OB-2 | ~9 phút (547 s) |
| (b) | Tải k6 LT-01, LT-02 | ~7 phút (404 s) |
| (c) | Sự cố dưới tải LT-03 + SCALE-2 | ~8,5 phút (510 s) |
| (d) | Schemathesis + coverage | ~1 phút (53 s) |
| (e) | Dữ liệu thực tế (PT 10.000 request; RP bỏ qua khi chưa có CSV) | ~1,5-2 phút (101 s; máy rảnh có thể ~40 s) |
| (f) | Evidence Pack + báo cáo | ~1,5 phút (83 s) |
| **Tổng** | | **~28 phút** (1.698 s; chưa tính lần đầu dựng image) |

**Nhận biết bị treo.** Mở cửa sổ Git Bash thứ hai và chạy:

```bash
docker ps --filter name=tadss-evaluator --format '{{.Names}} {{.Status}}'
ls -R "$(ls -1td evidence/EV-* | head -1)/raw" | tail -5
```

- Dòng đầu phải ra một container `tadss-evaluator-1` (chạy bằng `up`) hoặc `tadss-evaluator-run-...` (chạy bằng `run.sh`) đang `Up`.
- Dòng sau phải ra tên file mới xuất hiện (thư mục `raw/` lớn dần theo từng bước).
- Quá 10 phút mà `raw/` không thêm file nào: xem mục 10. Muốn dừng hẳn: `docker compose stop evaluator` (hoặc `Ctrl+C` nếu chạy bằng `run.sh`), rồi dọn bằng mục 11.

## 4. Đọc kết quả

Mục này chỉ cho bạn nhìn đâu trước để biết lượt chạy đạt hay không.

**Mã thoát** (dòng `Báo cáo trên máy: ... (mã thoát N ...)`, hoặc `>> Mã thoát` khi chạy bằng `run.sh`):

| Mã | Nghĩa | Làm gì |
| :--- | :--- | :--- |
| `0` | Đạt mọi tiêu chí | Mở báo cáo. |
| `1` | Chạy hết nhưng có tiêu chí không đạt | Mở báo cáo, xem mục KPI và các bước `failed`. |
| `2` | Lỗi hạ tầng, tham số sai hoặc không chạy hết | Đọc cột "Ghi chú" của bảng tóm tắt, rồi mục 10. |

**Bảng tóm tắt** (cuối màn hình) có một dòng mỗi bước: trạng thái `passed` / `failed` / `skipped` / `error`, thời gian, và ghi chú một dòng. Bước (f) liệt kê thêm SC1…SC8 và EV-1…EV-5.

**Báo cáo.** Mở thư mục kết quả rồi bấm đúp `report.html` (tự chứa, không cần mạng):

```bash
explorer.exe evidence
```

Xem theo thứ tự: mục **1 Kết luận KPI** (SC1…SC8) > mục **2 Các bước** > mục **10 Toàn vẹn (EV-1…EV-5)** phải toàn "đạt" > mục **11 Giới hạn**. Các mục 4-9 là chi tiết từng bước.

Ở bản nhanh, các trạng thái sau là **bình thường**: SC5/SC6 `NOT_APPLICABLE` (đo trên E-STAGING ở Phase 7–8, không thuộc evaluator); SC8 `INSUFFICIENT_DATA` (bản nhanh bỏ Stryker); SC7 `INSUFFICIENT_DATA` khi chưa commit `tools/evidence/hotel_adapter.py`. Ý nghĩa từng tiêu chí: [methodology](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

**Mục PT-03/PT-04 (dữ liệu khách sạn, gói 5.7b).** Đọc theo thứ tự này; mọi con số ở đây là **chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính**:

1. **Nhãn đối soát shadow** (`RECONCILED` / `UNKNOWN` / `FAILED`). `RECONCILED` là đạt; `UNKNOWN` là chưa kiểm được (thiếu Prometheus, counter reset, quá thời gian chờ: xem trường `reason`, thường chạy lại `--steps e,f` là đủ); `FAILED` là đỏ, báo lại. Đây là đối soát **tổng hợp** (DB + counter), không phải bằng chứng cho từng request; bằng chứng theo request là số ghép cặp ở bước 2.
2. **Ghép cặp** (`PAIRED`, `PRIMARY_FALLBACK`, `SHADOW_FALLBACK`, `UNPAIRED`) và `violations` (phải rỗng). Tỷ lệ `PAIRED` ≥ 99 % là ngưỡng vận hành để chạy bản đầy đủ, không phải ngưỡng thống kê. Chỉ các đơn `PAIRED` (`counts.compared`) được dùng để so sánh hai strategy; số đơn bị loại được đếm riêng, không thành 0.
3. **Hai biến thể hoàn tiền** (`REFUND_OF_PAID`, `REFUND_OF_TOTAL_CAPPED`) luôn đi cạnh nhau vì gốc tính hoàn tiền chưa chốt (Q-6); không biến thể nào là kết quả "chính". Rủi ro hoàn vượt của mã BE hiện tại (`AS_IS_BE`) là mục riêng, không dùng để xếp hạng.
4. **Hai cận của `S-SC`**: dataset chỉ có ngày hủy nên mỗi số được báo ở cận `low` (hủy lúc 00:00, hoàn nhiều) và `high` (hủy lúc 23:59, hoàn ít); thực tế nằm giữa hai cận. `NEED_APPROVAL` tính r = 1 làm số chính và r = 0 làm cận dưới. Câu kết luận xếp hạng (`ranking.statement`) chỉ nêu chính sách tốt hơn khi đúng ở mọi cận, biến thể, kịch bản và CI không chứa 0; còn lại là "không phân biệt được" hoặc "phụ thuộc giả định".

Xem nhanh (thay `<run_id>`):

```bash
python -c "import json; d=json.load(open('evidence/<run_id>/raw/d5/policy_economics.json',encoding='utf-8')); print((d['shadow']['reconciliation'] or {}).get('label', '-'), d['shadow']['pairing'], d['counts']['compared'])"
```

Cột D5 và quy tắc giấy phép (artifact nào được vào Evidence Pack chính thức): [DATASET_SCHEMA](../research/DATASET_SCHEMA_SPECIFICATION.md) Mục 2–3; phương pháp: [methodology](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) Mục 4.10.

Grafana của stack đánh giá ở `http://127.0.0.1:3002` (dashboard TADSS Runtime) để xem lại, tới khi bạn `docker compose down` hoặc dọn (mục 11).

## 5. Mỗi bước làm gì

Evaluator chạy tuần tự sáu bước; mỗi bước ghi dữ liệu thô vào `evidence/<run_id>/raw/`.

| Bước | Làm gì | Mã danh mục | Kết quả trong `raw/` |
| :--- | :--- | :--- | :--- |
| (a) test | Build Release `--warnaserror`, chạy unit + integration test (unit chạy tuần tự), `verify-expose.sh`, `verify-observability.sh`, `verify-export.sh`, `python tools/docs/check_docs.py` (DOC-1…3), ba bộ pytest của `tools/` (`evaluation`, `evidence`, `docs`), kiểm dashboard Grafana. Mọi kiểm tra ở mục 9 đều nằm ở đây. | UT, IT, NG, SEC, OB-2, OB-4, DOC-1…DOC-4... | `tests/` (trx, `catalog-results.csv`, coverage, log từng kiểm tra: `verify-*.log`, `pytest-*.log`, `check-docs.log`) |
| (b) tải | k6 LT-01 và LT-02 trên instance 1 (LT-05 chỉ ở bản đầy đủ). | LT-01, LT-02 (kịch bản và giao diện: mục con "Kịch bản tải k6" bên dưới) | `load/<kịch bản>/`, `load/windows.json` |
| (c) sự cố | k6 50 RPS trong khi tiêm lỗi theo lịch (strategy chậm/lỗi, cắt PostgreSQL, Redis chậm), rồi SCALE-2 qua nginx với hai instance. | LT-03, FI-02/03/07/08, SCALE-2, OB-3 | `load/lt03/`, `chaos/chaos_log.csv`, `metrics/`, `scale/scale2-checks.json`, `grafana/dashboard-snapshot.json` |
| (d) hợp đồng | Schemathesis trên `openapi-v2.yaml` (25 ví dụ; 500 ở bản đầy đủ), gộp coverage; Stryker chỉ ở bản đầy đủ. | CT-03, MT-01…03 | `contract/`, `coverage/`, `mutation/` |
| (e) dữ liệu thực tế | **RP**: gửi booking SmashCourt (CSV của bạn, mục 8) qua `eval-proxy`. **PT**: tải dataset khách sạn (ghim SHA-256), gửi 10.000 dòng (đầy đủ: ~118 nghìn) tới instance `eval-tadss-portability`. Kiểm từng response theo schema v2. | RP-01/02, PT-01/02 | `replay/`, `portability/` (`requests.jsonl`, `summary.json`, `source.json`) |
| (f) Evidence Pack | Xuất audit D1 từ PostgreSQL, dựng D2/D4/D5, tính SC1…SC8, kiểm EV-1…EV-5, sinh `report.html` và `manifest.json`. | SC, EV | `d1/`, `d2/`, `d4/`, `d5/`, `analysis/`, ngoài `raw/`: `report.html`, `manifest.json`, `steps.json` |

Ghi chú về (e): thiếu CSV SmashCourt thì RP `skipped`; không tải được dataset thì PT `skipped`; dataset sai SHA-256 hoặc có lỗi contract thì `failed`. Bước (f) vẫn chạy sau (e) kể cả khi (e) `failed`, để báo cáo ghi rõ lý do.
Chi tiết kỹ thuật: [methodology mục 6.1](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) và [DATASET_SCHEMA](../research/DATASET_SCHEMA_SPECIFICATION.md).

### Kịch bản tải k6 (bước b)

Kịch bản tải nằm ở `tests/load/` (`scenarios/lt01.js`, `lt02.js`, `lt03.js`, `lt05.js`, `scale2.js`, `config/modes.json`, `lib/`). Evaluator chạy LT-01, LT-02 (và LT-05 ở bản đầy đủ) trên instance 1 của stack đánh giá; LT-03 và SCALE-2 thuộc bước (c). T5.26 là việc của SV2: có thể chỉnh kịch bản nhưng **giữ giao diện** dưới đây để evaluator chạy được.

| Biến (`--env`) | Ý nghĩa |
| :--- | :--- |
| `BASE_URL` | Địa chỉ TADSS, mặc định `http://eval-tadss-1:8080` |
| `API_KEY` | Giá trị header `X-Api-Key` |
| `RUN_ID`, `SCENARIO` | Định danh lượt chạy và kịch bản (ghi vào mỗi dòng D2) |
| `MODE` | `quick` (mặc định) hoặc `full`; chọn khối trong `config/modes.json` |
| `SUMMARY_PATH` | Nơi `handleSummary` ghi `summary.json` |
| `SAMPLE_ONLY=N` | Chỉ in `N` payload (không gửi) để kiểm schema |

- **Đầu ra D2:** mỗi request một dòng JSON qua `console.log` (`ts`, `scenario`, `phase`, `http_status`, `duration_ms`, `decision_status`, `execution_type`, `correlation_id`, `idempotency_key`, `decision_id`). Chạy với `--console-output <file>`; k6 ghi file dạng logfmt (`time=… level=info msg="…"`), evaluator đổi thành `requests.jsonl` sạch.
- **Warm-up:** scenario `warmup` (tag `phase=warmup`) chạy trước scenario `main`; dòng warm-up bị loại khỏi thống kê ở bước (f).
- **Metric:** `--out opentelemetry` với `K6_OTEL_GRPC_EXPORTER_ENDPOINT`, `K6_OTEL_GRPC_EXPORTER_INSECURE=true`, `K6_OTEL_METRIC_PREFIX=k6_` (spike S-3). Không gắn tag theo từng request (tag `name` cố định) để tránh bùng nổ nhãn.

Hồ sơ tải trong `config/modes.json`:

| Kịch bản | Bản nhanh | Bản đầy đủ |
| :--- | :--- | :--- |
| LT-01 | `constant-arrival-rate` 50 RPS, warm-up 30 s + 2 phút, 1 lần | warm-up 1 phút + 10 phút, 3 lần lặp |
| LT-02 | `ramping-arrival-rate` 10 → 200 RPS theo 6 bậc × 30 s | 10 → 400 RPS theo 17 bậc × 50 s (~15 phút) |
| LT-05 | không chạy | 50 RPS, warm-up 1 phút + 60 phút |

k6 vẫn có thể bỏ lượt (`dropped_iterations`) khi tải cao hơn khả năng phục vụ; số lượt bỏ có trong `summary.json` và evaluator báo cáo riêng cho từng pha (warm-up và phần chính; cách xử lý ở mục 10). LT-02 cấp sẵn 400 VU (`preAllocatedVUs`).

**Payload** (`lib/payload.js`). Bảng trộn khai báo ngay trong file: chi nhánh cố định; giờ cao điểm/thấp điểm theo giờ chi nhánh (`Asia/Ho_Chi_Minh`); khách vãng lai và 5 hạng thành viên; giá dưới/trên ngưỡng giá trị lớn (2.000.000 VND); 1–3 item. Thời gian chơi luôn ở tương lai (7–30 ngày) để cửa sổ duyệt còn tồn tại. Payload hợp lệ schema v2 và bất biến domain: **response 400 nghĩa là bộ sinh sai**. Kiểm bằng `tools/evaluation/tests/test_payload_samples.py` (500 mẫu).

**Chạy thử riêng** (chỉ in 5 payload mẫu, không gửi):

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W)/tests/load:/load:ro" -v "$(pwd -W):/out" -w /load grafana/k6:2.3.0 \
  run -e SAMPLE_ONLY=5 --console-output /out/samples.log --quiet scenarios/lt01.js
```

```powershell
docker run --rm -v "${PWD}/tests/load:/load:ro" -v "${PWD}:/out" -w /load grafana/k6:2.3.0 run -e SAMPLE_ONLY=5 --console-output /out/samples.log --quiet scenarios/lt01.js
```

Trong Git Bash phải giữ `MSYS_NO_PATHCONV=1` và `$(pwd -W)`: nếu không, Git Bash tự đổi các đường dẫn `/load…`, `/out…` thành đường dẫn Windows và lệnh hỏng.

Chạy thật cần stack đánh giá: `bash tools/evaluation/run.sh quick --steps b` (mục 7), hoặc `docker compose up -d --build` chạy cả lượt a…f.

## 6. Bản đầy đủ

Bản đầy đủ cho số liệu chính thức (thêm LT-05, Stryker, CT-03 500 ví dụ, PT gửi toàn bộ dataset).

```bash
EVAL_MODE=full docker compose up -d --build           # Git Bash
$env:EVAL_MODE="full"; docker compose up -d --build   # PowerShell
bash tools/evaluation/run.sh full                     # hoặc: chỉ stack đánh giá, không bật app
```

- Thời lượng **ước tính ≈ 2,8 giờ** + khoảng 8-18 phút cho bước (e) (PT ~118 nghìn request, 107-330 request/s tùy độ bận của máy). Chưa đo trọn vẹn.
- Chỉ chạy khi máy rảnh và đã tắt chế độ ngủ. Muốn dọn ngay sau khi xong: thêm `--clean` (`bash tools/evaluation/run.sh full --clean`).

Trước khi chạy lượt đầy đủ chính thức, kiểm lần lượt:

1. **Xuất CSV SmashCourt trước** (mục 8) để phần RP có dữ liệu; thiếu file thì RP `skipped` và lượt đó không đủ chứng cứ cho hội đồng.
2. **Tự commit mọi thay đổi (mã, tài liệu, `check_docs.py`) trước khi chạy**, để kiểm mã của SC7 đạt và `manifest.json` ghi `dirty=false`: adapter `tools/evidence/hotel_adapter.py` chưa commit thì SC7 là `INSUFFICIENT_DATA`, còn thay đổi chưa commit ở `src/TADSS.Runtime.Api/**` hay `src/Shared.DecisionContractSDK/**` làm kiểm mã SC7 không đạt (việc commit do bạn tự làm). Cờ `dirty` chỉ được ghi vào manifest, EV-5 không đọc git. Thư mục `spec/` cố ý không commit (không stage) nên manifest vẫn ghi `dirty=true` vì `spec/`; điều đó **không** làm SC7 không đạt vì `spec/` nằm ngoài hai thư mục được kiểm.
3. Docker Desktop đang chạy, `docker network create smashcourt-network` đã có, máy cắm điện và đã tắt chế độ ngủ của Windows (cách làm ở mục 2).
4. **Đóng cửa sổ PowerShell sau khi chạy xong** (hoặc `Remove-Item Env:EVAL_MODE`), vì `$env:EVAL_MODE="full"` còn hiệu lực trong cửa sổ đó và lần `docker compose up -d --build` sau sẽ lại chạy bản đầy đủ ~2,8 giờ. Ở Git Bash, `EVAL_MODE=full docker compose ...` chỉ có hiệu lực cho đúng lệnh đó.

## 7. Chạy lại một bước

Dùng `run.sh --steps` khi chỉ muốn chạy lại vài bước (chữ cái từ a đến f, cách nhau bằng dấu phẩy); app vận hành không bị khởi động lại. `run.sh` từ chối chạy khi lượt do `docker compose up` khởi động còn đang chạy (hai lượt cùng lúc sẽ làm sai số đo).

| Muốn | Lệnh | Mất |
| :--- | :--- | :--- |
| Chỉ sự cố dưới tải, có báo cáo | `bash tools/evaluation/run.sh quick --steps c,f` | ~9 phút |
| Chỉ dữ liệu thực tế, có báo cáo | `bash tools/evaluation/run.sh quick --steps e,f` | 1-3 phút |
| Chỉ test | `bash tools/evaluation/run.sh quick --steps a` | 5-9 phút |
| Sai tham số (để thử) | `bash tools/evaluation/run.sh --steps x` | thoát mã 2, in lỗi |

Điều cần nhớ:
- **Mỗi lần chạy tạo một thư mục `evidence/<run_id>` mới**, không ghép với lượt trước. Muốn có `report.html` thì phải kèm `f` (ví dụ `c,f`). Báo cáo chỉ chứa các bước bạn vừa chạy, các tiêu chí còn lại ghi `INSUFFICIENT_DATA`.
- (a), (b), (c), (e) độc lập nhau. (d) đứng riêng vẫn chạy CT-03 nhưng bỏ phần coverage; muốn có coverage thì chạy `--steps a,d,f` (coverage lấy từ (a) của cùng lượt). (f) chỉ tổng hợp dữ liệu của lượt hiện tại.
- Có thể dùng biến môi trường thay cho cờ: `EVAL_STEPS=e,f bash tools/evaluation/run.sh`.
- Dựng lại `report.html` từ một pack đã có, **không** gửi lại request:

```bash
docker compose run --rm --no-deps --entrypoint python evaluator -m evidence.report /evidence/<run_id>
```

Thay `<run_id>` bằng tên thư mục trong `evidence/`. In EV-1…EV-5 từng dòng, `0` là đạt cả năm.

## 8. Thêm dữ liệu SmashCourt (tùy chọn)

Bước (e) phần RP cần CSV booking thật; không có thì RP bị bỏ qua và phần còn lại vẫn chạy.

1. Đặt chuỗi kết nối PostgreSQL của SmashCourt vào biến môi trường của **cửa sổ này** (không ghi vào file, không commit):

```bash
export SMASHCOURT_DATABASE_URL='postgresql://<user>:<mật khẩu>@<host>:5432/<db>?sslmode=require'
mkdir -p data/private
```

2. Chạy truy vấn xuất (chỉ đọc, không lấy tên, số điện thoại, email, ghi chú, mã booking). Nếu máy có `psql`:

```bash
psql "$SMASHCOURT_DATABASE_URL" -X -q --csv -v ON_ERROR_STOP=1 -f tools/evidence/sql/export_smashcourt_bookings.sql -o data/private/smashcourt_bookings.csv
```

   Chưa có `psql` thì dùng Docker (nếu database nằm trên máy bạn, đổi `localhost` trong URL thành `host.docker.internal`):

```bash
docker run --rm -i postgres:16.15-alpine psql "$SMASHCOURT_DATABASE_URL" -X -q --csv -v ON_ERROR_STOP=1 < tools/evidence/sql/export_smashcourt_bookings.sql > data/private/smashcourt_bookings.csv
```

3. Kiểm nhanh: số dòng phải bằng số booking + 1 dòng tiêu đề (215 booking thì ra 216), dòng đầu là tên cột:

```bash
wc -l data/private/smashcourt_bookings.csv
head -1 data/private/smashcourt_bookings.csv
```

4. Chạy lại riêng phần dữ liệu: `bash tools/evaluation/run.sh quick --steps e,f`. Trong bảng tóm tắt, dòng RP phải ghi `gửi N/N request ... 0 lỗi contract`.

Kiểm riêng câu truy vấn trên database dùng một lần (không cần kết nối của bạn, ~15 giây): `bash tools/evidence/sql/verify-export.sh`. Cuối cùng phải in `ĐẠT: verify-export`.

**Dữ liệu cá nhân.** `data/private/` đã nằm trong `.gitignore` và `.dockerignore`: **không commit, không gửi cho ai**. Evaluator mount thư mục này chỉ đọc và băm HMAC mọi định danh trước khi ghi bất cứ file nào vào `evidence/`.

## 9. Chạy test thủ công (không cần evaluator)

**Bạn không cần chạy mục này:** bước (a) của lượt đánh giá (`docker compose up -d --build`) đã chạy tất cả các lệnh dưới đây và ghi kết quả vào bảng tóm tắt. Mục này chỉ để tham khảo khi sửa mã và muốn kết quả nhanh cho riêng một phần.

| Việc | Lệnh | Thấy gì là đúng | Mất |
| :--- | :--- | :--- | :--- |
| Build | `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` | `0 Warning(s)`, `0 Error(s)` | ~15 s (lần đầu lâu hơn) |
| Unit test | `dotnet test tests/TADSS.UnitTests -c Release --no-build -nologo` | `Passed! - Failed: 0, Passed: 1874` | ~15 s |
| Integration test (cần Docker) | `dotnet test tests/TADSS.IntegrationTests -c Release --no-build -nologo` | `Passed! - Failed: 0, Passed: 240` (số của lần chạy trước; chưa chạy lại 12/10/2026 vì Docker tắt) | ~2 phút |
| Kiểm expose | `bash tools/expose/verify-expose.sh` | Bảng NG-1…SEC-6 toàn `PASS`, cuối `TẤT CẢ ĐẠT.` | ~1 phút (lần đầu tải image) |
| Kiểm observability | `bash tools/observability/verify-observability.sh` | OB-2, OB-4 `PASS`, cuối `TẤT CẢ ĐẠT.` | ~2 phút (lần đầu tải otel-lgtm 3,7 GB) |
| Kiểm truy vấn xuất | `bash tools/evidence/sql/verify-export.sh` | `ĐẠT: verify-export` | ~15 s |
| Kiểm tài liệu (DOC-1…3) | `python tools/docs/check_docs.py` | ba dòng `DOC-1: đạt`, `DOC-2: đạt`, `DOC-3: đạt`, thoát mã 0 (DOC-4 là một unit test .NET, đã nằm trong dòng Unit test) | ~1 s |

Ba bộ pytest của `tools/` (`evaluation`, `evidence`, `docs`) cần Python 3.11+ và một môi trường ảo (làm một lần; `.venv/` đã được gitignore):

```bash
python -m venv .venv && source .venv/Scripts/activate
pip install -r tools/evaluation/requirements.txt -r tools/evidence/requirements.txt
python -m pytest -q tools/evaluation
python -m pytest -q tools/evidence
python -m pytest -q tools/docs
```

Chạy từng bộ riêng (không gộp một lệnh pytest). Bộ `tools/docs` và `check_docs.py` chỉ dùng thư viện chuẩn của Python (cần `pytest` đã cài ở bước `pip` trên).

Thấy gì là đúng: `260 passed` cho `tools/evaluation` (khoảng 25 s; khi Docker tắt thì `257 passed, 3 skipped` vì 3 test sinh payload cần k6 qua Docker), `355 passed` cho `tools/evidence` (khoảng 5 s) và `34 passed` cho `tools/docs` (dưới 1 s). Lần sau chỉ cần `source .venv/Scripts/activate` rồi chạy pytest. Ba script `verify-*` tự dọn container của chúng khi thoát.

## 10. Sự cố thường gặp

Bảng này gom các lỗi đã gặp thật khi làm gói 5.5-5.6.

| Triệu chứng | Nguyên nhân | Cách xử lý |
| :--- | :--- | :--- |
| `tadss-runtime` khởi động lại liên tục, log `KeySha256 phải là chuỗi hex 64 ký tự` | Ô `KeySha256` trong `appsettings.Development.json` đang chứa key gốc thay vì mã băm của nó (chỉ phần vận hành lỗi; lượt đánh giá vẫn chạy) | `python tools/security/hash_api_key.py` (tự băm, key gốc chuyển vào `.env`, biến `TADSS_API_KEY_SMASHCOURT_BE`), rồi `docker compose up -d --build`. Chi tiết: README mục "Tạo API key và mã băm". |
| `network smashcourt-network declared as external, but could not be found` | Chưa tạo mạng dùng chung | `docker network create smashcourt-network` rồi `docker compose up -d --build` lại. |
| `Cannot connect to the Docker daemon` / `error during connect`, lệnh dừng ngay | Docker Desktop chưa bật | Mở Docker Desktop, chờ "Engine running", kiểm tra `docker info`, chạy lại. |
| Container bị dừng đột ngột, test/k6 chậm bất thường, máy đơ | Docker thiếu RAM (< 8 GB) | Tăng RAM cho Docker (mục 2), đóng ứng dụng khác, tắt stack Docker khác (`docker ps`). |
| Thời lượng bước dài gấp nhiều lần, biểu đồ lệch pha, FI-xx báo sai | Máy ngủ hoặc khựng giữa lượt, đồng hồ nhảy | Số đo của lượt đó **không dùng được**. Tắt chế độ ngủ (mục 2) và chạy lại. |
| `port is already allocated` ở cổng 3002 | Stack đánh giá lần trước còn chạy, hoặc ứng dụng khác dùng 3002 | `bash tools/evaluation/run.sh --clean-only`. Vẫn lỗi thì `netstat -ano \| grep :3002` để tìm tiến trình. |
| Mã thoát `2` | Hạ tầng lỗi (Docker, mạng, database của stack), tham số sai, hoặc một bước `error` làm các bước sau `skipped` | Đọc cột "Ghi chú" của bảng tóm tắt; `docker compose logs eval-tadss-1`; chạy lại bước lỗi theo mục 7. |
| Dựng image lần đầu thất bại (`pip` hoặc tải image đứt giữa chừng) | Mạng rớt khi build | Chạy lại đúng lệnh cũ; phần đã tải được lưu cache. |
| Bước (a) `failed`, đúng 1 test `ErrorHandlingOptionsTests...InvalidRetryAfter...` với `ObjectDisposedException` | Test không ổn định có sẵn (đua trong `WebApplicationFactory`, thỉnh thoảng ~1/10 lượt khi máy bận) | Chạy lại `bash tools/evaluation/run.sh quick --steps a`. Đỏ lặp lại hoặc test khác đỏ thì là lỗi thật. |
| (b) ghi chú `k6 bỏ N lượt` (dropped_iterations) | k6 hết VU nên bỏ lượt. Evaluator ghi chú theo từng pha: `ở pha chính` nghĩa là tải thực tế thấp hơn danh nghĩa (xem `summary.json`, số đo của kịch bản đó cần đọc kèm ghi chú); `ở warm-up, 0 ở pha chính` nghĩa là pha chính vẫn đủ tải, không ảnh hưởng số đo | Chỉ cần xử lý khi bỏ lượt ở pha chính: tắt ứng dụng nặng (mục 2) rồi chạy lại `--steps b,f`. |
| (c) báo FI-07 không đạt, thiếu 2/1262 request, mỗi cái ~1,6 s | Máy khựng ~1,5 s giữa pha Redis chậm (đã gặp 1 trong 6 lượt) | Chạy lại `--steps c,f`. Lặp lại nhiều lần thì kiểm tải máy (mục 2). |
| (c) SCALE-2 có vài `503 PERSISTENCE_UNAVAILABLE` ngay sau LT-03 | Instance 2 cần ~15 s nối lại PostgreSQL sau khi bị cắt; SCALE-2 chờ `/health/ready` tối đa 90 s trước khi chạy | Thường tự khỏi. Ghi chú nói "chưa sẵn sàng" thì chạy lại `--steps c,f`. |
| (e) PT `skipped` "không tải được dataset" | Không có mạng tới `raw.githubusercontent.com` | Kiểm mạng rồi chạy lại `--steps e,f`. Dataset được cache trong volume `eval-datasets`, lần sau không tải lại. |
| (e) PT `failed` "sai SHA-256" | File tải về bị hỏng hoặc nguồn đổi | Xóa cache `docker volume rm tadss_eval-datasets` và chạy lại. Vẫn sai: **không** sửa SHA, báo lại vì nguồn đã đổi. |
| (e) RP `skipped` "thiếu hoặc rỗng smashcourt_bookings.csv" | Chưa có CSV SmashCourt | Làm mục 8. Không bắt buộc: RP-01/02 sẽ ghi `INSUFFICIENT_DATA`. |
| SC7 `INSUFFICIENT_DATA` dù PT 0 lỗi contract | Kiểm "không sửa mã runtime" cần `tools/evidence/hotel_adapter.py` đã commit | Bạn commit adapter (theo quy trình git của bạn), rồi chạy lại `--steps e,f`. |
| Báo cáo thiếu mục / nhiều `INSUFFICIENT_DATA` | Lượt chỉ chạy một phần các bước | Bình thường (mục 7). Chạy đủ bước (`docker compose up -d --build` hoặc `run.sh` không `--steps`) để có đủ KPI. |
| `run.sh` báo "Evaluator do 'docker compose up' khởi động vẫn đang chạy" | Lượt tự chạy khi `up` chưa xong | Chờ lượt đó xong (`docker compose logs -f evaluator`) rồi chạy lại. |
| Ổ đĩa đầy, `docker system df` báo build cache vài GB | Build cache và image cũ tích tụ | `bash tools/evaluation/run.sh --clean-only` (mục 11). |
| Xóa xong Docker nhưng Windows vẫn thiếu ổ | Ổ ảo của Docker Desktop không tự co lại | Làm phần "Trả dung lượng" ở mục 11. |

## 11. Dọn Docker

Dọn sau khi xong việc để trả lại ổ đĩa. Hai mức:

```bash
docker compose down                         # dừng tất cả (app, ngrok, Grafana, stack đánh giá), giữ image và cache dataset
docker system df
bash tools/evaluation/run.sh --clean-only   # chỉ dọn phần đánh giá, app vận hành giữ nguyên
docker system df
```

- `--clean-only` gỡ 9 container của stack đánh giá cùng volume của nó (cache NuGet, file tiêm lỗi, cache dataset), xóa image của chế độ đánh giá (evaluator, runtime đánh giá, postgres, redis, nginx, toxiproxy, ryuk; `otel-lgtm` chỉ xóa khi Grafana vận hành không còn dùng), image treo và **toàn bộ build cache** (~6 GB). Lần chạy sau phải tải/build lại.
- `postgres`, `redis` cũng dùng chung với stack khác: nếu stack đó đang cần, chúng sẽ được tải lại khi dùng.
- Muốn dọn ngay sau một lượt: `bash tools/evaluation/run.sh quick --clean`.
- **Không dùng `docker system prune`**: nó xóa cả mạng `smashcourt-network` mà SmashCourt-BE cần.

**Trả dung lượng cho Windows.** Xóa image chỉ giải phóng bên trong ổ ảo của Docker; file ổ ảo không tự nhỏ lại. Hai cách:
1. Docker Desktop > Troubleshoot > **Clean / Purge data**. Cách này xóa **toàn bộ** image, container, volume của mọi dự án trên Docker.
2. Tắt Docker Desktop, mở PowerShell **quyền Administrator**: `wsl --shutdown`, rồi thu nhỏ file `docker_data.vhdx` (thường ở `%LOCALAPPDATA%\Docker\wsl\disk\`). Có Hyper-V thì dùng `Optimize-VHD -Path <đường dẫn vhdx> -Mode Full`; Windows Home không có lệnh này, dùng `diskpart` (`select vdisk file="<đường dẫn vhdx>"`, `attach vdisk readonly`, `compact vdisk`, `detach vdisk`, `exit`).
