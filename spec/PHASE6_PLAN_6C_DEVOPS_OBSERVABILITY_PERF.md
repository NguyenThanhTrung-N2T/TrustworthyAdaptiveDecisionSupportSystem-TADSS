# PHASE 6 — EPIC 6C: DEVOPS, QUAN SÁT VÀ KIỂM THỬ (BE + TADSS + AI) — PLAN CHỐT

| | |
| :--- | :--- |
| **Trạng thái** | **Bản 5 (31/10/2026) — bản chốt để duyệt và bắt đầu làm.** Bản 1 thiết kế; bản 2 sửa theo phản biện (Mục 12); bản 3 gộp chi phí, danh mục test, dashboard; bản 4 rút gọn và chia task nhỏ (Mục 7); bản 5 bổ sung **SmashCourt-AI** (triển khai 4 repo); bản 5.1 ghi câu trả lời Q1–Q7 của người dùng (Mục 11): cảnh báo qua Gmail, hai môi trường `stable`/`preview`, SLO tách theo hệ. Thiết kế chi tiết để tra khi làm: Phụ lục B. Chưa code gì |
| **Phạm vi** | **4 repo**: `SmashCourt-BE`, `TADSS-Runtime`, `SmashCourt-AI` (đóng gói image, CI/CD, quan sát, kiểm thử) và `smashcourt-fe` (chỉ workflow triển khai Vercel, không đụng mã giao diện) |
| **Thứ tự** | Giai đoạn 0 chuẩn bị ⇒ **giai đoạn 1 DevOps** ⇒ giai đoạn 2 kiểm thử |
| **Lịch** | Song song Epic 6B (FE), W09–W12 (02/11 – 29/11/2026) |
| **Mã quyết định** | P6-32…P6-56, nối tiếp [PHASE6_DESIGN_SPEC.md](PHASE6_DESIGN_SPEC.md); chép vào spec Phase 6 và methodology ở task 1.42 |

---

## 0. Tóm tắt

| Hệ thống | Gồm | Chạy ở | Ai bật |
| :--- | :--- | :--- | :--- |
| **Triển khai (CD)** | Push `master` (BE, TADSS, AI) ⇒ môi trường **`stable`**; push `develop` (BE, TADSS) ⇒ môi trường **`preview`**; mỗi lần: CI build + push image tag sha lên GHCR ⇒ runner kéo đúng sha ⇒ health + smoke ⇒ lỗi thì tự quay về; FE `master` lên Vercel production; dashboard **Vận hành** 3 tab (BE, TADSS, AI), lọc theo môi trường, cảnh báo qua **Gmail**; ngrok **chỉ cho BE của `stable`** | Máy nhóm, PostgreSQL cục bộ (mỗi môi trường một database) | Tự động |
| **Kiểm thử** | Môi trường `perf` kéo **cùng image** theo sha (BE, TADSS, AI với LLM giả); k6 chạy SP-01…10; dashboard **Kiểm thử** 3 tab: tầng 1 chỉ ô tổng hợp theo nhóm test, bấm ô đỏ mới ra chi tiết mã hỏng | **Chỉ local**, DB cục bộ, dữ liệu tổng hợp, không qua ngrok, không gọi Gemini thật | Người dùng tự bật |

Chi phí tiền: **0 đồng** (Mục 2).

---

## 1. Quyết định

| Mã | Nội dung | Trạng thái |
| :--- | :--- | :--- |
| P6-32 | Phạm vi **BE + TADSS + AI**; FE chỉ workflow Vercel (bản 5 thêm AI) | Đã chốt |
| P6-33 | Build một lần: image không bí mật, tag `sha-<7>` bất biến trên GHCR (public); container `Production` + `DEPLOY_ENVIRONMENT`; triển khai và đo theo sha/digest; sha của container kiểm bằng nhãn OCI của image (`docker inspect`), không thêm endpoint vào TADSS (hợp đồng v2 đóng băng) | Đã chốt |
| P6-34 | Bí mật ở GitHub Environment secrets, truyền bằng biến môi trường lúc chạy; BE kiểm cấu hình bắt buộc lúc khởi động | Đã chốt |
| P6-35 | CD qua GitHub runner tự host trên máy nhóm, **không có repo private riêng** (người dùng chốt 31/10: chỉ sửa CI/CD và GitHub Actions trong các repo hiện có). Mỗi repo (BE, TADSS, AI) có `ci.yml` (build, test, push image) và `deploy.yml` (kéo image theo sha về máy, health, smoke, quay về); máy cài runner cho cả ba repo (tài khoản cá nhân: mỗi runner gắn một repo). Chặn rủi ro runner trên repo public bằng ba lớp: (1) Settings › Actions bật **Require approval for all outside collaborators** ở cả ba repo; (2) `deploy.yml` chỉ chạy khi push `master`/`develop` hoặc chạy tay, không bao giờ theo pull request; (3) Environment `stable` (chỉ nhánh `master`) và `preview` (chỉ `develop`) giữ secrets, nhánh khác không lấy được. Hai môi trường: `stable` ⇐ `master` của BE, TADSS, AI (ngrok cho BE, nhận VNPay, demo) và `preview` ⇐ `develop` của BE, TADSS (dùng chung AI của `stable`; không URL công khai; thử thanh toán bằng IPN giả lập); chung một container PostgreSQL nhưng khác database; khi bỏ `develop` của TADSS thì gỡ TADSS khỏi `preview`. Phần dùng chung (PostgreSQL, Redis, bộ quan sát vận hành) đặt ở `TADSS-Runtime/deploy/`. Chỉ khởi động lại service vừa đổi; health 2 mức + sha + smoke chỉ đọc; quay về có kiểm lại; `UNSAFE` + cảnh báo. **Thay 01/11/2026 (người dùng chốt lại, xem dòng "Chốt lại (01/11, người dùng)" ở mục "Giai đoạn 1D bước 1 — Môi trường deploy và nạp dữ liệu"): chỉ một bản deploy, mọi tệp ở `SmashCourt-BE/deploy/`; không còn `stable`/`preview` và không có `TADSS-Runtime/deploy/`.** | Đã chốt, sửa 01/11 |
| P6-36 | FE: `lint` ⇒ `tsc` ⇒ `build` ⇒ Vercel; **chỉ `master` triển khai** (production); `develop` chỉ chạy CI (người dùng chốt 31/10) | Đã chốt |
| P6-37 | Hai bộ quan sát tách biệt: vận hành (`TADSS-Runtime/deploy/`) và kiểm thử (`TADSS-Runtime/perf/`). **Thay 01/11/2026: bộ vận hành nằm ở `SmashCourt-BE/deploy/` (Alloy ⇒ Grafana Cloud), bộ kiểm thử vẫn ở `TADSS-Runtime/perf/`.** | Đã chốt, sửa 01/11 |
| P6-38 | OpenTelemetry chuẩn chung; mỗi bộ một Collector làm cổng OTLP; metric/log/trace tách rõ | Đã chốt |
| P6-39 | k6 là nguồn chuẩn phía client; Prometheus/Grafana để quan sát, phân rã; đường metric k6 xác minh bằng spike | Đã chốt |
| P6-40 | Lượt chính thức tắt trace, log Warning+; trace chỉ ở lượt phân tích; đo overhead bằng A/A | Đã chốt |
| P6-41 | Môi trường `perf` riêng, chỉ local, image theo sha, giới hạn tài nguyên | Đã chốt |
| P6-42 | Dữ liệu ba lớp: tham chiếu (thống kê 215 booking), benchmark (sinh tất định), thật (không nạp) | Đã chốt |
| P6-43 | Đo riêng tác động TADSS: cặp bật/tắt cùng điều kiện và trạng thái dữ liệu; phân rã theo trung bình + trace, không cộng percentile | Đã chốt |
| P6-44 | ≥ 3 lượt mỗi cấu hình, trung vị + khoảng, thứ tự ABBA | Đã chốt |
| P6-45 | Ranh giới tin cậy CD: `deploy.yml` mỗi repo chỉ triển khai **service của chính repo đó**, image cố định trong workflow, sha lấy từ lần chạy CI vừa xong (`workflow_run`, chỉ khi CI đạt) hoặc nhập tay khi chạy tay (kiểm đúng 40 ký tự hex); môi trường suy từ nhánh (BE/TADSS `master` ⇒ `stable`, `develop` ⇒ `preview`; AI `master` ⇒ `stable`; nhánh khác ⇒ từ chối); kiểm commit thuộc đúng nhánh + nhãn revision của image bằng sha; secrets chỉ ở job triển khai; không còn token giữa các repo | Đã chốt |
| P6-46 | Khôi phục DB + xóa Redis + kiểm checksum trước mỗi lượt đo | Đã chốt |
| P6-47 | Lượt `INFRA_LIMITED` / `ABORTED` không dùng để kết luận năng lực ứng dụng | Đã chốt |
| P6-48 | Chi phí 0 đồng: hai môi trường triển khai (`stable`, `preview`) dùng PostgreSQL + Redis cục bộ (Neon gói miễn phí không chịu được chạy liên tục); **ngrok chỉ cho hệ thống triển khai; kiểm thử chỉ chạy local**; Neon giữ cho dev, E2E, demo. **Thay 01/11/2026: chỉ một bản deploy (không còn `stable`/`preview`), vẫn PostgreSQL + Redis cục bộ.** | Đã chốt, sửa 01/11 |
| P6-49 | Dashboard Grafana 13.x, mỗi bộ **3 tab BE / TADSS / AI** (bản 5: thêm AI); dashboard vận hành có biến lọc môi trường (`stable`, `preview`; từ 01/11/2026 chỉ còn một bản deploy); cảnh báo gửi qua **Gmail** (SMTP Gmail, mật khẩu ứng dụng lưu ở secrets); mọi panel có mô tả 6 ý, có test CI chặn panel thiếu mô tả; dự phòng hai dashboard liên kết nếu tab chưa ổn | Đã chốt |
| P6-50 | Danh mục test: **một nguồn chi tiết** là `tests/catalog.yaml` (máy đọc, khớp methodology); plan và dashboard chỉ hiện tổng hợp theo nhóm; chi tiết chỉ mở khi có lỗi (Mục 5); chỉ thêm không bỏ, trùng thì gộp (LT-04 ⇒ SP-01) | Đã chốt |
| **P6-51** | **SmashCourt-AI và ba tài khoản ngrok** (cập nhật 31/10 theo người dùng): mỗi service có tài khoản ngrok riêng — BE, TADSS (token mới, người dùng tự đặt), AI (giữ `ngrok-ai`); BE gọi TADSS và AI bằng URL đặt trong `.env` của BE (`TADSS_BASE_URL` + `TADSS_API_KEY`; `AIService:BaseUrl`), đổi URL không phải sửa code. AI vẫn chưa có xác thực giữa BE và AI nên URL ngrok của AI cho phép người ngoài dùng quota Gemini ⇒ **SEC-AI-2 (khóa xác thực BE ⇒ AI) đưa lên làm sớm**; smoke của AI chỉ gọi `/health`; bài đo dùng LLM giả qua `GEMINI_BASE_URL`; model đổi chỉ trong `.env` (đã đổi sang `gemini-3.6-flash` 31/10, kiểm model tồn tại) | Đã chốt |
| **P6-52** | **SLO tách theo hệ** (người dùng chốt 31/10): bảng SLO ở task 2.18 là của **BE** (trải nghiệm người dùng gọi qua BE); **TADSS giữ SLO riêng đã có**: SC1 (quyết định bình thường p95 < 150 ms), SC2 (khi strategy lỗi p99 ≤ 700 ms, ngân sách pipeline 500 ms); **AI** p95 < 300 ms cho phần xử lý của AI (không gồm độ trễ LLM giả), lỗi < 0,5 %, chốt ở 2.18 (02/11, cách đo SP-10A/B: B.7.4). Tab nào chấm theo SLO của hệ đó | Đã chốt |
| **P6-53** | **Bỏ index trùng của `bookings`** (người dùng chốt 05/11, phát hiện ở giai đoạn 2C): `idx_bookings_status_date (status, booking_date)` trùng khóa với `idx_booking_status_date` (cùng khóa, có `INCLUDE (id, branch_id, created_at)`, giữ lại); bản trùng không giúp truy vấn nào nhưng mọi lần ghi phải cập nhật và mọi lần lập kế hoạch phải khóa nó. Script `SmashCourt-BE/docs/database/phase6_drop_duplicate_booking_index.sql` (kiểm index giữ lại trước khi bỏ, chạy lại được), **đã áp `tadss-dev` 05/11** (trước khi áp `schema_snapshot.py` chỉ lệch đúng index này; sau khi áp "đã áp đủ", giống hệt `schema_smashcourt.sql`), **đã áp PostgreSQL của bản deploy (`smashcourt-postgres`, DB `smashcourt`) 06/11** (chỉ còn `idx_booking_status_date`, BE không cần khởi động lại vì bản deploy không bật auto-prepare), `main` sau Gate M6; môi trường perf (`perf-postgres`, DB `smashcourt` + mẫu `smashcourt_seed`, dựng từ volume cũ trước khi sửa `schema_smashcourt.sql`) **giữ index trùng tới hết lần tắt 3** (người dùng chốt 06/11) để SP-01 sau P6-54 chỉ khác lượt trước đúng một yếu tố (auto-prepare), áp sau đó; `schema_smashcourt.sql` + bản sao test BE đã cập nhật; `schema_snapshot.py` kiểm "đã bỏ" và index giữ lại | Đã chốt |
| **P6-54** | **Auto-prepare của Npgsql cho BE** (người dùng duyệt 05/11, nguồn: thí nghiệm E4): `Database:MaxAutoPrepare` (`Extensions/NpgsqlAutoPrepareExtensions`, dùng chung `Program.cs` + fixture integration), **mặc định 0 = tắt**; **người dùng chốt 05/11: chỉ kiểm thử và đánh giá hiệu năng bật (perf `--db-prepare 50`, test auto-prepare), bản deploy KHÔNG bật** (test `DeployConfiguration_DoesNotEnableAutoPrepare` khóa `deploy/docker-compose.yml` và `appsettings.json`; mặc định perf 0 = như deploy); chuỗi kết nối ghi rõ `Max Auto Prepare` thì thắng; giá trị ngoài 0–256 dừng lúc khởi động. Neon `tadss-dev` (BE dev) đi qua `-pooler` (PgBouncer chế độ transaction, có hỗ trợ prepared statement mức giao thức nhưng Npgsql yêu cầu `No Reset On Close` khi qua PgBouncer) ⇒ giữ tắt tới khi kiểm riêng. **Quy tắc vận hành:** chạy script đổi kiểu cột xong phải khởi động lại BE (câu đã prepare lỗi `0A000 cached plan must not change result type`; thêm/bỏ index thì PostgreSQL tự lập lại kế hoạch). Test: 11 unit (mặc định, giá trị, chuỗi kết nối thắng, giá trị sai), 5 integration trên PostgreSQL 17.6 (kế hoạch chung + kết quả đúng; khóa câu chi tiết booking 65 ⇒ 24 từ lần chạy thứ 11; giữ câu đã prepare khi kết nối về pool; bỏ index không lỗi; đổi kiểu cột lỗi), bộ integration + HTTP chung chạy đúng cấu hình deploy (tắt), riêng `NpgsqlAutoPrepareTests` dùng scope cấu hình perf. **Hệ quả cho báo cáo:** số "sau" của P6-54 là cấu hình đánh giá, không phải cấu hình bản deploy đang chạy. Phương án bật khi E2E Phase 7 không đạt hiệu năng: Mục 10.1 (điều kiện, 5 bước kiểm, nguồn) | Đã chốt |
| **P6-55** | **Hệ số bcrypt 12 ⇒ 11** (người dùng chốt 11/11, nguồn: task 2.27): mọi hash mới (đăng ký, quên / đặt lại / đổi mật khẩu, tạo và reset tài khoản nhân viên) đi qua `PasswordHelper.HashPassword` với `PasswordHelper.BcryptWorkFactor = 11` (trước đó 6 chỗ viết cứng 12, `ProfileService` đọc khóa `BCrypt:WorkFactor` không có trong cấu hình nên cũng là 12; khóa này bỏ). Lý do: kiểm mật khẩu chiếm 96 % thời gian Đăng nhập và khoảng 45 % CPU BE ở tải danh nghĩa; 11 nhanh khoảng 2 lần, vẫn trên mức tối thiểu 10. **Không băm lại lúc đăng nhập** (người dùng chốt): hash cũ hệ số 12 vẫn kiểm được (hệ số nằm trong hash) và chỉ thành 11 khi người dùng đổi / đặt lại mật khẩu, nên tài khoản cũ đăng nhập chưa nhanh hơn. Test: `PasswordHelperTests` (băm rồi kiểm đúng mật khẩu, mỗi hash có muối riêng, hash 12 cũ vẫn kiểm được). Dữ liệu perf (`datagen.py`) đổi sang hash 11 | Đã chốt |
| **P6-56** | **Bật / tắt TADSS trên bản deploy khi đang chạy, không khởi động lại BE** (người dùng chốt 12/11: bật cờ để kiểm BE + TADSS trước Phase 7, chưa kiểm FE; cần thao tác đơn giản như một nút để việc đặt sân không bị gián đoạn): BE đọc thêm `runtime-config/feature-flags.json` làm nguồn cấu hình cuối, tự nạp lại khi đổi (`Extensions/RuntimeConfigExtensions.cs`, không có thư mục thì không thêm nguồn); cờ đọc qua `IOptionsMonitor` như cũ; `TadssFlagChangeLogger` ghi Warning mỗi lần cờ đổi. Bản deploy gắn volume `smashcourt-deploy-be-runtime-config` vào `/app/runtime-config` (giữ cờ qua các lần CD), không ghim cờ bằng biến môi trường; `deploy/tadss-flag.sh on|off|status` ghi file (tạm rồi `mv`) và đợi dòng log xác nhận. Loại: biến môi trường (phải tạo lại container, không đổi nóng như đề cương Mục 7 cam kết). Hệ quả đã báo người dùng khi bật: FE chưa có màn duyệt (6B), đơn `PENDING_APPROVAL` / `BOOKING_REJECTED` hiển thị sai trên FE; strategy 2.1.0 từ chối khoảng 40 % lần đặt với dữ liệu perf; rủi ro C1 nếu FE production trỏ vào bản deploy. Test: `RuntimeConfigExtensionsTests` (9), `TadssFlagChangeLoggerTests` (7), thuộc BE-IT-19. Hướng dẫn: RUNBOOK mục 2.4 |

---

## 2. Chi phí (kiểm 31/10/2026 theo trang giá chính thức)

| Thành phần | Chi phí | Giới hạn cần nhớ |
| :--- | :--- | :--- |
| GitHub Actions (repo public), runner tự host | 0 | Repo public miễn phí; runner tự host miễn phí |
| GHCR (package public) | 0 | Private mới có giới hạn 500 MB / 1 GB truyền |
| Vercel Hobby | 0 | Chỉ phi thương mại; 100 lần triển khai/ngày |
| ngrok Free (3 tài khoản: BE, TADSS, AI) | 0 | Mỗi tài khoản 20.000 request/tháng, 1 GB, 1 domain; BE gọi TADSS/AI qua ngrok cũng tính vào quota của TADSS/AI; trang cảnh báo với request trình duyệt ⇒ FE phải gửi header `ngrok-skip-browser-warning` |
| Neon Free (dev, E2E, demo) | 0 | 100 CU-giờ/project/tháng dùng chung mọi nhánh; Hangfire làm Neon không tạm dừng ⇒ không dùng cho staging chạy liên tục |
| Gemini API (AI) | 0 | Gói miễn phí có giới hạn tốc độ (xem trong AI Studio); dữ liệu gửi qua gói miễn phí được Google dùng để cải thiện sản phẩm ⇒ không gửi dữ liệu cá nhân của khách vào prompt nếu không cần; `gemini-2.0-flash` đã deprecate ⇒ dùng model còn trong gói miễn phí (vd `gemini-3.6-flash`) |
| Prometheus, Grafana, Collector, Tempo, Loki, exporter, Pushgateway | 0 | Tốn RAM/CPU máy (Mục 6) |
| Cảnh báo qua Gmail | 0 | Dùng chung giới hạn khoảng 500 thư/ngày của Gmail; nên dùng một tài khoản Gmail riêng cho cảnh báo (mật khẩu ứng dụng, cần bật xác minh 2 bước) để không chia quota với thư nghiệp vụ |

Chi phí không phải tiền: máy phải bật để CD chạy; khi đo chính thức phải tắt `stable` và `preview`.

---

## 3. Kiến trúc

```
GitHub public: SmashCourt-BE, TADSS-Runtime, SmashCourt-AI, SmashCourt-FE
   │ CI: build, test, quét lỗ hổng, push ghcr.io/…:sha-<7>        (FE: lint/tsc/build ⇒ Vercel)
   ▼
deploy.yml của từng repo ── runner tự host (máy nhóm, đăng ký cho cả ba repo)
   │ kiểm P6-45 ⇒ pull ⇒ up --no-deps --wait ⇒ health ⇒ sha ⇒ smoke ⇒ quay về nếu lỗi
   ▼
Máy nhóm (Docker)
 ├─ stable  (master):  BE, TADSS, AI (nội bộ), ngrok (chỉ BE)           (image theo sha)
 │                       AI ──► Gemini API
 ├─ preview (develop): BE, TADSS (dùng chung AI của stable, không URL công khai)
 │    chung: PostgreSQL SmashCourt (database stable + preview), PostgreSQL TADSS (2 database), Redis (2 prefix)
 │    └─ quan sát vận hành: Collector, Prometheus (15 ngày), Loki, Grafana 13, cAdvisor; cảnh báo Gmail
 └─ perf (local, bật tay): BE, TADSS, AI (cùng image), LLM giả, PostgreSQL ×2, Redis, Mailpit, k6
      └─ quan sát kiểm thử: Collector, Prometheus, Tempo, Loki*, Grafana 13,
         postgres_exporter ×2, redis_exporter, cAdvisor, Pushgateway (nạp kết quả test)
```

Chế độ đánh giá của TADSS (`docker compose up -d --build`, D-14) giữ nguyên; kết quả của nó được nạp lên dashboard kiểm thử.

---

## 4. Hạng mục test (tổng hợp theo nhóm)

Chi tiết từng mã nằm ở **một chỗ**: `tests/catalog.yaml` (task 2.1) sinh từ [methodology](../docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) Mục 4. Bảng dưới chỉ để biết đủ nhóm hay chưa.

| Nhóm | Mã | Hệ thống | Trạng thái 31/10 |
| :--- | :--- | :--- | :--- |
| Unit / Integration TADSS | UT-01…15, IT-01…15 | TADSS | Đạt |
| Contract | CT-01…04 | TADSS, BE | Đạt |
| Mutation | MT-01…03 | TADSS | Đạt |
| Chịu lỗi TADSS / scale | FI-01…10, SCALE-1…3 | TADSS | Đạt |
| Tải TADSS | LT-01, 02, 03, 05 | TADSS | Đạt (chạy lại trên 2.1.0) |
| Bảo mật / expose / quan sát / tài liệu TADSS | SEC-1…7, NG-1…5, OB-1…5, DOC-1…4 | TADSS | Đạt |
| Đánh giá trên dữ liệu | RP-01…02, PT-01…04 | TADSS | Chờ CSV / lượt đầy đủ |
| Unit / Integration BE | BE-UT-01…13, BE-IT-01…26 | BE | Đạt |
| Bất biến booking | SC6 (CI + môi trường triển khai) | BE + TADSS | CI đạt; trên môi trường triển khai chưa chạy |
| Lint và cú pháp AI | AI-LINT (ruff, compileall — đã có trong CI AI) | AI | Đạt |
| **Mới:** unit AI | AI-UT-01…05 (dựng prompt, đọc/kiểm response của LLM bằng LLM giả, xử lý lỗi/timeout/429 của Gemini, kiểm schema vào/ra, `/health`) | AI | Giai đoạn 1 (repo AI chưa có test nào) |
| **Mới:** hợp đồng BE ↔ AI | AI-CT-01 (DTO `SmashCourt-BE/Integrations/AI/DTOs` khớp `app/models/ai_schemas.py`, snake_case) | BE + AI | Giai đoạn 1 |
| FE | FE-01…12 | FE | Chưa làm (6B) |
| E2E | E2E-01…14 | Toàn hệ thống | Chưa chạy (Phase 7–8) |
| Chịu lỗi tích hợp | FI-11…14 | BE ↔ TADSS | Chưa chạy |
| **Mới:** đóng gói và CD | OPS-01…10 | BE + TADSS + AI | Giai đoạn 1 |
| **Mới:** quan sát BE | OB-BE-1…4 | BE | Giai đoạn 1 |
| **Mới:** quan sát AI | OB-AI-1…3 (metric FastAPI + lời gọi Gemini: độ trễ, token, lỗi, 429; trace BE ⇒ AI; dashboard tab AI) | AI | Giai đoạn 1 |
| **Mới:** bảo mật BE | SEC-BE-1…7 | BE | SEC-BE-7 ở giai đoạn 1, còn lại làm sau |
| **Mới:** bảo mật AI | SEC-AI-1 (AI không lộ ra ngoài: không ngrok, không cổng host), SEC-AI-2 (xác thực BE ⇒ AI, làm sau), SEC-AI-3 (khóa Gemini không vào image/log), SEC-AI-4 (quét gói Python có lỗ hổng trong CI) | AI | SEC-AI-1, 3, 4 ở giai đoạn 1; SEC-AI-2 làm sau |
| **Mới:** hiệu năng hệ thống | SP-01…09 (SP-01 gộp LT-04), SP-10 (AI với LLM giả) | BE + TADSS + AI | Giai đoạn 2 |
| KPI | SC1…SC8 | — | Tổng hợp từ các nhóm trên |

Khoảng 205 hạng mục; không bỏ mục nào.

---

## 5. Dashboard

### 5.1. Quy tắc ghi chú (mọi panel)
Mô tả 6 ý: đo gì; đơn vị; cách đọc; ngưỡng và nguồn ngưỡng; metric/file nguồn; mã test liên quan. Mỗi tab có panel chữ "Cách đọc tab này". Test CI `DashboardDescriptionTests` chặn panel thiếu mô tả, thiếu đơn vị, thiếu mã test, hoặc tham chiếu metric không tồn tại.

### 5.2. "Vận hành" (3 tab)
| Khu vực | Tab BE | Tab TADSS | Tab AI |
| :--- | :--- | :--- | :--- |
| Tổng quan | Sẵn sàng, phiên bản đang chạy, lần triển khai gần nhất | Như BE | Như BE |
| RED | Request/giây, lỗi 5xx, p95/p99 theo nhóm endpoint | Như BE | Như BE (theo router: chat, suggest, analytics) |
| Nghiệp vụ | Đơn theo nhánh, kết quả gọi TADSS, kết quả gọi AI, IPN, job hết hạn | Quyết định, đường lùi, hàng chờ, override, outcome, strategy version | Lời gọi Gemini: độ trễ, token vào/ra, lỗi, bị giới hạn tốc độ (429), model đang dùng |
| Phụ thuộc | DB, pool, BE gọi TADSS và AI (`/health/dependencies`) | DB, Redis, cache, breaker | Gemini API |
| Tài nguyên | CPU, RAM, GC, thread pool | Như BE | CPU, RAM container |
| Gói miễn phí | Request ngrok trong tháng / 20.000 | — | Lời gọi Gemini trong ngày, số lần 429 |

Cảnh báo (gửi Gmail, ghi rõ môi trường): BE, TADSS hoặc AI không sẵn sàng 2 phút; 5xx > 5 % trong 5 phút; triển khai thất bại / `UNSAFE`; đường lùi TADSS > 20 % trong 10 phút; ngrok > 80 % quota; Gemini trả 429 liên tục 10 phút.

### 5.3. "Kiểm thử" (3 tab BE / TADSS / AI, ba tầng)
- **Tầng 1 (mặc định):** mỗi nhóm test ở Mục 4 là **một ô**: số mã đạt / tổng, màu xanh (đạt hết), đỏ (có mã hỏng), vàng (`INFRA_LIMITED` hoặc chưa đủ lượt), xám (chưa chạy), kèm commit và thời điểm chạy gần nhất. Hàng đầu là KPI SC1–SC8. Hiệu năng (SP) có thêm một hàng biểu đồ chính: p95/p99 client cạnh server, RPS, lỗi, tài nguyên.
- **Tầng 2 (bấm ô):** bảng mã của nhóm đó, **mặc định chỉ hiện mã hỏng**: mã, tên, lỗi rút gọn, commit, link báo cáo gốc, link mô tả trong methodology.
- **Tầng 3:** báo cáo gốc (trx, HTML Playwright, summary k6, Stryker, JSON SC6, trace).

Nguồn: bộ nạp kết quả đọc file kết quả (kể cả tải từ CI bằng `gh run download`) ⇒ Pushgateway; nhãn mã/nhóm lấy từ `tests/catalog.yaml`.

---

## 6. Tài nguyên (Docker 8 CPU / 8 GB)

| Bộ | CPU | RAM |
| :--- | ---: | ---: |
| `stable` + `preview` + quan sát vận hành | ~6,5 | ~5,6 GB |
| Perf + quan sát kiểm thử + k6 (gồm AI và LLM giả) | ~10 (trần) | ~7,2 GB |

Hai bộ không chạy cùng lúc khi đo chính thức (tắt cả `stable` lẫn `preview`). Khi không đo, có thể tắt `preview` lúc không cần xem trước để nhẹ máy. Giới hạn từng container ghi trong compose, chỉnh sau spike (task 2.16). Chi tiết: Phụ lục B.6.3.

---

## 7. Task

Quy ước: **A** = agent (Claude) làm; **N** = người dùng làm; **A+N** = cả hai. Ước lượng tính bằng giờ công. Task có code làm theo TDD, build + test xanh trước khi báo xong, không tự commit. Mỗi nhóm task có một cổng kiểm cuối.

### Giai đoạn 0 — Chuẩn bị

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 0.1 | Quét bí mật trong lịch sử git của BE | A | — | 1 | Báo cáo file/commit/loại khóa (không in giá trị) |
| 0.2 | Quét bí mật lịch sử TADSS, AI và FE; kiểm `data/private`, evidence chưa ẩn danh, `.env` của AI chưa từng commit | A | — | 2 | Như 0.1 |
| 0.3 | Đổi khóa nếu bị lộ; quyết định hai file `.docx` | N | 0.1, 0.2 | 0,5–2 | Không còn khóa đang dùng nằm trong lịch sử |
| 0.4 | Chuyển TADSS public; bật Dependabot alerts, secret scanning | N | 0.3 | 0,5 | Repo public |
| 0.5 | Ở **mỗi repo BE, TADSS, AI**: tạo Environment `stable` (chỉ nhánh `master`) và `preview` (chỉ `develop`; AI không cần); bật **Require approval for all outside collaborators** trong Settings › Actions | N | — | 0,75 | Environment và luật duyệt có ở cả ba repo |
| 0.6 | Ghim phiên bản: Grafana 13.x, Prometheus 3.x, Collector contrib, Tempo, Loki, exporter, cAdvisor, Pushgateway, k6 2.3.0 | A | — | 1 | Bảng phiên bản trong plan con |
| 0.7 | Spike: Collector ⇒ Prometheus nhận metric OTLP | A | 0.6 | 2 | Metric thử hiện trong Prometheus |
| 0.8 | Spike: k6 `--out opentelemetry` ⇒ Collector ⇒ Prometheus | A | 0.7 | 1,5 | Metric k6 hiện, khớp summary |
| 0.9 | Spike: cAdvisor đọc CPU/RAM container trên Docker Desktop | A | 0.6 | 1 | Có số liệu từng container |
| 0.10 | Spike: Grafana 13 nạp dashboard tab (schema v2) bằng file provisioning | A | 0.6 | 2 | Dashboard 2 tab hiện, hoặc chốt dự phòng |
| 0.11 | Ghi kết quả spike, chọn dự phòng cho mục không đạt | A | 0.7–0.10 | 0,5 | Bảng kết quả cuối plan |
| 0.12 | Kiểm model Gemini đang cấu hình trong `.env` của AI (người dùng xem, không gửi giá trị khóa) còn hoạt động không; ghi model sẽ dùng | N | — | 0,25 | Biết model hiện tại |

**Cổng 0:** không còn bí mật trong lịch sử; bốn repo public; Environment + luật duyệt có ở ba repo; spike ghi xong.

### Giai đoạn 1A — Đóng gói (6C-1)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 1.1 | Quét mọi chỗ BE đọc cấu hình ⇒ bảng khóa (bí mật? bắt buộc? giá trị theo môi trường) | A | — | 2 | Bảng trong plan con 6C-1 |
| 1.2 | Như 1.1 cho TADSS | A | — | 1,5 | Như 1.1 |
| 1.3 | File `deploy/stable.env.example`, `deploy/preview.env.example`, `perf/perf.env.example` (TADSS) chỉ có tên biến | A | 1.1, 1.2 | 1 | Ba file có đủ khóa |
| 1.4 | `.dockerignore` BE loại file bí mật + test OPS-01 (liệt kê file trong image) | A | — | 1,5 | OPS-01 xanh; `dotnet run` vẫn chạy |
| 1.5 | Như 1.4 cho TADSS | A | — | 1 | OPS-01 xanh |
| 1.6 | Image BE chạy non-root (OPS-02) | A | 1.4 | 1 | Container không phải root |
| 1.7 | Build nhận `GIT_SHA`, `BUILD_VERSION` ⇒ nhãn OCI, cả hai repo | A | — | 1 | `docker inspect` thấy nhãn đúng |
| 1.8 | BE kiểm cấu hình bắt buộc lúc khởi động (OPS-03) | A | 1.1 | 3 | Thiếu khóa ⇒ dừng, nêu tên khóa |
| 1.9 | BE `/health/live`, `/health/ready` (OPS-04) | A | — | 2 | DB mất ⇒ ready 503, live 200 |
| 1.10 | BE `/health/dependencies` gọi `/health/live` của TADSS và `/health` của AI (OPS-04) | A | 1.9 | 1,5 | TADSS hoặc AI dừng ⇒ báo lỗi, ready vẫn 200 |
| 1.11 | Metric `build_info` ở BE (OPS-05) | A | 1.7 | 1 | Đúng sha |
| 1.12 | Metric `build_info` ở TADSS (nội bộ, không đổi hợp đồng v2) (OPS-05) | A | 1.7 | 1 | Đúng sha; test kiến trúc TADSS xanh |
| 1.13 | Compose BE chạy bằng `image: …:${IMAGE_TAG}`, `Production` + `DEPLOY_ENVIRONMENT` | A | 1.4, 1.7 | 1,5 | `compose pull && up` chạy được |
| 1.14 | Như 1.13 cho TADSS | A | 1.5, 1.7 | 1 | Như 1.13 |
| 1.15 | Thử cùng image chạy ba cấu hình (`stable`, `preview`, `perf`) chỉ bằng biến môi trường (OPS-06) | A | 1.13, 1.14 | 1 | Cả ba healthy |
| 1.43 | AI: bảng cấu hình (`GEMINI_API_KEY`, `GEMINI_MODEL`, `APP_ENV`, thêm `GEMINI_BASE_URL` cho LLM giả) | A | — | 0,5 | Bảng trong plan con |
| 1.44 | AI: build nhận `GIT_SHA`, `BUILD_VERSION` ⇒ nhãn OCI; giữ non-root, `.env` ngoài image (đã có); test OPS-01 cho AI | A | — | 1 | `docker inspect` thấy nhãn; OPS-01 xanh |
| 1.45 | AI: dựng pytest + LLM giả; AI-UT-01…05 | A | — | 5 | Test chạy được không cần Gemini |
| 1.46 | AI: đổi model mặc định khỏi `gemini-2.0-flash` (đã deprecate) sang model còn trong gói miễn phí; đọc `GEMINI_BASE_URL` (chỉ dùng cho LLM giả) | A | 0.12, 1.45 | 1 | Test xanh; gọi thật một lần do người dùng chạy |
| 1.47 | AI-CT-01: test so DTO AI của BE với schema của AI (snake_case, đủ trường) | A | 1.45 | 2 | Lệch ⇒ đỏ |
| 1.48 | AI: compose chạy bằng image, giữ `ngrok-ai` (tài khoản ngrok riêng của AI); SEC-AI-1 đổi thành: cổng host chỉ `127.0.0.1` | A | 1.44 | 0,5 | AI chạy bằng image, URL ngrok của AI hoạt động |

**Cổng 1A:** OPS-01…06 đạt cho cả BE, TADSS, AI; AI-UT, AI-CT-01, SEC-AI-1 đạt; toàn bộ suite BE, TADSS, AI xanh.

### Giai đoạn 1B — CI đẩy image (6C-2a)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 1.16 | CI BE quét gói có lỗ hổng (SEC-BE-7) | A | — | 1 | Bước chạy, có gói lỗi ⇒ đỏ |
| 1.17 | CI BE: PR test + build (không push); push `develop`/`master` ⇒ GHCR `sha-<7>` + tag nhánh, cache; gửi yêu cầu triển khai kèm nhánh | A | 1.7 | 2 | Image BE trên GHCR |
| 1.18 | Như 1.17 cho TADSS | A | 1.7 | 1,5 | Image TADSS trên GHCR |
| 1.19 | CI kiểm image vừa push: không bí mật, non-root | A | 1.17, 1.18 | 1 | Kiểm chạy trên đúng image |
| 1.49 | CI AI: thêm pytest + quét gói Python có lỗ hổng (SEC-AI-4); push `master` ⇒ GHCR `sha-<7>`; PR không push; kiểm image không chứa `.env`, non-root (SEC-AI-3) | A | 1.44, 1.45 | 2 | Image AI trên GHCR |
| 1.20 | Đặt ba package GHCR public (BE, TADSS, AI) | N | 1.17, 1.18, 1.49 | 0,25 | Kéo image không cần đăng nhập |
| 1.21 | Tạo Vercel token, lấy org/project id, nhập secrets FE; tắt tự triển khai theo Git của Vercel | N | — | 0,5 | Secrets có |
| 1.22 | Workflow FE: `lint` ⇒ `tsc` ⇒ `build`; chỉ `master` ⇒ Vercel production; `develop` chỉ CI (OPS-07) | A | 1.21 | 2 | Push `master` ⇒ production; push `develop` ⇒ không triển khai; lỗi `tsc` ⇒ không triển khai |
| 1.23 | Ghi vào hướng dẫn FE: workflow mới, header `ngrok-skip-browser-warning` | A | 1.22 | 0,5 | Hướng dẫn cập nhật |

**Cổng 1B:** push một commit BE, TADSS, AI ⇒ có image trên GHCR; FE triển khai qua cổng kiểm.

### Giai đoạn 1C — Quan sát vận hành (6C-3a)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 1.24 | BE thêm OpenTelemetry (cùng phiên bản TADSS): ASP.NET Core, HttpClient, Npgsql, runtime | A | — | 3 | Metric tự động xuất ra Collector thử |
| 1.25 | Metric nghiệp vụ BE đặt tên một nơi, nhãn có giới hạn (OB-BE-1) | A | 1.24 | 3 | Test đúng tên/nhãn |
| 1.26 | Lan truyền trace BE ⇒ TADSS (OB-BE-2) | A | 1.24 | 2 | Một lượt đặt sân chung `trace_id` |
| 1.50 | AI thêm OpenTelemetry (FastAPI, httpx) + metric lời gọi Gemini (độ trễ, token, lỗi, 429) (OB-AI-1); nhận `traceparent` từ BE (OB-AI-2) | A | 1.45 | 4 | Metric và trace AI tới Collector |
| 1.27 | `TADSS-Runtime/deploy/` + `deploy/` của BE và AI: compose `stable` (BE, TADSS, AI nội bộ, ngrok chỉ cho BE) và `preview` (BE, TADSS, gọi AI của `stable`); PostgreSQL và Redis dùng chung, tách database/prefix theo môi trường | A | 1.13, 1.14, 1.48 | 3 | Hai môi trường chạy bằng image GHCR, không lẫn dữ liệu |
| 1.28 | Script nạp/làm mới DB của từng môi trường từ bản dump Neon (chọn môi trường, hỏi xác nhận trước khi ghi đè) | A | 1.27 | 2 | Cả hai môi trường có dữ liệu nền |
| 1.29 | Compose quan sát vận hành: Collector, Prometheus (15 ngày), Loki, Grafana 13, cAdvisor | A | 0.11, 1.27 | 2 | Metric BE + TADSS vào Prometheus |
| 1.30 | Dashboard "Vận hành" tab BE (mô tả từng panel) | A | 1.29 | 3 | Tab có dữ liệu |
| 1.31 | Dashboard "Vận hành" tab TADSS | A | 1.29 | 2 | Tab có dữ liệu |
| 1.51 | Dashboard "Vận hành" tab AI (OB-AI-3) | A | 1.29, 1.50 | 2 | Tab có dữ liệu |
| 1.32 | `DashboardDescriptionTests` + kiểm metric tồn tại (OB-BE-3), áp cho cả 3 tab | A | 1.30, 1.31, 1.51 | 2 | Panel thiếu mô tả ⇒ đỏ |
| 1.33 | Chuẩn bị Gmail gửi cảnh báo: tài khoản (nên riêng), bật xác minh 2 bước, tạo mật khẩu ứng dụng, chọn địa chỉ nhận; nhập vào secrets | N | — | 0,5 | Có tài khoản gửi + địa chỉ nhận |
| 1.34 | Cấu hình SMTP Gmail cho Grafana Alerting; 6 luật cảnh báo (gồm AI), nội dung ghi rõ môi trường; thử bằng sự cố giả (OB-BE-4) | A | 1.29, 1.33 | 2,5 | Thư cảnh báo tới hộp thư |

**Cổng 1C:** OB-BE-1…4, OB-AI-1…3 đạt; dashboard vận hành 3 tab có dữ liệu thật từ `stable` và `preview`.

### Giai đoạn 1D — CD tự động (6C-2b)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 1.35 | Hướng dẫn cài runner (user riêng không admin hoặc WSL) ⇒ người dùng đăng ký runner cho **cả ba repo** trên cùng máy, nhãn `smashcourt-host` | A+N | 0.5 | 1,5 | Ba runner online |
| 1.36 | Nhập secrets vào Environment `stable`/`preview` của từng repo theo bảng 1.1/1.2/1.43 (mỗi repo chỉ giữ secrets của service mình) | N | 1.3, 1.35 | 1 | Đủ secrets ba repo |
| 1.37 | `deploy.yml` mỗi repo: kích hoạt bằng `workflow_run` của CI (chỉ khi đạt) hoặc chạy tay; kiểm sha, nhánh ⇒ môi trường, commit thuộc nhánh, image tồn tại, nhãn revision (OPS-08) | A | 1.17, 1.18, 1.49 | 3 | Sha/nhánh sai ⇒ từ chối |
| 1.38 | Job triển khai vào đúng môi trường suy từ nhánh: pull ⇒ `up -d --no-deps --wait` ⇒ health 2 mức ⇒ sha (`docker inspect`) ⇒ smoke chỉ đọc (AI: chỉ `/health`, không gọi Gemini) | A | 1.37, 1.27 | 3 | Triển khai đúng sha, đúng môi trường |
| 1.39 | Quay về + kiểm lại bản cũ; `UNSAFE` + cảnh báo; `concurrency`; GitHub Deployments; `workflow_dispatch` nhập tag | A | 1.38 | 3 | Có đủ đường lỗi |
| 1.40 | Nối CI ⇒ `deploy.yml` trong từng repo (không cần token giữa các repo) | A | 1.37 | 1 | Push ⇒ tự triển khai |
| 1.41 | Diễn tập: triển khai thật vào `stable` và `preview` (mỗi service một lần, service khác và môi trường kia không khởi động lại); image hỏng ⇒ quay về; hỏng cả hai ⇒ `UNSAFE`; payload sai ⇒ từ chối (OPS-09, OPS-10) | A+N | 1.39, 1.40 | 3 | Các ca đúng với cả 3 service |
| 1.42 | Runbook vận hành; cập nhật CLAUDE.md (4 repo), README, MASTER plan, CONFIGURATION_REFERENCE, methodology (mã mới), spec Phase 6 (P6-32…50), hướng dẫn FE | A | 1.41 | 3 | `check_docs` đạt |

**Cổng giai đoạn 1 (DevOps xong):** OPS-01…10, OB-BE-1…4, OB-AI-1…3, SEC-BE-7, SEC-AI-1/3/4, AI-UT, AI-CT-01 đạt; push `master` ⇒ `stable`, push `develop` ⇒ `preview` tự chạy đúng sha; dashboard vận hành 3 tab có ghi chú; cảnh báo hoạt động; FE lên Vercel qua cổng kiểm.

### Giai đoạn 2A — Bộ quan sát kiểm thử (6C-3b)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 2.1 | `tests/catalog.yaml` sinh từ methodology (mã, nhóm, hệ thống, công cụ, nơi chạy, tiêu chí, link) + test kiểm khớp methodology | A | — | 3 | Mọi mã methodology có trong catalog và ngược lại |
| 2.2 | Compose quan sát kiểm thử (Collector, Prometheus, Tempo, Loki tùy chọn, Grafana, exporter ×3, cAdvisor, Pushgateway) | A | 0.11 | 3 | Mọi nguồn metric vào Prometheus |
| 2.3 | Bộ nạp kết quả: trx của BE và TADSS, JUnit XML của pytest AI (kể cả tải từ CI bằng `gh run download`) | A | 2.1, 2.2 | 3 | Ô Unit/Integration của 3 hệ có số đúng |
| 2.4 | Bộ nạp: Stryker + `metrics.json`/evidence của evaluator | A | 2.3 | 2 | Ô Mutation, FI, LT, RP/PT có số |
| 2.5 | Bộ nạp: summary k6, JSON SC6, báo cáo Playwright | A | 2.3 | 2 | Ô SP, SC6, E2E có số |
| 2.6 | Dashboard "Kiểm thử" tầng 1: ô tổng hợp theo nhóm + KPI, tab BE, TADSS, AI | A | 2.3 | 3,5 | Mỗi nhóm một ô, đúng màu |
| 2.7 | Tầng 2: bảng mã hỏng của nhóm + link báo cáo + link methodology | A | 2.6 | 2 | Bấm ô đỏ ⇒ thấy mã hỏng |
| 2.8 | Hàng biểu đồ hiệu năng (SP) ở hai tab: p95/p99 client cạnh server, RPS, lỗi, phân rã trung bình, tài nguyên | A | 2.6 | 3 | Có dữ liệu từ một lượt thử |
| 2.9 | `DashboardDescriptionTests` áp cho dashboard kiểm thử | A | 2.6–2.8 | 1 | Panel thiếu mô tả ⇒ đỏ |

### Giai đoạn 2B — Môi trường `perf` (6C-4)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 2.10 | Compose `perf` (image theo sha cho BE, TADSS, AI; LLM giả; PostgreSQL 17 + 16, Redis, Mailpit, k6) + chặn chuỗi kết nối không phải container cục bộ | A | 1.15 | 3 | Một lệnh bật/tắt |
| 2.11 | Cấu hình đo: rate limit nâng, VNPay khóa giả, SMTP Mailpit, Hangfire bật, cờ TADSS theo lượt | A | 2.10 | 1,5 | IPN do k6 ký được chấp nhận |
| 2.30 | LLM giả cho `perf`: server nhỏ trả response đúng định dạng Gemini với độ trễ cấu hình được; AI trỏ `GEMINI_BASE_URL` vào đó | A | 1.46, 2.10 | 3 | AI trả lời đầy đủ mà không gọi Google |
| 2.12 | Định dạng file thống kê + file phân bố `PLACEHOLDER` | A | — | 1,5 | Generator chạy được không cần CSV |
| 2.13 | Bộ sinh dữ liệu tất định (khoảng 2.000 khách, đơn có sẵn) + checksum | A | 2.12 | 4 | Hai lần cùng seed ⇒ cùng checksum |
| 2.14 | Khôi phục trạng thái: database mẫu cho hai DB + xóa Redis + kiểm checksum | A | 2.13 | 2 | Khôi phục < 1 phút, checksum khớp |
| 2.15 | Thư viện k6: hành trình J1–J7, think time có seed, tag hành trình/`phase`, ký IPN, metadata lượt, đếm tỷ lệ hành trình và tỷ lệ request | A | 2.11 | 5 | Lượt smoke chạy đủ hành trình |
| 2.16 | Spike tài nguyên 5 bước; chỉnh giới hạn container; tiêu chí `INFRA_LIMITED` | A | 2.14, 2.15 | 3 | Bảng tài nguyên chốt |
| 2.17 | Chốt trường xuất 215 booking ⇒ người dùng xuất CSV vào `data/private/` ⇒ script rút file thống kê | A+N | 2.12 | 2 | File thống kê tham chiếu (không dữ liệu cá nhân) |

### Giai đoạn 2C — Kịch bản và phân tích (6C-5)

| # | Task | Ai | Cần trước | Giờ | Xong khi |
| :---: | :--- | :---: | :--- | ---: | :--- |
| 2.18 | Chốt SLO trước khi đo, **theo từng hệ** (P6-52): BE (Phụ lục B.7.2), TADSS (SC1, SC2 đã có), AI (p95 < 300 ms phần xử lý AI, lỗi < 0,5 %, SP-10A/B ở B.7.4) + tải danh nghĩa + quy tắc đo. **Xong 02/11** (người dùng chốt; threshold k6 + `verdict.py` theo `perf/k6/slo.json`) | N | — | 0,5 | Ghi vào plan, không đổi sau khi có kết quả |
| 2.19 | SP-08 baseline năng lực | A | 2.16, 2.18 | 2 | Biết năng lực hiện tại |
| 2.20 | SP-01 Load ×3 (gồm LT-04) | A | 2.19 | 3 | 3 lượt hợp lệ |
| 2.21 | SP-06 A/B TADSS (ABBA, khôi phục dữ liệu mỗi lượt) | A | 2.20 | 4 | Chênh lệch có khoảng dao động |
| 2.22 | SP-07 A/A overhead telemetry | A | 2.20 | 2 | Overhead đo được |
| 2.23 | SP-02 Stress + SP-05 điểm gãy | A | 2.20 | 3 | Mức tải gãy + trạng thái tài nguyên |
| 2.24 | SP-03 Spike | A | 2.20 | 2 | Thời gian hồi phục |
| 2.25 | SP-04 Soak 90–120 phút | A | 2.20 | 3 + chạy | Không rò rỉ / hoặc chỉ ra rò rỉ |
| 2.26 | SP-09 + FI-11…14 dưới tải (Toxiproxy giữa BE và TADSS) + SC6 sau mỗi kịch bản | A | 2.20 | 4 | SC6 = 0 vi phạm |
| 2.31 | SP-10: tải các endpoint AI (chat, suggest, analytics) qua BE với LLM giả, đo phần xử lý của BE + AI (không gồm độ trễ Google) | A | 2.20, 2.30 | 3 | p95/p99 của AI có số, ghi rõ không gồm Gemini |
| 2.27 | Phân tích nút cổ chai bằng trace (lượt phân tích riêng) | A | 2.20–2.26 | 4 | Danh sách nguyên nhân có bằng chứng |
| 2.28 | Tối ưu trước/sau: mỗi lần đổi một yếu tố, ≥ 3 lượt mỗi bên | A+N | 2.27 | 6–10 | Kết quả có kiểm chứng |
| 2.29 | Báo cáo hiệu năng (kết luận theo SLO, tác động TADSS, hạn chế) + chọn lượt làm Evidence | A | 2.28 | 4 | Báo cáo + manifest |

**Cổng giai đoạn 2:** SP-01…10 có kết quả hợp lệ; dashboard kiểm thử 3 tab hiện đủ mọi nhóm ở Mục 4; báo cáo hiệu năng hoàn chỉnh.

---

## 8. Cách dùng sau khi xong

**Triển khai (tự động):** push `master` của BE, TADSS, AI ⇒ môi trường `stable`; push `develop` của BE, TADSS ⇒ môi trường `preview`; mỗi lần: CI xanh ⇒ image `sha-<7>` lên GHCR ⇒ runner triển khai service đó vào đúng môi trường ⇒ health + sha + smoke ⇒ thông báo kết quả. Quay về tay: chạy workflow `deploy` trong repo của service đó (Actions › deploy › Run workflow), nhập tag cũ. Xem: dashboard "Vận hành".

**Kiểm thử (tay, local):**
1. Tắt `stable` và `preview` (nếu đo chính thức).
2. Bật `perf` với tag image cần đo.
3. Chạy một kịch bản (vd SP-01, cờ bật, seed cố định); script tự khôi phục dữ liệu, warm-up, đo, ghi kết quả, đánh giá hợp lệ, nạp lên dashboard.
4. Nạp kết quả các nhóm khác (CI, evaluator, E2E) bằng bộ nạp.
5. Xem dashboard "Kiểm thử": ô đỏ ⇒ bấm xem mã hỏng ⇒ mở báo cáo gốc.

Lệnh cụ thể ghi trong runbook ở task 1.42 và 2.29.

---

## 9. Ước lượng

| Giai đoạn | Giờ công | Ngày công (≈ 6 giờ/ngày) |
| :--- | ---: | ---: |
| 0 Chuẩn bị | ≈ 13 | 2 |
| 1 DevOps (1A–1D, gồm AI, hai môi trường) | ≈ 108 | 18 |
| 2A Quan sát kiểm thử | ≈ 22 | 4 |
| 2B Perf (gồm LLM giả) | ≈ 25 | 4 |
| 2C Kịch bản, phân tích (gồm SP-10) | ≈ 45–49 + thời gian chạy | 7,5–8 |
| **Tổng** | **≈ 213** | **≈ 35,5**, chưa tính thời gian chạy soak và chờ thao tác của người dùng |

Ước lượng tăng so với bản 3 vì chia task nhỏ làm lộ việc kiểm và tài liệu; không phải cam kết.

## 10. Việc để lại (ngoài Epic 6C)

SEC-AI-2 (xác thực giữa BE và AI: đổi hợp đồng hai repo); N-37 (ràng buộc DB đơn hủy có lý do); gửi thử thư Gmail thật; dọn dữ liệu thử dev; chạy lại lượt đánh giá đầy đủ TADSS 2.1.0 + Evidence Pack (kết quả hiện ở tab TADSS qua bộ nạp); E-STAGING cho E2E (Phase 7); SEC-BE-1…6 (bảo mật BE ngoài quét gói).

### 10.1. Phương án dự phòng: bật auto-prepare (P6-54) cho môi trường chạy E2E (Phase 7)

**Khi nào xét bật:** chỉ khi đợt E2E / LT-04 trên E-STAGING (hoặc bản deploy) có hiệu năng không đạt SLO B.7.2 **và** dấu hiệu đúng loại nghẽn đã chẩn đoán: phiên PostgreSQL chờ `LWLock: LockManager`, khóa ngoài fast-path (`pg_locks.fastpath = false`) > 16 / phiên. Chậm vì nguyên nhân khác (CPU BE, TADSS, mạng, Neon ngủ) thì bật không giúp, không bật. Mặc định giữ **tắt** như người dùng chốt 05/11.

**Cơ sở (nguồn đã kiểm 05/11):** cùng cơ chế đo được ở E4 (khóa câu chi tiết booking 65 ⇒ 24, CPU PostgreSQL −44 %, p90 đặt sân 1499 ⇒ 166 ms ở 30 hành trình/giây):
- [Npgsql – Prepared Statements](https://www.npgsql.org/doc/prepare.html): `Max Auto Prepare` mặc định 0 (tắt); tính năng "should be considered somewhat experimental; test carefully", thấy hành vi lạ thì tắt. [Npgsql – Performance](https://www.npgsql.org/doc/performance.html) xếp prepare vào nhóm cải thiện hiệu năng quan trọng; EF Core không tự prepare.
- [pganalyze – GitLab và tranh chấp LWLock lock_manager](https://pganalyze.com/blog/5mins-postgres-LWLock-lock-manager-contention): GitLab chậm khoảng 2 giờ, hàng trăm câu chờ `lock_manager`; mỗi kết nối 16 khóa fast-path; câu nối nhiều bảng cùng index bị khóa lúc lập kế hoạch vượt giới hạn; prepared statement tránh khóa lặp lại lúc lập kế hoạch; bỏ index không dùng giảm số khóa (như P6-53).
- [AWS Database Blog – lock manager contention](https://aws.amazon.com/blogs/database/improve-postgresql-performance-diagnose-and-mitigate-lock-manager-contention): đọc đồng thời trên bảng nhiều index / partition làm cạn fast path ngay cả với thao tác chỉ đọc; giữ mỗi tiến trình trong 16 slot (đơn giản hóa phép nối, quản lý index).
- [PostgresAI – The roots of LWLock:LockManager](https://postgres.ai/blog/20251007-postgres-marathon-2-003): SELECT khóa bảng và mọi index lúc lập kế hoạch "unless you use prepared statements"; trước PostgreSQL 18 tối đa 16 khóa fast-path / tiến trình (PostgreSQL 18 có thay đổi giới hạn này, commit `c4d5cb71d`). [PostgresAI – bài về khóa](https://v2.postgres.ai/blog/tags/locks).

**Kiểm bắt buộc trước khi bật (tất cả phải đạt, ghi kết quả vào báo cáo Phase 7):**
1. **Kết nối:** chuỗi kết nối của môi trường đó không qua PgBouncer chế độ transaction; nếu qua (Neon host `-pooler`, như `tadss-dev` của BE dev hiện nay) thì đổi sang endpoint không pooler, hoặc thêm `No Reset On Close=true` theo [Npgsql – Compatibility](https://www.npgsql.org/doc/compatibility.html) và chạy lại toàn bộ bước 2–4 trên đúng endpoint đó.
2. **Bộ test BE với auto-prepare bật:** `SMASHCOURT_TEST_DB_MAX_AUTO_PREPARE=50 dotnet test SmashCourt-BE.Tests` ⇒ toàn bộ integration + HTTP đạt (đã chạy 05/11: 2656 đạt, 1 bỏ qua). Không đặt biến thì bộ test chạy đúng cấu hình deploy (tắt).
3. **E2E-01…E2E-14 + K-SC6** trên E-STAGING với `Database__MaxAutoPrepare=50`: đạt như lượt tắt; so `GET /api/v2/decisions/{id}` và dữ liệu đơn không khác lượt tắt.
4. **Hiệu năng:** LT-04 (và SP-01 nếu chạy) trước / sau cùng điều kiện; không endpoint nào chậm đi (so p50 / p90 từng endpoint như bảng E2 / E4).
5. **Vận hành:** mọi script đổi schema (`SmashCourt-BE/docs/database/*.sql`) chạy xong ⇒ **khởi động lại BE** (câu đã prepare lỗi `0A000 cached plan must not change result type` khi kiểu cột đang đọc thay đổi; test `PreparedStatement_WhenReadColumnTypeChanges_…` khóa hành vi này). Thêm / bỏ index thì PostgreSQL tự lập lại kế hoạch.

**Cách bật / tắt:** biến môi trường `Database__MaxAutoPrepare` của container BE (`50` bật, `0` hoặc bỏ biến = tắt) rồi khởi động lại BE; không cần build image. Bật ở bản deploy thì phải sửa đồng thời test `DeployConfiguration_DoesNotEnableAutoPrepare` (đang khóa quyết định "deploy không bật") — tức là một quyết định mới của người dùng, ghi vào P6-54.

## 11. Câu hỏi đã chốt (31/10/2026)

| # | Câu hỏi | Người dùng chốt | Ở đâu |
| :---: | :--- | :--- | :--- |
| Q1 | Kênh cảnh báo | **Gmail** | P6-49, 1.33, 1.34 |
| Q2 | Hai file `.docx` khi TADSS public | **Giữ** trong repo | 0.3 |
| Q3 | SLO | Bảng SLO là của **BE**; TADSS giữ SC1/SC2; AI chốt ở 2.18 | P6-52, 2.18 |
| Q4 | Tỷ lệ hành trình J1–J7 | Đồng ý: J1 55 %, J5 15 %, J3+J4 15 %, J2 5 %, J6 7 %, J7 3 % | 2.15 |
| Q5 | Xuất CSV 215 booking | Đồng ý: sau khi chốt trường và cách bỏ định danh | 2.17 |
| Q6 | Model Gemini | Đồng ý: `gemini-3.6-flash` | 1.46 |
| Q7 | Nhánh triển khai | AI và FE: `master`; BE và TADSS: **`master` và `develop`** (hai môi trường `stable`/`preview`; sau này bỏ `develop` của TADSS) | P6-35, P6-36, P6-45 |

Chốt thêm 31/10: **bỏ repo private**, CD đặt trong `deploy.yml` của từng repo (P6-35); TADSS chuyển public. Đã chốt từ trước: GHCR; PostgreSQL cục bộ cho môi trường triển khai; lưu metric 15 ngày; kiểm thử chỉ local; ngrok chỉ cho triển khai (BE của `stable`).

## 12. Phản biện 31/10 và cách xử lý (tóm tắt)

| Ý phản biện | Xử lý ở |
| :--- | :--- |
| Quay về phải chứng minh hệ thống khỏe | 1.38, 1.39, 1.41 |
| Ranh giới tin cậy dispatch/runner | P6-35, P6-45, 1.37, 1.40 |
| Không cộng percentile | P6-43, Phụ lục B.5.2 |
| Cùng trạng thái dữ liệu khi bật/tắt TADSS | P6-46, 2.14, 2.21 |
| SLO là mục tiêu, chốt trước khi đo | 2.18, 2.19 |
| Tỷ lệ hành trình khác tỷ lệ request | 2.15 |
| Ranh giới dữ liệu 215 booking | 2.12, 2.17 |
| Phân biệt giới hạn máy đo | P6-47, 2.16 |
| Dựng quan sát từng bước | 0.7–0.10, 2.2–2.8 |
| Percentile theo nhóm, kèm số mẫu | 2.29, Phụ lục B.7.6 |

---

## 13. Kết quả thực hiện

### Task 0.1–0.2 — Quét bí mật trong lịch sử git (31/10/2026) ✅

**Cách quét:** gitleaks v8.30.1 (image `ghcr.io/gitleaks/gitleaks:v8.30.1`), repo mount chỉ đọc, mặc định `git log -p --all --full-history` (mọi nhánh), `--redact` (báo cáo không chứa giá trị); cộng một lượt quét bổ sung theo mẫu riêng của dự án (chuỗi kết nối Neon/PostgreSQL có mật khẩu, HashSecret/TmnCode VNPay, khóa Google/Gemini, mật khẩu ứng dụng SMTP, khóa ký JWT, token ngrok, Cloudinary, Google OAuth secret, khối khóa riêng); kiểm các file bí mật chưa từng commit.

**Sự cố khi quét và cách xử lý:** hai lượt đầu báo "no leaks" nhưng log ghi "scanned ~0 bytes": đường dẫn Windows có `\` ghép biến làm Docker mount thư mục rỗng; lượt đầu còn do `--log-opts='--all'` thay mất tham số git mặc định. Đã chuyển sang đường dẫn dạng `D:/…`, in số file trong mount và số commit trước khi quét. Kết quả chỉ được tính khi số commit gitleaks quét khớp số commit có thêm dòng do git đếm.

| Repo | Commit quét / tổng | Byte | Phát hiện gitleaks | Kết luận |
| :--- | ---: | ---: | ---: | :--- |
| SmashCourt-BE | 201 / 224 | 12,4 MB | 7 | Token mẫu trong `docs/use-cases/02-dang-nhap-tai-khoan.md` ⇒ báo nhầm |
| TADSS-Runtime | 175 / 192 | 35,0 MB | 7.226 | 7.214 trong `docs/research/datasets/latency_caching_dataset.jsonl` (UUID, hash request); 3 trong `docker-compose.yml` (SHA-256 của hằng không bí mật của stack đánh giá); 1 `SecurityOptionsValidator.cs` (hash của khóa local công khai `tadss-local-dev-key`); 5 khóa giả trong test; 3 đường dẫn trong `CLAUDE.md` ⇒ đều báo nhầm |
| SmashCourt-AI | 15 / 17 | 0,09 MB | 0 | Sạch |
| smashcourt-fe | 103 / 118 | 4,6 MB | 0 | Sạch |

Quét bổ sung theo mẫu riêng: mọi kết quả là giá trị giả hoặc mẫu (`Password=u`, `fake.invalid`, `thu-nghiem`, mật khẩu hằng của PostgreSQL trong stack đánh giá cục bộ, tên biến `NewPassword`…). HashSecret trong test BE là khóa giả (chú thích "chỉ để ký IPN giả trong test"); TmnCode trong test không phải bí mật (mã merchant, công khai trong mọi URL thanh toán VNPay).

**File bí mật và dữ liệu riêng:** `appsettings.Development.json`, `.env`, `.env.local`, `.env.production`, `*.pfx`, `*.pem`, `data/private/` **chưa từng được commit** ở cả 4 repo. Hai fixture booking của TADSS (`smashcourt_bookings_sample.csv`, `smashcourt_expected_export.csv`) chỉ có UUID và số liệu tổng hợp, không có tên, số điện thoại, email.

**Kết luận:** không có bí mật thật trong lịch sử của 4 repo ⇒ task 0.3 (đổi khóa) không cần làm; TADSS chuyển public được (task 0.4). Gợi ý khi public: thêm `.gitleaks.toml` để bỏ qua các báo nhầm đã xác minh ở trên (dataset, hằng stack đánh giá, khóa test) để lần quét sau và secret scanning của GitHub không báo lại.

### Giai đoạn 1B — CI đẩy image (task 1.16–1.19, 1.49; 31/10/2026) — mã xong, chờ lượt CI đầu trên GitHub

- **Một script cho 3 repo:** `tools/ci/build-image.sh <image> [--push]` (bản chép giống hệt ở BE, TADSS, AI): `docker build` một lần với `GIT_SHA`, `BUILD_VERSION=<nhánh>-<sha7>` ⇒ `check-image.sh` trên đúng image đó (OPS-01, OPS-02) ⇒ nhãn revision phải bằng sha đầy đủ ⇒ `--push` mới đẩy `sha-<7>` + tag nhánh. Chặn đẩy ngoài `master`/`develop`; `sha-<7>` đã có trên GHCR (chạy lại CI cùng commit) ⇒ không ghi đè (tag bất biến). Cache: kéo tag nhánh làm `--cache-from` (cache inline), không cần buildx. Digest ghi vào Summary của lượt chạy.
- **Workflow:** mỗi repo thêm job `image` (`needs` job test/lint; quyền `packages: write` chỉ ở job này; đăng nhập GHCR bằng `GITHUB_TOKEN` chỉ khi `push`). `BRANCH`, `GIT_SHA` truyền qua `env` (không chèn thẳng `head_ref` vào lệnh). TADSS bỏ hai bước cũ "Verify Docker Image Packaging" + "non-root" (đã nằm trong `check-image.sh`).
- **Quét lỗ hổng:** BE thêm `tools/ci/check-vulnerable-packages.sh` (chép SEC-7 của TADSS, đổi solution) sau restore (1.16). AI thêm `pytest` và `pip-audit==2.10.1 -r requirements-dev.txt` (1.49, SEC-AI-4); lượt quét đầu trên máy tìm thấy `python-dotenv 1.0.1` dính PYSEC-2026-2270 ⇒ nâng `1.2.2` (23/23 test xanh, ruff sạch, quét lại sạch).
- **AI đẩy image cả `develop`:** người dùng chốt cuối 31/10 "AI / BE / TADSS: develop hoặc master đều chạy CI/CD"; image `develop` của AI có trên GHCR, còn môi trường nào chạy AI do `deploy.yml` (1D) quyết định.
- **Đã kiểm trên máy:** actionlint 1.7.7 sạch cả 3 workflow; shellcheck sạch; 3 bản `build-image.sh` giống hệt; build thật image AI không đẩy ⇒ check-image đạt, revision = sha; `--push` trên nhánh `feat/...` ⇒ từ chối, thoát 1.
- **Còn lại của 1B:** lượt CI đầu tiên trên GitHub (cổng 1B); người dùng đặt 3 package GHCR public (1.20) sau lần đẩy đầu.

### Giai đoạn 1D bước 1 — Môi trường deploy và nạp dữ liệu (task 1.27, 1.28; 01/11/2026) ✅

- **Chốt lại (01/11, người dùng):** chỉ **một** bản deploy trên máy (bỏ `stable`/`preview` và việc tách môi trường theo nhánh); push `develop` hoặc `master` đều triển khai vào bản này, lần push gần nhất thắng (bình thường là `develop`, nơi có code mới và DB test). AI triển khai từ `master`. FE Vercel gọi BE qua ngrok. Tên container/database trung tính: `smashcourt-be`, `tadss-runtime`, `smashcourt-ai`, database `smashcourt`, `tadss`. Email, VNPay, cookie như bản công khai (Gmail thật; Mailpit chỉ cho đợt E2E qua `docker-compose.mailpit.yml`).
- **DB đã xác minh (chỉ đọc, 01/11):** `ep-patient-bread` = DB gốc (215 booking, chưa áp Phase 6, đang comment trong cấu hình BE, để dành demo); `ep-royal-hat` = **tadss-dev** (đã áp Phase 6, 232 booking, BE dev đang dùng) ⇒ nguồn nạp; `ep-broad-king` = DB riêng của TADSS (chỉ schema `tadss`, 0 quyết định) ⇒ không có gì để chép, TADSS deploy tự migrate vào database `tadss` trên máy.
- **Tệp (gọn lại 01/11 theo yêu cầu người dùng):** mọi thứ ở `SmashCourt-BE/deploy/`: `docker-compose.yml` (postgres, redis, be, ngrok, tadss, ai trong một project `smashcourt-deploy`; mỗi service chỉ nhận khóa nó cần), `deploy.sh <be|tadss|ai|all> [tag]`, `seed-db.sh`, `init-db.sql`, `.env.example`. TADSS, AI không có thư mục deploy riêng. Bí mật: một file `~/.smashcourt-deploy/.env` (sinh từ cấu hình dev; khóa JWT/OTP và mật khẩu PostgreSQL mới, ngẫu nhiên). TADSS chạy Production, `ApplyMigrationsOnStartup=true` chỉ cho database trên máy. Lệnh: [`docs/guides/RUNBOOK.md`](../docs/guides/RUNBOOK.md) mục 2.
- **`seed-db.sh`:** chép schema `public` (không chép `hangfire`), phiên chỉ đọc trên Neon; chặn ba lớp: endpoint phải đúng `SEED_SOURCE_ENDPOINT`, nguồn phải có `bookings.tadss_decision_id` (Phase 6), gõ `smashcourt` để xác nhận. Đã thử: endpoint sai ⇒ từ chối; trỏ vào DB gốc ⇒ từ chối "chưa áp Phase 6"; trả lời sai ⇒ không làm gì. Nạp thật: 31 bảng, 2244 dòng, đếm từng bảng khớp nguồn. Sự cố khi viết: `docker run -i` ở các bước trước nuốt mất câu trả lời xác nhận ⇒ chỉ bước nạp đọc stdin; `source .env` hỏng vì giá trị như `Tên <email>` ⇒ script chỉ đọc đúng các khóa cần dùng như văn bản.
- **Lỗi thật tìm ra khi chạy Production:** BE sập lúc khởi động vì `RecurringJob.AddOrUpdate` (API tĩnh) cần `JobStorage.Current`, chỉ được đặt khi dashboard Hangfire bật (cấu hình Development). Sửa: `IRecurringJobManager` từ DI; test `UseHangfireServices_WhenDashboardOffAndStorageNotResolvedYet_RegistersAllRecurringJobs` đỏ trước, xanh sau; toàn bộ BE 2609 đạt, build 0 warning.
- **Kiểm:** BE, TADSS, AI healthy; BE gọi được TADSS và AI; URL ngrok công khai trả Healthy; CORS cho `smashcourt-ai.vercel.app`; 6 container ~430 MB RAM. Tạm thời BE chạy image build trên máy có bản sửa Hangfire, tới khi CI đẩy `develop` mới.

### Giai đoạn 1D bước 2–3 — Runner và CD (task 1.35–1.40; 01/11/2026) ✅ (chờ lượt push đầu)

- **Thiết kế gọn (thay `deploy.yml` + `workflow_run` của plan):** job `deploy` thêm vào cuối `ci.yml` của BE, TADSS, AI: `if: push`, `needs: image`, `runs-on: [self-hosted, smashcourt-deploy]`, `concurrency` theo service, lấy bộ deploy từ SmashCourt-BE nhánh develop (repo public, không cần token) rồi chạy `deploy/cd.sh <service> <sha>`. Không cần token giữa các repo, không phụ thuộc nhánh mặc định như `workflow_run`. Nhãn runner khai ở `.github/actionlint.yaml`.
- **`deploy/cd.sh`:** từ chối (thoát 3) khi sha sai dạng hoặc image `sha-<7>` không có trên GHCR (từ 11/11: kéo image trước khi đụng container, thử lại 5 lần khi lỗi mạng, không tới được GHCR thì thoát 4 thay vì báo nhầm là không có image; `deploy.sh` không kéo lại tag `sha-` đã có); triển khai ⇒ kiểm nhãn revision = sha, health, smoke chỉ đọc (BE `GET /api/cancel-policies`, TADSS `/health/ready`, AI `/health`) ⇒ hỏng thì quay về bản đang chạy trước (theo nhãn revision của container cũ, tag `sha-<7>` bất biến) và kiểm lại (thoát 1), quay về cũng hỏng ⇒ UNSAFE (thoát 2).
- **Đã kiểm trên máy:** sha sai ⇒ 3; image không có ⇒ 3; TADSS commit hiện tại ⇒ 0; **quay về thật** bằng image BE hỏng có sẵn trên GHCR (`sha-8197d12`, bản trước khi sửa Hangfire, sập ở Production) ⇒ phát hiện unhealthy, quay về `d02f326`, kiểm lại đạt, thoát 1, 26 giây. actionlint 1.7.7 và shellcheck sạch.
- **Runner (đổi sang chạy trong Docker, 01/11 theo yêu cầu người dùng: tự chạy khi mở Docker, không cài vào máy):** image `smashcourt-runner:2.338.2` = `ghcr.io/actions/actions-runner:2.338.2` + Docker Compose v5.6.1 (SHA-256 kiểm khi build), `SmashCourt-BE/deploy/runner/` (Dockerfile, entrypoint, compose 3 service, `restart: unless-stopped`). Mount: socket Docker (`group_add: ["0"]` vì socket Docker Desktop là root:root 660; user vẫn `runner` uid 1001) và `~/.smashcourt-deploy/.env` chỉ đọc. Đăng ký một lần (`entrypoint.sh register`, mã đăng ký qua biến môi trường), thông tin đăng ký giữ trong volume `smashcourt-runner-*`, `--disableupdate`. Runner Windows ban đầu (`C:\actions-runner`) đã gỡ đăng ký; thư mục để người dùng tự xóa. Hệ quả thiết kế: compose deploy không còn gắn file từ máy (`init-db.sql` chuyển thành `configs.content`), smoke trong `cd.sh` gọi bằng `docker exec` (đúng cả khi chạy trên máy lẫn trong container), smoke thử lại 3 lần (request đầu sau khi PostgreSQL khởi động lại có thể dính kết nối cũ bị cắt `57P01`), `cd.sh` dọn image cũ (giữ bản đang chạy + bản trước). Đã kiểm từ trong container runner: bản hiện tại ⇒ 0; bản hỏng `sha-8197d12` ⇒ tự quay về, 1; AI, TADSS ⇒ 0; PostgreSQL không bị tạo lại; **khởi động lại Docker Desktop ⇒ cả hệ thống và 3 runner tự chạy lại**, runner online, BE API 200.
- **Bảo mật (P0 của phản biện):** cả 3 repo đặt `fork-pr-contributor-approval = all_external_contributors` qua API (trước đó `first_time_contributors`).
- **Còn lại:** lượt push đầu chạy job deploy thật (cổng 1D), diễn tập 1.41 trên GitHub (push bản hỏng có chủ đích), runbook 1.42.

### Giai đoạn 1C — Quan sát vận hành (task 1.24, 1.29–1.34, 1.50, 1.51; 01/11/2026) — mã xong, chờ tài khoản Grafana Cloud

- **Đổi so với plan (01/11, người dùng chốt):** Grafana của bộ vận hành đặt trên **Grafana Cloud** (gói Free) để có URL host mở ở đâu cũng được; máy deploy chỉ chạy thêm container `smashcourt-alloy` (Grafana Alloy 1.21.3) thay cho Collector + Prometheus + Loki + Grafana trên máy (P6-37, P6-38 vẫn đúng: bộ vận hành và bộ kiểm thử tách biệt, OTLP là chuẩn chung; bộ kiểm thử vẫn chạy local). Cảnh báo dùng email có sẵn của Grafana Cloud nên **bỏ task 1.33** (Gmail + mật khẩu ứng dụng, P6-49 phần Gmail). Ba "tab" là 3 dashboard cùng tag hiện thành hàng nút đầu trang (dự phòng của P6-49), JSON schema cổ điển nên chạy trên mọi bản Grafana.
- **Luồng:** BE, TADSS, AI ⇒ OTLP (metric, trace, log; 60 giây/lần vì gói Free tính theo số điểm mỗi phút) ⇒ Alloy (bỏ metric không dùng, xóa nhãn `pool.name` chứa chuỗi kết nối, cAdvisor đo CPU/RAM và image từng container, blackbox gọi health 3 service trong mạng Docker) ⇒ Grafana Cloud. Cấu hình Alloy nằm trong `configs.content` của `deploy/docker-compose.yml` (giống `init-db`); compose không so `configs.content` khi quyết định tạo lại container nên `deploy.sh obs` luôn `--force-recreate`. Alloy không bắt buộc: thiếu khóa Grafana Cloud thì CD và app vẫn chạy.
- **Phiên bản đang chạy (OPS-05):** đọc từ nhãn image của container qua cAdvisor (`image` = `…:sha-<7>`, `container_label_org_opencontainers_image_revision`) cho cả 3 service, đúng image thật đang chạy; không thêm `build_info` vào mã (task 1.11, 1.12 thay bằng cách này; TADSS không đổi mã).
- **BE (1.24, OB-BE-1/2):** `Extensions/TelemetryExtensions.cs`, section `Telemetry` (tắt mặc định, validator khi bật): metric ASP.NET Core, HttpClient, Npgsql, runtime; histogram request 10 bucket; trace gốc chỉ từ request vào (`RequestRootSampler`: bỏ trace nền Hangfire), bỏ `/health`, xóa query string; log qua OTLP; OpenTelemetry 1.15.3 cùng bộ với TADSS. 25 test mới (validator, sampler, đăng ký provider, meter dashboard dùng, bucket).
- **AI (1.50, OB-AI-1):** `app/services/telemetry.py` (chỉ bật khi `OTEL_ENABLED=true`), FastAPI + httpx instrumentation (semconv HTTP ổn định, cùng tên metric với BE), metric Gemini `gemini.request.duration` (nhãn `outcome` ok/quota/error) và `gemini.tokens` ghi ở một chỗ (`llm_service._generate`); OpenTelemetry Python 1.45.2 / 0.66b2, pip-audit sạch. 7 test mới (gồm một test bật OTel trong tiến trình riêng).
- **Dashboard + cảnh báo (1.30–1.32, 1.34, 1.51):** nguồn duy nhất `SmashCourt-BE/deploy/grafana/dashboards.py` (39 panel, mô tả 6 ý); `grafana_cloud.py check` (CI BE, OB-BE-3: thiếu ý mô tả, thiếu mã test, metric không có trong `KNOWN_METRICS` ⇒ đỏ) và `push` (thư mục, 3 dashboard, điểm nhận email, 5 luật: không sẵn sàng 2 phút hoặc mất số liệu, BE/AI 5xx > 5 %, TADSS đường lùi > 20 %, Gemini 429 liên tục 10 phút). CD thất bại / UNSAFE do GitHub gửi email. ngrok không có metric nên quota ngrok xem trên trang ngrok.
- **Đã kiểm trên máy** (Grafana local `grafana/otel-lgtm` đứng thay Grafana Cloud, xóa sau khi kiểm): image BE build từ mã mới chạy trong bản deploy, Alloy nhận đủ metric/log/trace, cAdvisor đọc được container Docker Desktop (cần gắn `/run/containerd/containerd.sock`), blackbox thấy 3 service; `push` chạy hai lần liên tiếp không lỗi; chạy thử mọi truy vấn của panel BE đều có dữ liệu; bộ lọc làm mất metric DNS/Kestrel/hàng đợi HttpClient, lời gọi tới chính Alloy và nhãn `pool_name`. Panel quyết định của TADSS trống là đúng (cờ `EnableTadssDecision` tắt). Image AI build từ mã mới: một câu chat qua `POST /api/ai/chat` cho **một trace chung BE ⇒ AI ⇒ Gemini** (OB-AI-2; Gemini 4,5 trong 4,6 giây), URL span Gemini không chứa khóa; metric Gemini có đủ nhãn model, `outcome`, token vào/ra; mọi truy vấn tab AI trả đúng chuỗi. Sau khi kiểm, bản deploy trả về image GHCR (`sha-4621a50`, `sha-823f531`, `sha-cca4526`), xóa container/image thử.
- **Đổi chỗ file bí mật (01/11, người dùng: sợ lỡ tay xóa file trong thư mục người dùng):** `~/.smashcourt-deploy/.env` chuyển thành `smashcourt-deploy.env` ở thư mục gốc workspace (không phải git repo); `deploy/env-file.sh` chọn file (biến `SMASHCOURT_ENV_FILE` ⇒ file ở gốc ⇒ `~/.smashcourt-deploy/.env` trong runner, nơi compose runner gắn file ở gốc vào, chỉ đọc).
- **Email cảnh báo tiếng Việt (01/11, người dùng: email mặc định tiếng Anh, Gmail tự dịch khó hiểu):** `push` đẩy thêm mẫu thông báo `smashcourt-email` (tiêu đề `[SmashCourt] SỰ CỐ / MẤT SỐ LIỆU / ĐÃ ỔN LẠI: <luật> (<dịch vụ>)`; thân: tình trạng có giá trị dạng %, cách xử lý, giờ bắt đầu/kết thúc theo giờ Việt Nam, link dashboard). Ca không có số liệu (Docker/máy deploy tắt, Alloy dừng) ghi rõ là mất số liệu, không phải service lỗi. Đã render thử 4 ca bằng API test mẫu của Grafana Cloud.
- **Còn lại:** người dùng tạo tài khoản Grafana Cloud và điền 6 khóa (hướng dẫn: [`RUNBOOK.md`](../docs/guides/RUNBOOK.md) mục 4), `deploy.sh obs`, `grafana_cloud.py push`; kiểm cảnh báo bằng sự cố giả (dừng AI 3 phút ⇒ email); trace BE ⇒ TADSS (OB-BE-2) chỉ xem được khi bật cờ `EnableTadssDecision` (cùng cơ chế `traceparent` đã kiểm với AI).

### Giai đoạn 2A — Bộ quan sát kiểm thử (task 2.1–2.9; 01/11/2026) ✅ (chờ lượt CI đầu có chỉ mục mã)

- **2.1 Danh mục:** methodology thêm Mục 4.16 (38 mã Epic 6C: OPS-01…10, OB-BE-1…4, OB-AI-1…3, SEC-BE-7, SEC-AI-1…4, AI-UT-01…05, AI-CT-01, SP-01…10; SEC-BE-1…6 chưa có nội dung nên chưa thêm). `tests/catalog.yaml` sinh từ Mục 4 (`python perf/board.py catalog`): 189 mã, 25 nhóm, tab BE 96 / TADSS 90 / AI 24 (OPS ở cả ba; FI-11…14, CT-04, LT-04 thuộc BE). Test khớp hai chiều với methodology và cùng tập mã với `coverage_map` của evaluator; evaluator TADSS coi mã Epic 6C là ngoài phạm vi lượt đánh giá TADSS đơn lẻ.
- **2.2 Compose** `perf/docker-compose.yml` (project `smashcourt-test-obs`, mạng `smashcourt-test`): Prometheus 3.15 (nhận OTLP, giữ 365 ngày), Pushgateway 1.11 (lưu bền), Alloy 1.21 (OTLP cho k6/app + cAdvisor), Grafana 13.3 (provisioning, vào thẳng quyền xem). **Đổi so với plan:** Alloy thay Collector + cAdvisor + exporter (một container, như bộ vận hành); Tempo, exporter PostgreSQL/Redis và Loki để tới gói 6C-4 khi có môi trường perf (Tempo hiện không có tag phát hành trên Docker Hub để ghim; trace chỉ cần cho lượt phân tích, P6-40).
- **2.3–2.5 Bộ nạp** `perf/testboard/` (`python perf/board.py`): CI ba repo qua `gh run download` (BE, TADSS: trx + chỉ mục `catalog-index.*.json`; AI: JUnit XML), test chạy trên máy, Evidence Pack (`catalogCoverage` + `kpis`), k6 summary (hai định dạng threshold; `--limited` cho lượt INFRA_LIMITED), SC6 JSON của BE, Playwright JSON. Mỗi lần nạp thay đúng nguồn đó (`perf/results/state.json`), lưu bản gốc `perf/results/<thời điểm>-<nguồn>/`, đẩy cả bảng lên Pushgateway. Mã gộp nhiều nguồn lấy trạng thái xấu nhất. Trx không giữ trait ⇒ BE thêm `CatalogIndex` (trait `Catalog`, biến `SMASHCOURT_CATALOG_INDEX_DIR`, cùng định dạng chỉ mục TADSS); AI thêm marker `catalog` + thuộc tính JUnit; CI ba repo xuất các file đó vào artifact.
- **2.6–2.9 Dashboard** "SmashCourt Kiểm thử - BE/TADSS/AI" sinh từ `perf/testboard/dashboards.py`: KPI SC1–SC8; tầng 1 một ô mỗi nhóm (tính riêng theo tab), bấm ô ⇒ tầng 2 lọc nhóm; bảng mã mặc định chỉ hiện mã hỏng, link methodology và báo cáo (tầng 3); bảng nguồn kết quả; hàng SP (RPS/VU, p95/p99 k6 cạnh server, lỗi, CPU/RAM container perf). `python perf/board.py check` (job CI `testboard`) chặn catalog lệch methodology, JSON lệch nguồn, panel thiếu ý mô tả hoặc metric không khai báo. 51 test pytest.
- **Đã kiểm trên máy:** nạp Evidence Pack `EV-20261017-EVAL-full-r1` (73 mã, KPI), unit test TADSS 1915 và BE 1747 chạy với chỉ mục (33 và 41 mã), pytest AI 30 (6 mã); lượt k6 thử 2 req/s × 60 s vào endpoint đọc của bản deploy qua Alloy ⇒ tên metric k6 trong Prometheus khớp truy vấn, p95 client qua Prometheus 9,9 ms khớp summary 9,8 ms, nạp làm SP-08 dạng **hạn chế** (không phải lượt đo); chụp dashboard bằng Edge headless để soát bố cục.
- **Còn lại:** lượt CI đầu sau khi push để `python perf/board.py ci all` có chỉ mục; môi trường perf (2B) nối vào mạng `smashcourt-test`.

## Phụ lục B — Thiết kế chi tiết (bản 2, giữ nguyên)

Phụ lục này là phần thiết kế của bản 2 (đã sửa theo phản biện), giữ nguyên nội dung để tra cứu khi viết plan con từng gói. Số mục trong phụ lục mang tiền tố `B.`; các chỗ nhắc "Mục x" bên trong phụ lục là số mục của chính phụ lục. Phần nào khác với Mục 0–12 ở trên (vd bộ quan sát vận hành nay ở repo `smashcourt-deploy`, staging dùng PostgreSQL cục bộ, sha kiểm bằng nhãn image thay cho endpoint phiên bản, danh mục test nằm ở `tests/catalog.yaml`, môi trường `staging` của phụ lục nay tách thành `stable` ⇐ `master` và `preview` ⇐ `develop`, cảnh báo qua Gmail) thì theo Mục 0–12.

### B.3. Phần 1 — Đóng gói và cấu hình (gói 6C-1)

#### B.3.1. Image (BE và TADSS)
- `.dockerignore` loại bí mật (P6-33). `dotnet run` trên máy vẫn dùng `appsettings.Development.json` như cũ.
- BE chạy non-root như TADSS; giữ multi-stage, `tzdata`, `TZ=Asia/Ho_Chi_Minh`.
- Build nhận `GIT_SHA`, `BUILD_VERSION` ⇒ nhãn OCI (`org.opencontainers.image.revision`, `.version`, `.source`, `.created`) và biến môi trường trong image. Ứng dụng báo phiên bản qua metric `build_info` (nhãn `service`, `version`, `revision`) và endpoint phiên bản (không cần đăng nhập, không lộ cấu hình).

#### B.3.2. Cấu hình lúc chạy
- Container `Production` + `DEPLOY_ENVIRONMENT` (P6-33). Hệ quả: Swagger BE tắt; TADSS bắt API key thật (đã có validator); cookie `Secure` theo cấu hình.
- Danh sách khóa bí mật và khóa theo môi trường lập trong plan 6C-1 bằng cách quét mọi chỗ code đọc cấu hình (`IOptions`, `Configuration[...]`), không đoán. Nhóm đã biết của BE: `ConnectionStrings`, `Jwt:Key`, `Otp:HmacSecret`, `Smtp:*`, `Google:ClientSecret`, `VnPay:TmnCode/HashSecret`, khóa gọi TADSS, Cloudinary.
- BE thêm kiểm cấu hình bắt buộc lúc khởi động: thiếu khóa ⇒ container dừng với thông báo nêu tên khóa (không in giá trị).
- Mỗi môi trường một file `deploy/<env>.env.example` / `perf/perf.env.example` chỉ có tên biến, cho người chạy tay.

#### B.3.3. Compose và health check
- Compose dùng `image: ghcr.io/nguyenthanhtrung-n2t/<image>:${IMAGE_TAG}` (vẫn giữ `build:` cho người cần build tại chỗ). Triển khai: `docker compose pull && docker compose up -d --wait`.
- BE thêm `/health/live` (tiến trình sống) và `/health/ready` (kết nối PostgreSQL); TADSS **không** nằm trong ready của BE vì BE có đường lùi cọc 100 %. Thêm một endpoint kiểm phụ thuộc chỉ đọc (vd `/health/dependencies`, không cần đăng nhập, không lộ cấu hình) báo BE có gọi tới được `/health/live` của TADSS hay không: dùng cho smoke test sau triển khai (4.2) và dashboard vận hành, không ảnh hưởng ready.
- Không tự migrate: schema BE do người dùng chạy script; TADSS giữ `ApplyMigrationsOnStartup=false` trên Neon. `perf` nạp schema bằng script khởi tạo PostgreSQL.
- Thư mục: mỗi repo có `deploy/` cho service của mình; **compose triển khai cả hệ thống + bộ quan sát vận hành** ở `TADSS-Runtime/deploy/`; **môi trường đo + bộ quan sát kiểm thử + k6** ở `TADSS-Runtime/perf/`. Mạng chung `smashcourt-network` như hiện tại.

#### B.3.4. Ngoại lệ có chủ đích
- Chế độ đánh giá của TADSS (D-14, `docker compose up -d --build`) vẫn build từ mã nguồn: bằng chứng phải gắn đúng mã đang chạy (kể cả chưa commit); stack này không dùng bí mật.
- FE: Next.js gắn `NEXT_PUBLIC_*` vào bản build nên Vercel build theo từng môi trường.

#### B.3.5. Kiểm (6C-1)
Image không chứa file bí mật (kiểm danh sách file trong image ở CI); container BE non-root; container thiếu khóa bắt buộc dừng với thông báo đúng; health check đúng khi DB mất/có; endpoint phiên bản và `build_info` trả đúng sha; cùng một image chạy được ở `staging` và `perf` chỉ bằng đổi biến môi trường.


### B.4. Phần 2 — CI/CD (gói 6C-2)

#### B.4.1. CI (BE và TADSS)
- Giữ restore, build, test. BE thêm quét gói lỗ hổng như TADSS.
- Pull request: test + build image (không push). Push `develop`/`master`: build + push GHCR (`sha-<7>`, tag nhánh), cache build.

#### B.4.2. CD (BE + TADSS, trên máy nhóm)
- Runner tự host **chỉ gắn repo TADSS-Runtime** (private). Lý do: runner gắn repo public cho phép pull request của người ngoài chạy mã trên máy (GitHub khuyến cáo không làm).
- CI của BE xong ⇒ `repository_dispatch` sang TADSS-Runtime với `{service: be, sha}`; CI của TADSS gọi thẳng job triển khai. Token: fine-grained, chỉ repo TADSS-Runtime, có hạn, nằm trong Environment `dispatch` của BE chỉ nhánh `develop` đọc được. Quyền cụ thể GitHub đòi cho `repository_dispatch` (nhiều khả năng là ghi *Contents*, tức ghi được cả mã TADSS) **xác minh theo tài liệu GitHub lúc làm 6C-2**; nếu quyền quá rộng thì dùng phương án không cần token: job lịch trên runner hỏi GHCR mỗi vài phút, thấy tag `develop` của BE trỏ sang digest mới thì chạy cùng chuỗi kiểm P6-45 rồi triển khai (đổi lại trễ vài phút).
- Job triển khai (chỉ job này có secrets, P6-45):
  1. Kiểm payload và nguồn gốc image (P6-45).
  2. Đăng nhập GHCR; ghi tag mới **chỉ cho service vừa build**; service kia giữ tag đang chạy (file trạng thái trên máy).
  3. `pull` ⇒ `up -d --no-deps --wait <service>`: chỉ service đó khởi động lại.
  4. Kiểm health hai mức: `/health/live` (tiến trình) và `/health/ready` (sẵn sàng phục vụ); endpoint phiên bản trả đúng sha.
  5. **Smoke test chỉ đọc**, không tạo dữ liệu: BE trả 200 đúng envelope ở một endpoint công khai chỉ đọc (danh sách sân); TADSS `/health/ready`; BE gọi tới được TADSS (endpoint kiểm phụ thuộc chỉ đọc của BE, gọi `/health/live` của TADSS). **Không** gọi `POST /api/v2/decisions` trên staging: bản ghi audit của TADSS không xóa được.
  6. Health, sha, smoke đều đạt ⇒ thành công. Không đạt ⇒ quay về tag trước, **chạy lại bước 4–5 cho bản cũ**; đạt ⇒ báo "đã quay về"; không đạt ⇒ trạng thái `UNSAFE` + cảnh báo, chờ người xử lý.
- Giới hạn của quay về: chỉ đổi image; dữ liệu bản mới đã ghi không bị hoàn tác. Vì CD không bao giờ chạy migration (schema do người dùng chạy script, tương thích ngược), bản cũ đọc được dữ liệu bản mới ghi. Lỗi phát hiện muộn sau khi đã nhận request: cảnh báo vận hành + nút quay về tay.
- Mỗi môi trường một lần triển khai tại một thời điểm (`concurrency`); nút chạy tay (`workflow_dispatch`, nhập service + tag) để triển khai lại hoặc quay về bản cũ; ghi GitHub Deployments.
- Môi trường: `staging` (tự động từ `develop`, kèm ngrok cho VNPay). `production` cho demo/bảo vệ để sau (tay, có duyệt).
- Bộ quan sát vận hành triển khai cùng job (Mục 5.4).

#### B.4.3. FE lên Vercel
Như P6-36. Không sửa mã giao diện; ghi thay đổi workflow vào [PHASE6_FE_HANDOFF.md](PHASE6_FE_HANDOFF.md).

#### B.4.4. Kiểm (6C-2)
Push lên `develop` ở BE ⇒ image mới trên GHCR ⇒ máy nhóm chạy đúng sha mới mà không ai thao tác, TADSS không bị khởi động lại; image hỏng (health hoặc smoke không đạt) ⇒ tự quay về bản trước và bản cũ được kiểm lại healthy; quay về cũng hỏng (thử bằng image cố ý hỏng cả hai) ⇒ `UNSAFE` + cảnh báo; payload dispatch sai dạng, sha không thuộc `develop`, image không có hoặc nhãn revision lệch ⇒ từ chối, không triển khai; pull request không push image, không chạm runner; triển khai tay một tag cũ được; FE chỉ lên Vercel khi lint/tsc/build đạt.

#### B.4.5. Người dùng phải làm
Cài runner trên máy nhóm (có hướng dẫn từng bước); tạo GitHub Environment `staging` và nhập secrets; tạo token cho `repository_dispatch`; cấp Vercel token và project id.


### B.5. Phần 3 — Quan sát BE và TADSS (gói 6C-3)

#### B.5.1. Instrumentation BE (OpenTelemetry, cùng phiên bản 1.15.x với TADSS)
- **Metric tự động:** ASP.NET Core (`http.server.request.duration` theo route, method, status), HttpClient (`http.client.request.duration` — gồm lời gọi TADSS), Npgsql/EF Core (thời gian lệnh, kết nối, pool), runtime .NET (GC, thread pool, bộ nhớ, exception).
- **Metric nghiệp vụ BE** (ít, nhãn có giới hạn giá trị): đơn tạo theo nhánh kết quả (`PENDING`, `CONFIRMED`, `PENDING_APPROVAL`, bị từ chối), kết quả gọi TADSS (thành công, timeout, lỗi, đường lùi 100 %), thời gian gọi TADSS, kết quả IPN, số đơn job hủy, `build_info`.
- **Trace:** request vào BE ⇒ truy vấn DB ⇒ gọi TADSS (lan truyền `traceparent` sang TADSS để thấy một trace xuyên hai hệ). Lấy mẫu theo P6-40.
- **Log:** giữ logging hiện tại, thêm xuất OTLP có `trace_id` để nối log với trace; mức theo môi trường.
- Đặt tên metric và nhãn một nơi (theo cách `TadssMetricNames` của TADSS); không đưa định danh theo request (bookingId, userId) vào nhãn.

#### B.5.2. Phân rã thời gian xử lý (đáp ứng P6-43)
Ba cách nhìn, mỗi cách một câu hỏi, không trộn:
- **Từng request (trace, lượt phân tích):** cây span cha–con cho biết request *này* tốn ở đâu: các truy vấn DB (có thể nhiều), lời gọi TADSS, chờ pool, phần xử lý của BE. Span có thể lồng nhau hoặc chạy song song nên đọc theo cây, không cộng thẳng. Dùng chủ yếu cho request chậm (đuôi p95/p99).
- **Trung bình (metric, mọi lượt):** thời gian trung bình **cộng được** khi các phần nối tiếp nhau: từ `_sum`/`_count` của histogram tính thời gian trung bình mỗi request đặt sân của BE, của DB (tổng thời gian lệnh DB chia số request) và của lời gọi TADSS; phần chênh là "còn lại" (logic BE, chờ pool, xếp hàng). Đây là phân rã tổng hợp hợp lệ.
- **Phân bố từng thành phần (metric):** p50/p95/p99 của BE, của lời gọi TADSS, của lệnh DB, xem riêng từng cái. **Không** lấy p95 BE trừ p95 DB trừ p95 TADSS để suy ra p95 phần còn lại: các percentile này thuộc các tập request khác nhau.

#### B.5.2a. Dựng theo từng bước
Không chờ đủ bộ mới kiểm: (1) metric BE + TADSS tới được Prometheus; (2) metric k6 vào cùng bộ + dashboard tổng quan tối thiểu; (3) exporter DB, Redis, container; (4) trace xuyên BE ⇒ DB ⇒ TADSS; (5) dashboard phân tích + đo overhead. Bước nào hỏng thì biết lỗi ở lớp đó. Dashboard và trace nâng cao không được trì hoãn một bài load cơ bản.

#### B.5.3. Bộ quan sát kiểm thử (`perf/`)
| Thành phần | Vai trò | Ghi chú |
| :--- | :--- | :--- |
| OpenTelemetry Collector (contrib) | Cổng OTLP duy nhất cho BE, TADSS, k6; chia tín hiệu; lấy mẫu trace | Tính vào ngân sách tài nguyên (Mục 6.3) |
| Prometheus | Lưu metric | Cách nhận từ Collector xác minh ở spike (P6-39) |
| Tempo | Lưu trace | Chỉ bật trong lượt phân tích |
| Loki | Lưu log | Tùy chọn; lượt chính thức chỉ Warning trở lên |
| Grafana | Dashboard (JSON, provisioning) | Không mở trong lượt chính thức |
| `postgres_exporter` × 2 | DB SmashCourt và DB TADSS, nhãn `db=smashcourt` / `db=tadss` | Kết nối, giao dịch, khóa, cache hit, thống kê truy vấn chậm (`pg_stat_statements`) |
| `redis_exporter` | Redis của TADSS | Bộ nhớ, kết nối, lệnh/giây, hit/miss |
| cAdvisor | CPU/RAM/mạng theo container | Trên Docker Desktop (WSL2) cần kiểm khả năng đọc số liệu ở spike |

#### B.5.4. Bộ quan sát vận hành (`deploy/`)
Collector + Prometheus + Grafana (+ Loki cho log lỗi), triển khai cùng CD. **Không** gắn `postgres_exporter` vào Neon (giữ compute Neon thức, tốn giờ free tier) và Redis Cloud: dùng metric phía ứng dụng (Npgsql, cache của TADSS) thay thế. Lưu metric có thời hạn (vd 15 ngày).

#### B.5.5. Dashboard
- **Bộ kiểm thử — "Tổng quan bài đo" (một dashboard, BE + TADSS):** hàng *Tải* (VU, RPS, lỗi theo k6); *Chất lượng phản hồi* (p50/p95/p99 client theo k6 cạnh p95/p99 server theo BE, theo nhóm hành trình); *Phân rã* (BE / DB / TADSS); *TADSS* (độ trễ quyết định, tỷ lệ đường lùi, cache hit, strategy version); *Tài nguyên* (CPU/RAM từng container, GC, thread pool); *DB* (hai DB); *Redis*. Biến lọc: `run_id`, `deployment.environment`, trạng thái cờ. Mỗi lượt có annotation bắt đầu/kết thúc.
- **Bộ kiểm thử — dashboard phân tích:** theo endpoint; DB (truy vấn chậm, khóa, pool); trace (exemplar từ histogram sang Tempo).
- **Bộ vận hành — "Sức khỏe hệ thống" (một dashboard, BE + TADSS):** sẵn sàng, RED (rate, error, duration) theo service, TADSS quyết định và đường lùi, job Hangfire, phiên bản đang chạy (`build_info`), tài nguyên container.
- Mỗi dashboard có test kiểm metric tham chiếu tồn tại (mở rộng cách `DashboardMetricReferenceTests` của TADSS sang BE).
- Không lưu ảnh dashboard (quy ước đã có): bằng chứng là JSON snapshot và biểu đồ vẽ lại từ số liệu.

#### B.5.6. Kiểm (6C-3)
Metric BE xuất hiện ở Prometheus với đúng tên/nhãn; trace một lượt đặt sân đi qua BE ⇒ DB ⇒ TADSS chung một `trace_id`; dashboard không tham chiếu metric không tồn tại; overhead telemetry đo được (Mục 7.10).


### B.6. Phần 4 — Môi trường `perf` và mô hình tải (gói 6C-4)

#### B.6.1. Thành phần
BE và TADSS (image theo sha), PostgreSQL 17 (SmashCourt, nạp `schema_smashcourt.sql` + dữ liệu benchmark), PostgreSQL 16 + Redis 7 (TADSS, migration TADSS áp tự động trong `perf` vì DB dùng một lần), Mailpit, bộ quan sát kiểm thử, k6 (`grafana/k6:2.3.0`, chạy khi đo). Lệnh bật/tắt một dòng; `down -v` để dựng lại sạch.

#### B.6.2. Cấu hình đo
Rate limit nâng (giới hạn đăng nhập 5/phút/IP sẽ chặn bài đo); VNPay dùng TmnCode/HashSecret **giả** của riêng `perf` để k6 tự ký IPN (BE dựng URL nội bộ, không gọi VNPay); SMTP vào Mailpit; Hangfire bật (job chạy như thật, ghi nhận ảnh hưởng ở soak); cờ TADSS theo lượt (P6-43). Không trỏ tới Neon, Gmail hay VNPay thật — có kiểm chặn trong script khởi động.

#### B.6.3. Ngân sách tài nguyên (đề xuất, chỉnh sau spike)
| Container | CPU | RAM |
| :--- | ---: | ---: |
| BE | 2 | 1 GB |
| TADSS | 1,5 | 768 MB |
| PostgreSQL SmashCourt | 1 | 1 GB |
| PostgreSQL TADSS | 0,5 | 512 MB |
| Redis | 0,25 | 128 MB |
| Collector | 0,5 | 256 MB |
| Prometheus | 0,5 | 1 GB |
| Tempo (lượt phân tích) | 0,5 | 512 MB |
| Grafana | 0,25 | 256 MB |
| Exporter + cAdvisor | 0,5 | 256 MB |
| k6 | 1,5 | 1 GB |

Tổng giới hạn vượt 8 CPU logic: giới hạn là trần, không phải phần giữ riêng, các container vẫn tranh CPU/RAM. Trong lượt chính thức tắt hệ thống `staging` + bộ quan sát vận hành.

**Spike tài nguyên (bắt buộc trước lượt chính thức):** (1) cả bộ chạy không tải: đo mức nền; (2) k6 tải thấp: xác nhận k6 chưa bị giới hạn; (3) tăng từng bậc: ghi CPU, RAM, nhiệt độ/hạ xung nếu đọc được; (4) một lượt tắt mọi dịch vụ không cần; (5) chỉnh bảng trên theo số đo. Tiêu chí lượt hợp lệ theo P6-47 là **điều kiện chất lượng của từng lượt**, không chỉ là dòng hạn chế ở cuối báo cáo.

#### B.6.4. Dữ liệu (P6-42)
- **Tham chiếu:** script rút từ 215 booking (CSV ở `data/private/`, không vào repo) ra file thống kê tổng hợp: phân bố giờ bắt đầu theo ngày trong tuần, thời gian đặt trước (lead time), số sân/đơn, thời lượng, tỷ lệ trạng thái cuối (hoàn tất, hủy, không đến, hết hạn thanh toán), phân bố lịch sử khách (số đơn hoàn tất/không đến/hết hạn — quyết định đường xử lý của strategy). Chỉ file thống kê (không có dữ liệu cá nhân) được commit. Dùng lại `tools/evidence/dataset_analysis.py` nếu phù hợp.
- **Benchmark:** bộ sinh tất định (seed cố định) tạo chi nhánh, sân, bảng giá, N khách (vd 2.000) với lịch sử theo phân bố, đơn có sẵn chiếm một tỷ lệ khung giờ (để có xung đột sân như thật), tài khoản nhân viên/quản lý. Cùng seed ⇒ cùng dữ liệu ⇒ so sánh được giữa các lượt.
- **Thật:** không nạp. Script khởi động `perf` từ chối chuỗi kết nối không phải container cục bộ.
- **Ranh giới khi xuất 215 booking (trước 6C-4):** liệt kê trước các trường cần cho phân bố (giờ bắt đầu, ngày, lead time, số sân, thời lượng, trạng thái cuối, số đơn theo trạng thái của từng khách đã băm); bỏ hoặc gộp mọi trường định danh (tên, điện thoại, email, ghi chú); chỉ người dùng giữ CSV nguồn trong `data/private/`; chỉ file thống kê vào repo; dữ liệu benchmark phải sinh lại được từ file thống kê mà không cần CSV.
- **Không phụ thuộc cứng vào CSV:** khi CSV chưa sẵn, generator, compose và đường metric phát triển bằng một file phân bố giả lập **gắn nhãn rõ `PLACEHOLDER`**; lượt chính thức chỉ chạy với file thống kê tham chiếu đã xác minh, báo cáo ghi nguồn phân bố của từng lượt.
- **Giới hạn:** dữ liệu theo phân bố 215 booking là cơ sở workload có giới hạn, không đại diện mọi hành vi khách hàng; ghi vào phần phương pháp.

#### B.6.5. Mô hình người dùng (Phần 4 của danh sách)
- **Hành trình (journey):** J1 xem sân/lịch trống; J2 đăng nhập; J3 đặt sân online ⇒ J4 thanh toán (lấy link, IPN do k6 ký) hoặc hủy; J5 xem lịch sử/chi tiết đơn; J6 nhân viên xem danh sách, check-in/check-out; J7 quản lý xem hàng chờ và duyệt (chỉ có khi cờ bật và strategy trả `NEED_APPROVAL`).
- **Tỷ lệ hành trình (đề xuất, cần duyệt):** J1 55 %, J5 15 %, J3+J4 15 %, J2 5 %, J6 7 %, J7 3 %. Không có log truy cập thật nên tỷ lệ là **giả định có lý do** (đọc nhiều hơn ghi, theo hành vi đặt sân thông thường), ghi rõ ở hạn chế; giờ và lead time của J3 lấy theo dữ liệu tham chiếu.
- **Virtual User:** mỗi VU là một người dùng có phiên đăng nhập riêng (đăng nhập một lần, làm mới token trước 15 phút).
- **Think time:** phân bố ngẫu nhiên có seed: xem trang 2–6 giây, điền form đặt sân 8–20 giây, giữa hai hành trình 5–15 giây.
- **Workload model:** hỗn hợp hành trình theo tỷ lệ trên, mỗi hành trình một tag để tách số liệu. **Tỷ lệ hành trình khác tỷ lệ request HTTP** (một lượt xem lịch gọi nhiều request hơn một lượt đăng nhập): báo cáo ghi cả hai, dashboard ghi rõ đang hiển thị tỷ lệ nào. Hành trình có điều kiện (J7 chỉ có khi strategy trả `NEED_APPROVAL`) được đếm số mẫu thực tế; lệch xa tỷ lệ dự kiến thì ghi và giải thích.
- **Open model và closed model:** *closed* (`ramping-vus`, số người cố định, mỗi người chờ phản hồi rồi mới làm tiếp) cho góc nhìn trải nghiệm; *open* (`constant-arrival-rate`, `ramping-arrival-rate`, request đến theo nhịp bất kể hệ thống chậm) cho SLO throughput và tìm điểm gãy — tránh sai số "coordinated omission" của closed model khi hệ thống chậm đi.

#### B.6.6. Kiểm (6C-4)
Dựng `perf` từ đầu bằng một lệnh, hai lần cùng seed ra dữ liệu giống hệt (so checksum); script chặn chuỗi kết nối ngoài; IPN do k6 ký được BE chấp nhận; spike xác minh đường metric k6 ⇒ Collector ⇒ Prometheus và cAdvisor trên Docker Desktop với đúng phiên bản ghim.


### B.7. Phần 5 — Kịch bản, SLO và phân tích (gói 6C-5)

Ánh xạ 12 hạng mục được đề xuất:

| Hạng mục | Mục |
| :--- | :--- |
| 1. Mục tiêu và phạm vi | 7.1 |
| 2. Tiêu chí chấp nhận (SLO) | 7.2 |
| 3. Môi trường và công cụ | 5.3, 6.1–6.3 |
| 4. Mô hình hóa người dùng | 6.5 |
| 5. Ramp-up | 7.3 |
| 6. Load / Stress / Spike / Soak | 7.4 |
| 7. Điểm gãy | 7.5 |
| 8. Phân tích p95/p99 | 7.6 |
| 9. Giám sát bằng Grafana | 5.5 |
| 10. Kết quả và nút cổ chai | 7.7 |
| 11. Tối ưu và so sánh trước/sau | 7.8 |
| 12. Kết luận và hạn chế | 7.9 |

#### B.7.1. Mục tiêu và phạm vi
Đo năng lực **cả hệ thống** SmashCourt-BE + TADSS cho các hành trình 6.5; cổng thanh toán không chạy thật (k6 ký IPN), email vào Mailpit. Không thay các bài đo riêng TADSS (LT-01…05 trong evaluator, E-EVAL) mà bổ sung tầng hệ thống. Không thay E2E (đúng/sai nghiệp vụ trên giao diện, E-STAGING).

#### B.7.2. SLO và quy tắc đo (task 2.18, **người dùng chốt 02/11/2026**, không đổi sau khi có kết quả)

Phân biệt bốn thứ: **SLO mục tiêu** (bảng dưới, chốt và ghi lại **trước** khi có kết quả); **baseline thực nghiệm** (mức hệ thống đạt được hiện tại, đo bằng một lượt tăng tải ban đầu, gồm kiểm mức 50 request/giây có khả thi trên máy này không); **kết quả đo** (từng lượt); **kết luận** (đạt hay không, bằng chứng mạnh tới đâu). Nếu máy không đạt được mức danh nghĩa thì báo cáo đó là giới hạn hạ tầng và điều chỉnh phạm vi có giải thích; **không hạ ngưỡng sau khi đã thấy kết quả**: lượt chính thức không đạt thì báo cáo là không đạt và phân tích nguyên nhân. Các ngưỡng 31/10 soạn trước mọi lượt đo; các lượt thử 2.16 (p95 chung ≤ 0,1 s tới 84 request/giây trên máy sạch) **không** dùng để siết ngưỡng. Nguồn máy đọc duy nhất của các con số dưới đây: `perf/k6/slo.json` (k6 dựng threshold, `perf/stack/verdict.py` chấm).

**SLO của BE** (phía client k6, chỉ pha `steady`, theo nhãn `slo` của request):

| Nhóm | p95 | p99 | Gồm |
| :--- | ---: | ---: | :--- |
| Đọc `read` | < 300 ms | < 800 ms | xem chi nhánh, sân, lịch trống, lịch sử, chi tiết đơn, danh sách/lịch/tổng quan của nhân viên, hàng chờ duyệt |
| Đặt sân `booking` | < 1000 ms | < 2000 ms | `POST /api/bookings/online`, gồm gọi TADSS (p95 trùng ngưỡng SC1 của E2E) |
| Đăng nhập `login` | < 500 ms | < 1000 ms | BCrypt cố ý chậm, tốn CPU |
| IPN `ipn` | < 500 ms | < 1000 ms | |
| Ghi `write` (thêm 02/11) | < 500 ms | < 1000 ms | hủy đơn, check-in, check-out, duyệt cọc; chấm trên cả nhóm, báo cáo thêm p95 **từng endpoint** khi đủ mẫu (thêm lúc chưa xem số liệu riêng của nhóm: `analyze.py` 2.16 chỉ in p95 chung) |
| Lỗi | < 0,5 % | | xem quy tắc lỗi dưới |
| Tải danh nghĩa | ≥ 50 request/giây hỗn hợp | | khoảng 20 hành trình/giây; hệ số đổi chốt bằng SP-08 (2.19) |

**TADSS** giữ SLO riêng (P6-52): SC1 p95 < 150 ms phía server (`tadss_decision_duration`) trong mọi lượt có TADSS; SC2 p99 ≤ 700 ms khi strategy lỗi, chấm ở SP-09. **AI**: SP-10, Mục B.7.4.

**Quy tắc đo chung**

- **Timeout** 5 s mỗi request (`slo.json` `timeout`); request timeout là lỗi và vẫn nằm trong mẫu số.
- **Tỷ lệ lỗi** = (5xx + timeout/lỗi mạng + 4xx **ngoài** danh sách hợp lệ) / tổng request của nhóm (metric k6 `request_errors`). Mỗi endpoint có danh sách cặp (status, `code` của envelope) hợp lệ, mỗi cặp ghi lý do (`slo.json` `businessRejections`); cặp hợp lệ đếm riêng ở `business_rejections` theo `code`. Bằng chứng 02/11: các lượt trước không có 409 nào, 4xx nghiệp vụ chỉ ở J3 (`400 BOOKING_REJECTED`, `400 SLOT_UNAVAILABLE_NOTIFY_REGISTERED`).

  | Endpoint | Cặp hợp lệ | Lý do |
  | :--- | :--- | :--- |
  | `POST /api/bookings/online` | 400 `BOOKING_REJECTED`, `SLOT_UNAVAILABLE`, `SLOT_UNAVAILABLE_NOTIFY_REGISTERED` | TADSS từ chối (`REPEATED_PAYMENT_EXPIRY`); hai VU tranh cùng khung giờ giữa lúc xem lịch và lúc đặt |
  | `POST /api/bookings/{id}/check-in` | 400 `BAD_REQUEST`, 409 `CONFLICT` | k6 chọn đơn đầu danh sách trong ngày, chưa tới hoặc đã quá giờ check-in; hai nhân viên ảo cùng xử lý một đơn |
  | `POST /api/bookings/{id}/checkout` | 400 `BAD_REQUEST`, 409 `CONFLICT` | đơn vừa bị nhân viên ảo khác check-out |
  | `POST /api/bookings/{id}/approve` | 409 `APPROVAL_ALREADY_RESOLVED`, `BOOKING_NOT_PENDING_APPROVAL`, `APPROVAL_EXPIRED` | nhiều VU dùng chung tài khoản quản lý một chi nhánh tranh nhau duyệt; job hết hạn duyệt chạy theo chu kỳ |
  | Hủy đơn, IPN, mọi endpoint đọc, đăng nhập | không có | gặp 4xx là lỗi (vd hủy ngay sau khi đặt phải thành công) |

- **Pha**: warm-up `WARMUP` (2 phút) tăng dần 25 % ⇒ 75 % mức danh nghĩa (`phase=warmup`) ⇒ chuyển 10 s + **1 phút ổn định ở mức đầu của pha đo** (`phase=settle`) ⇒ `phase=steady`. Chỉ `steady` vào thống kê và threshold. Nhãn pha gắn theo thời điểm gửi request (một scenario, không đổi nhãn giữa chừng hành trình).
- **Đủ mẫu**: p95 cần ≥ 100 mẫu (mỗi cửa sổ hoặc cả pha), p99 cần ≥ 1000 mẫu và chỉ tính trên **toàn pha steady**; thiếu thì ghi "không đủ dữ liệu", **không bao giờ coi là đạt**; mọi bảng có cột số mẫu. Nhóm thiếu mẫu p99 ở từng lượt (ước tính SP-01 15 phút: Đăng nhập, Ghi khoảng 900) thì p99 tính trên **mẫu thô ghép của 3 lượt SP-01** (cùng profile, tham số, seed checksum, image; `analyze.py pool`), **không lấy trung bình các p99**, ghi rõ là số gộp.
- **Phân vị chính xác**: k6 ghi mẫu thô từng request (`raw.csv.gz`); cửa sổ, số mẫu và phân vị tính trên mẫu thô (nội suy tuyến tính giữa hai hạng như k6). Histogram k6 trong Prometheus có biên …250, 500, 750, 1000, 2500… ms nên 300 ms và 800 ms nằm giữa hai biên: Prometheus chỉ dùng cho tài nguyên và hiển thị.
- **Chấm**: threshold k6 chấm SLO trên toàn pha steady của `load`, `soak`, `closed` (thoát 99 khi vượt; `stress`, `spike`, `breakpoint` vượt SLO là dự kiến nên chỉ chấm theo tiêu chí riêng ở B.7.4–B.7.5); `verdict.json` (`perf/stack/verdict.py`) là kết luận cuối: ĐẠT / KHÔNG ĐẠT / KHÔNG ĐỦ DỮ LIỆU theo từng nhóm và từng tiêu chí. Lượt k6 bị dừng giữa chừng (mã thoát khác 0 và 99) hoặc pha steady ngắn hơn lịch quá 30 s là `INCOMPLETE`, không bao giờ "đạt".
- **Tách CPU giữa hệ thống và công cụ đo** (k6 chạy cùng máy): `cpuset` chia VM Docker thành hai phần, container hệ thống (BE, TADSS, AI, hai PostgreSQL, Redis, Mailpit, LLM giả) dùng các CPU đầu, k6 và bộ quan sát (Prometheus, Alloy, cAdvisor, Grafana, Tempo, Pushgateway) dùng 3 CPU cuối (máy 8 CPU: `0-4` / `5-7`), trần `cpus` từng container giữ nguyên B.6.3; cấu hình thực tế ghi vào `metadata.json`. Giới hạn: CPU của VM là luồng của máy thật, Windows và Docker Desktop vẫn dùng chung.
- **Trần VU** (open model): `maxVUs` = 1,5 × tốc độ đỉnh × thời lượng hành trình trung bình (8,35 s ở lượt 02/11; đo lại ở SP-08 và cập nhật `slo.json` `journeySeconds`), **cấp sẵn toàn bộ** để k6 không khởi tạo VU giữa lượt (thời lượng hành trình dao động, J3 tới 30 s: lượt thử 02/11 chỉ cấp sẵn 1 × thì bậc 10 hành trình/giây bỏ 1,17 % lượt khi hệ thống vẫn khỏe); nhờ vậy `dropped_iterations` phản ánh hệ thống chậm chứ không phải k6 thiếu VU (lượt breakpoint rạng sáng 02/11 bỏ 326 lượt vì đụng trần 400). RAM k6 khoảng 1 MB/VU (spike ×5 khoảng 1300 VU, trần k6 2 GB). Lượt có CPU k6 ≥ 80 % trần hoặc RAM k6 ≥ 90 % trần trong một phút, hoặc phút không có số liệu, là `INFRA_LIMITED` (P6-47).
- **Tái lập**: `perf/results/k6/<mã lượt>/` gồm `metadata.json` (seed + checksum, tag + commit image 3 hệ, commit TADSS-Runtime + trạng thái sửa dở, SHA-256 script k6, `slo.json` và compose perf, tham số lệnh, `cpuset`/trần từng container), `schedule.json` (lịch tải thực dùng), `summary.json`, `raw.csv.gz`, `k6-exit`, `resources.json` (JSON kết quả các truy vấn Prometheus cố định của lượt; không chụp TSDB vì chứa mọi lượt, hàng trăm MB), `verdict.json`.
- **Tự kiểm**: `perf.sh run load ... --selftest` đổi ngưỡng p95 Đọc thành < 1 ms, mã lượt có hậu tố `selftest`, không nạp được lên dashboard; phải ra k6 thoát 99 và `verdict.json` KHÔNG ĐẠT.
- **Vị trí trong báo cáo**: chương "Đánh giá hiệu năng", SLO chỉ kết luận về hiệu năng; đặt cạnh kiểm thử tính đúng (unit, integration, E2E) và độ tin cậy TADSS khi strategy lỗi (SP-09, SC2).

#### B.7.3. Ramp-up
**Warm-up 2 phút** (JIT, pool kết nối, cache TADSS) tăng dần 25 % ⇒ 75 % mức danh nghĩa, gắn `phase=warmup`; chuyển 10 s rồi **1 phút `phase=settle`** ở mức đầu của pha đo; cả hai loại khỏi thống kê và threshold, chỉ tính `phase=steady` (B.7.2). Think time, tỷ lệ hành trình: B.6.5.

#### B.7.4. Kịch bản (tiêu chí bằng số chốt 02/11)
| Kịch bản | Mô tả | Trả lời câu hỏi | Tiêu chí |
| :--- | :--- | :--- | :--- |
| Load (SP-01) | Mức danh nghĩa 15 phút (open model) | Đạt SLO ở tải bình thường không | Bảng SLO B.7.2 trên toàn pha steady; ×3 lượt, p99 thiếu mẫu tính trên mẫu ghép 3 lượt |
| Stress (SP-02) | 100 ⇒ 150 ⇒ 200 ⇒ 250 % danh nghĩa, mỗi bậc chuyển 10 s + giữ 3 phút, rồi về 100 % 5 phút | Hệ thống xuống cấp thế nào, lỗi gì xuất hiện trước, có tự hồi phục không | Mô tả: từng bậc chấm như điểm gãy (B.7.5), ghi tín hiệu vi phạm đầu tiên; bậc cuối 100 % đạt tiêu chí cửa sổ = tự hồi phục |
| Spike (SP-03) | Danh nghĩa 3 phút ⇒ ×5 (chuyển 10 s) giữ 1 phút ⇒ về danh nghĩa (10 s) giữ 5 phút | Có sập không, mất bao lâu để hồi phục | Thời gian hồi phục = từ lúc về danh nghĩa tới đầu chuỗi **3 cửa sổ 10 s liên tiếp** có p95 Đọc < 300 ms (≥ 100 mẫu) và lỗi < 0,5 %; ≤ 60 s là ĐẠT, lâu hơn là KHÔNG ĐẠT, không hồi phục trong 5 phút ghi "không hồi phục" |
| Soak (SP-04) | Danh nghĩa 90 phút | Rò rỉ bộ nhớ/kết nối, tác động job Hangfire theo thời gian | ĐẠT khi đủ cả: SLO B.7.2 trên toàn pha; RAM BE **trung bình phút 15–25** so với **trung bình 10 phút cuối** của pha steady tăng ≤ 10 % (so trung bình vì RAM .NET dao động theo chu kỳ GC); số kết nối PostgreSQL của BE so cùng hai khoảng tăng ≤ 10 %; p95 Đọc mọi cửa sổ 10 phút ≤ 1,2 × cửa sổ đầu |
| A/B TADSS (SP-06) | Load với cờ bật và cờ tắt (P6-43) | TADSS cộng bao nhiêu vào p95/p99, tốn bao nhiêu tài nguyên | So sánh như B.7.8 |
| A/A overhead (SP-07) | Load với telemetry bật và tắt | Phép đo có làm méo kết quả không (P6-40) | So sánh như B.7.8 |
| AI (SP-10, task 2.31) | Khung đánh giá gọn (người dùng 02/11: AI chỉ gọi model miễn phí, use case phụ; đủ số liệu đạt/không và dùng lại khi thay bằng model tự xây). Qua BE, 2 request/giây × 10 phút, chia đều chat, gợi ý đặt sân, phân tích tổng quan | Phần xử lý của AI có đạt SLO không; toàn chặng BE ⇒ AI mất bao lâu | **SP-10A** LLM giả trễ 0 ms nhưng vẫn qua lời gọi HTTP và serialize JSON thật: p95 < 300 ms, lỗi < 0,5 % trên histogram `http.server.request.duration` của AI (bắt đầu khi request vào FastAPI, kết thúc khi gửi xong phản hồi: gồm dựng prompt, gọi HTTP tới LLM, parse + kiểm schema, serialize; không gồm mạng BE ⇒ AI và phần của BE). Biên bucket 250/500 ms: p95 ≤ 250 ms là ĐẠT chắc, trong 250–500 ms ghi số nội suy kèm "chưa kết luận chắc"; không sửa mã AI để thêm biên. **SP-10B** trễ 800 ms: chỉ báo cáo toàn chặng, không chấm. Báo cáo ghi SP-10A **không** chứng minh đạt SLO với LLM thật; thay model sau này thì chạy lại đúng SP-10B để so |

**Quy trình mỗi lượt (P6-46, P6-47):** khôi phục DB + xóa Redis ⇒ kiểm checksum ⇒ warm-up riêng ⇒ lượt đo ⇒ ghi kết quả + metadata ⇒ đánh giá hợp lệ. Thứ tự ưu tiên chạy: load trước, rồi stress, spike, soak; tối ưu và chạy lại để làm bằng chứng sau cùng.

#### B.7.5. Điểm gãy (SP-05, chốt 02/11)
`ramping-arrival-rate` **theo bậc**: từ 50 % danh nghĩa (10 hành trình/giây), mỗi bậc thêm 25 % (5 hành trình/giây) tới tối đa 300 % (60); mỗi bậc chuyển 10 s rồi giữ 3 phút; bỏ 30 s đầu của phần giữ, xét **2 cửa sổ 1 phút trọn vẹn** sau đó. Một cửa sổ **vi phạm** khi: p95 Đọc > 300 ms hoặc p95 Đặt sân > 1000 ms (chỉ xét nhóm có ≥ 100 mẫu trong cửa sổ; các nhóm khác ít mẫu mỗi phút nên chỉ báo cáo), hoặc lỗi > 0,5 %, hoặc `dropped_iterations` > 1 % số lượt dự kiến của cửa sổ. **Điểm gãy = bậc thấp nhất có ít nhất một cửa sổ vi phạm.** Ghi lại tại bậc gãy và bậc liền trước (JSON truy vấn Prometheus trong `verdict.json`): CPU/RAM từng container, số lần GC theo thế hệ, hàng đợi và số luồng thread pool, pool kết nối Npgsql (đang dùng/rảnh, request chờ, tối đa), kết nối PostgreSQL theo trạng thái, khóa DB, tỷ lệ đường lùi TADSS (`tadss_decisions_total{execution_type="FALLBACK"}`).

#### B.7.6. Phân tích p95/p99
So **client** (k6, toàn bộ mẫu, chính xác) với **server** (histogram BE, xấp xỉ theo bucket): chênh lệch = mạng + xếp hàng ở Kestrel + thời gian k6. Phân vị từ histogram là xấp xỉ: ghi biên bucket và sai số; ưu tiên histogram mũ (exponential) nếu đường Collector ⇒ Prometheus giữ được. Giải thích theo trải nghiệm: p95 là 1/20 lượt chậm nhất người dùng gặp thường xuyên; p99 quyết định cảm giác "thỉnh thoảng treo".

Báo cáo chính thức tối thiểu có, **theo từng nhóm hành trình/endpoint** (không gộp mọi endpoint thành một số): p50/p95/p99 phía client (k6); p50/p95/p99 phía server khi histogram cho phép, kèm biên bucket; tỷ lệ lỗi; throughput; tài nguyên cùng khoảng thời gian; **số mẫu** và thời gian đo; trạng thái hợp lệ của lượt (P6-47). Hai phía là hai góc nhìn trên hai tập mẫu khác nhau (client gồm mạng và xếp hàng), không so như cùng tập.

#### B.7.7. Kết quả và nút cổ chai
Mỗi lượt sinh `perf/results/<run_id>/` (gitignore; lượt chọn làm bằng chứng chép sang `docs/research/evidence-packs/` như Evidence Pack): summary JSON của k6, cấu hình đã dùng (không bí mật), digest image BE/TADSS, sha 3 repo, strategy version, trạng thái cờ, seed, thông số máy, JSON kết quả các truy vấn Prometheus chính, `manifest.json` (SHA-256). Script dựng báo cáo so sánh nhiều lượt. Giả thuyết nút cổ chai cần kiểm: pool kết nối DB, tranh chấp khóa sân (slot lock) khi nhiều người đặt cùng giờ đẹp, BCrypt khi đăng nhập dồn, độ trễ/timeout TADSS gây đường lùi, job Hangfire chạy chồng, rate limiter.

#### B.7.8. Tối ưu và so sánh trước/sau
Mỗi cải tiến chỉ đổi **một** yếu tố (chỉ số DB, kích thước pool, truy vấn N+1, cache…), image mới theo sha, chạy lại đúng kịch bản và seed, ≥ 3 lượt mỗi bên, so trung vị và khoảng (P6-44). Chỉ gọi là **cải thiện** khi khoảng min–max của hai bên không chồng nhau; chồng nhau thì ghi "không phân biệt được" (chốt 02/11). Thay đổi code là việc của BE/TADSS theo quy trình thường (test, build, review); tài liệu ghi lý do và kết quả.

#### B.7.9. Kết luận và hạn chế
Mức đáp ứng từng SLO; hạn chế đã biết: một máy chạy cả tải lẫn hệ thống, laptop hạ xung, PostgreSQL cục bộ khác Neon (không có độ trễ mạng tới Neon), tỷ lệ hành trình là giả định, không có người dùng thật.

#### B.7.10. Kiểm (6C-5)
Mỗi kịch bản chạy được bằng một lệnh có tham số (kịch bản, cờ, seed, tag image); threshold k6 phản ánh đúng SLO; lượt dừng giữa chừng không sinh kết quả "đạt"; báo cáo dựng lại được từ thư mục kết quả.

---

### Giai đoạn 2B — Môi trường `perf` (task 2.10, 2.11, 2.12, 2.13, 2.14, 2.15, 2.30; 01/11/2026) — phần chính xong; còn exporter + Tempo, spike tài nguyên (2.16), file thống kê thật (2.17)

- **Một lệnh** (người dùng chốt 01/11: gói gọn, chạy đơn giản): `bash perf/perf.sh up | seed | run <profile> | reset | status | down` ([`RUNBOOK.md`](../docs/guides/RUNBOOK.md)). `up` lấy tag image **đang chạy ở bản deploy** (đo đúng bản thật), bật cả bộ quan sát kiểm thử; `run` tự khôi phục dữ liệu, ghi `metadata.json` (image, commit, cờ TADSS, seed + checksum, CPU/RAM máy) và `--code SP-xx` nạp lên dashboard.
- **2.10, 2.11 Compose** `perf/stack/docker-compose.yml` (project `smashcourt-perf`, container `perf-*`, mạng `smashcourt-test`): BE/TADSS/AI theo tag, PostgreSQL 17 nạp `docs/database/schema_smashcourt.sql` (bỏ schema `hangfire` của bản dump vì thiếu dòng phiên bản ⇒ Hangfire cài lại sẽ lỗi `column updatecount already exists`), PostgreSQL 16 + Redis cho TADSS, Mailpit, LLM giả, k6 (profile). Hằng không bí mật (JWT, OTP, VNPay giả, khóa TADSS `tadss-perf-key`), rate limit nâng, Hangfire bật, telemetry 5 giây, trace tắt, trần CPU/RAM theo B.6.3. `perf/stack/tests/test_compose.py` chặn host ngoài stack, biến bí mật, `env_file`, thiếu trần tài nguyên.
- **2.30 LLM giả** `perf/stack/fake_llm.py` (thư viện chuẩn, chạy thẳng trên `python:3.12.12-alpine`): khung `generateContent`, JSON có các khóa router AI đọc, `usageMetadata`, trễ 800 ± 200 ms. Đã kiểm: BE ⇒ AI ⇒ LLM giả trả lời trong khoảng 1,3 s, metric Gemini của AI có số liệu.
- **2.12–2.14 Dữ liệu** `perf/stack/datagen.py` + `distribution.placeholder.json` (nhãn PLACEHOLDER): 2.000 khách ⇒ 43.144 dòng, 7.335 đơn (lịch sử hoàn tất / không đến / hết hạn thanh toán + đơn có sẵn 14 ngày tới, 30 % khung giờ), 1,4 s; nạp qua mọi ràng buộc schema thật (gồm chống trùng sân). `seed` dựng database mẫu `smashcourt_seed` + `tadss_seed`; `reset` 18–31 s, checksum nội dung khớp; đã kiểm một đơn đặt trong lượt và một quyết định TADSS biến mất sau reset.
- **2.15 k6** `perf/k6/mix.js` + `lib/` : 7 hành trình theo tỷ lệ Q4, think time có seed, tag `name`/`slo`/`journey`/`phase`, IPN tự ký, 4xx nghiệp vụ đếm riêng; profile smoke, load, stress, spike, soak, breakpoint, closed. **Lượt smoke** (6 VU, đủ J1…J7): 0 % lỗi, 11/11 đơn đặt được, 10 IPN hợp lệ (`payment_ipn_logs.is_valid`) ⇒ 10 đơn `PAID_ONLINE`, 1 khách hủy; nạp làm SP-08 dạng hạn chế.
- **Quan sát đầu tiên (chưa phải lượt đo):** ngay sau reset BE nguội: p95 chung 5,5 s (đăng nhập 7,5 s, đặt sân 6,3 s, IPN 2,4 s, phía server khớp phía client); chạy nóng p95 1,1 s. Đăng nhập (BCrypt cost 12) và đặt sân là ứng viên nút cổ chai cho SP-08 (2.19); warm-up của các profile loại phần nguội.
- **Bổ sung 02/11 — quan sát:** Tempo `grafana/tempo:2.10.10` (Tempo 3.x chưa có trên Docker Hub; nhánh 2.10 vẫn phát hành) trong bộ quan sát, Alloy chuyển trace OTLP sang; exporter PostgreSQL (hai DB, kết nối vào database `postgres` vì `seed`/`reset` cần database nguồn không còn phiên) và Redis chạy **trong Alloy** (không thêm container); hàng "Hiệu năng" thêm 3 panel (kết nối theo trạng thái, giao dịch/giây, khóa BE / Redis TADSS). Đã kiểm một trace đặt sân: 35 span BE ⇒ TADSS ⇒ PostgreSQL, tổng 420 ms, TADSS 23 ms. `check` của dashboard sửa lỗi coi `pg_stat_activity_count` là đuôi `_count` của histogram (test thêm).
- **2.17 không cần người dùng xuất lại:** `data/private/smashcourt_bookings.csv` (gói 5.6, 215 booking, không tên/điện thoại/email) đủ trường ⇒ `perf/stack/reference_stats.py` rút `distribution.reference.json` (chỉ phân bố tổng hợp, commit được; test chặn định danh lọt ra) và `seed` dùng mặc định. Phân bố thật khác placeholder: 61 % đặt trong ngày, cao điểm 6–7 giờ và 18 giờ, 13 % đơn 2 sân, 25 % lượt đặt của khách hết hạn thanh toán ≥ 2 lần ⇒ TADSS 2.1.0 từ chối một phần (`BOOKING_REJECTED`, đúng quy tắc `REPEATED_PAYMENT_EXPIRY`) và có đơn `PENDING_APPROVAL` cho J7. Seed: 20.404 đơn, 101.364 dòng. Hạn chế: dữ liệu môi trường phát triển (nhiều đơn thử nghiệm hủy / không đến).
- **2.16 thử tài nguyên (máy 8 CPU / 8 GB, bản deploy + runner vẫn chạy):** (1) không tải: perf 0,37 lõi, 540 MB; tất cả container SmashCourt 0,64 lõi, 2,5 GB. (2–3) tăng đều 2 ⇒ 40 hành trình/giây: k6 tối đa 0,3 lõi (không bị giới hạn); **nút thắt là CPU của BE** (trần 2 lõi): p95 ≤ 0,41 s tới khoảng 68 request/giây, 76 request/giây p95 vọt 3,6 s, khoảng 90 request/giây BE chạm 2 lõi và p95 > 10 s; PostgreSQL 0,8/1 lõi, TADSS ≤ 0,1, AI ≤ 0,3, RAM BE ≤ 590 MB; lỗi ≤ 0,05 %. Mức danh nghĩa đề xuất 50 request/giây có dư địa. **Lượt thử đầu phát hiện lỗi của mô hình tải:** VU mới của open model tự đăng nhập ⇒ khi hệ thống chậm, số VU tăng tới 800 và BCrypt dồn thành bão đăng nhập (đăng nhập 5 % ⇒ 14 % request) làm BE sụp sớm ở 40 request/giây; sửa: `setup()` đăng nhập sẵn 300 khách + nhân viên + quản lý, token perf sống 240 phút, đăng nhập chỉ đo ở J2 (lượt sau: 1,4–1,8 %). **Lượt thứ hai bị máy đo đứng 3 phút** (mọi nguồn mất số liệu, Pushgateway 80/100 mẫu) ⇒ `perf/stack/analyze.py` (tự chạy sau mỗi lượt, ghi `resources.json`) đánh dấu `INFRA_LIMITED`; con số trên chỉ là chỉ báo. (5) Bảng B.6.3 giữ nguyên (trần, không phải phần giữ riêng; BE 2 lõi là trần ràng buộc cần đo). (4) 02/11, người dùng cho phép: dừng tạm 10 container của bản deploy + 3 runner (`docker stop`, giữ volume), chạy lại đúng lượt tăng tải, bật lại (health, URL ngrok 200, runner nhận việc): **không còn phút mất số liệu**; ở cùng mức tải CPU BE thấp hơn khoảng 30–45 % (72,7 request/giây: 0,77 lõi so với 1,13) và p95 ≤ 0,10 s tới 84 request/giây, 0,47 s ở 101 request/giây, 0 % lỗi; chưa chạm điểm gãy ở 40 hành trình/giây ⇒ SP-05 (2C) cần `--max-rate` cao hơn. Phút cuối p95 2,6 s khi CPU BE chỉ 1,1 lõi ⇒ có nút thắt ngoài CPU, phân tích bằng trace ở 2.27. **Kết luận 2.16:** lượt chính thức bắt buộc tắt bản deploy + runner (tranh CPU làm sai số liệu và gây VM đứng); bảng B.6.3 giữ nguyên.
- **2B xong.** Sang 2C: chốt SLO (2.18, người dùng).

### Giai đoạn 2C — Kịch bản, phân tích (02/11: task 2.18 xong; 03/11: 2.19 SP-08 xong; 04/11: chẩn đoán E1–E4 + SP-01 ×3 trước khi sửa; 05/11: P6-53 bỏ index trùng, P6-54 auto-prepare chỉ cho kiểm thử/đánh giá, máy đo theo core vật lý; tiếp: lần tắt 3 — SP-01 ×3 sau P6-54 + thí nghiệm CPU)

- **2.18 chốt** (người dùng duyệt 4 điểm + quy tắc đo, 02/11): bảng SLO và quy tắc ở B.7.2, tiêu chí kịch bản B.7.4, điểm gãy B.7.5, so sánh trước/sau B.7.8. Máy đọc: `perf/k6/slo.json` (một nguồn cho threshold k6 và phần chấm).
- **Hiện thực:** `client.js` timeout 5 s, Rate `request_errors` + danh sách cặp (status, `code`) hợp lệ theo endpoint, nhãn `phase` theo thời điểm gửi; `mix.js` mỗi profile open một scenario (warm-up 25 ⇒ 75 % ⇒ settle 1 phút ⇒ steady), stress/spike/breakpoint theo bậc và thông số của `slo.json`, trần VU 1,5 × tốc độ đỉnh × thời lượng hành trình cấp sẵn toàn bộ, threshold theo nhóm SLO (load/soak/closed), `schedule.json`; `perf.sh` ghim `cpuset` (hệ thống `0-4`, k6 + bộ quan sát `5-7`), container k6 tên `perf-k6`, `--out csv` mẫu thô, `k6-exit`, metadata tái lập, `--selftest`, `pool`; `perf/stack/verdict.py` (chấm từ mẫu thô) + `analyze.py` (`verdict.json`, `INFRA_LIMITED` khi k6 chạm trần, `INCOMPLETE`, tài nguyên tại bậc gãy, gộp lượt); dashboard đọc `verdict.json` (thiếu mẫu / lượt không hợp lệ là hạn chế, lượt tự kiểm bị từ chối). Test: 134 pytest `perf/testboard perf/stack`.
- **Kiểm trên máy (bản deploy vẫn chạy, chỉ kiểm chức năng, không phải lượt đo):** (1) lần đầu lộ lỗi `setup()` gọi đồng hồ pha ngoài scenario ⇒ k6 thoát 107 ⇒ verdict `INCOMPLETE` đúng như thiết kế; sửa (`__VU === 0` là setup). (2) **Tự kiểm** `run load --rate 2 --duration 1m --warmup 30s --selftest`: k6 thoát 99, `verdict.json` KHÔNG ĐẠT; số `verdict.py` tính từ mẫu thô **khớp tuyệt đối** summary k6 (Đọc n = 342, p95 46,0 ms, p99 69,9 ms). (3) `run load --rate 5 --duration 2m`: Đọc ĐẠT (n = 1662), các nhóm khác KHÔNG ĐỦ DỮ LIỆU; k6 thoát 99 vì p95 Đăng nhập 756 ms trên **24 mẫu** ⇒ minh họa vì sao kết luận theo `verdict.json`; `raw.csv.gz` 300 KB / 2,5 phút; RAM k6 khoảng 1 MB/VU; 29 từ chối nghiệp vụ hợp lệ, 0 lỗi. (4) Breakpoint ngắn 2 ⇒ 6 ⇒ 10 hành trình/giây: lần đầu cấp sẵn 1 × trần VU ⇒ bậc 10 bỏ 1,17 % lượt khi hệ thống vẫn khỏe (p95 Đọc 191 ms, BE 0,57 lõi) ⇒ báo động giả; sửa cấp sẵn toàn bộ ⇒ 0 lượt bỏ, verdict "chưa tới điểm gãy". Snapshot tài nguyên chạy được trên Prometheus thật (GC, thread pool, pool Npgsql 100, khóa, đường lùi TADSS). Ghi nhận: `setup()` đăng nhập sẵn khoảng 300 tài khoản làm BE chạm 2 lõi khoảng 2 phút trước warm-up (ngoài mọi pha đo).
- **2.19 SP-08 baseline xong (03/11, lượt `20261103-213705-breakpoint-SP-08`, hợp lệ OK):** máy sạch (người dùng cho phép dừng 7 container bản deploy + 3 runner, sau lượt bật lại: health, URL ngrok 200, runner nhận việc), image đúng bản deploy (BE `sha-c9df6d6`, TADSS `sha-c5cc1a7`, AI `sha-7aad965`), breakpoint `--rate 20`, bậc 10 ⇒ 60 hành trình/giây, giữ 3 phút.
  - **Hệ số:** 2,72 request / hành trình (2,68–2,76 ở mọi bậc chưa quá tải) ⇒ 50 request/giây = 18,4 hành trình/giây ⇒ **`--rate` danh nghĩa 19** (≈ 51,7 request/giây). Thời lượng hành trình 7,1–7,9 s ở bậc chưa quá tải (8,3 s ở bậc 50) ⇒ giữ `journeySeconds` 8,35 (còn dư trần VU).
  - **Điểm gãy theo tiêu chí B.7.5 = 20 hành trình/giây (≈ 55 request/giây)**, không phải do hết CPU: một đợt nghẽn khoảng 20 s lúc 21:49:54 (cửa sổ 2 của bậc 20: p95 Đọc 2264 ms, lỗi 1,88 % là timeout 5 s) rồi bậc 25 lại bình thường (p95 52–68 ms). Các đợt tương tự lặp lại ở bậc 30, 35, 40, 45 (p95 Đọc 372–996 ms trong một cửa sổ). **Bão hòa thật** từ bậc 50–55 (≈ 135 request/giây): CPU BE chạm trần 2 lõi, lỗi 2–15 %, k6 bắt đầu bỏ lượt; bậc 60 sụp (lỗi tới 98 %).
  - **Bằng chứng đợt nghẽn** (Prometheus, lấy mẫu 5 s): kết nối PostgreSQL đang chạy 1 ⇒ **68**, trong đó **46 chờ `LWLock: LockManager`**; khóa **1705 `AccessShareLock`** (khoảng 25 / kết nối, vượt 16 khóa fast-path mỗi phiên) + 80 `RowExclusiveLock`; phiên `idle in transaction` dài **3,6 s**; 6 request chờ pool Npgsql; hàng đợi thread pool 9; CPU BE chỉ 0,95 / 2 lõi. Không trùng checkpoint (log PostgreSQL: 14:46:46–14:48:16 và 14:51:47 UTC, sync vài chục ms). Mọi endpoint chậm cùng lúc (`GET /api/courts` trung vị 5 ⇒ 174 ms), nặng nhất `POST /api/bookings/online` (trung vị 5 s = timeout).
  - **Giả thuyết cho 2.27 (chưa kết luận):** một transaction (nghi luồng đặt sân) giữ khóa trong lúc chờ việc khác ⇒ request sau xếp hàng ⇒ nhiều phiên cùng lấy khóa quan hệ ngoài fast-path ⇒ tranh chấp LockManager. Kiểm bằng lượt phân tích `up --trace` + `pg_stat_activity` / `pg_locks` chi tiết; không sửa trước khi có bằng chứng.
- **Chẩn đoán đợt nghẽn trước khi đo SP-01 (người dùng chốt 03/11, kèm review ngoài; hai lần tắt bản deploy + runner):**
  - **Đã kiểm, loại bỏ:** lời gọi TADSS đã ở ngoài transaction từ 6A-2 (`BookingService` bước 13: transaction 1 commit ⇒ TADSS ⇒ transaction 2); mọi transaction ghi dùng `TransactionScopeHelper.ReadCommitted()` có `TransactionScopeAsyncFlowOption.Enabled`. PostgreSQL perf và bản deploy đều 17.6 (16 khóa fast-path / phiên); perf `max_connections` 200, `Maximum Pool Size` 100; Hangfire 4 job mỗi phút, 2 job mỗi 5 phút, 1 job mỗi giờ. Đợt nghẽn SP-08 (9 đợt) bắt đầu ở giây 40, 20, 30, 40, 0, 50… cách nhau 40–290 s, không trùng lịch job, dày lên theo tải; lúc nghẽn không có phiên chờ khóa dòng (`wait_event_type = Lock`), chỉ chờ `LockManager`.
  - **Nghi phạm mới:** collector `locks` của exporter PostgreSQL trong Alloy kiểm thử đọc `pg_locks` mỗi 5 s; đọc `pg_locks` phải khóa mọi phân vùng của bộ quản lý khóa (đúng chỗ tranh chấp). Alloy của bản deploy (Grafana Cloud) **không** thu PostgreSQL ⇒ nếu đây là nguyên nhân, điểm gãy SP-08 bi quan hơn hệ thống thật. Từ 03/11 collector `locks` **tắt** trong mọi lượt chính thức (SP-01 trước và sau); **hạn chế: SP-08 chạy khi collector còn bật**. Dashboard và `verdict.json` không còn số khóa PostgreSQL ở lượt chính thức.
  - **Định nghĩa đợt nghẽn** (`slo.json` `episodes`, `verdict.json` mọi lượt): cửa sổ 10 s có p95 Đọc > 300 ms (cửa sổ ≥ 100 mẫu); cửa sổ xấu bắt đầu cách cửa sổ xấu trước ≤ 30 s là cùng một đợt; chỉ pha steady.
  - **Lần tắt 1 (khoảng 1,5 giờ):** (a) lượt chẩn đoán `load --rate 30 --duration 15m`, `up --trace`, `--pg-sample locks` (mỗi 1 s `pg_stat_activity`; `pg_locks` theo phiên, có `fastpath`, chỉ khi ≥ 10 phiên đang chạy hoặc có phiên chờ LockManager, tối đa một lần / 2 s), không nạp dashboard, **không dùng để đếm tần suất nghẽn**; (b) bậc thang thí nghiệm, mỗi lượt `load --rate 30 --duration 15m`, trace tắt, `--pg-sample activity`, không nạp dashboard, mỗi phép so chỉ khác một yếu tố: **E1** pool 100 + collector bật (như SP-08) ⇒ **E2** pool 100 + collector tắt (so E1: collector) ⇒ **E3** chỉ khi E2 còn nghẽn: pool 20 + collector tắt (so E2: pool; theo dõi `db_client_connections_pending_requests` và timeout vì Npgsql chờ pool tới 15 s > timeout 5 s của k6). Tiêu chí chốt trước: E1 phải có ≥ 3 đợt (nếu không: Inconclusive); **Confirmed** khi lượt thí nghiệm còn 0–1 đợt; mỗi bên một lượt nên là kết luận chẩn đoán, "đã cải thiện" vẫn chỉ kết luận bằng SP-01 ×3 trước/sau.
  - **Lần tắt 2 (khoảng 1,2 giờ):** SP-01 ×3 ở `--rate 19`, collector tắt, `--pg-sample activity` (giữ y hệt ở lượt sau) = số **trước**.
  - **Mức kết luận:** Confirmed = thí nghiệm can thiệp đổi một yếu tố làm đợt nghẽn biến mất / giảm rõ; Probable = tương quan (khóa ngoài fast-path > 16 / phiên trùng lúc chờ LockManager); Inconclusive = không có hai điều trên. Phương án sửa so sánh sau đó (chờ duyệt): giảm pool, `AsSplitQuery` / giảm số bảng-index mỗi câu lệnh (khóa đọc ngoài transaction giữ theo câu lệnh; lúc lập kế hoạch PostgreSQL khóa bảng và mọi index), nâng PostgreSQL 18 (nhiều khóa fast-path hơn; đổi cả bản deploy). SP-05 sau sửa chỉ mô tả điểm gãy mới (SP-08 khác hai yếu tố: collector và bản sửa).
  - **Công cụ:** `perf/stack/pgsampler.py` (psql trong container riêng trên CPU công cụ đo, giữ một kết nối; `pg-samples.jsonl.gz`, `hangfire-jobs.json`), `perf.sh up --pg-locks on|off --db-pool N`, `perf.sh run --pg-sample activity|locks --tag TÊN`; metadata ghi `dbPool`, `pgLocksCollector`, `pgSample`, `tag` đọc từ container đang chạy.
- **Lần tắt 1 (04/11 00:12–00:40, người dùng dừng sau lượt chẩn đoán; E1–E3 dời sang lần sau):** lượt `20261104-001351-load-diag` (hợp lệ OK, `--rate 30` 15 phút, trace 100 %, collector `locks` tắt, `--pg-sample locks`: 1199 mẫu activity, 30 mẫu locks, 178 trạng thái job Hangfire). Trước đó (03/11 khuya) file đĩa Docker chuyển từ C: sang `D:\DockerData` (người dùng yêu cầu, hết dung lượng C:); C: và D: là hai phân vùng của **cùng một ổ NVMe**, không đổi tốc độ đĩa. Số đo của lượt này bị trace 100 % làm méo (CPU BE 1,1–1,96 / 2 lõi suốt lượt, p95 Đọc 1,6 s) nên **chỉ dùng để chẩn đoán**. Kết quả (mức **Probable**, chưa phải thí nghiệm can thiệp):
  - **Nghẽn vẫn xảy ra khi collector `locks` đã tắt** (5 đợt) ⇒ collector không phải nguyên nhân duy nhất; vai trò của nó chờ cặp E1/E2.
  - **Ba đợt đầu, cùng một dấu hiệu:** 31–70 phiên đang chạy, chờ chủ yếu `LockManager` (8–31 phiên) và `BufferContent`; 505–999 khóa **ngoài fast-path**, tối đa **56–61 khóa / phiên** (gấp 3–4 lần giới hạn 16; phiên đặt sân khóa `bookings`, `booking_courts`, `booking_price_items`, `invoices`… cùng mọi index); phiên `idle in transaction` dài **2,2–8,7 s** (transaction 1 của đặt sân mở mà ứng dụng chưa gửi câu tiếp, sau `INSERT INTO invoices`).
  - **Hai đợt cuối, dấu hiệu khác:** chỉ 3–8 phiên đang chạy, 0 khóa ngoài fast-path, chờ `WALWrite` / `WalSync` / `ClientRead`, p95 tới 1,9 s ⇒ chậm không nằm ở bộ quản lý khóa (nghi CPU BE gần trần do trace, hoặc ghi WAL); cần lượt không trace (E1/E2) để tách.
  - **Job Hangfire:** job mỗi phút có mặt quanh mọi đợt (tất yếu ở lượt 15 phút) ⇒ không phân biệt được, chưa kết luận.
  - **Giả thuyết dẫn đầu (Probable, 04/11 sáng; đã thay bằng kết quả đọc mã bên dưới: không có lỗi riêng trong transaction 1, nguồn khóa là câu đọc 12 bảng):** transaction 1 đặt sân giữ khóa dòng lâu khi ứng dụng chậm gửi câu lệnh (thread pool / CPU), trong lúc mỗi phiên cần khoảng 60 khóa quan hệ ngoài fast-path ⇒ nhiều phiên cùng lấy khóa qua bộ quản lý khóa ⇒ dồn ứ. Việc tiếp: đọc mã transaction 1 (không cần tắt deploy), rồi E1/E2/(E3).
- **Đọc mã transaction 1 + dữ liệu lượt chẩn đoán (04/11, không tắt deploy):**
  - **Không có lỗi riêng trong mã transaction 1:** phiên `idle in transaction` dừng rải đều ở **mọi** câu của transaction 1 và 2 (đọc giá, `nextval`, `INSERT bookings/booking_courts/booking_price_items/invoices/slot_locks`, `UPDATE`…), tuổi tính từ câu cuối tối đa 1,5–3,9 s (con số 8,7 s ở trên tính từ đầu transaction) ⇒ ứng dụng chậm đều giữa các câu, không kẹt ở một dòng mã.
  - **Nguồn khóa ngoài fast-path:** câu đọc chi tiết booking `GetByIdWithDetailsAsync` (12 bảng trong một câu, không `AsSplitQuery`; dùng ở bước 15 đặt online, `GET /api/me/bookings/{id}`, hủy, check-in/out, IPN) cần khoảng 66 khóa quan hệ vì PostgreSQL khóa **mọi index** của mọi bảng khi lập kế hoạch (`bookings` có 12 index); 163 mẫu khóa của câu này: trung bình 49, tối đa 72 khóa trong một câu tự commit. Thử trên PostgreSQL 17.6 (2 bảng, 12 index): lần chạy còn lập kế hoạch giữ 14 khóa, kế hoạch chung của câu đã prepare giữ 4 (chỉ index được dùng). Có index trùng `idx_booking_status_date` (INCLUDE) / `idx_bookings_status_date` cùng `(status, booking_date)`.
  - **Trace Tempo của một đặt sân 9,9 s trong đợt nghẽn:** khoảng trống 0,9 s phía BE không có câu SQL; `DELETE slot_locks` 1,7 s, `SELECT customer_loyalties` 1,0 s; gọi TADSS 799 ms phía BE trong khi TADSS xử lý 52 ms (nhận trễ 514 ms) ⇒ chạm `TimeoutMs` 800 ms, BE gọi lại (lần hai là phát lại idempotent). BE không có lời gọi đồng bộ chặn luồng (`.Result`, `.Wait()`, `GetResult()`).
  - Thêm núm `perf.sh up --db-prepare N` (`Max Auto Prepare` của Npgsql, mặc định 0 = như bản deploy; metadata `dbAutoPrepare`) cho nhánh E4 (người dùng duyệt 04/11).
- **Lần tắt 2 (04/11 21:36 – 05/11 00:05, gộp theo người dùng; script tự bật lại 10 container bản deploy):** image BE `sha-d760e6d`, TADSS `sha-20f6294`, AI `sha-7aad965`; mọi lượt hợp lệ OK, `--pg-sample activity`, trace tắt.

  | Lượt (`load --rate 30`, 15 phút) | Yếu tố | Đợt nghẽn | Cửa sổ 10 s xấu / 91 | p95 Đọc xấu nhất |
  |---|---|---|---|---|
  | E1 `20261104-213802-load-E1` | pool 100, collector `locks` bật | 11 | 18 | 2705 ms |
  | E2 | pool 100, collector tắt | 11 | 32 | 3258 ms (một đợt 9 cửa sổ) |
  | E3 | pool 20 | 9 | 15 | 1631 ms (đa số 0,5–0,65 s) |
  | E4 | pool 100, `Max Auto Prepare` 50 | 5 | 5 (mỗi đợt 1 cửa sổ) | 1736 ms (còn lại 0,36–0,75 s) |

  - **Kết luận theo mức đã chốt** (mỗi nhánh một lượt; E1 và E2 cùng bản chất mà 18 so với 32 cửa sổ xấu ⇒ dao động giữa các lượt lớn):
    - collector `locks` là nguyên nhân: **loại** (E1 = E2 = 11 đợt);
    - khóa lúc lập kế hoạch (mọi index) gây tranh chấp LockManager: **Probable, mạnh** — E4 giảm cửa sổ xấu 32 ⇒ 5, không đợt nào dài quá 10 s, vượt xa dao động E1/E2; chưa Confirmed vì còn 5 đợt (tiêu chí 0–1);
    - quá nhiều phiên DB đồng thời (pool 100): **Inconclusive** — E3 giảm độ nặng (15 cửa sổ, p95 thường < 0,65 s) nhưng 15 nằm trong dải dao động 18–32;
    - ứng dụng chậm giữa các câu (CPU / thread pool BE): **Inconclusive**.
  - **SP-01 ×3 trước khi sửa** (`--rate 19`, cấu hình mặc định; `20261104-230201/232300/234352-load-SP-01-r1…r3`, gộp `pooled-20261104-230201-load-SP-01-r1-3runs.json`): **KHÔNG ĐẠT** chỉ vì Đăng nhập. Đọc p95 40,2 ms / p99 273,7 ms; Đặt sân 252,5 / 833,4 ms; IPN p95 155,5 ms, Ghi p95 135,6 ms (p99 không đủ dữ liệu); lỗi 0,009 % (141 875 request) — đều đạt. **Đăng nhập p95 813,5 ms (< 500), p99 1310,5 ms (< 1000): KHÔNG ĐẠT**; nhanh nhất 294 ms, trung vị 369–399 ms ⇒ do chính BCrypt work factor 12 (khoảng 300 ms CPU mỗi lần kiểm mật khẩu, `AuthService`), không do đợt nghẽn. Đợt nghẽn vẫn còn ở tải danh nghĩa: 2 / 3 / 2 đợt mỗi lượt (2 đợt p95 Đọc ≈ 3 s).
- **Người dùng chốt 05/11:** (b) bỏ index trùng của `bookings` ⇒ P6-53; (c) **giữ BCrypt work factor 12** (bảo mật là một tiêu chí), tìm cách đạt hiệu năng mà không hạ; đo ngoài đợt nghẽn: Đăng nhập p50 378 / p95 735 / p99 1064 ms (2267 mẫu), trong đợt nghẽn p95 1812 ms ⇒ hết nghẽn vẫn chưa đạt 500 ms.
- **Mức kết luận E4 theo tiêu chí đã chốt** (Confirmed khi mốc ≥ 3 đợt và nhánh can thiệp ≤ 1 đợt): E2 11 đợt (≥ 3, mốc hợp lệ), E4 **5 đợt** (> 1) ⇒ **Probable mạnh, chưa Confirmed**; số cửa sổ xấu 32 ⇒ 5, không đợt nào > 10 s, CPU PostgreSQL 0,39 ⇒ 0,22, CPU BE 1,19 ⇒ 0,99, p50 cả 13 endpoint nhanh hơn 30–45 %, không endpoint nào chậm đi; RAM đỉnh PostgreSQL 509 ⇒ 622 MB. Áp dụng làm P6-54.
- **Cấu trúc CPU máy đo (05/11, `lscpu` trong VM Docker):** i5-1135G7, **4 core vật lý × 2 luồng**, cặp luồng (0,1) (2,3) (4,5) (6,7). cpuset hiện tại hệ thống `0-4` / công cụ `5-7` ⇒ hai nhóm **dùng chung core vật lý 2** (luồng 4 và 5); WSL2 không ghim vCPU vào luồng vật lý của Windows nên ánh xạ chỉ là cấu trúc Linux nhìn thấy.
- **Vì sao E4 còn 5 đợt:** kế hoạch chung vẫn giữ **24 khóa** cho câu chi tiết booking (đo ở integration test: 65 ⇒ 24 từ lần chạy thứ 11), **vẫn > 16 slot fast-path** ⇒ khoảng 8 khóa / câu vẫn đi qua bộ quản lý khóa dùng chung. Auto-prepare bỏ được phần khóa do lập kế hoạch, **nguyên nhân gốc (một câu gộp 12 bảng) còn nguyên**. **Bước tối ưu tiếp theo (chưa làm, cần duyệt):** giảm số quan hệ mỗi câu xuống ≤ 16 khóa. Lưu ý từ đếm index: `AsSplitQuery` một mình có thể chưa đủ vì câu gốc vẫn nối các quan hệ một-một (`bookings` 13 + `branches` 3 + `users` 7 + `invoices` 5 + `booking_promotions` 6 quan hệ) và mỗi câu con nối lại `bookings`; hướng chắc hơn là chiếu chỉ cột / bảng cần cho từng luồng (bước 15 đặt online, chi tiết của khách, hủy) kèm đo số khóa bằng test như `NpgsqlAutoPrepareTests`.
- **Hạn chế của mọi lượt đo tới 05/11 (ghi vào báo cáo):** (1) cpuset `0-4` / `5-7` để k6 + bộ quan sát dùng chung core vật lý 2 với hệ thống; (2) chế độ nguồn Windows khi cắm sạc là **Best power efficiency** (`ActiveOverlayAcPowerScheme` `961cc777-…`, kiểm 05/11), không phải Best performance; (3) chưa ghi xung nhịp CPU. Các lượt so sánh trong cùng điều kiện (E1–E4, SP-01 trước) vẫn so được với nhau; không so tuyệt đối với lượt sau khi đổi điều kiện.
- **Máy đo từ 05/11** (`perf/stack/hostcheck.py`, tích hợp `perf.sh`): `up --cpu-layout physical` (mặc định, `0-5` / `6-7`) hoặc `legacy` (`0-4` / `5-7`, cho cặp trước/sau P6-54); `up --be-cpus N` (mặc định 2; > số luồng hệ thống thì từ chối, ≥ số luồng − 1 thì cảnh báo tranh CPU); `run` kiểm trước lượt: chạy pin ⇒ dừng (trừ `--allow-battery`), RAM VM Docker cần > 95 % (trần RAM container perf + container khác đang dùng; bản deploy còn chạy là bị chặn) ⇒ dừng, đĩa Docker < 5 GB ⇒ dừng, chế độ nguồn khác Best performance ⇒ cảnh báo; `--cooldown GIÂY` nghỉ trước k6; xung nhịp CPU Windows mỗi 5 s ⇒ `cpu-clock.json` (trung vị, p10, tỷ lệ mẫu < 80 % trung vị); metadata ghi `cpuTopology`, `beCpus`, `hostCheck`, `cooldownSeconds`. Sau lượt đo phải tắt container perf (lần tắt 2 để perf chạy song song bản deploy từ 00:04 ⇒ Windows còn 2 GB RAM trống; đã tắt).
- **Tiếp:** (a) SP-01 ×3 sau P6-54: `up --cpu-layout legacy --db-prepare 50`, rate 19, 2 CPU (chỉ đổi một yếu tố so với lượt trước); (c) Đăng nhập, thí nghiệm CPU (bố cục physical, **auto-prepare tắt như deploy**, người dùng chốt 05/11): BE 2 ⇒ 3 ⇒ (4) CPU, mỗi mức một lượt, mức đạt chạy ×3; BE 4 CPU chiếm 4 / 6 luồng hệ thống nên PostgreSQL, TADSS, AI tranh CPU, kết quả mức 4 đọc thận trọng; nếu kết luận BE cần ≥ 3 CPU ⇒ ghi vào tài liệu triển khai như yêu cầu cấu hình (không chỉ B.6.3); máy đo: cắm sạc, đóng ứng dụng nặng, nghỉ 120 s giữa các lượt; **người dùng giữ chế độ nguồn Best power efficiency (05/11)** cho mọi lượt ⇒ so sánh giữa các lượt hợp lệ (cùng chế độ, ghi ở `hostCheck` + `cpu-clock.json`), số tuyệt đối (năng lực, số CPU cần) là **cận dưới** so với Best performance; SP-01 sau P6-54 không nạp dashboard (`--code` không dùng) vì dashboard SP-01 là cấu hình deploy (auto-prepare tắt); rồi SP-01 ×3 sau sửa (cùng `--pg-sample activity`) + SP-05.
- **Lần tắt 3 (06/11 23:45 – 07/11 02:03, `perf/results/shutdown3.sh`, log `shutdown3.log`):**
  - **Lượt bị hủy:** lượt r1 đầu tiên (23:31) bị máy vào Modern Standby hai lần giữa lượt (23:31:59–23:32:21, 23:35:57–23:36:36; sự kiện Kernel-Power 506/507), sau đó 145 request timeout trong 10 s lúc 23:41 (checkpoint PostgreSQL `sync=4,1 s` cùng lúc). Đổi tên `20261106-233128-load-SP01-p654-r1-INVALID-sleep`, chạy lại cả lần tắt kèm tiến trình giữ máy thức (`SetThreadExecutionState`, `perf/stack/keepawake.ps1`, không đổi cài đặt nguồn); 6 lượt sau đó không có sự kiện ngủ nào, `throttledShare` ≤ 0,017.
  - **Lỗi công cụ (sửa 07/11):** `analyze.py` đọc `cpus` của compose bằng `float()` nên hỏng từ khi BE dùng `${PERF_BE_CPUS:-2}` (05/11) ⇒ lượt r1 thiếu `resources.json`/`verdict.json` (phân tích lại xong từ dữ liệu thô, Prometheus còn giữ). Sửa: biến compose ⇒ biến môi trường hoặc mặc định; trần CPU BE lấy từ `beCpus` của `metadata.json` (giá trị container thật); 3 test mới trong `test_analyze.py`.
  - **(A) SP-01 ×3 sau P6-54** (legacy, auto-prepare 50, BE 2 CPU; `20261106-234815/…-load-SP01-p654-r1…r3`, gộp `pooled-20261106-234815-load-SP01-p654-r1-3runs.json`): **PASS mọi nhóm cả p95 lẫn p99** — Đọc 14,4 / 90,7 ms, Đặt sân 86,8 / 178,6, **Đăng nhập 401,5 / 559,7 (đạt; BCrypt 12 giữ nguyên)**, IPN 48,5 / 114,6, Ghi 32,6 / 96,1, lỗi 0,000 %; đợt nghẽn **0 / 0 / 0** (trước sửa 04/11: 2 / 3 / 2, gộp FAIL, Đăng nhập 813,5 / 1310,5).
  - **(B) Thí nghiệm CPU** (physical, auto-prepare **tắt**, mỗi mức một lượt, chạy nối tiếp): CPU2 Đăng nhập p95 450,0 ms (đạt), Đọc 16,2, 0 đợt; CPU3 577,8 (trượt), 19,8, 1 đợt; CPU4 645,3 (trượt), 24,0, 3 đợt. BE dùng trung bình **0,62 / 0,72 / 0,80 lõi** (đỉnh phút ≤ 1,01) ⇒ **không bao giờ chạm trần 2 lõi**; xung nhịp trung vị 3491 ⇒ 3298 ⇒ 3104 MHz (máy nóng dần sau khoảng 2 giờ đo liên tục). Kết quả xấu đi theo xung nhịp, không theo trần CPU.
  - **Kết luận (mức theo tiêu chí đã chốt):**
    - **Thí nghiệm thêm CPU KHÔNG đạt mục tiêu (ghi trung thực vào báo cáo):** hướng "tăng CPU để Đăng nhập đạt SLO mà vẫn giữ BCrypt 12" (người dùng đề xuất 05/11) **không được chứng minh**: BE 3 CPU (577,8 ms) và 4 CPU (645,3 ms) đều **trượt** SLO Đăng nhập p95 < 500 ms, kém hơn 2 CPU (450,0 ms). Thêm CPU không cải thiện Đăng nhập trên máy đo vì BE không dùng hết 2 lõi; Đăng nhập p95 do thời gian một lần BCrypt (xung nhịp) + xếp hàng quyết định. Hạn chế: số CPU tăng cùng chiều với nhiệt độ máy (chạy nối tiếp), hai yếu tố lẫn nhau, nên kết quả chỉ cho thấy thêm CPU *không giúp*, không chứng minh thêm CPU *làm chậm*.
    - **Giữ nguyên số CPU cũ (người dùng chốt 07/11):** môi trường perf về **BE 2 CPU** (mặc định `perf.sh up`, như mọi lượt trước 06/11; đã về sau lần tắt 3); bản deploy **không đổi** (BE không giới hạn CPU, `NanoCpus=0`, chưa từng bị sửa trong các lần đo). Không ghi yêu cầu "BE ≥ 3 CPU" vào tài liệu triển khai. Tùy chọn `--be-cpus` giữ lại trong `perf.sh` chỉ để lặp lại thí nghiệm.
    - **Đăng nhập nhạy với nhiệt độ máy:** máy mát 390–450 ms (đạt), máy nóng 578–645 ms (trượt); khoảng dư so với SLO 500 ms mỏng ⇒ báo cáo ghi điều kiện máy (xung nhịp từng lượt ở `cpu-clock.json`).
    - **Vai trò của auto-prepare chưa tách được:** CPU2 (auto-prepare tắt, physical) cũng 0 đợt nghẽn, nên so A với SP-01 ngày 04/11 khác cả ngày đo / tình trạng máy (04/11 chạy sau khoảng 2 giờ E1–E4, chưa ghi xung nhịp) chứ không chỉ một yếu tố ⇒ nguyên nhân khóa lúc lập kế hoạch giữ mức **Probable** (như sau E4), chưa Confirmed. Auto-prepare giảm CPU PostgreSQL rõ (0,13–0,14 so với 0,20–0,26 lõi, cùng xu hướng E4). **Cần lượt đối chứng cùng ngày, máy mát, đầu buổi:** legacy + auto-prepare tắt + BE 2 CPU (cấu hình SP-01 trước sửa); có nghẽn ⇒ auto-prepare có tác dụng; không ⇒ nghẽn 04/11 do tình trạng máy, báo cáo ghi đúng như vậy.
    - Bước giảm khóa ≤ 16 (đề xuất, không phải tiêu chí người dùng) **không làm** (người dùng 06/11: bản deploy giữ code cũ, chỉ làm nếu thật cần; hiện 0 đợt ở cả hai cấu hình).
  - Môi trường perf vẫn giữ index trùng P6-53 (người dùng chốt 06/11) tới khi chạy xong lượt đối chứng, để lượt đó cùng điều kiện DB với SP-01 trước sửa.
- **Kế hoạch các buổi đo còn lại (lập 07/11, chờ người dùng chọn ngày; ước tính thời gian máy, đã gồm khôi phục dữ liệu + warm-up + nghỉ 120 s, mỗi lượt load 15 phút ≈ 22 phút):**
  - **Điều kiện chung mỗi buổi:** hỏi người dùng trước khi tắt bản deploy + runner; cắm sạc; chạy `perf/stack/keepawake.ps1` ở nền; không gập màn hình; kiểm sự kiện ngủ (Kernel-Power 42/506/507) sau mỗi lượt; buổi ≤ khoảng 2 giờ 15 (06/11 xung nhịp tụt sau khoảng 2 giờ); cuối buổi `perf.sh up` ⇒ tắt container perf ⇒ bật lại bản deploy (script mẫu: `perf/results/shutdown3.sh`, gitignore). Từ sau lượt đối chứng mọi kịch bản chạy **cấu hình của bản deploy**: auto-prepare tắt, cpuset physical, BE 2 CPU; sau lượt đối chứng áp P6-53 lên DB perf (`smashcourt` + `smashcourt_seed`).
  - **Buổi 1 (≈ 1 giờ 50):** đối chứng ×3 **đầu buổi khi máy mát** (`up --cpu-layout legacy`, auto-prepare tắt, BE 2 CPU, rate 19, `--pg-sample activity --cooldown 120`; so với 3 lượt A 06/11 theo B.7.8) ⇒ SP-03 Spike (≈ 15 phút) ⇒ SP-02 Stress (≈ 25 phút).
  - **Buổi 2 (≈ 1 giờ 15 máy):** SP-05 Điểm gãy (50 ⇒ 300 %, 11 bậc, ≈ 45 phút) + SP-10 AI (A/B, ≈ 30 phút). Chuẩn bị trước (không cần tắt deploy): Toxiproxy cho SP-09 (≈ nửa ngày).
  - **Buổi 3 (≈ 1 giờ 45):** SP-04 Soak 90 phút.
  - **Buổi 4 (≈ 2 giờ 15):** SP-06 A/B TADSS, 6 lượt ABBA (3 mỗi bên). Chuẩn bị trước: nút bật/tắt cờ TADSS và telemetry trong `perf.sh` cho SP-06/07 (≈ 2–3 giờ).
  - **Buổi 5 (≈ 2 giờ 15 + 1 giờ 30, có thể tách):** SP-07 A/A telemetry 6 lượt (rút còn 2 mỗi bên nếu chỉ cần ước lượng overhead) ⇒ SP-09 + FI-11…14 dưới tải (≈ 4 lượt, SC2 p99 ≤ 700 ms, SC6 = 0).
  - **Tổng:** khoảng 10,5–11 giờ máy + chuẩn bị (≈ 1 ngày) + phân tích và báo cáo 2.29 (≈ 1 ngày).
- **Đợt chạy hết 07/11 (người dùng: "hôm nay chạy hoàn toàn cho xong"):** thêm **SP-01 ×3 chính thức** ở cấu hình bản deploy (physical, auto-prepare tắt, BE 2 CPU, P6-53 đã áp DB perf, `--code SP-01`) vì lượt A 06/11 bật auto-prepare không dùng làm số chính thức; thứ tự: buổi 1 đối chứng ×3 + SP-03 + SP-02 ⇒ buổi 2 SP-01 ×3 + SP-05 ⇒ buổi 3 SP-04 + SP-10 ⇒ buổi 4 SP-06 ⇒ buổi 5 SP-07 ⇒ buổi 6 SP-09; giữa hai buổi bật lại bản deploy khoảng 20 phút cho máy nguội.
- **Kết quả 07/11 — buổi 1 + 1b (đối chứng auto-prepare cùng ngày, legacy, BE 2 CPU, DB perf còn index trùng như 04/11; image BE `sha-d4592c1`, TADSS `sha-a1b2490`):**
  | Lượt (thứ tự chạy) | Auto-prepare | Xung nhịp trung vị | Đợt nghẽn | Đọc p95 / p99 | Đặt sân p95 | Đăng nhập p95 | CPU PostgreSQL |
  | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
  | CTRL-r1 (17:19) | 0 | 2730 MHz | 6 | 207 / 909 ms | 1380 | 1846 | 0,35 lõi |
  | CTRL-r2 | 0 | 3019 | 6 | 123 / 1175 | 713 | 1552 | 0,27 |
  | CTRL-r3 | 0 | 3207 | 2 | 24 / 232 | 139 | 581 | 0,21 |
  | AP50-r1 (19:26) | 50 | 3381 | 3 | 22 / 206 | 124 | 588 | 0,16 |
  | CTRL-r4 | 0 | 3350 | 1 | 23 / 171 | 157 | 552 | 0,21 |
  | AP50-r2 | 50 | 3416 | 0 | 13 / 99 | 86 | 385 | 0,12 |
  | CTRL-r5 | 0 | 3413 | 2 | 19 / 202 | 137 | 523 | 0,20 |
  | AP50-r3 | 50 | 3412 | 0 | 13 / 103 | 91 | 407 | 0,12 |
  - Ghi chú trung thực: 17:23:40–17:25:40 Claude lỡ chạy một lệnh grep đệ quy nặng trên đĩa trùng đầu pha steady của CTRL-r1 (đợt nghẽn đầu tiên 17:24:20 trùng khoảng này; 5 đợt còn lại 17:28–17:35 thì không). Máy vừa khởi động lại: Microsoft PC Manager, `dptf_helper`, trình duyệt chạy nền; xung nhịp tăng dần 2730 ⇒ 3207 MHz trong buổi 1 ⇒ buổi 1b chạy **xen kẽ** AP50 / CTRL để trôi máy chia đều hai bên. Không lượt nào có sự kiện ngủ.
  - **Kết luận theo B.7.8 (cải thiện chỉ khi khoảng min–max không chồng nhau, so các lượt cùng dải xung nhịp 3350–3416 MHz):** auto-prepare **giảm CPU PostgreSQL** (0,12–0,16 so với 0,20–0,21 lõi, không chồng ⇒ **cải thiện**); độ trễ và số đợt nghẽn **không phân biệt được** (AP50 0 / 0 / 3 đợt, Đọc p95 13–22 ms; CTRL 1 / 2 đợt, 19–23 ms: chồng nhau vì AP50-r1), dù AP50-r2, r3 tốt hơn rõ (0 đợt, Đăng nhập đạt). Nguyên nhân khóa lúc lập kế hoạch giữ mức **Probable**. **Yếu tố chi phối rõ nhất là trạng thái máy:** cùng cấu hình auto-prepare tắt, 2730–3019 MHz ⇒ 6 đợt / lượt, khoảng 3400 MHz ⇒ 1–2 đợt; Đăng nhập p95 dao động 385–1846 ms theo xung nhịp ⇒ báo cáo ghi điều kiện máy của từng lượt, kết luận năng lực trên laptop này là cận dưới.
- **Kết quả 07/11 — buổi 1, cấu hình bản deploy (physical, auto-prepare tắt, BE 2 CPU, P6-53 đã áp DB perf):** **SP-03 Spike ĐẠT**: hồi phục **30 s** sau ×5 (ngưỡng ≤ 60 s), 3408 MHz; 2 đợt nghẽn (một lúc ×5 như dự kiến, một đợt 645 ms ở pha danh nghĩa sau hồi phục). **SP-02 Stress** (mô tả, hợp lệ OK, 3140 MHz): 100 % (19 hành trình/s) Đọc p95 22–28 ms; 150 % (29) 36–93 ms; **200 % (38) vi phạm Đọc p95 (600 ms ở cửa sổ thứ hai)**; 250 % (48) Đọc p95 1,9–2,9 s và lỗi 0,96 %, bỏ lượt 0,38 %; về 100 % **tự hồi phục** (Đọc p95 15–77 ms, 0 % lỗi). Điểm suy giảm đầu tiên: Đọc ở khoảng 2 × tải danh nghĩa.
- **Kết quả 07/11 — buổi 2 (cấu hình bản deploy: physical, auto-prepare tắt, BE 2 CPU, P6-53 đã áp DB perf; 21:36–23:36, không sự kiện ngủ, xung nhịp 3307–3500 MHz):**
  - **SP-01 ×3 chính thức** (`20261107-213903/…-load-SP-01-r1…r3`, gộp `pooled-20261107-213903-load-SP-01-r1-3runs.json`, đã nạp dashboard `SP-01`): **mọi nhóm đạt p95**, đợt nghẽn **0 / 0 / 0**, lỗi **0,000 %** (141 327 request). p95 / p99: Đọc 16,2 / 96,4 ms; Đặt sân 109,0 / 277,4; Đăng nhập 420,0 / 565,4 (BCrypt 12); IPN 55,6 / 182,2 (p99 thiếu mẫu, n = 774); Ghi 55,0 / 199,1 (p99 thiếu mẫu, n = 210) ⇒ kết luận gộp **KHÔNG ĐỦ DỮ LIỆU** chỉ vì p99 của IPN và Ghi.
  - **Thiếu mẫu do giờ chạy (phát hiện 07/11):** `journeys.js` chọn giờ đặt và đơn để check-in / check-out theo **giờ thật** (sân mở 06–22 giờ VN): chạy 21:39–22:45 thì hôm nay gần hết giờ ⇒ ít đơn mới, gần như không đơn đang chơi ⇒ Ghi 210 mẫu (06/11 chạy qua nửa đêm: 1 968), Đặt sân 1 868 (06/11: 3 187). Tỷ lệ request theo nhóm phụ thuộc giờ chạy; báo cáo ghi giờ của từng lượt. **Đề xuất:** chạy lại SP-01 ×3 ban ngày ở ngày đo 2 để p99 IPN / Ghi đủ mẫu.
  - **SP-05 Điểm gãy** (`20261107-224719-breakpoint-SP-05`, hợp lệ OK, 3307 MHz): đạt mọi bậc tới 43 hành trình/s (225 %); **gãy ở 48 hành trình/s (250 %)**: Đọc p95 2751 ms, Đặt sân 5001 ms, lỗi 0,44 %. Tại bậc gãy **BE 1,65 / 2 lõi** (khoảng 83 %), PostgreSQL 0,46 lõi ⇒ nút cổ chai là **CPU của BE**, không còn là nghẽn khóa PostgreSQL. So SP-08 03/11 (nghẽn ở khoảng 20 hành trình/s, LockManager): năng lực khoảng **2,4 lần**, chưa quy được cho một yếu tố (khác nhau: cpuset physical, P6-53, image BE `sha-d4592c1`, tình trạng máy). SP-02 Stress (18:46, 3140 MHz) vi phạm sớm hơn ở 38 (bậc tăng đột ngột 50 % một lần, máy chậm hơn): báo cáo ghi cả hai.
  - **Chạy thử công cụ (không phải lượt đo):** SP-10 (`ai`, LLM giả 0 ms, 1 phút): 3 endpoint × 40 request, 0 lỗi, AI server p95 22 ms (n = 120 khớp), toàn chặng p95 21–23 ms; Toxiproxy (`up --faults` + smoke): proxy `tadss` hoạt động, thêm/gỡ toxic qua API được, 10 quyết định TADSS và 6 đơn `deposit_source = TADSS` trong 5 phút ⇒ BE ⇒ Toxiproxy ⇒ TADSS chạy đúng. Ghi chú: Claude thử API bằng toxic `probe` (trễ 1 ms) vài giây đầu lượt smoke rồi gỡ.
- **SP-09 (người dùng chốt 07/11, trước khi đo):** mỗi kiểu lỗi một lượt `load --rate 19 --duration 15m` (`perf.sh up --faults`: BE gọi TADSS qua Toxiproxy `perf-toxiproxy`, chỉ bật bằng profile `faults`; `run --fault KIND`): khoảng 5 phút steady bình thường ⇒ **5 phút lỗi** ⇒ hồi phục (mốc thật ở `faults.json`; `perf/stack/faults.py`). **FI-11** `stop-tadss` (dừng container TADSS), **FI-12** `latency` (Toxiproxy +1500 ms chiều TADSS ⇒ BE, vượt `TimeoutMs` 800 ms), **FI-14** `reset` (Toxiproxy `reset_peer` chiều BE ⇒ TADSS). **Đạt khi đủ cả:** lỗi request trong khoảng lỗi < 0,5 % (5xx, timeout, 4xx ngoài danh sách hợp lệ); từ 60 s sau khi gỡ lỗi Đặt sân p95 < 1000 ms và lỗi < 0,5 %; mọi đơn online tạo trong khoảng lỗi (bỏ 2 s ở hai đầu) cọc 100 % `FULL_PAYMENT_FALLBACK`, không đơn chờ duyệt; TADSS không có hai quyết định PRIMARY cho một đơn; **SC6 = 0 vi phạm** (bộ kiểm C# của BE, `Category=Staging`, chạy ngay sau lượt trên DB perf qua cổng loopback 15432 / TADSS 18081, trước lần khôi phục dữ liệu kế tiếp). Chấm ở `analyze.py fault_verdict` (khi có `faults.json`, thay SLO toàn pha; SLO toàn pha vẫn ghi ở `sloWholeRun`). **Không chạy dưới tải:** FI-13 (tắt cờ khi đang chạy: biến môi trường cần khởi động lại BE) ⇒ dựa BE-IT-19 (integration, `IOptionsMonitor`); SC2 (strategy lỗi p99 ≤ 700 ms) cần FaultInjection của TADSS chỉ có ở môi trường Testing ⇒ dựa lượt evaluator FI-01…10; báo cáo ghi rõ hai ca này không thuộc SP-09.
- **SP-10 (công cụ 07/11):** `perf.sh run ai --rate 2 --duration 10m` (profile `ai` trong `mix.js`: chat / gợi ý đặt sân / phân tích tổng quan chia đều theo thứ tự lượt, qua BE); `perf.sh up --llm-delay 0 --llm-jitter 0` cho **SP-10A** (chấm), mặc định 800 ± 200 ms cho **SP-10B** (chỉ báo cáo). Chấm ở `analyze.py ai_verdict` trên histogram `http_server_request_duration_seconds` của AI (`job="smashcourt-ai"`, route `/api/v1/ai/*`) trong pha steady theo B.7.4 (≤ 250 ms ĐẠT, (250; 500] KHÔNG ĐỦ DỮ LIỆU vì nằm trong bucket, > 500 ms hoặc lỗi 5xx ≥ 0,5 % KHÔNG ĐẠT); ngưỡng ở `slo.json` `ai`.
- **SP-07 (công cụ 07/11):** `perf.sh up --telemetry-off` tắt OpenTelemetry của BE, TADSS, AI bằng một biến `PERF_TELEMETRY`; lượt tắt không có số liệu server/Prometheus của app nên so **phía client (k6)** và CPU container (cAdvisor vẫn chạy).
- **Kết quả 08/11 — ngày đo 2 (cấu hình bản deploy: physical, auto-prepare tắt, BE 2 CPU, P6-53 đã áp DB perf; image BE `sha-d4592c1`, TADSS `sha-43cb89c`, AI `sha-7aad965`; script `perf/results/day2.sh` ⇒ `session2b.sh`, `session3.sh`, `session4.sh`, gitignore; 10:11–15:53, không sự kiện ngủ, mọi lượt hợp lệ OK):**
  - **SP-01 ×3 ban ngày (10:14–11:20, 30–100 phút sau khởi động; `…-load-SP-01-day-r1…r3`, gộp `pooled-20261108-101424-load-SP-01-day-r1-3runs.json`): KHÔNG ĐẠT, chỉ vì Đăng nhập p95** 851 / 603 / 576 ms (ngưỡng 500); Đọc p95 25–39 ms, Đặt sân 155–191, IPN 61–130, Ghi 42–60 đều đạt; đợt nghẽn 2 / 1 / 2; gộp: Đăng nhập p99 4970 ms và lỗi 1,04 %, Đặt sân lỗi 0,505 % (timeout 5 s trong đợt nghẽn 5 s của r1). Ban ngày IPN đủ mẫu (gộp n = 1 084, p99 431 ms ĐẠT), Ghi n = 2 263 (p99 290 ms ĐẠT).
  - **Nguyên nhân (Probable): trạng thái máy, không phải tải ban ngày.** Ban ngày đặt sân online nhiều hơn tối 07/11 khoảng 50 % (920–956 so với 623 mỗi lượt) nhưng 3 lượt SP-06 bên A chiều cùng ngày **cùng cấu hình, cùng tỷ lệ request** (765–896 đơn online) có Đăng nhập 390–428 ms, 0 đợt nghẽn. Khác nhau là xung nhịp: p10 sáng 2886–2952 MHz, chiều 3046–3092; theo cửa sổ 2 phút, cửa sổ nào Đăng nhập > 500 ms thì xung nhịp 2840–3300 MHz và BE tốn 0,7–1,4 lõi cho cùng lượng việc (bình thường 0,6). Đã loại: Defender không quét, Windows Update tải lúc 11:42 (sau SP-01), job `DetectNoShowBookingsAsync` chạy 24–31 s ở lượt sáng nhưng lúc khởi động, trước pha đo, không trùng đợt nghẽn; Edge 5 tiến trình nền buổi sáng, 18 buổi chiều (nhiều hơn mà vẫn đạt). ⇒ Kết luận SP-01 ghi cả hai: tối 07/11 và chiều 08/11 ĐẠT mọi nhóm, sáng 08/11 (máy mới khởi động) Đăng nhập KHÔNG ĐẠT; năng lực Đăng nhập (BCrypt 12) trên laptop này phụ thuộc xung nhịp, là cận dưới; không đổi ngưỡng. Lời khuyên "chờ 30 phút sau khởi động" sửa ở `PERF_ENVIRONMENT.md` mục 6, nay là `RUNBOOK.md` mục 8.2.
  - **SP-04 Soak 90 phút (`20261108-112353-soak-SP-04`, 11:24–12:59, xung nhịp trung vị 3399 MHz): KHÔNG ĐẠT theo luật bộ nhớ**: working set container BE tăng 15,5 % (357 ⇒ 412 MB, ngưỡng 10 %). Mọi nhóm đạt p95 / p99 (Đọc 15,8 / 110 ms, Đặt sân 108 / 236, Đăng nhập 421 / 566, IPN 63 / 113), trôi p95 Đọc 1,15 lần (ngưỡng 1,2), kết nối DB không tăng (−1,8 %), 2 đợt nghẽn. **Heap .NET không tăng:** `process_runtime_dotnet_gc_objects_size_bytes` dao động 63–147 MB theo chu kỳ GC suốt 90 phút, bộ nhớ GC đã cấp 150–206 MB phẳng; chỉ working set tăng dần (309 ⇒ 355 ⇒ 454 MB phút cuối) ⇒ không có dấu hiệu rò bộ nhớ managed; phần tăng nằm ngoài heap managed (bộ nhớ native / GC chưa trả lại hệ điều hành), nguyên nhân **chưa xác định** (Possible). Muốn kết luận cần soak dài hơn (≥ 3 giờ) xem working set có chững không.
  - **SP-10 A (LLM giả 0 ms): ĐẠT**: AI server p95 19 ms, p99 24 ms (n = 1 198, ≤ 250 ms là đạt chắc), 0 lỗi; toàn chặng qua BE p95: chat 16 ms, gợi ý đặt sân 25, phân tích 22. **SP-10 B (800 ± 200 ms, chỉ báo cáo):** AI server p95 975 ms (p99 1614 ms nội suy trong bucket rộng); chat và phân tích toàn chặng p50 816–827, p95 996 ms (BE thêm khoảng 15 ms); gợi ý đặt sân p95 19 ms vì tài khoản khách của k6 chưa có lịch sử đặt sân, AI trả gợi ý mặc định không gọi LLM ⇒ SP-10 B chỉ đo độ trễ LLM ở chat và phân tích.
  - **SP-06 A/B TADSS (thứ tự A B B A A B, 13:35–15:52, xung nhịp trung vị 3408–3447 MHz đều hai bên; gộp `pooled-20261108-133737-load-SP06-A1-3runs.json`, `pooled-20261108-140031-load-SP06-B2-3runs.json`):** mọi nhóm đạt SLO ở cả hai bên (kết luận từng lượt KHÔNG ĐỦ DỮ LIỆU chỉ vì p95 riêng của duyệt / check-out / hủy thiếu mẫu). Theo B.7.8: **Đặt sân p95 A 90,6–94,8 ms, B 63,7–76,2 ms, không chồng ⇒ bước quyết định cọc qua TADSS thêm khoảng 15–30 ms p95** (gộp 92,9 so với 69,9; p99 217 so với 180), rất xa ngưỡng 1000 ms. Đăng nhập (390–428 / 402–409), Đọc (15–17 / 16), IPN không phân biệt được. Ghi p95 A 37–46, B 30–37 (chồng sát) nhưng hai bên khác loại request (chỉ bên A có duyệt cọc) nên không quy cho TADSS.
  - **Khác biệt tải giữa A và B (không phải lỗi):** bên A mỗi lượt có khoảng 480–565 lần đặt online trả 400, bên B khoảng 100. Phần dôi ra là **TADSS từ chối** (`DefaultDecisionStrategy` trả `REJECTED` khoảng 390–460 quyết định mỗi lượt theo `tadss_decisions_total`; BE hủy đơn, nhả sân, trả 400 `BOOKING_REJECTED`); khoảng 100 lần còn lại ở cả hai bên là tranh chấp khung giờ (`SLOT_UNAVAILABLE`). Tỷ lệ từ chối cao vì dữ liệu khách của perf; ghi để biết khi bật cờ `EnableTadssDecision` thì tỷ lệ đơn bị từ chối phụ thuộc strategy và dữ liệu khách. Hệ quả cho SP-06: bên A có ít đơn chờ thanh toán hơn (khoảng 572 + 40 chờ duyệt so với 670), nên A/B đo "luồng có TADSS" so với "luồng không TADSS" chứ không phải cùng luồng chỉ khác một lời gọi.
  - **Còn lại:** ngày đo 3 = buổi 5 (SP-07, `session5.sh`) + buổi 6 (SP-09, `session6.sh`); bắt đầu sau khi máy chạy khoảng 1,5 giờ (hoặc sau một lượt bỏ đi). Sau đó báo cáo 2.29, runbook 1.42, diễn tập GitHub 1.41.
- **Việc còn lại của Epic 6C (rà 08/11 tối, đối chiếu GitHub / GHCR / Vercel):**
  - **Đã xác nhận xong, không còn treo:** cổng 1B (CI ba repo đẩy image; ba package GHCR kéo được không cần đăng nhập, task 1.20); cổng 1D (job `Deploy (máy deploy)` xanh ở lượt CI gần nhất của BE, TADSS, AI); FE: secrets Vercel có (1.21), job `deploy-production` của `master` xanh (1.22); 1C: bộ vận hành chạy trên Grafana Cloud.
  - **A. Ngày đo 3 (cần tắt bản deploy, khoảng 4 giờ máy):** điều kiện như các buổi trước (bật Docker Desktop, máy đã chạy khoảng 1,5 giờ, cắm sạc, tắt Edge và PC Manager, hỏi người dùng trước khi tắt deploy).
    1. `perf/results/session5.sh`: SP-07 A/A telemetry, thứ tự A B B A A B (`--telemetry-off`), khoảng 2 giờ 20 (task 2.22).
    2. `perf/results/session6.sh`: SP-09 FI-11 `stop-tadss`, FI-12 `latency`, FI-14 `reset` dưới tải + SC6 sau mỗi lượt, khoảng 1 giờ 15 (task 2.26). Cần build sẵn `SmashCourt-BE.Tests` (lệnh SC6 dùng `--no-build`).
    3. Một lượt phân tích bật trace (`perf.sh up --trace`, `load --rate 19 --duration 15m`, không dùng làm số chính thức), khoảng 25 phút (task 2.27); script viết trước khi chạy.
    4. Tùy chọn (người dùng quyết): soak dài khoảng 3 giờ để kết luận working set của BE (SP-04 08/11 KHÔNG ĐẠT luật bộ nhớ nhưng heap .NET phẳng).
  - **B. Không cần tắt bản deploy:**
    5. 2.27 phân tích nút cổ chai từ trace + 2.28 tổng hợp trước/sau đã đo (P6-53, P6-54 auto-prepare, thí nghiệm CPU 2/3/4).
    6. 2.29 báo cáo hiệu năng (kết luận theo SLO từng kịch bản, tác động TADSS từ SP-06, hạn chế: laptop phụ thuộc xung nhịp, DB local thay Neon, tỷ lệ hành trình theo giờ thật) + chọn lượt làm Evidence + nạp kết quả lên dashboard Kiểm thử (`python perf/board.py`).
    7. 1.41 diễn tập CD trên GitHub: push bản hỏng có chủ đích cho từng service ⇒ tự quay về; sha / image sai ⇒ từ chối; ghi kết quả (bản deploy khởi động lại vài lần, cần người dùng push hoặc chạy workflow).
    8. 1.42 runbook vận hành (deploy, quay về, `seed-db.sh`, xem DB deploy bằng pgAdmin cổng `127.0.0.1:15432`, xử lý sự cố) + cập nhật CLAUDE.md 4 repo (CLAUDE.md gốc còn ghi BE ở nhánh `feat/phase-6-deposit`, thực tế `develop`; `PHASE6_FE_HANDOFF.md` cũng vậy), README, MASTER plan, CONFIGURATION_REFERENCE, spec Phase 6.
  - **C. Người dùng:** kiểm cảnh báo Grafana Cloud bằng sự cố giả (dừng AI khoảng 3 phút ⇒ có email) nếu chưa làm; kiểm `NEXT_PUBLIC_API_BASE_URL` của Vercel production: nếu trỏ vào bản deploy (BE Phase 6) thì FE hiện tại dính C1 (`paymentUrl` null) ⇒ ưu tiên sửa A11 của 6B; đổi mật khẩu Neon `main`; sau Gate M6 áp script phase6 + P6-53 lên `main`; quyết định bật `EnableTadssDecision` (SP-06: `DefaultDecisionStrategy` từ chối khoảng 40 % đơn với dữ liệu perf).
  - **Tùy chọn, không chặn 6C:** ba cảnh báo khởi động trong log BE (DataProtection-Keys, https port, `_OriginalFormat_` trùng nội dung) vô hại; muốn bảng log sạch thì gắn volume khóa DataProtection, bỏ `UseHttpsRedirection` khi chạy sau proxy, xóa thuộc tính `{OriginalFormat}` ở Alloy.
- **Kết quả 10–11/11 — ngày đo 3 (cấu hình bản deploy: physical, auto-prepare tắt, BE 2 CPU; image BE `sha-d4592c1`, TADSS `sha-4406260`, AI `sha-7aad965`; script `perf/results/day3.sh` ⇒ `session5.sh`, `session6.sh`, `session7.sh`, rồi `session5b.sh` chạy bù; 22:07–02:48, không sự kiện ngủ, mọi lượt hợp lệ OK; máy đã chạy khoảng 2 giờ trước lượt đầu):**
  - **Sự cố khi bắt đầu (21:57), đã xử lý:** (1) ghcr.io không kết nối được (Google, GitHub vẫn vào được) mà `perf.sh up` luôn kéo lại image nên dừng giữa chừng (`set -e`), hai lượt đầu chạy trên môi trường dở dang ⇒ sửa `perf.sh`: `pull --policy missing` (tag `sha-` bất biến, image đã có thì không kéo), test `test_up_pulls_only_missing_images_so_a_registry_outage_does_not_abort_a_session` (đỏ trước, xanh sau; 179 test perf đạt). (2) Dừng chuỗi bằng TaskStop chỉ dừng script cha, buổi con và k6 chạy tiếp ⇒ có lúc hai chuỗi chạy song song; đã giết cả cây bằng SIGKILL (không kích hoạt `trap` bật lại deploy), lượt dở chuyển `perf/results/k6/_aborted/`, thêm khóa `perf/results/.session.lock` vào `session-lib.sh` (buổi thứ hai bị từ chối; khóa mồ côi tự lấy lại; đã thử cả hai ca). Không lượt dở nào được dùng.
  - **Chế độ nguồn:** hostcheck cảnh báo "Best power efficiency" khi cắm sạc; kiểm log thì **mọi ngày đo từ 01/11 đều ở chế độ này** ⇒ giữ nguyên để cùng điều kiện; báo cáo ghi vào hạn chế (có thể góp phần vào dao động xung nhịp).
  - **SP-07 A/A telemetry (A = OpenTelemetry bật như deploy, B = `--telemetry-off`; thứ tự A1 B2 B3 A4 A5 B6, bù B8 A7):** **A1 bị loại**: trình duyệt Brave dùng khoảng 26 % CPU máy trong lúc đo, xung nhịp trung vị 2778 MHz (các lượt khác 3393–3503), 3 đợt nghẽn, Đăng nhập p95 877 ms; người dùng tắt Brave lúc khoảng 22:34 và chọn chạy bù (`session5b.sh`). B2 giữ lại (pha steady 22:39:38–22:54:59 bắt đầu sau 10 request timeout lúc 22:38 khi người dùng thao tác; 3445 MHz, 0 đợt nghẽn). Lượt sạch: A = A4, A5, A7; B = B2, B3, B6, B8 (gộp `pooled-…-SP07-A4-3runs.json`, B gộp từ B3, B6, B8 và B2, B3, B6; bản gộp A1, A4, A5 của `session5.sh` không dùng). Mọi lượt sạch 0 đợt nghẽn, lỗi 0 %.
    | Chỉ số (min–max) | A: telemetry bật (3 lượt) | B: tắt (4 lượt) | B.7.8 |
    | :--- | ---: | ---: | :--- |
    | Đọc p95 / p99 | 15,1–17,1 / 94–133 ms | 14,9–15,7 / 84–110 ms | chồng |
    | Đặt sân p95 | 92–100 ms | 87–101 ms | chồng |
    | **Đăng nhập p95** | **418–478 ms** | **368–398 ms** | **không chồng** |
    | IPN p95 / Ghi p95 | 44–65 / 40–48 ms | 39–80 / 36–124 ms | chồng |
    | CPU BE / TADSS / AI / PostgreSQL (trung vị) | 0,59–0,61 / 0,020–0,022 / 0,093–0,100 / 0,18–0,19 lõi | 0,58–0,61 / 0,019–0,025 / 0,093–0,097 / 0,18–0,21 lõi | chồng |
    | RAM BE | 291–357 MB | 271–343 MB | chồng |
    | Xung nhịp trung vị | 3479–3503 MHz | 3393–3459 MHz | — |
    **Kết luận:** chi phí của OpenTelemetry (metric + trace mẫu + log qua OTLP) **không đo được** ở Đọc, Đặt sân, IPN, Ghi, CPU, RAM của cả ba app (khoảng chồng nhau); **riêng Đăng nhập p95 cao hơn 20–110 ms khi bật** (không chồng, dù xung nhịp bên A còn nhỉnh hơn), vẫn trong ngưỡng 500 ms. Nguyên nhân chưa tách được (Possible: Đăng nhập là request nặng CPU nhất (BCrypt 12), phần ghi span/log đi kèm chen vào đúng lúc CPU BE bận); 3–4 lượt mỗi bên chỉ cho thấy có chênh, không đủ ước lượng chính xác.
  - **SP-09 chịu lỗi TADSS dưới tải (19 hành trình/s, lỗi 5 phút từ phút thứ khoảng 8, SC6 ngay sau lượt trên DB perf): ĐẠT cả ba.**
    | Kiểu lỗi | Lỗi request trong / sau lỗi | Đặt sân p95 từ 60 s sau khi gỡ | Đơn online trong lúc lỗi cọc 100 % `FULL_PAYMENT_FALLBACK` | Quyết định PRIMARY trùng | SC6 |
    | :--- | ---: | ---: | ---: | ---: | :--- |
    | FI-11 dừng TADSS (00:39–00:44) | 0 % / 0 % | 96 ms | 197 / 197 | 0 | ĐẠT (TADSS khỏe lại sau 5,7 s) |
    | FI-12 Toxiproxy +1500 ms (01:02–01:07) | 0 % / 0 % | 98 ms | 197 / 197 | 0 | ĐẠT |
    | FI-14 `reset_peer` (01:25–01:30) | 0 % / 0 % | 139 ms | 196 / 196 | 0 | ĐẠT |
    **Ghi nhận ở FI-12 (không phải lỗi, quyết định thiết kế để người dùng xem):** trong lúc TADSS chậm, Đặt sân p95 của cả lượt **1660 ms** (vượt ngưỡng Đặt sân 1000 ms trong 5 phút lỗi) vì BE chờ TADSS `TimeoutMs` 800 ms rồi **thử lại một lần** khi timeout / lỗi mạng (`TadssClient.SendAsync`, `allowRetry` cho lời gọi quyết định, `IsRetryable`: Timeout, Network, HTTP ≥ 500) ⇒ khoảng 2 × 800 ms trước khi rơi về cọc 100 %. Muốn giữ Đặt sân < 1 s cả khi TADSS chậm: hạ `TimeoutMs` (khoảng 400 ms) hoặc không thử lại khi timeout; đổi lại ít quyết định thật hơn khi TADSS chỉ chậm thoáng qua. Ở FI-14 lỗi tới ngay nên BE rơi về dự phòng gần như tức thì (Đặt sân p95 cả lượt 101 ms).
  - **Lượt phân tích trace 2.27 (`up --trace`, trace 100 %, 01:38–02:01; không dùng làm số chính thức):** 0 đợt nghẽn, Đăng nhập p95 402 ms, Đọc p95 15,6 ms; Tempo của bộ quan sát kiểm thử nhận đủ trace (tìm trong 30 phút chạm giới hạn 1000 trace, gần hết là `smashcourt-be`: `GET api/branches/basic`, `GET api/courts`, time-grid, `POST api/auth/login`…). Phân tích trace làm ở bước 2.27 (không cần tắt deploy, Tempo giữ dữ liệu).
  - **Còn lại của 6C:** 2.27 phân tích trace + 2.28 tổng hợp trước/sau; 2.29 báo cáo hiệu năng (kèm các ghi nhận: SP-01 sáng 08/11, soak working set, FI-12 thử lại, SP-07 Đăng nhập, chế độ nguồn); 1.41 diễn tập CD; 1.42 runbook + tài liệu. Ngày đo đã hết; soak 3 giờ vẫn là tùy chọn của người dùng.
- **Task 2.27 — phân tích nút cổ chai (11/11, không tắt deploy; công cụ mới `perf/stack/bottleneck.py` + 6 test `perf/stack/tests/test_bottleneck.py`, ghi `bottleneck.json` trong thư mục lượt):** hai cách nhìn theo B.5.2. (a) **Trung bình** từ `_sum`/`_count` của pha steady (mọi lượt có telemetry): mỗi request BE = DB (tổng lệnh Npgsql, gồm cả job nên là cận trên) + lời gọi TADSS + phần còn lại; kèm pool kết nối, rate limiter. (b) **Trace** (lượt `up --trace` `20261111-014033-load-trace-2.27`, Tempo qua Grafana "Kiểm thử"): mỗi endpoint 100 trace mẫu + 30 trace đuôi (trên p95 của mẫu); thời gian DB / TADSS / AI của một request là hợp các span con (chồng nhau tính một lần), không trừ percentile.
  | Endpoint (trace, ms) | p50 / p95 | Trung bình: DB + TADSS + BE | Câu SQL | Đuôi: tổng (DB / BE) |
  | :--- | ---: | :--- | ---: | :--- |
  | `POST api/auth/login` | 313 / 374 | 14 + 0 + **309** | 6 | 431 (49 / 382) |
  | `POST api/bookings/online` | 59 / 97 | **37** + 7,5 + 20 | **25** | 155 (100 / 29) |
  | `POST …/cancel` (khách) | 27 / 71 | 15 + 0 + 18 | 9 | 98 (40 / 57) |
  | `GET …/vnpay/ipn` | 26 / 40 | 13 + 0 + 14 | 8 | 62 (36 / 25) |
  | `POST …/approve` | 32 / 52 | 14 + 4 + 9 | 5 | 58 (32 / 16) |
  | Đọc (`time-grid`, `bookings`, `me/bookings`, `schedule`, `courts`…) | 3–11 / 6–18 | DB 60–75 % | 2–5 | 11–40 (DB chiếm 70–90 %) |
  Trung bình mọi request BE: lượt trace 14,2 ms = DB 5,6 + TADSS 0,19 + còn lại 8,4; 6 lượt SP-01 chính thức: 13,7–23,5 ms (DB 5,2–12,5; khoảng 3,3–3,4 câu SQL mỗi request; TADSS 8,4–10,8 ms mỗi lời gọi). Riêng SP-01-day-r1 (lượt có đợt nghẽn 5 s): 73,1 ms (DB 49,0).
  **Nguyên nhân có bằng chứng (mức kết luận):**
  1. **BCrypt hệ số 12 khi đăng nhập là phần nặng nhất của CPU BE (Confirmed về thời gian, Probable về CPU):** 96 % thời gian Đăng nhập là xử lý trong BE (309 / 323 ms, chỉ 14 ms DB); `AuthService` kiểm bằng `BCrypt.Verify` trên hash hệ số 12. Ở tải danh nghĩa 0,90 lần đăng nhập/s × 309 ms ≈ 0,28 lõi = **khoảng 45 % CPU của BE** (giả định phần BE này nằm trên CPU). Khớp ba quan sát độc lập: nút cổ chai ở điểm gãy là CPU BE (SP-05: 1,65 / 2 lõi), Đăng nhập là nhóm nhạy nhất với xung nhịp (SP-01 sáng 08/11, CTRL 07/11) và Đăng nhập p95 cao hơn khi bật telemetry (SP-07). Hướng xử lý (quyết định của người dùng, không làm trong 6C): hạ hệ số xuống 11 (khoảng 2 lần nhanh hơn) hoặc 10 (khoảng 4 lần; mức tối thiểu OWASP khuyến nghị cho bcrypt) với hash mới và nâng dần khi đăng nhập; thêm CPU không giúp trên máy này (06/11).
  2. **Đợt nghẽn hiếm: câu đọc chi tiết booking 12 bảng dồn trên 1 lõi PostgreSQL (Probable):** trong đợt 5 s của SP-01-day-r1 (10:25:40–10:26:10, xung nhịp 2836 MHz) PostgreSQL có tới **54 phiên chạy cùng lúc**, phần lớn `active` không chờ gì (tranh CPU), câu chiếm nhiều nhất là `SELECT t.id, t.approval_expires_at, …` (chi tiết booking, 98 mẫu); CPU PostgreSQL trung bình phút đó 0,62 lõi (trần 1), BE 1,41 lõi; pool BE dùng tới **97 / 100 kết nối, 82 request chờ kết nối** (mọi lượt khác ≤ 16, không chờ) ⇒ máy chậm đi ⇒ request dồn ⇒ quá nhiều câu nặng song song ⇒ timeout 5 s ⇒ tự hồi phục. Chờ khóa (`LockManager`) gần như hết sau P6-53 (3 mẫu ở đợt thứ hai). Hướng xử lý nếu cần: phản hồi sau khi ghi dùng phép chiếu nhẹ hơn câu 12 bảng; giới hạn pool (E3 pool 20 chưa phân biệt được); auto-prepare (P6-54) đã giảm CPU PostgreSQL.
  3. **Đặt sân online chạy 25 câu SQL mỗi request (Confirmed, chưa phải nút cổ chai):** DB chiếm 57 % thời gian, đuôi chậm do DB (100 / 155 ms); có câu đọc lặp (sân, khung giờ đọc hai lần). Còn xa ngưỡng (p95 97–191 ms so với 1000) nên chỉ ghi là ứng viên tối ưu.
  4. **Đuôi DB của request đọc ở tải danh nghĩa nằm phía BE, không phải PostgreSQL (Possible):** lượt trace 82 % thời điểm PostgreSQL không có phiên nào chạy, nhiều nhất 3 phiên, chờ chỉ `ClientRead` (đợi chính BE) và `WalSync`; span DB đo trong tiến trình BE nên kéo dài khi BE bận CPU (BCrypt).
  5. **TADSS:** 8,4–10,8 ms mỗi lời gọi, khoảng 12 % thời gian đặt sân (khớp SP-06: +15–30 ms p95); đường lỗi chậm do thử lại 2 × 800 ms (SP-09 FI-12).
  **Giả thuyết B.7.7 đã loại (có số):** pool kết nối ở tải danh nghĩa (dùng ≤ 16 / 100, 0 chờ, 0 timeout ở mọi lượt trừ đợt nghẽn trên); rate limiter (0 request bị từ chối, chỉ kết quả `acquired`); job Hangfire chạy chồng (job dài nhất `DetectNoShowBookingsAsync` 6–37 s luôn ở pha khởi động sau khi khôi phục dữ liệu, ngoài pha steady ở mọi lượt; trong steady chỉ có `ReportOutcomesAsync` 4 s ở lượt đã loại SP07-A1); tranh chấp slot lock gây chậm (khoảng 100 lần 400 `SLOT_UNAVAILABLE` mỗi lượt là kết quả nghiệp vụ, không làm chậm request).
- **Task 2.28 — tối ưu trước / sau (tổng hợp, không chạy thêm):** mỗi thay đổi đã đo theo B.7.8 (một yếu tố, ≥ 3 lượt hoặc một lượt chẩn đoán có ghi rõ mức kết luận):
  | Thay đổi | Đo | Kết quả |
  | :--- | :--- | :--- |
  | Tắt collector `pg_locks` của Alloy (03/11) | E1 / E2 (04/11) | Không phải nguyên nhân (11 = 11 đợt nghẽn) |
  | Pool 100 ⇒ 20 (thử, không áp) | E3 | Không phân biệt được (15 cửa sổ xấu, trong dải 18–32 của E1 / E2) |
  | Auto-prepare 50 (P6-54, chỉ kiểm thử / đánh giá) | E4; AP50 xen kẽ CTRL 07/11 (3 + 2 lượt) | **Cải thiện CPU PostgreSQL** (0,12–0,16 so với 0,20–0,21 lõi, không chồng); độ trễ, số đợt nghẽn **không phân biệt được** |
  | Bỏ index trùng `idx_bookings_status_date` (P6-53, áp tadss-dev, deploy, perf) | SP-01 ×3 06/11 so với 04/11; trace 11/11 | Hết chờ `LockManager` ở tải danh nghĩa; so trước / sau không tách được khỏi trạng thái máy (khác ngày) ⇒ ghi Probable |
  | BE 2 ⇒ 3 ⇒ 4 CPU (thử, không áp) | 06/11 | **Không cải thiện** (Đăng nhập 450 / 578 / 645 ms, BE chỉ dùng 0,6–0,8 lõi, xung nhịp giảm) ⇒ giữ 2 CPU |
  | Tổng hợp năng lực | SP-08 03/11 ⇒ SP-05 07/11 | Gãy ở khoảng 20 ⇒ 48 hành trình/s (khoảng 2,4 lần), nút cổ chai chuyển từ khóa PostgreSQL sang CPU BE; không quy được cho một yếu tố (đổi cùng lúc cpuset, P6-53, image BE) |
  Chưa thử (để người dùng quyết, xem 2.27): hệ số BCrypt; câu phản hồi nhẹ hơn câu 12 bảng; gộp câu SQL của đặt online; `TimeoutMs` / thử lại khi TADSS chậm. Bản deploy không đổi trong 6C ngoài P6-53.
- **Sự cố CD 11/11 (TADSS `7b6d45e`, cũng `6313e12`):** job "Docker Image" đẩy `tadss-runtime:sha-7b6d45e` lên GHCR thành công nhưng job Deploy thoát 3 "không có image": mạng của máy deploy tới ghcr.io (20.205.243.164) chập chờn từ 10/11 tối (github.com vẫn vào được), `docker manifest inspect` hết thời gian chờ và `cd.sh` gộp lỗi mạng với "không có image". Bản deploy vẫn chạy TADSS `sha-4406260` (hai commit sau chỉ đổi tài liệu và công cụ perf). **Sửa (SmashCourt-BE `deploy/`):** `cd.sh` kéo image trước khi đụng container, 5 lần thử cách 15–60 s, "manifest unknown / not found" ⇒ 3, lỗi khác hết lượt ⇒ **4** (chưa đụng gì); `deploy.sh` dùng `pull --policy missing` khi tag là `sha-` (bất biến; tag nhánh khi chạy tay vẫn luôn kéo). Kiểm: shellcheck sạch; chạy thật ba ca không đụng container (sha sai ⇒ 3; `docker` giả trả "manifest unknown" ⇒ 3; `docker` giả trả lỗi kết nối ⇒ 5 lần thử, 154 s, thoát 4); bảng mã thoát ở `RUNBOOK.md` mục 3 (trước 11/11: `DOCKER_COMMANDS.md`). Các repo lấy bộ deploy từ `SmashCourt-BE` nhánh `develop` lúc chạy job nên bản sửa áp cho cả TADSS và AI sau khi push BE.
- **Kết quả CD sau bản sửa (11/11):** TADSS `0efa68d` xanh toàn bộ, job Deploy triển khai `sha-0efa68d` bằng `cd.sh` mới; BE `fbd9ee6` lần đầu đỏ ở một test, chạy lại job hỏng thì xanh cả Build & Test, Docker Image, Deploy (`sha-fbd9ee6`). Bản deploy: BE `sha-fbd9ee6`, TADSS `sha-0efa68d`, AI `sha-7aad965`, đều healthy. **Test chập chờn (theo dõi, chưa sửa):** `OnlineBookingTadssFailureTests.NetworkError_IsRetriedOnce_ThenFullPayment` (BE-IT, CI 11/11 14:32 UTC): đếm 1 kết nối thay vì 2 (dòng 99), test chạy 68 ms nên không phải do hết `TimeoutMs`; lượt `fbd9ee6` không đổi mã C#, CI lượt trước và lần chạy lại đều xanh, chạy riêng 15 lần trên máy đạt 15/15 ⇒ không tái hiện. Giả thuyết (Possible): vòng nhận kết nối của server giả chạy trên thread pool lệch nhịp với lúc đọc bộ đếm khi máy CI bận. Lặp lại thì: ghi `FailureKind` của từng lần gọi trong test để biết lần hai thất bại vì gì, rồi mới sửa (chờ theo điều kiện thay cho đọc ngay).
- **Người dùng chốt ba đề xuất của 2.27 (11/11):** (1) **giữ nguyên** `TimeoutMs` 800 ms và một lần thử lại khi gọi TADSS: đúng mục tiêu thiết kế (chờ có hạn, thử lại, hỏng thì cọc 100 %), Đặt sân p95 khoảng 1,66 s khi TADSS chậm (FI-12) là đánh đổi đã chấp nhận, báo cáo 2.29 ghi như vậy; (2) **hạ bcrypt xuống 11** ⇒ P6-55 (đã code, BE 0 warning, toàn bộ 2675 test đạt, 1 bỏ qua như trước); (3) câu chi tiết booking 12 bảng (`BookingRepository.GetByIdWithDetailsAsync`) **để làm hướng cải tiến**: 19 chỗ gọi thuộc ba nhóm (đọc chi tiết cho FE, nhiều nhất là `/me/bookings/{id}` 2,6 lần/s; nạp trước khi ghi để ra quyết định nghiệp vụ: duyệt, hủy, check-in, check-out, dịch vụ, hoàn tiền, thanh toán lại; đọc lại sau khi ghi để trả DTO và gửi email / sự kiện: đặt online 1 lần/s, walk-in, sau duyệt, sau thêm / xóa dịch vụ). Chỉ nhóm cuối làm nhẹ được mà vẫn phải trả đủ `BookingDto`, chiếm khoảng 1/4 số lần chạy; chi phí chính là lập kế hoạch câu 12 bảng, đúng thứ auto-prepare (P6-54) xử lý ⇒ hướng cải tiến ghi ở báo cáo: bật auto-prepare cho bản deploy khi thấy đúng triệu chứng (điều kiện ở Mục 10.1), về lâu dài mỗi luồng một câu đọc vừa đủ kèm test theo luồng.
- **Đo SP-01 sau P6-55 (11–12/11, buổi 8 và 8b; BE `sha-8ee8e63`, cấu hình bản deploy, DB mẫu dựng lại với hash hệ số 11):**
  - **Buổi 8 bị loại (ghi trung thực):** trong lúc đo, Claude chạy build / test .NET và xử lý mẫu thô trên chính máy đo ⇒ r1 xung nhịp trung vị 3024 MHz, r2 nghẽn 100 s trùng lúc build, r3 bị preflight chặn (RAM VM 96 %). Cất ở `perf/results/k6/_excluded/`. Quy tắc bổ sung: trong lúc đo không chạy build, test hay xử lý dữ liệu nặng trên máy (kể cả công cụ của trợ lý); chỉ đọc log nhẹ.
  - **Buổi 8b (chạy lại sạch, không seed, sau khi khởi động lại Grafana / Tempo của dashboard Kiểm thử cho đủ RAM):** `p655b-r1` (2857 MHz, 1 đợt nghẽn, hợp lệ, KHÔNG ĐỦ DỮ LIỆU), `p655b-r2` (2775 MHz, 4 đợt nghẽn, hợp lệ, KHÔNG ĐẠT: Đăng nhập p95 698 ms, Đặt sân p99 2,64 s), `p655b-r3` (3407 MHz) **không dùng**: máy bị tắt từ menu Start lúc 00:38:54 (Windows event 1074), trước khi hết pha đo; bước gộp và bật lại bản deploy không chạy (bản deploy tắt tới 12/11).
  - **Kết luận (báo cáo hiệu năng Mục 10.2):** Đăng nhập p50 313–318 ⇒ 172–173 ms (khoảng −45 %, không chồng, dù xung nhịp sau thấp hơn khoảng 20 %) ⇒ **cải thiện** phần kiểm mật khẩu. p95 / p99 **chưa kết luận** (2 lượt hợp lệ, cả hai xung nhịp thấp). Đối chiếu mọi lượt từ 06/11: xung nhịp trung vị ≥ khoảng 3400 MHz hầu như 0 đợt nghẽn, ≤ khoảng 3100 MHz luôn 1–6 đợt ⇒ ghi vào hạn chế. **Còn:** một buổi SP-01 ×3 khi máy ổn định (`perf/results/session8b.sh`, đổi tag), rồi gộp, `bottleneck.py`, gói bằng chứng.
- **Dọn tài liệu (11–12/11):** bốn hướng dẫn cũ (`DOCKER_COMMANDS.md`, `EVALUATION_RUN_GUIDE.md`, `PERF_ENVIRONMENT.md`, `TEST_DASHBOARD.md`) gộp vào [`docs/guides/RUNBOOK.md`](../docs/guides/RUNBOOK.md) (phần runbook của task 1.42 xong); `PHASE5_SUMMARY.md` thành [`docs/PROJECT_SUMMARY.md`](../docs/PROJECT_SUMMARY.md) có Mục 0 Phase 6; spec, plan, báo cáo đã xong chuyển vào `spec/archive/phase5/`, `spec/archive/phase6/`; kết quả thô `perf/results/` sắp lại (mục lục `perf/results/INDEX.md`, lượt loại / bị thay thế / dừng giữa chừng ở `_excluded/`, `_superseded/`, `_aborted/`). Báo cáo 2.29 là [`docs/report/PERFORMANCE_REPORT.md`](../docs/report/PERFORMANCE_REPORT.md); gói bằng chứng ở `perf/evidence/PERF-6C-20261111/` (bộ thu `perf/stack/perf_evidence.py`, không đặt ở `docs/research/evidence-packs/` vì DOC-1 bắt mọi file dưới `docs/` có dòng kiểm kê). Đề cương `.md` có Mục 10 "Thay đổi so với đề cương đã nộp" (bản `.docx` 15/10 giữ làm bản lịch sử, người dùng chốt 12/11); 215 booking gọi đúng là dữ liệu phát sinh khi phát triển và demo Đồ án 1.
