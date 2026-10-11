# KẾ HOẠCH THỰC THI ĐỒ ÁN 2 (MASTER EXECUTION PLAN) — v2.1

## NỀN TẢNG RUNTIME ENGINE RA QUYẾT ĐỊNH CHỊU LỖI (TADSS)

| | |
|---|---|
| **Dự án** | TADSS (Trustworthy Adaptive Decision Support System) |
| **Hệ thống đối chứng** | SmashCourt — hệ thống đặt sân cầu lông trực tuyến (Đồ án 1) |
| **Thời lượng** | 16 tuần: **W01 (07/09/2026) → W16 (26/12/2026)** |
| **Nhân sự** | **SV1** — Lead Backend: TADSS-Runtime, SmashCourt-BE, hạ tầng kiểm thử & minh chứng · **SV2** — Frontend: smashcourt-fe, Playwright, kịch bản k6 |
| **Phiên bản kế hoạch** | v2.1 — 05/10/2026: khớp khung thời gian chính thức 07/09 – 26/12/2026 (16 tuần); bỏ pilot với người dùng thật, đánh giá chất lượng quyết định bằng dữ liệu import · v2.0 — re-baseline 01/10/2026 |

> **Vai trò của tài liệu này:** đây là nguồn duy nhất (single source of truth) về **lịch trình, phase, milestone, quality gate và phân vai**. Các tài liệu khác chỉ dẫn link về đây, không chép lại:
> - Phân rã task chi tiết: [`BACKLOG_SPRINTS.md`](BACKLOG_SPRINTS.md) · WBS, phụ thuộc, rủi ro: [`PROJECT_WBS_MAPPING.md`](PROJECT_WBS_MAPPING.md)
> - Thuật ngữ, ranh giới phạm vi, bản đồ tài liệu: [`THESIS_DEVELOPMENT_PLAYBOOK.md`](THESIS_DEVELOPMENT_PLAYBOOK.md)
> - Danh mục kiểm thử & KPI đo lường: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md)

---

## 0. LÝ DO RE-BASELINE (v1.0 → v2.0)

Sau Phase 4, nhóm rà soát lại toàn bộ mã nguồn và tài liệu (01/10/2026) và ghi nhận các vấn đề sẽ gây xung đột khi sang giai đoạn tích hợp và khi phát triển lên Khóa luận:

1. **Minh chứng chưa chạy trên hạ tầng thật:** integration test và benchmark Phase 3–4 dùng EF Core InMemory, strategy giả lập bằng `Task.Delay`. InMemory không thực thi unique index, partial index, `jsonb` hay transaction, nên nhánh xử lý tranh chấp idempotency (`23505`) chưa từng được kiểm chứng. Số liệu Tuần 8 chỉ phản ánh overhead của pipeline trong phòng lab.
2. **Contract v1 không biểu diễn được nghiệp vụ cọc:** response không có số tiền cọc yêu cầu; override không đặt được mức cọc và không lưu trạng thái đích; không lọc được theo chi nhánh; không nhận khách vãng lai và booking nhiều sân.
3. **Lỗi múi giờ:** khung giờ cao điểm được so trên giờ UTC; mã băm request phụ thuộc múi giờ của máy chủ.
4. **Audit chưa thực sự append-only:** override cập nhật trực tiếp lên bản ghi máy; không lưu input của quyết định nên không thể replay hay dùng làm dữ liệu cho Khóa luận.
5. **Thiếu bảo vệ và vận hành:** API chưa có xác thực service-to-service; hàng chờ duyệt không có cơ chế thay thế bản cũ/hết hạn; chưa có observability.
6. **Tài liệu chồng chéo và có tuyên bố không khớp mã nguồn** (RFC 8785, phiên bản package, tên file test, phạm vi outer timeout).

**Nghiệp vụ đặt cọc đã chốt (áp dụng cho toàn bộ tài liệu):**

- TADSS **xác định được** mức cọc → trả `APPROVED` kèm **số tiền cọc**; SmashCourt tạo thanh toán VNPay theo đúng số tiền đó.
- TADSS **không xác định được** vì lý do nghiệp vụ (rủi ro cao, giá trị lớn) → trả `NEED_APPROVAL`; **Manager quyết định** mức cọc hoặc từ chối.
- **Mọi sự cố đều cọc 100%:** strategy lỗi/timeout/circuit mở → fallback của TADSS trả `APPROVED` cọc 100% (`executionType = FALLBACK`; D-13, 04/10/2026 — trước đó fallback trả `NEED_APPROVAL`); TADSS bị tắt (feature flag), lỗi hoặc không phản hồi → SmashCourt áp dụng cọc **100%**; không kịp duyệt trước giờ chơi → `APPROVED` cọc 100%.
- Khách hàng **không** tự chọn mức cọc. Không bao giờ tự động duyệt (No Auto-Approve).

---

## I. NGUYÊN TẮC THỰC THI

1. **Tuần tự theo khối:** TADSS hoàn thiện trước (kể cả hạ tầng kiểm thử và kịch bản kiểm thử) → **BE và FE phát triển song song** → **Integrate** → **Kiểm thử & đánh giá** (chỉ kiểm thử và sửa lỗi, không thêm tính năng).
2. **Chuẩn bị kiểm thử trước khi code tính năng:** toàn bộ công cụ kiểm thử (Testcontainers, Toxiproxy, k6, Playwright, Schemathesis, Stryker.NET, OpenTelemetry + Grafana, notebook báo cáo) được dựng trong Phase 5; danh mục kiểm thử của toàn hệ thống được viết xong tại Milestone 5.
3. **Không cắt giảm kiểm thử:** mọi test case trong danh mục là bắt buộc, phải chạy và đạt. Không có phân tầng Must/Should/Could cho kiểm thử.
4. **Minh chứng phải chạy trên hạ tầng thật:** PostgreSQL 16 và Redis 7 thật (container), lỗi được tiêm ở tầng mạng (Toxiproxy). EF Core InMemory và độ trễ giả lập bằng `Task.Delay` không được dùng làm minh chứng.
5. **Gate đóng mới mở phase:** phase sau chỉ bắt đầu khi gate của phase trước đạt đủ tiêu chí ở Mục IV.

---

## II. MA TRẬN TRÁCH NHIỆM

| Cấu phần | Owner | Reviewer | Deliverable |
|:---|:---:|:---:|:---|
| TADSS: SDK, Runtime, Strategies, Contract v2 | SV1 | SV2 (góc nhìn consumer) | `Shared.DecisionContractSDK` v2, `TADSS.Runtime.Api`, strategies v2 |
| Hạ tầng kiểm thử & observability | SV1 | SV2 | Testcontainers; chế độ đánh giá một lệnh `docker compose up -d --build` (D-12, D-14: TADSS, Postgres, Redis, Toxiproxy, `grafana/otel-lgtm`, evaluator điều phối k6, Schemathesis, Stryker.NET và dựng báo cáo minh chứng) |
| Harness replay & portability | SV1 | SV2 | Replay 215 booking SmashCourt, adapter Hotel Booking Demand |
| SmashCourt-BE (tích hợp cọc, duyệt, job) | SV1 | SV2 | API booking cọc, proxy duyệt, job hết hạn, job báo outcome |
| smashcourt-fe (khách + Manager) | SV2 | SV1 | Màn hình cọc, chờ duyệt, dashboard duyệt |
| Kịch bản k6 & Playwright | SV2 | SV1 | Script k6 (TADSS + E2E), bộ Playwright E2E |
| Báo cáo, slide, video | Cả hai | Cả hai | Báo cáo Đồ án 2, slide 20–25 trang, video demo |

---

## III. LỘ TRÌNH TỔNG THỂ

| Phase | Tuần | Thời gian | Nội dung | Trạng thái |
|:---|:---:|:---|:---|:---:|
| **1 — Foundation** | W01 | 07/09 – 13/09 (thực tế 05/09 – 12/09) | Solution, schema `tadss`, contract nháp | ✅ M1 |
| **2 — Contract SDK v1** | W02 | 14/09 – 20/09 (thực tế 13/09 – 18/09) | SDK, Rich Domain Model, đóng băng v1 | ✅ M2 |
| **3 — Runtime Engine** | W03 | 21/09 – 27/09 (thực tế 19/09 – 22/09) | Web API, chọn strategy qua DI, Default/Mock | ✅ M3 |
| **4 — Resilience & Audit** | W04 | 28/09 – 04/10 (thực tế 23/09 – 01/10) | Polly v8, cache, audit đồng bộ, micro-benchmark lab | ✅ M4 |
| **5 — Hoàn thiện TADSS + hạ tầng kiểm thử** | W05–W08 | 05/10 – 01/11 (bắt đầu thực tế 02/10) | Rà soát và điều chỉnh kế hoạch (Gate MR đạt 01/10), contract v2, sửa lõi, toàn bộ công cụ kiểm thử, danh mục kiểm thử toàn hệ thống | ⏳ M5 |
| **6 — BE ∥ FE** | W09–W12 | 02/11 – 29/11 | SmashCourt-BE và smashcourt-fe phát triển song song theo API contract đã chốt | ⏳ M6 |
| **7 — Integrate** | W13 | 30/11 – 06/12 | Ghép FE ↔ BE ↔ TADSS bằng Docker Compose, **feature freeze** | ⏳ M7 |
| **8 — Kiểm thử & đánh giá** | W14–W15 | 07/12 – 20/12 | Chạy toàn bộ kế hoạch kiểm thử (chế độ đánh giá một lệnh), E2E qua SmashCourt, đánh giá trên dữ liệu import, báo cáo minh chứng; chỉ sửa lỗi | ⏳ M8 |
| **9 — Đóng gói & bảo vệ** | W16 | 21/12 – 26/12 | Báo cáo, slide, video, diễn tập | ⏳ M9 |

```mermaid
gantt
    title TADSS — Đồ án 2 (kế hoạch v2.1, 07/09 – 26/12/2026)
    dateFormat  YYYY-MM-DD
    axisFormat  %d/%m
    section Đã hoàn thành
    Phase 1 Foundation            :done, p1, 2026-09-05, 2026-09-12
    Phase 2 Contract SDK v1       :done, p2, 2026-09-13, 2026-09-18
    Phase 3 Runtime Engine        :done, p3, 2026-09-19, 2026-09-22
    Phase 4 Resilience & Audit    :done, p4, 2026-09-23, 2026-10-01
    section TADSS
    Phase 5 TADSS + test infra    :active, p5, 2026-10-02, 2026-11-01
    section Song song
    Phase 6 SmashCourt-BE (SV1)   :p6b, 2026-11-02, 2026-11-29
    Phase 6 smashcourt-fe (SV2)   :p6f, 2026-11-02, 2026-11-29
    section Hoàn thiện
    Phase 7 Integrate             :p7, 2026-11-30, 2026-12-06
    Phase 8 Kiểm thử & đánh giá   :p8, 2026-12-07, 2026-12-20
    Phase 9 Đóng gói              :p9, 2026-12-21, 2026-12-26
```

---

## IV. CHI TIẾT TỪNG PHASE

### ✅ Phase 1 — Foundation & Architecture (W01, thực tế 05/09 – 12/09)

- **Đã làm:** solution .NET 8 (SDK / Runtime / Strategies / Tests), schema `tadss` với bảng `tadss.decision_executions`, EF Core migration, Serilog, CI build + Docker.
- **Milestone 1:** solution build 0 warning, migration áp dụng được, contract nháp v1.

### ✅ Phase 2 — Decision Contract SDK v1 (W02, thực tế 13/09 – 18/09)

- **Đã làm:** `Reservation` (rich domain model) và value objects, DTO, enum, `IReservationDecisionStrategy`; 7 JSON schema; snapshot 32 public type (`baseline_public_api.txt`); kiểm thử kiến trúc (dependency purity, public API surface).
- **Milestone 2:** contract v1.0.0 đóng băng, độ bao phủ SDK 99,49% (hồ sơ v1 đã gỡ khỏi repo ở Phase 5, tra lại qua tag `phase4-baseline`).

### ✅ Phase 3 — Decision Runtime Engine (W03, thực tế 19/09 – 22/09)

- **Đã làm:** 4 endpoint v1 (`POST /decisions`, `GET /decisions/{id}`, `GET /decisions/pending-approvals`, `POST /decisions/{id}/override`); `StrategyResolver` tra cứu theo tên trên dictionary đăng ký qua DI; `DefaultDecisionStrategy`, `MockAlternativeDecisionStrategy`; idempotency theo `X-Idempotency-Key` + `request_hash`; optimistic concurrency bằng cột `version`; correlation id; lỗi RFC 7807.
- **Milestone 3:** runtime chạy độc lập.

### ✅ Phase 4 — Resilience, Cache & Audit (W04, thực tế 23/09 – 01/10)

- **Đã làm:** pipeline Polly v8 (Outer Timeout 500ms → Fallback → Retry 2 lần/30ms → Circuit Breaker → Inner Timeout 120ms); `FallbackDecisionStrategy` trả `NEED_APPROVAL` (hành vi Phase 4; từ D-13 fallback trả `APPROVED` cọc 100%); cache theo request-hash (Redis + `IMemoryCache`); ghi audit đồng bộ qua MediatR; bộ kiểm thử tiêm lỗi F1–F7; micro-benchmark 19 kịch bản × 4 mức tải.
- **Milestone 4:** 510 test tự động pass (483 unit + 27 integration).
- **Ghi chú sau audit:** micro-benchmark Tuần 8 chạy trên EF Core InMemory với strategy giả lập, chỉ được dùng để mô tả **overhead của pipeline trong phòng lab**, không dùng làm minh chứng SLA. Module chuẩn hóa dataset Tuần 9 bị gỡ bỏ ở bước rà soát đầu Phase 5 vì dữ liệu sinh từ DB in-memory. Báo cáo benchmark Tuần 8 đã gỡ khỏi repo ở Phase 5, tra lại qua tag `phase4-baseline`.

### ⏳ Phase 5 — Hoàn thiện TADSS + hạ tầng kiểm thử (W05–W08, 05/10 – 01/11; bắt đầu thực tế 02/10)

#### ✅ Bước 0 — Rà soát và điều chỉnh kế hoạch (01/10 – 11/10, Gate MR đạt 01/10)

- **SV1:** audit mã nguồn và tài liệu; đồng bộ toàn bộ tài liệu theo kế hoạch v2.0; gỡ module `Normalization` cùng 6 dataset tổng hợp; build + test xanh.
- **SV2:** review tài liệu từ góc nhìn FE/UX; xác nhận luồng nghiệp vụ cọc và duyệt.
- **Gate MR:** `git status` sạch ở cả 4 repo; không còn tài liệu mâu thuẫn với mã nguồn; TADSS build `--warnaserror` và test pass; tag `phase4-baseline` được tạo trước khi dọn.
- **Kết quả Gate MR (01/10/2026):** 4 repo sạch; build 0 warning; 473 unit + 27 integration test pass, CI `develop` xanh; tag `phase4-baseline` (`243a421`) đã đẩy lên remote. Review của SV2 (TR.4) tiếp tục đến 11/10 và gộp góp ý vào T5.24.

#### Kế hoạch theo tuần

| Tuần | SV1 (TADSS + hạ tầng) | SV2 (chuẩn bị kiểm thử) |
|:---|:---|:---|
| **W05** (05–11/10) | ✅ Gói 5.1–5.3 xong sớm (02/10 – 05/10): contract v2 (OpenAPI + JSON schema + SDK), migration DB v2, integration test chuyển sang **Testcontainers** (PostgreSQL 16 + Redis 7, gỡ EF InMemory), hiện thực v2 (số cọc trong response, strategies v2, snapshot request, bảng `decision_overrides` / `decision_outcomes` append-only + trigger, hàng chờ theo chi nhánh có hạn, API key, sửa múi giờ, map exception), Docker Compose + ngrok. Gói **5.4**: định tuyến strategy theo từng request + shadow mode; cách ly timeout cho strategy không hợp tác; chính sách cache theo strategy + breaker cho Redis | Review contract v2 từ phía consumer; dựng **Playwright** trong `smashcourt-fe`; dựng dự án **k6** |
| **W06** (12–18/10) | ✅ (xong sớm 06–08/10) Gói **5.5** (phần 1): gắn **OpenTelemetry**, dựng dashboard **Grafana**; khung **chế độ đánh giá một lệnh** (D-12; từ D-14 chạy cùng `docker compose up -d --build`) | Viết script k6 cho TADSS (tải danh nghĩa, tăng dần, soak); bắt đầu đặc tả kịch bản E2E |
| **W07** (19–25/10) | ✅ (xong sớm 08–09/10) Gói **5.5** (phần 2): evaluator điều phối kịch bản lỗi **Toxiproxy**, k6, **Schemathesis**, **Stryker.NET** và bộ sinh báo cáo | Hoàn thiện script k6; khung kịch bản Playwright (đặc tả từng bước, chờ FE) |
| **W08** (26/10–01/11) | ✅ (xong sớm 09/10) phần mã của gói **5.6**: harness **replay** 215 booking (xuất ẩn danh) và adapter **Hotel Booking Demand**, đổ dữ liệu vào báo cáo của chế độ đánh giá (bước (e) của evaluator, Evidence Pack thành bước (f)), kèm hướng dẫn chạy từng bước `docs/guides/EVALUATION_RUN_GUIDE.md`; bản nhanh chạy bằng một lệnh `docker compose up -d --build` (D-14) đạt mọi tiêu chí áp dụng được (lượt `EV-20261011-EVAL-quick-r1`, ~28 phút, SC7 `PASS`); **còn chờ người dùng:** xuất CSV SmashCourt (U-3), xác nhận giấy phép dataset (U-2). Gói **5.7** (lập kế hoạch 11/10): rà soát tài liệu có kiểm tự động (DOC-1…DOC-4); **đóng băng contract v2** ngay trong gói, trước lượt chạy đầy đủ; chạy **bản đầy đủ** của chế độ đánh giá (toàn bộ danh mục TADSS, ước tính ~2,8 giờ) trên đúng bản đã đóng băng → đạt 100%, chốt Evidence Pack; chốt **API contract BE ↔ FE** cho Phase 6 (route theo `bookingId`, hàng chờ đọc từ DB của BE, cọc dạng tỷ lệ); chốt môi trường tích hợp (Docker Compose + ngrok, VNPay sandbox, dữ liệu trên nhánh Neon riêng `staging`) | Review API contract BE ↔ FE; hoàn tất danh mục kiểm thử E2E/FE |

- **Gate M5 — TADSS Complete & Test-Ready:**
  - Toàn bộ test case TADSS trong danh mục (UT, IT, CT, FI, LT mức TADSS, MT) đạt trên hạ tầng thật; không còn EF InMemory trong solution.
  - Contract v2 đóng băng (OpenAPI v2, JSON schema v2, public API baseline v2).
  - Dashboard Grafana, kịch bản Toxiproxy, k6, Schemathesis, Stryker và bộ sinh báo cáo chạy được bằng một lệnh (`docker compose up -d --build`, chế độ đánh giá, D-12, D-14) và đã có lượt chạy đầy đủ sinh `evidence/<run_id>/`.
  - Danh mục kiểm thử **toàn hệ thống** (gồm BE, FE, E2E) viết xong; API contract BE ↔ FE đã chốt.

### ⏳ Phase 6 — SmashCourt-BE ∥ smashcourt-fe (W09–W12, 02/11 – 29/11)

| Tuần | SV1 — SmashCourt-BE | SV2 — smashcourt-fe |
|:---|:---|:---|
| **W09** (02–08/11) | Thay đổi DB SmashCourt (trạng thái `PENDING_APPROVAL`, trường cọc trên hóa đơn, liên kết `decision_id`); `TadssClient` (timeout, idempotency) + feature flag `EnableTadssDecision` qua `IOptionsMonitor` | **Thiết kế UI/UX**: hiển thị cọc cho khách, trạng thái chờ duyệt, dashboard duyệt, chi tiết quyết định |
| **W10** (09–15/11) | Luồng đặt sân online theo cọc (VNPay theo số tiền cọc), nhánh 100% khi tắt flag/TADSS lỗi; endpoint proxy cho Manager (phạm vi chi nhánh, danh tính từ JWT) | Build màn hình khách (đặt sân → cọc → thanh toán; chờ duyệt) trên mock API theo contract đã chốt |
| **W11** (16–22/11) | Job hết hạn duyệt (hủy đơn, giải phóng sân, không tự duyệt); job báo outcome về TADSS; thu phần còn lại khi checkout; sự kiện SignalR | Build dashboard Manager (hàng chờ theo chi nhánh, duyệt với mức cọc, từ chối, xung đột) + realtime |
| **W12** (23–29/11) | Test BE (unit + integration Testcontainers) theo danh mục; script k6 E2E cùng SV2; sửa lỗi | Playwright trên mock; hoàn thiện UI; sửa lỗi |

- **Gate M6 — BE & FE Complete:** toàn bộ test case BE trong danh mục đạt; FE build + lint + Playwright (trên mock) đạt; không còn task tính năng mở.

### ⏳ Phase 7 — Integrate (W13, 30/11 – 06/12)

- Ghép FE ↔ BE ↔ TADSS bằng Docker Compose (môi trường tích hợp E-STAGING trên máy nhóm, dữ liệu trên nhánh Neon riêng `staging`, VNPay sandbox); chạy smoke E2E; chuẩn bị dữ liệu import cho đánh giá (215 booking ẩn danh, dataset khách sạn).
- **Feature freeze cuối W13.**
- **Gate M7 — Integrated:** các luồng E2E chính chạy thông trên môi trường tích hợp (APPROVED, NEED_APPROVAL → duyệt, từ chối, hết hạn, tắt flag, TADSS sập).

### ⏳ Phase 8 — Kiểm thử & đánh giá (W14–W15, 07/12 – 20/12)

- Chạy **bản đầy đủ** của chế độ đánh giá một lệnh (`EVAL_MODE=full docker compose up -d --build`, D-12, D-14) trên hạ tầng cô lập; quan sát trực tiếp trên Grafana.
- Chạy E2E (Playwright) và LT-04 (k6 qua SmashCourt-BE) trên môi trường tích hợp.
- Đánh giá chất lượng quyết định bằng dữ liệu import: replay 215 booking SmashCourt (RP) và dataset Hotel Booking Demand (PT). Không có pilot với người dùng thật: toàn bộ kiểm thử do nhóm tự thực hiện ở mức phát triển.
- Chỉ sửa lỗi; mọi lần sửa phải chạy lại các test case liên quan.
- **Gate M8 — Evaluation Complete:** 100% test case đạt; đo đủ KPI SC1–SC8; báo cáo HTML và Evidence Pack hoàn chỉnh.

**Kế hoạch kiểm thử** (công cụ và test case được chuẩn bị ở Phase 5, chạy liên tục khi phát triển, chạy đầy đủ ở Phase 8; mọi kiểm thử dùng PostgreSQL/Redis thật; mã danh mục và KPI xem [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md)):

| Tên | Nội dung kiểm thử | Công cụ |
| :--- | :--- | :--- |
| Kiểm thử đơn vị (UT) | Kiểm tra từng hàm, từng lớp nhỏ khi chạy riêng lẻ: tính tiền cọc theo hạng khách và khung giờ (ví dụ khách Gold đặt sân 200.000đ → cọc 30% = 60.000đ), cửa sổ phê duyệt (đặt sát giờ chơi → cọc 100%), chọn strategy, băm request làm khóa cache, chuyển lỗi thành mã lỗi. Đạt khi 100% test case đúng (SC8). | xUnit, FluentAssertions, Moq |
| Đánh giá chất lượng bộ test (MT-01…03) | Công cụ tự cài lỗi giả vào mã nguồn (đổi mức cọc 30% thành 0%, đổi `>` thành `>=`…) rồi chạy lại bộ test; test phải báo sai thì mới tính là bắt được lỗi. Đạt khi mutation score ≥ 70% (SC8). | Stryker.NET |
| Kiểm thử tích hợp (IT) | Gọi API của TADSS với PostgreSQL và Redis thật dựng tự động trong container: gửi cùng một request hai lần → chỉ tạo một quyết định; hai Manager cùng duyệt một đơn → chỉ một người thành công (SC4); mỗi response 200 có đúng một bản ghi `PRIMARY`; lệnh sửa/xóa lưu vết bị cơ sở dữ liệu chặn (SC3). | xUnit, Testcontainers |
| Kiểm thử hợp đồng API (CT) | Kiểm tra TADSS luôn trả đúng định dạng đã thống nhất với SmashCourt: so từng response với JSON Schema; tự sinh hàng nghìn request (thiếu trường, số âm, ngày sai, chuỗi rất dài, ký tự lạ) từ OpenAPI. Đạt khi 0 lỗi 5xx, 0 response sai schema, dữ liệu sai trả 400 kèm mã lỗi (CT-03). | JsonSchema.Net, Schemathesis |
| Kiểm thử chịu lỗi (FI-01…10) | Cố ý gây sự cố và kiểm tra phản ứng: strategy chậm, treo hoặc ném lỗi → `APPROVED` cọc 100% với pipeline ≤ 500 ms, không vào hàng chờ duyệt (SC2); mất PostgreSQL → 503, không bản ghi dở dang; mất Redis → vẫn phục vụ; client hủy request giữa chừng → không hỏng dữ liệu; hết sự cố → tự hồi phục. | xUnit, Toxiproxy, decorator tiêm lỗi |
| Kiểm thử tải (LT-01…03, LT-05) | Giả lập nhiều yêu cầu cùng lúc: 50 RPS × 10 phút; tăng dần 10 → 400 RPS; 50 RPS × 60 phút; vừa tải vừa gây sự cố; theo dõi trên Grafana. Đạt khi p95 < 150 ms (SC1), p99 ≤ 700 ms khi có sự cố (SC2), không rò rỉ bộ nhớ/kết nối khi chạy dài. | k6, Grafana |
| Kiểm thử bảo mật (SEC-1…7) | Kiểm tra chỉ SmashCourt gọi được TADSS và TADSS không bị lạm dụng: thiếu/sai API key → 401; vượt giới hạn tốc độ → 429; request quá lớn → 413; log không chứa khóa, mật khẩu. | xUnit, k6 |
| Feature Flag & toàn vẹn đơn đặt sân (FI-11…14) | Tắt cờ `EnableTadssDecision` khi đang có người đặt sân: cờ hiệu lực < 2 giây không cần khởi động lại, mọi đơn cọc 100% (SC5); sau mỗi kịch bản sự cố, chạy bộ truy vấn bất biến trên DB SmashCourt: 0 booking hỏng (SC6). | xUnit, SQL |
| Kiểm thử đầu–cuối trên giao diện (E2E, LT-04) | Tự động thao tác trên trình duyệt qua FE → BE → TADSS: khách đặt sân → thấy mức cọc → thanh toán VNPay (sandbox) → Manager duyệt / từ chối / để hết hạn. Đạt khi các luồng chính chạy thông và p95 tạo booking < 1000 ms (SC1). | Playwright, k6 |
| Đánh giá trên dữ liệu đặt sân thật (RP-01…02) | Import 215 booking thật của SmashCourt (ẩn danh), gửi lần lượt vào TADSS và so sánh với chính sách cũ (cọc 100% cho mọi đơn): tỷ lệ duyệt ngay / cần Manager xem xét, mức cọc trung bình theo hạng khách và khung giờ; kết quả là bảng so sánh hai chính sách. | Script Python |
| Đánh giá độc lập nghiệp vụ (PT-01…02) | Import ≥ 10.000 đơn đặt phòng khách sạn từ dataset công khai Hotel Booking Demand; script chuyển từng đơn sang request v2 (khách sạn → chi nhánh, ngày nhận – trả phòng → khung thời gian, giá × số đêm → tổng tiền) rồi gửi vào. Đạt khi 0 lỗi contract và 0 thay đổi mã runtime/SDK (SC7). | Script Python |

**Kết quả đầu ra:** báo cáo HTML (đạt/không đạt theo SC1–SC8, biểu đồ p50/p95/p99, biểu đồ diễn biến sự cố, số test đạt theo nhóm) cùng dữ liệu thô và `manifest.json` (SHA-256) trong `evidence/<run_id>/` — nguồn số liệu cho Chương 3–4 của báo cáo và phần trình bày trước hội đồng.

### ⏳ Phase 9 — Đóng gói & bảo vệ (W16, 21/12 – 26/12/2026)

- Hoàn thiện báo cáo (Chương 3–4 lấy số liệu từ Evidence Pack), slide 20–25 trang, video demo (đặt sân → TADSS → VNPay → duyệt → tiêm lỗi trực tiếp trên Grafana), diễn tập.
- **Gate M9 — Defense Ready.**
- Báo cáo viết cuốn chiếu: Chương 1–2 hoàn thành trước cuối W13; Chương 3–4 trong W15–W16.

---

## V. QUALITY GATES & DEFINITION OF DONE

| Gate | Tiêu chí | Minh chứng |
|:---|:---|:---|
| **G1 Build** | TADSS 0 warning với `--warnaserror`; SmashCourt-BE không thêm warning so với baseline; FE `tsc` + lint đạt | Log build CI |
| **G2 Unit** | 100% unit test đạt | Báo cáo test (trx) |
| **G3 Integration (hạ tầng thật)** | 100% integration test đạt trên PostgreSQL/Redis thật qua Testcontainers; không có EF InMemory | Báo cáo test + log container |
| **G4 Contract** | Public API baseline v2 khớp; response hợp lệ theo JSON schema v2; Schemathesis 0 lỗi | Báo cáo Schemathesis |
| **G5 Test quality** | Mutation score Stryker ≥ 70% cho SDK, Strategies và service lõi | Báo cáo HTML Stryker |
| **G6 Resilience** | 100% kịch bản FI đạt kết quả kỳ vọng trên hạ tầng thật | Log chaos + Grafana |
| **G7 Performance** | Đạt ngưỡng SC1, SC2 bằng k6 | Báo cáo k6 + Grafana |
| **G8 Integration E2E** | 100% kịch bản Playwright đạt; 0 booking mâu thuẫn sau ma trận lỗi E2E | Playwright report/video, kết quả truy vấn bất biến |
| **G9 Evidence** | Evidence Pack đủ thành phần, gắn commit và cấu hình; notebook sinh lại được | `docs/research/evidence-packs/` |

**Definition of Done cho mỗi task:** mã nguồn + test case tương ứng trong danh mục đạt + tài liệu liên quan được cập nhật + build/verify xanh ở repo bị ảnh hưởng.

Định nghĩa đo lường chi tiết của KPI SC1–SC8: xem Mục 5 trong [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

---

## VI. QUY TẮC DÙNG THUẬT NGỮ (ACADEMIC TONE LOCK)

| Tránh dùng | Thay bằng |
|:---|:---|
| "Chịu lỗi cấp Enterprise", "an toàn mức ngân hàng" | "Các cơ chế chịu lỗi giúp duy trì khả năng ra quyết định khi strategy gặp timeout hoặc lỗi" |
| "Chuẩn hóa theo RFC 8785 (JCS)" | "Chuẩn hóa canonical theo thứ tự trường cố định trước khi băm SHA-256" |
| "In-vivo audit harvest", "8 bất biến toán học" | "Trích xuất bản ghi audit từ PostgreSQL thật kèm manifest SHA-256" |
| "Phân giải chiến lược O(1)" | "Tra cứu strategy theo tên trên dictionary đăng ký qua DI" |
| "100% không lỗi" | "100% test case trong danh mục đạt" (luôn nêu rõ 100% của cái gì) |
| "Hệ thống AI tự động hoàn toàn" | "Hệ thống hỗ trợ ra quyết định có con người phê duyệt (human-in-the-loop)" |
| "Append-only" cho bảng có `UPDATE` | Chỉ dùng "append-only" khi DB chặn `UPDATE/DELETE` bằng trigger |

---

*Kế hoạch v2.1 — 05/10/2026 (khung 07/09 – 26/12/2026); re-baseline v2.0 ngày 01/10/2026. Mọi thay đổi lịch trình tiếp theo cập nhật trực tiếp tại tài liệu này.*
