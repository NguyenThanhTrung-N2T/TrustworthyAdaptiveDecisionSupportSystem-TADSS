# SỔ TAY ĐIỀU HƯỚNG ĐỒ ÁN 2 & KHÓA LUẬN (PLAYBOOK) — v2.0

> **Vai trò của tài liệu:** khóa thuật ngữ, ranh giới phạm vi giữa Đồ án 1 / Đồ án 2 / Khóa luận, câu hỏi nghiên cứu kỹ thuật của Đồ án 2 và **bản đồ tài liệu (mỗi chủ đề do đúng một file sở hữu)**. Lịch trình: [`MASTER_EXECUTION_PLAN.md`](MASTER_EXECUTION_PLAN.md).

---

## 0. KHÓA THUẬT NGỮ

| Viết tắt | Tên đầy đủ | Định nghĩa | Phạm vi |
| :--- | :--- | :--- | :--- |
| **TADSS** | Trustworthy Adaptive Decision Support System | Nền tảng runtime độc lập: nhận yêu cầu quyết định, định tuyến tới strategy, thực thi trong pipeline chịu lỗi, lưu audit bất biến, cho phép con người phê duyệt/ghi đè. Trong Đồ án 2, *Trustworthy* là **độ tin cậy vận hành** (chịu lỗi, audit, kiểm soát của con người); *Adaptive* là khả năng **chọn strategy theo cấu hình và ngữ cảnh request**, không đồng nghĩa có AI/ML. | Đồ án 2 |
| **TADS** | Trustworthy Adaptive Decision Strategy | Strategy quyết định thích ứng (điểm tin cậy có suy giảm theo thời gian, mô hình dự báo no-show) hiện thực `IReservationDecisionStrategy`. | Khóa luận |
| **ATDA** | Adaptive Trust Decision Architecture | Khung kiến trúc nghiên cứu mô tả quan hệ giữa runtime (TADSS) và strategy thích ứng (TADS). | Khóa luận |

### Thuật ngữ kỹ thuật chuẩn

| Không dùng | Dùng | Giải thích |
| :--- | :--- | :--- |
| Semantic cache | **Request-hash cache** | So khớp chính xác theo SHA-256, không hiểu ngữ nghĩa |
| Chuẩn hóa RFC 8785 (JCS) | **Chuẩn hóa canonical theo thứ tự trường cố định** | Mã nguồn dựng object canonical với thứ tự trường cố định, không cài đặt JCS |
| Phân giải strategy O(1) | **Tra cứu strategy theo tên trên dictionary đăng ký qua DI** | Không nạp DLL động lúc chạy; thêm strategy cần đăng ký DI và deploy lại |
| Hot-swap strategy | **Đổi strategy bằng cấu hình định tuyến** | Quy tắc định tuyến đọc từ cấu hình; strategy phải được đăng ký sẵn |
| Append-only (cho bảng có UPDATE) | **Append-only** chỉ khi DB chặn `UPDATE/DELETE` bằng trigger | Từ v2: `decision_executions`, `decision_overrides`, `decision_outcomes` đều được trigger bảo vệ |
| Audit trong transaction boundary của MediatR | **Ghi audit đồng bộ, fail-closed** | Request chỉ trả kết quả sau khi bản ghi audit đã được lưu thành công |
| Circuit Breaker đếm 5 lỗi | **Circuit Breaker mở khi tỷ lệ lỗi ≥ ngưỡng trong cửa sổ mẫu** | `FailureRatio = 0.5`, `SamplingDuration = 30s`, tối thiểu 10 request |
| Retry 3 lần | **Retry 2 lần (tổng 3 lần thử)** | 3 × 120ms + 2 × 30ms = 420ms < 500ms |
| In-vivo harvest, bất biến toán học | **Trích xuất bản ghi audit từ PostgreSQL thật kèm manifest SHA-256** | |
| Schema `tadss_*` | **Schema `tadss`** | |

---

## I. ĐỊNH VỊ BA HỌC PHẦN

| Học phần | Câu hỏi cốt lõi | Sản phẩm | Kiểm thử |
| :--- | :--- | :--- | :--- |
| **Đồ án 1** | *How to book a court* | SmashCourt (Next.js + .NET + FastAPI): đặt sân, VNPay, chatbot | Unit test + kiểm thử thủ công |
| **Đồ án 2** | *How to execute a decision safely* | TADSS runtime + tích hợp cọc/duyệt vào SmashCourt | Danh mục kiểm thử toàn hệ thống trên hạ tầng thật, chaos, tải, E2E, replay, pilot |
| **Khóa luận** | *How to make the decision intelligently* | Strategy TADS chạy trên TADSS | Đánh giá mô hình, đánh giá off-policy, shadow mode, mô phỏng |

### Ranh giới phạm vi Đồ án 2

| Trong phạm vi | Ngoài phạm vi (Khóa luận / future work) |
| :--- | :--- |
| Contract SDK v2, runtime, strategies quy tắc (Default theo hạng, MockAlternative theo khung giờ), định tuyến theo request, shadow mode | Thuật toán Adaptive Trust Score, mô hình học máy dự báo no-show |
| Audit bất biến (execution, override, outcome) — nguồn dữ liệu cho Khóa luận | Decision Trace API giải thích quyết định |
| Tích hợp SmashCourt: cọc, chờ duyệt, dashboard Manager, feature flag, job hết hạn/outcome | Mô phỏng đa tác tử, ablation study, ràng buộc công bằng |
| Hạ tầng kiểm thử & minh chứng: Testcontainers, Toxiproxy, k6, Playwright, Schemathesis, Stryker.NET, OpenTelemetry + Grafana, notebook | Event sourcing / outbox phân tán |
| Replay 215 booking SmashCourt, adapter Hotel Booking Demand, pilot UAT | |

### Ranh giới kiến trúc

1. **Database:** TADSS dùng chung máy chủ PostgreSQL với SmashCourt nhưng tách hoàn toàn ở schema `tadss`; SmashCourt không đọc/ghi trực tiếp bảng của TADSS và ngược lại.
2. **Thanh toán:** TADSS không biết VNPay; TADSS chỉ trả quyết định và số tiền cọc. SmashCourt sở hữu giao dịch đặt sân và thanh toán.
3. **Domain portability:** domain khác (khách sạn, phòng họp) chỉ cần adapter phía client ánh xạ về contract v2; không sửa mã runtime.
4. **Dữ liệu cho Khóa luận:** snapshot input, quyết định, override và outcome do Đồ án 2 lưu là đầu vào cho Khóa luận.

---

## II. CÂU HỎI NGHIÊN CỨU KỸ THUẬT CỦA ĐỒ ÁN 2

- **RQ1 — Resilience & tính toàn vẹn giao dịch:** Khi strategy, Redis, PostgreSQL hoặc kết nối SmashCourt ↔ TADSS gặp sự cố (tiêm lỗi ở tầng mạng trên hạ tầng thật), hệ thống có luôn trả về quyết định an toàn trong ngân sách thời gian và giữ cho booking của SmashCourt không rơi vào trạng thái mâu thuẫn không?
- **RQ2 — Governance & khả năng kiểm toán:** Mô hình audit bất biến (execution + override + outcome, được DB bảo vệ) có đảm bảo mọi quyết định truy vết được, không bị sửa sau khi ghi, và chống ghi đè đồng thời của nhiều Manager không?
- **RQ3 — Chất lượng tích hợp & sử dụng:** Khi tích hợp vào luồng đặt sân thật, TADSS ảnh hưởng thế nào đến độ trễ đặt sân, tỷ lệ đơn cần duyệt, thời gian duyệt và mức độ dễ dùng (SUS) đối với khách và Manager?

Giả thuyết của Khóa luận (H1, ràng buộc công bằng): xem [`RESEARCH_THESIS_DEFENSE.md`](../research/RESEARCH_THESIS_DEFENSE.md).

---

## III. BẢN ĐỒ TÀI LIỆU — MỖI CHỦ ĐỀ MỘT NGUỒN

| Chủ đề | File sở hữu | Ghi chú |
| :--- | :--- | :--- |
| Tổng quan repo, cách chạy | [`README.md`](../../README.md) | Ngắn gọn, dẫn link |
| Tổng quan hệ thống & luồng nghiệp vụ | [`PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md`](../PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md) | Sơ đồ cho báo cáo |
| Đề cương Đồ án 2 | [`PROPOSAL_DO_AN_2.md`](PROPOSAL_DO_AN_2.md) | Nguồn để dựng lại bản docx |
| Lịch trình, phase, gate, phân vai | [`MASTER_EXECUTION_PLAN.md`](MASTER_EXECUTION_PLAN.md) | **Nguồn duy nhất về thời gian** |
| Task chi tiết | [`BACKLOG_SPRINTS.md`](BACKLOG_SPRINTS.md) | |
| WBS, phụ thuộc, rủi ro | [`PROJECT_WBS_MAPPING.md`](PROJECT_WBS_MAPPING.md) | |
| Thuật ngữ, phạm vi, RQ, bản đồ tài liệu | Tài liệu này | |
| Kiến trúc phần mềm (hiện trạng + mục tiêu Phase 5) | [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md) | |
| Bất biến lưu trữ & schema `tadss` | [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md) | |
| Phân loại lỗi & pipeline chịu lỗi | [`RESILIENCE_AND_FAULT_TAXONOMY.md`](../architecture/RESILIENCE_AND_FAULT_TAXONOMY.md) | |
| Quyết định kiến trúc | [`ARCHITECTURE_DECISION_RECORDS.md`](../architecture/ARCHITECTURE_DECISION_RECORDS.md) + [`adr/`](../architecture/adr/) | |
| Tích hợp SmashCourt ↔ TADSS + **contract v2** | [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md), `openapi-v2.yaml`, `schemas/v2/` | Nguồn cho Phase 5–6; contract v1 (Phase 1–4) đã gỡ, tra lại qua tag `phase4-baseline` |
| Danh mục kiểm thử, KPI, giao thức thực nghiệm | [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md) | **Nguồn duy nhất về test case & KPI** |
| Dữ liệu minh chứng & Evidence Pack | [`DATASET_SCHEMA_SPECIFICATION.md`](../research/DATASET_SCHEMA_SPECIFICATION.md) | |
| Định hướng Khóa luận | [`RESEARCH_THESIS_DEFENSE.md`](../research/RESEARCH_THESIS_DEFENSE.md) | |
| Khảo sát ý tưởng ban đầu | [`RESEARCH_PROBLEM_BACKLOG.md`](../research/RESEARCH_PROBLEM_BACKLOG.md) | Lưu trữ lịch sử |

**Quy tắc cập nhật:** khi một thông số thay đổi (thời gian, tham số pipeline, endpoint, KPI…), chỉ sửa ở file sở hữu; các file khác dẫn link thay vì chép lại con số.

---

## IV. CHIẾN LƯỢC VIẾT BÁO CÁO

| Chương | Nội dung | Nguồn | Thời điểm |
| :--- | :--- | :--- | :--- |
| 1 — Tổng quan | Bối cảnh, vấn đề, mục tiêu, phạm vi | PROPOSAL, Playbook | Xong trước cuối W15 |
| 2 — Thiết kế | Kiến trúc, contract v2, DB, pipeline, luồng tích hợp | SAS, INVARIANTS, TAXONOMY, ADR, INTEGRATION_SPEC | Xong trước cuối W15 |
| 3 — Hiện thực & kiểm thử | Hiện thực 3 repo, hạ tầng kiểm thử, danh mục kiểm thử | Mã nguồn, EXPERIMENTAL_DESIGN | W17–W18 |
| 4 — Đánh giá | KPI SC1–SC9, chaos, tải, replay, portability, pilot | Evidence Pack + notebook | W17–W18 |
| 5 — Kết luận | Đóng góp, hạn chế, hướng Khóa luận | RESEARCH_THESIS_DEFENSE | W18 |

**Nguyên tắc trình bày số liệu:** mọi con số trong báo cáo phải truy được về Evidence Pack (run id, commit, cấu hình, file dữ liệu thô); luôn nêu rõ môi trường đo (local docker-compose hay staging) và cỡ mẫu.
