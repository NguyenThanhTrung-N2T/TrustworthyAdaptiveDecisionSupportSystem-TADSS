# PHASE 6 — EPIC 6C: DEVOPS, QUAN SÁT VÀ KIỂM THỬ (BE + TADSS + AI) — PLAN CHỐT

| | |
| :--- | :--- |
| **Trạng thái** | **Bản 5 (31/10/2026) — bản chốt để duyệt và bắt đầu làm.** Bản 1 thiết kế; bản 2 sửa theo phản biện (Mục 12); bản 3 gộp chi phí, danh mục test, dashboard; bản 4 rút gọn và chia task nhỏ (Mục 7); bản 5 bổ sung **SmashCourt-AI** (triển khai 4 repo); bản 5.1 ghi câu trả lời Q1–Q7 của người dùng (Mục 11): cảnh báo qua Gmail, hai môi trường `stable`/`preview`, SLO tách theo hệ. Thiết kế chi tiết để tra khi làm: Phụ lục B. Chưa code gì |
| **Phạm vi** | **4 repo**: `SmashCourt-BE`, `TADSS-Runtime`, `SmashCourt-AI` (đóng gói image, CI/CD, quan sát, kiểm thử) và `smashcourt-fe` (chỉ workflow triển khai Vercel, không đụng mã giao diện) |
| **Thứ tự** | Giai đoạn 0 chuẩn bị ⇒ **giai đoạn 1 DevOps** ⇒ giai đoạn 2 kiểm thử |
| **Lịch** | Song song Epic 6B (FE), W09–W12 (02/11 – 29/11/2026) |
| **Mã quyết định** | P6-32…P6-50, nối tiếp [PHASE6_DESIGN_SPEC.md](PHASE6_DESIGN_SPEC.md); chép vào spec Phase 6 và methodology ở task 1.42 |

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
| P6-35 | CD qua GitHub runner tự host trên máy nhóm, **không có repo private riêng** (người dùng chốt 31/10: chỉ sửa CI/CD và GitHub Actions trong các repo hiện có). Mỗi repo (BE, TADSS, AI) có `ci.yml` (build, test, push image) và `deploy.yml` (kéo image theo sha về máy, health, smoke, quay về); máy cài runner cho cả ba repo (tài khoản cá nhân: mỗi runner gắn một repo). Chặn rủi ro runner trên repo public bằng ba lớp: (1) Settings › Actions bật **Require approval for all outside collaborators** ở cả ba repo; (2) `deploy.yml` chỉ chạy khi push `master`/`develop` hoặc chạy tay, không bao giờ theo pull request; (3) Environment `stable` (chỉ nhánh `master`) và `preview` (chỉ `develop`) giữ secrets, nhánh khác không lấy được. Hai môi trường: `stable` ⇐ `master` của BE, TADSS, AI (ngrok cho BE, nhận VNPay, demo) và `preview` ⇐ `develop` của BE, TADSS (dùng chung AI của `stable`; không URL công khai; thử thanh toán bằng IPN giả lập); chung một container PostgreSQL nhưng khác database; khi bỏ `develop` của TADSS thì gỡ TADSS khỏi `preview`. Phần dùng chung (PostgreSQL, Redis, bộ quan sát vận hành) đặt ở `TADSS-Runtime/deploy/`. Chỉ khởi động lại service vừa đổi; health 2 mức + sha + smoke chỉ đọc; quay về có kiểm lại; `UNSAFE` + cảnh báo | Đã chốt |
| P6-36 | FE: `lint` ⇒ `tsc` ⇒ `build` ⇒ Vercel; **chỉ `master` triển khai** (production); `develop` chỉ chạy CI (người dùng chốt 31/10) | Đã chốt |
| P6-37 | Hai bộ quan sát tách biệt: vận hành (`TADSS-Runtime/deploy/`) và kiểm thử (`TADSS-Runtime/perf/`) | Đã chốt |
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
| P6-48 | Chi phí 0 đồng: hai môi trường triển khai (`stable`, `preview`) dùng PostgreSQL + Redis cục bộ (Neon gói miễn phí không chịu được chạy liên tục); **ngrok chỉ cho hệ thống triển khai; kiểm thử chỉ chạy local**; Neon giữ cho dev, E2E, demo | Đã chốt |
| P6-49 | Dashboard Grafana 13.x, mỗi bộ **3 tab BE / TADSS / AI** (bản 5: thêm AI); dashboard vận hành có biến lọc môi trường (`stable`, `preview`); cảnh báo gửi qua **Gmail** (SMTP Gmail, mật khẩu ứng dụng lưu ở secrets); mọi panel có mô tả 6 ý, có test CI chặn panel thiếu mô tả; dự phòng hai dashboard liên kết nếu tab chưa ổn | Đã chốt |
| P6-50 | Danh mục test: **một nguồn chi tiết** là `tests/catalog.yaml` (máy đọc, khớp methodology); plan và dashboard chỉ hiện tổng hợp theo nhóm; chi tiết chỉ mở khi có lỗi (Mục 5); chỉ thêm không bỏ, trùng thì gộp (LT-04 ⇒ SP-01) | Đã chốt |
| **P6-51** | **SmashCourt-AI và ba tài khoản ngrok** (cập nhật 31/10 theo người dùng): mỗi service có tài khoản ngrok riêng — BE, TADSS (token mới, người dùng tự đặt), AI (giữ `ngrok-ai`); BE gọi TADSS và AI bằng URL đặt trong `.env` của BE (`TADSS_BASE_URL` + `TADSS_API_KEY`; `AIService:BaseUrl`), đổi URL không phải sửa code. AI vẫn chưa có xác thực giữa BE và AI nên URL ngrok của AI cho phép người ngoài dùng quota Gemini ⇒ **SEC-AI-2 (khóa xác thực BE ⇒ AI) đưa lên làm sớm**; smoke của AI chỉ gọi `/health`; bài đo dùng LLM giả qua `GEMINI_BASE_URL`; model đổi chỉ trong `.env` (đã đổi sang `gemini-3.6-flash` 31/10, kiểm model tồn tại) | Đã chốt |
| **P6-52** | **SLO tách theo hệ** (người dùng chốt 31/10): bảng SLO ở task 2.18 là của **BE** (trải nghiệm người dùng gọi qua BE); **TADSS giữ SLO riêng đã có**: SC1 (quyết định bình thường p95 < 150 ms), SC2 (khi strategy lỗi p99 ≤ 700 ms, ngân sách pipeline 500 ms); **AI** đề xuất p95 < 300 ms cho phần xử lý của AI (không gồm độ trễ LLM giả), lỗi < 0,5 %, chốt ở 2.18. Tab nào chấm theo SLO của hệ đó | Đã chốt (AI chờ 2.18) |

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
| 2.18 | Chốt SLO trước khi đo, **theo từng hệ** (P6-52): BE (Phụ lục B.7.2), TADSS (SC1, SC2 đã có), AI (đề xuất p95 < 300 ms phần xử lý AI, lỗi < 0,5 %) + tải danh nghĩa | N | — | 0,5 | Ghi vào plan, không đổi sau khi có kết quả |
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
- **Tệp (gọn lại 01/11 theo yêu cầu người dùng):** mọi thứ ở `SmashCourt-BE/deploy/`: `docker-compose.yml` (postgres, redis, be, ngrok, tadss, ai trong một project `smashcourt-deploy`; mỗi service chỉ nhận khóa nó cần), `deploy.sh <be|tadss|ai|all> [tag]`, `seed-db.sh`, `init-db.sql`, `.env.example`. TADSS, AI không có thư mục deploy riêng. Bí mật: một file `~/.smashcourt-deploy/.env` (sinh từ cấu hình dev; khóa JWT/OTP và mật khẩu PostgreSQL mới, ngẫu nhiên). TADSS chạy Production, `ApplyMigrationsOnStartup=true` chỉ cho database trên máy. Lệnh: [`docs/guides/DOCKER_COMMANDS.md`](../docs/guides/DOCKER_COMMANDS.md) mục 0.
- **`seed-db.sh`:** chép schema `public` (không chép `hangfire`), phiên chỉ đọc trên Neon; chặn ba lớp: endpoint phải đúng `SEED_SOURCE_ENDPOINT`, nguồn phải có `bookings.tadss_decision_id` (Phase 6), gõ `smashcourt` để xác nhận. Đã thử: endpoint sai ⇒ từ chối; trỏ vào DB gốc ⇒ từ chối "chưa áp Phase 6"; trả lời sai ⇒ không làm gì. Nạp thật: 31 bảng, 2244 dòng, đếm từng bảng khớp nguồn. Sự cố khi viết: `docker run -i` ở các bước trước nuốt mất câu trả lời xác nhận ⇒ chỉ bước nạp đọc stdin; `source .env` hỏng vì giá trị như `Tên <email>` ⇒ script chỉ đọc đúng các khóa cần dùng như văn bản.
- **Lỗi thật tìm ra khi chạy Production:** BE sập lúc khởi động vì `RecurringJob.AddOrUpdate` (API tĩnh) cần `JobStorage.Current`, chỉ được đặt khi dashboard Hangfire bật (cấu hình Development). Sửa: `IRecurringJobManager` từ DI; test `UseHangfireServices_WhenDashboardOffAndStorageNotResolvedYet_RegistersAllRecurringJobs` đỏ trước, xanh sau; toàn bộ BE 2609 đạt, build 0 warning.
- **Kiểm:** BE, TADSS, AI healthy; BE gọi được TADSS và AI; URL ngrok công khai trả Healthy; CORS cho `smashcourt-ai.vercel.app`; 6 container ~430 MB RAM. Tạm thời BE chạy image build trên máy có bản sửa Hangfire, tới khi CI đẩy `develop` mới.

### Giai đoạn 1D bước 2–3 — Runner và CD (task 1.35–1.40; 01/11/2026) ✅ (chờ lượt push đầu)

- **Thiết kế gọn (thay `deploy.yml` + `workflow_run` của plan):** job `deploy` thêm vào cuối `ci.yml` của BE, TADSS, AI: `if: push`, `needs: image`, `runs-on: [self-hosted, smashcourt-deploy]`, `concurrency` theo service, lấy bộ deploy từ SmashCourt-BE nhánh develop (repo public, không cần token) rồi chạy `deploy/cd.sh <service> <sha>`. Không cần token giữa các repo, không phụ thuộc nhánh mặc định như `workflow_run`. Nhãn runner khai ở `.github/actionlint.yaml`.
- **`deploy/cd.sh`:** từ chối (thoát 3) khi sha sai dạng hoặc image `sha-<7>` không có trên GHCR; triển khai ⇒ kiểm nhãn revision = sha, health, smoke chỉ đọc (BE `GET /api/cancel-policies`, TADSS `/health/ready`, AI `/health`) ⇒ hỏng thì quay về bản đang chạy trước (theo nhãn revision của container cũ, tag `sha-<7>` bất biến) và kiểm lại (thoát 1), quay về cũng hỏng ⇒ UNSAFE (thoát 2).
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
- **Còn lại:** người dùng tạo tài khoản Grafana Cloud và điền 6 khóa (hướng dẫn: [`DOCKER_COMMANDS.md`](../docs/guides/DOCKER_COMMANDS.md) mục "Quan sát"), `deploy.sh obs`, `grafana_cloud.py push`; kiểm cảnh báo bằng sự cố giả (dừng AI 3 phút ⇒ email); trace BE ⇒ TADSS (OB-BE-2) chỉ xem được khi bật cờ `EnableTadssDecision` (cùng cơ chế `traceparent` đã kiểm với AI).

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

#### B.7.2. SLO (đề xuất, cần duyệt)

Phân biệt bốn thứ: **SLO mục tiêu** (bảng dưới, chốt và ghi lại **trước** khi có kết quả); **baseline thực nghiệm** (mức hệ thống đạt được hiện tại, đo bằng một lượt tăng tải ban đầu, gồm kiểm mức 50 request/giây có khả thi trên máy này không); **kết quả đo** (từng lượt); **kết luận** (đạt hay không, bằng chứng mạnh tới đâu). Nếu máy không đạt được mức danh nghĩa thì báo cáo đó là giới hạn hạ tầng và điều chỉnh phạm vi có giải thích; **không hạ ngưỡng sau khi đã thấy kết quả**.

| Nhóm | p95 | p99 | Ghi chú |
| :--- | ---: | ---: | :--- |
| Đọc (J1, J5, J6 danh sách) | < 300 ms | < 800 ms | |
| Đặt sân (J3, gồm gọi TADSS) | < 1000 ms | < 2000 ms | p95 trùng ngưỡng SC1 của E2E đã có |
| Đăng nhập (J2) | < 500 ms | < 1000 ms | BCrypt cố ý chậm, tốn CPU |
| IPN (J4) | < 500 ms | < 1000 ms | |
| TADSS phía server trong hệ thống | < 150 ms | — | Ngưỡng SC1 của TADSS |
| Lỗi (5xx + timeout) | < 0,5 % | | 4xx nghiệp vụ dự kiến (409 trùng sân, 400 hết hạn) đếm riêng, không tính lỗi |
| Throughput danh nghĩa | ≥ 50 request/giây hỗn hợp | | Đề xuất: bằng mức tải danh nghĩa LT-01 của TADSS; cần duyệt |

#### B.7.3. Ramp-up
Tăng theo bậc (vd 25 % ⇒ 50 % ⇒ 75 % ⇒ 100 % mức danh nghĩa, mỗi bậc giữ 2 phút); **warm-up 2 phút** (JIT, pool kết nối, cache TADSS) gắn tag `phase=warmup` và loại khỏi thống kê và threshold; chỉ tính `phase=steady`.

#### B.7.4. Kịch bản
| Kịch bản | Mô tả | Trả lời câu hỏi |
| :--- | :--- | :--- |
| Load | Mức danh nghĩa 15 phút (open model) | Đạt SLO ở tải bình thường không |
| Stress | Tăng bậc vượt danh nghĩa tới khi vượt SLO | Hệ thống xuống cấp thế nào, lỗi gì xuất hiện trước |
| Spike | Từ danh nghĩa nhảy ×5 trong 1 phút rồi về | Có sập không, mất bao lâu để hồi phục |
| Soak | Danh nghĩa 90–120 phút | Rò rỉ bộ nhớ/kết nối, tác động job Hangfire theo thời gian |
| A/B TADSS | Load với cờ bật và cờ tắt (P6-43) | TADSS cộng bao nhiêu vào p95/p99, tốn bao nhiêu tài nguyên |
| A/A overhead | Load với telemetry bật và tắt | Phép đo có làm méo kết quả không (P6-40) |

**Quy trình mỗi lượt (P6-46, P6-47):** khôi phục DB + xóa Redis ⇒ kiểm checksum ⇒ warm-up riêng ⇒ lượt đo ⇒ ghi kết quả + metadata ⇒ đánh giá hợp lệ. Thứ tự ưu tiên chạy: load trước, rồi stress, spike, soak; tối ưu và chạy lại để làm bằng chứng sau cùng.

#### B.7.5. Điểm gãy
`ramping-arrival-rate` tăng đều; điểm gãy = mức tải thấp nhất mà p95 vượt SLO **liên tục 1 phút** hoặc lỗi vượt ngưỡng. Ghi trạng thái tại điểm gãy: CPU/RAM từng container, pool kết nối và thời gian chờ pool, thread pool, GC, khóa DB, tỷ lệ đường lùi TADSS.

#### B.7.6. Phân tích p95/p99
So **client** (k6, toàn bộ mẫu, chính xác) với **server** (histogram BE, xấp xỉ theo bucket): chênh lệch = mạng + xếp hàng ở Kestrel + thời gian k6. Phân vị từ histogram là xấp xỉ: ghi biên bucket và sai số; ưu tiên histogram mũ (exponential) nếu đường Collector ⇒ Prometheus giữ được. Giải thích theo trải nghiệm: p95 là 1/20 lượt chậm nhất người dùng gặp thường xuyên; p99 quyết định cảm giác "thỉnh thoảng treo".

Báo cáo chính thức tối thiểu có, **theo từng nhóm hành trình/endpoint** (không gộp mọi endpoint thành một số): p50/p95/p99 phía client (k6); p50/p95/p99 phía server khi histogram cho phép, kèm biên bucket; tỷ lệ lỗi; throughput; tài nguyên cùng khoảng thời gian; **số mẫu** và thời gian đo; trạng thái hợp lệ của lượt (P6-47). Hai phía là hai góc nhìn trên hai tập mẫu khác nhau (client gồm mạng và xếp hàng), không so như cùng tập.

#### B.7.7. Kết quả và nút cổ chai
Mỗi lượt sinh `perf/results/<run_id>/` (gitignore; lượt chọn làm bằng chứng chép sang `docs/research/evidence-packs/` như Evidence Pack): summary JSON của k6, cấu hình đã dùng (không bí mật), digest image BE/TADSS, sha 3 repo, strategy version, trạng thái cờ, seed, thông số máy, JSON kết quả các truy vấn Prometheus chính, `manifest.json` (SHA-256). Script dựng báo cáo so sánh nhiều lượt. Giả thuyết nút cổ chai cần kiểm: pool kết nối DB, tranh chấp khóa sân (slot lock) khi nhiều người đặt cùng giờ đẹp, BCrypt khi đăng nhập dồn, độ trễ/timeout TADSS gây đường lùi, job Hangfire chạy chồng, rate limiter.

#### B.7.8. Tối ưu và so sánh trước/sau
Mỗi cải tiến chỉ đổi **một** yếu tố (chỉ số DB, kích thước pool, truy vấn N+1, cache…), image mới theo sha, chạy lại đúng kịch bản và seed, ≥ 3 lượt mỗi bên, so trung vị và khoảng (P6-44). Thay đổi code là việc của BE/TADSS theo quy trình thường (test, build, review); tài liệu ghi lý do và kết quả.

#### B.7.9. Kết luận và hạn chế
Mức đáp ứng từng SLO; hạn chế đã biết: một máy chạy cả tải lẫn hệ thống, laptop hạ xung, PostgreSQL cục bộ khác Neon (không có độ trễ mạng tới Neon), tỷ lệ hành trình là giả định, không có người dùng thật.

#### B.7.10. Kiểm (6C-5)
Mỗi kịch bản chạy được bằng một lệnh có tham số (kịch bản, cờ, seed, tag image); threshold k6 phản ánh đúng SLO; lượt dừng giữa chừng không sinh kết quả "đạt"; báo cáo dựng lại được từ thư mục kết quả.

---

### Giai đoạn 2B — Môi trường `perf` (task 2.10, 2.11, 2.12, 2.13, 2.14, 2.15, 2.30; 01/11/2026) — phần chính xong; còn exporter + Tempo, spike tài nguyên (2.16), file thống kê thật (2.17)

- **Một lệnh** (người dùng chốt 01/11: gói gọn, chạy đơn giản): `bash perf/perf.sh up | seed | run <profile> | reset | status | down` ([`PERF_ENVIRONMENT.md`](../docs/guides/PERF_ENVIRONMENT.md)). `up` lấy tag image **đang chạy ở bản deploy** (đo đúng bản thật), bật cả bộ quan sát kiểm thử; `run` tự khôi phục dữ liệu, ghi `metadata.json` (image, commit, cờ TADSS, seed + checksum, CPU/RAM máy) và `--code SP-xx` nạp lên dashboard.
- **2.10, 2.11 Compose** `perf/stack/docker-compose.yml` (project `smashcourt-perf`, container `perf-*`, mạng `smashcourt-test`): BE/TADSS/AI theo tag, PostgreSQL 17 nạp `docs/database/schema_smashcourt.sql` (bỏ schema `hangfire` của bản dump vì thiếu dòng phiên bản ⇒ Hangfire cài lại sẽ lỗi `column updatecount already exists`), PostgreSQL 16 + Redis cho TADSS, Mailpit, LLM giả, k6 (profile). Hằng không bí mật (JWT, OTP, VNPay giả, khóa TADSS `tadss-perf-key`), rate limit nâng, Hangfire bật, telemetry 5 giây, trace tắt, trần CPU/RAM theo B.6.3. `perf/stack/tests/test_compose.py` chặn host ngoài stack, biến bí mật, `env_file`, thiếu trần tài nguyên.
- **2.30 LLM giả** `perf/stack/fake_llm.py` (thư viện chuẩn, chạy thẳng trên `python:3.12.12-alpine`): khung `generateContent`, JSON có các khóa router AI đọc, `usageMetadata`, trễ 800 ± 200 ms. Đã kiểm: BE ⇒ AI ⇒ LLM giả trả lời trong khoảng 1,3 s, metric Gemini của AI có số liệu.
- **2.12–2.14 Dữ liệu** `perf/stack/datagen.py` + `distribution.placeholder.json` (nhãn PLACEHOLDER): 2.000 khách ⇒ 43.144 dòng, 7.335 đơn (lịch sử hoàn tất / không đến / hết hạn thanh toán + đơn có sẵn 14 ngày tới, 30 % khung giờ), 1,4 s; nạp qua mọi ràng buộc schema thật (gồm chống trùng sân). `seed` dựng database mẫu `smashcourt_seed` + `tadss_seed`; `reset` 18–31 s, checksum nội dung khớp; đã kiểm một đơn đặt trong lượt và một quyết định TADSS biến mất sau reset.
- **2.15 k6** `perf/k6/mix.js` + `lib/` : 7 hành trình theo tỷ lệ Q4, think time có seed, tag `name`/`slo`/`journey`/`phase`, IPN tự ký, 4xx nghiệp vụ đếm riêng; profile smoke, load, stress, spike, soak, breakpoint, closed. **Lượt smoke** (6 VU, đủ J1…J7): 0 % lỗi, 11/11 đơn đặt được, 10 IPN hợp lệ (`payment_ipn_logs.is_valid`) ⇒ 10 đơn `PAID_ONLINE`, 1 khách hủy; nạp làm SP-08 dạng hạn chế.
- **Quan sát đầu tiên (chưa phải lượt đo):** ngay sau reset BE nguội: p95 chung 5,5 s (đăng nhập 7,5 s, đặt sân 6,3 s, IPN 2,4 s, phía server khớp phía client); chạy nóng p95 1,1 s. Đăng nhập (BCrypt cost 12) và đặt sân là ứng viên nút cổ chai cho SP-08 (2.19); warm-up của các profile loại phần nguội.
- **Bổ sung 02/11 — quan sát:** Tempo `grafana/tempo:2.10.10` (Tempo 3.x chưa có trên Docker Hub; nhánh 2.10 vẫn phát hành) trong bộ quan sát, Alloy chuyển trace OTLP sang; exporter PostgreSQL (hai DB, kết nối vào database `postgres` vì `seed`/`reset` cần database nguồn không còn phiên) và Redis chạy **trong Alloy** (không thêm container); hàng "Hiệu năng" thêm 3 panel (kết nối theo trạng thái, giao dịch/giây, khóa BE / Redis TADSS). Đã kiểm một trace đặt sân: 35 span BE ⇒ TADSS ⇒ PostgreSQL, tổng 420 ms, TADSS 23 ms. `check` của dashboard sửa lỗi coi `pg_stat_activity_count` là đuôi `_count` của histogram (test thêm).
- **2.17 không cần người dùng xuất lại:** `data/private/smashcourt_bookings.csv` (gói 5.6, 215 booking, không tên/điện thoại/email) đủ trường ⇒ `perf/stack/reference_stats.py` rút `distribution.reference.json` (chỉ phân bố tổng hợp, commit được; test chặn định danh lọt ra) và `seed` dùng mặc định. Phân bố thật khác placeholder: 61 % đặt trong ngày, cao điểm 6–7 giờ và 18 giờ, 13 % đơn 2 sân, 25 % lượt đặt của khách hết hạn thanh toán ≥ 2 lần ⇒ TADSS 2.1.0 từ chối một phần (`BOOKING_REJECTED`, đúng quy tắc `REPEATED_PAYMENT_EXPIRY`) và có đơn `PENDING_APPROVAL` cho J7. Seed: 20.404 đơn, 101.364 dòng. Hạn chế: dữ liệu môi trường phát triển (nhiều đơn thử nghiệm hủy / không đến).
- **2.16 thử tài nguyên (máy 8 CPU / 8 GB, bản deploy + runner vẫn chạy):** (1) không tải: perf 0,37 lõi, 540 MB; tất cả container SmashCourt 0,64 lõi, 2,5 GB. (2–3) tăng đều 2 ⇒ 40 hành trình/giây: k6 tối đa 0,3 lõi (không bị giới hạn); **nút thắt là CPU của BE** (trần 2 lõi): p95 ≤ 0,41 s tới khoảng 68 request/giây, 76 request/giây p95 vọt 3,6 s, khoảng 90 request/giây BE chạm 2 lõi và p95 > 10 s; PostgreSQL 0,8/1 lõi, TADSS ≤ 0,1, AI ≤ 0,3, RAM BE ≤ 590 MB; lỗi ≤ 0,05 %. Mức danh nghĩa đề xuất 50 request/giây có dư địa. **Lượt thử đầu phát hiện lỗi của mô hình tải:** VU mới của open model tự đăng nhập ⇒ khi hệ thống chậm, số VU tăng tới 800 và BCrypt dồn thành bão đăng nhập (đăng nhập 5 % ⇒ 14 % request) làm BE sụp sớm ở 40 request/giây; sửa: `setup()` đăng nhập sẵn 300 khách + nhân viên + quản lý, token perf sống 240 phút, đăng nhập chỉ đo ở J2 (lượt sau: 1,4–1,8 %). **Lượt thứ hai bị máy đo đứng 3 phút** (mọi nguồn mất số liệu, Pushgateway 80/100 mẫu) ⇒ `perf/stack/analyze.py` (tự chạy sau mỗi lượt, ghi `resources.json`) đánh dấu `INFRA_LIMITED`; con số trên chỉ là chỉ báo. (5) Bảng B.6.3 giữ nguyên (trần, không phải phần giữ riêng; BE 2 lõi là trần ràng buộc cần đo). (4) 02/11, người dùng cho phép: dừng tạm 10 container của bản deploy + 3 runner (`docker stop`, giữ volume), chạy lại đúng lượt tăng tải, bật lại (health, URL ngrok 200, runner nhận việc): **không còn phút mất số liệu**; ở cùng mức tải CPU BE thấp hơn khoảng 30–45 % (72,7 request/giây: 0,77 lõi so với 1,13) và p95 ≤ 0,10 s tới 84 request/giây, 0,47 s ở 101 request/giây, 0 % lỗi; chưa chạm điểm gãy ở 40 hành trình/giây ⇒ SP-05 (2C) cần `--max-rate` cao hơn. Phút cuối p95 2,6 s khi CPU BE chỉ 1,1 lõi ⇒ có nút thắt ngoài CPU, phân tích bằng trace ở 2.27. **Kết luận 2.16:** lượt chính thức bắt buộc tắt bản deploy + runner (tranh CPU làm sai số liệu và gây VM đứng); bảng B.6.3 giữ nguyên.
- **2B xong.** Sang 2C: chốt SLO (2.18, người dùng).
