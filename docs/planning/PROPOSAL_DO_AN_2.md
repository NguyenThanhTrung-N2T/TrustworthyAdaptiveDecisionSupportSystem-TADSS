# ĐỀ CƯƠNG ĐỒ ÁN 2 (PROJECT PROPOSAL) — BẢN ĐIỀU CHỈNH v2.0

## THIẾT KẾ VÀ XÂY DỰNG NỀN TẢNG RUNTIME ENGINE RA QUYẾT ĐỊNH CHỊU LỖI CHO HỆ THỐNG ĐẶT CHỖ DỊCH VỤ

**Tên tiếng Anh:** *Design and Development of a Fault-Tolerant Decision Runtime Engine for Service Reservation Systems*

| | |
| :--- | :--- |
| **Sinh viên thực hiện** | *(Điền tên, MSSV — SV1: Lead Backend; SV2: Frontend)* |
| **Giảng viên hướng dẫn** | *(Điền tên GVHD)* |
| **Khoa / Ngành** | Công nghệ Thông tin / Kỹ thuật Phần mềm |
| **Hệ thống đối chứng** | SmashCourt — hệ thống đặt sân cầu lông trực tuyến (Đồ án 1) |
| **Sản phẩm chính** | **TADSS** (Trustworthy Adaptive Decision Support System) — nền tảng runtime ra quyết định |
| **Thời gian** | 31/08/2026 – 03/01/2027 (18 tuần) |

> **Ghi chú phiên bản:** bản v2.0 (01/10/2026) điều chỉnh đề cương ban đầu sau khi hoàn thành Phase 1–4. Các thay đổi chính so với bản ban đầu được tóm tắt ở Mục 12.

---

## 1. GIỚI THIỆU & BỐI CẢNH

Trong các hệ thống đặt chỗ dịch vụ (sân thể thao, phòng họp, phòng khám, khách sạn), các quyết định vận hành như **mức đặt cọc**, giới hạn hủy lịch hay ưu tiên tài nguyên giờ cao điểm ảnh hưởng trực tiếp tới doanh thu và trải nghiệm khách hàng.

Ở Đồ án 1, SmashCourt đã hoàn thiện luồng đặt sân trực tuyến tích hợp VNPay. Tuy nhiên hệ thống chỉ có **một chính sách tĩnh**: mọi khách, mọi khung giờ đều thanh toán 100% khi đặt online. Thực tế vận hành cần chính sách linh hoạt hơn — ví dụ cọc thấp cho khách thân thiết giờ thấp điểm, cọc cao hơn giờ cao điểm, hoặc cần người quản lý xem xét các đơn giá trị lớn hay khách có lịch sử bùng sân.

Nhúng trực tiếp các chính sách thay đổi liên tục này vào mã CRUD của SmashCourt gây ra ba rủi ro kỹ thuật: (1) phải sửa và triển khai lại cả ứng dụng mỗi khi đổi chính sách; (2) thành phần quyết định chậm hoặc lỗi kéo sập luồng đặt sân và thanh toán; (3) không có vết giải trình vì sao một quyết định được đưa ra, và không có cách để quản lý can thiệp an toàn.

**TADSS** được xây dựng như một nền tảng runtime độc lập: nhận yêu cầu quyết định, chọn chiến lược phù hợp, thực thi chiến lược trong pipeline chịu lỗi, lưu vết bất biến và cho phép con người phê duyệt. Trong Đồ án 2, *Adaptive* nghĩa là khả năng **chọn và chuyển đổi chiến lược theo cấu hình và ngữ cảnh của từng yêu cầu**, không đồng nghĩa với việc TADSS chứa thuật toán học máy. Chiến lược học máy là nội dung của Khóa luận.

---

## 2. PHÁT BIỂU VẤN ĐỀ

1. **Ràng buộc chặt giữa nghiệp vụ và logic quyết định:** chính sách cọc nằm trong mã nghiệp vụ; đổi chính sách đồng nghĩa sửa và triển khai lại ứng dụng.
2. **Sự cố lan truyền:** thành phần quyết định chậm hoặc lỗi làm hỏng luồng đặt sân nếu không có cơ chế cô lập.
3. **Thiếu khả năng kiểm toán và kiểm soát của con người:** không lưu vết đầu vào và lý do của quyết định; không có cách duyệt/ghi đè an toàn trong khi vẫn giữ nguyên quyết định gốc của máy.

---

## 3. MỤC TIÊU

### 3.1. Mục tiêu tổng quát

Thiết kế, xây dựng và **kiểm chứng trên hạ tầng thật** nền tảng TADSS, đồng thời tích hợp nó vào luồng đặt sân của SmashCourt để chứng minh khả năng vận hành thực tế.

### 3.2. Mục tiêu cụ thể

- **O1 — Decision Contract SDK:** thư viện hợp đồng dùng chung (mô hình đặt chỗ chuẩn hóa, DTO, enum, interface strategy); v1 đã đóng băng, v2 bổ sung số tiền cọc, booking nhiều tài nguyên, khách vãng lai, lịch sử khách, múi giờ chi nhánh.
- **O2 — Decision Runtime API:** nhận yêu cầu, **định tuyến strategy theo từng request**, trả kết quả chuẩn hóa gồm trạng thái và **số tiền cọc**; hỗ trợ **shadow mode** để chạy thử strategy mới.
- **O3 — Pipeline chịu lỗi Polly v8:** Outer Timeout → Fallback → Retry → Circuit Breaker → Inner Timeout, kèm cách ly strategy không hợp tác.
- **O4 — Audit bất biến:** lưu snapshot đầu vào, quyết định, ghi đè và kết quả thực tế vào PostgreSQL; cơ sở dữ liệu chặn sửa/xóa.
- **O5 — Human-in-the-loop:** Manager duyệt kèm mức cọc hoặc từ chối; chống ghi đè trùng và đồng thời; không bao giờ tự động duyệt.
- **O6 — Tích hợp SmashCourt:** thanh toán cọc qua VNPay theo quyết định của TADSS; màn hình chờ duyệt cho khách; dashboard duyệt cho Manager; feature flag bật/tắt nóng; cọc 100% khi TADSS tắt hoặc không phản hồi.
- **O7 — Hệ thống kiểm chứng và minh chứng:** kiểm thử trên PostgreSQL/Redis thật, tiêm lỗi ở tầng mạng, kiểm thử tải, kiểm thử E2E giao diện, mutation testing, observability, replay dữ liệu SmashCourt, kiểm chứng trên dataset công khai và pilot với người dùng thật — mọi kết quả được đóng gói thành Evidence Pack có thể kiểm tra lại.

---

## 4. RANH GIỚI KIẾN TRÚC

```text
Lớp 3: Strategy nghiên cứu (TADS)     ──► KHÓA LUẬN (học máy, điểm tin cậy)
       │ hiện thực IReservationDecisionStrategy
       ▼
Lớp 2: Decision Runtime (TADSS)       ──► ĐỒ ÁN 2 (SDK, routing, Polly, audit, override, cache, observability)
       │ HTTP REST contract v2 (API key)
       ▼
Lớp 1: Sản phẩm đối chứng (SmashCourt) ──► Đồ án 1, được mở rộng ở Đồ án 2 (cọc, chờ duyệt, dashboard Manager)
```

1. **Thanh toán:** TADSS không biết VNPay; TADSS chỉ trả quyết định và số tiền cọc. SmashCourt sở hữu giao dịch đặt sân và thanh toán.
2. **Cơ sở dữ liệu:** TADSS dùng chung máy chủ PostgreSQL với SmashCourt nhưng tách hoàn toàn ở schema `tadss`.
3. **Độc lập domain:** domain khác chỉ cần adapter phía client ánh xạ về contract v2; mã runtime không đổi. Được kiểm chứng bằng dataset đặt phòng khách sạn công khai.
4. **Dữ liệu cho Khóa luận:** snapshot đầu vào, quyết định, ghi đè và kết quả thực tế do Đồ án 2 lưu lại là nguồn dữ liệu cho Khóa luận.

---

## 5. PHẠM VI

### 5.1. Trong phạm vi Đồ án 2

- Decision Contract SDK v1 (đã xong) và v2.
- Runtime API v2: định tuyến strategy, shadow mode, pipeline chịu lỗi, cache theo request-hash, audit bất biến, override, outcome, xác thực API key, OpenTelemetry.
- Hai strategy quy tắc: `DefaultDecisionStrategy` (cọc theo hạng thành viên) và `MockAlternativeDecisionStrategy` (cọc theo khung giờ), cùng `FallbackDecisionStrategy`.
- Tích hợp SmashCourt-BE và smashcourt-fe: luồng cọc, chờ duyệt, dashboard Manager, job hết hạn duyệt, job báo kết quả, feature flag.
- Hệ thống kiểm chứng: danh mục kiểm thử toàn hệ thống (mọi test case là bắt buộc), Evidence Pack, pilot UAT.

### 5.2. Ngoài phạm vi

- Thuật toán điểm tin cậy thích ứng, mô hình học máy dự báo no-show, API giải thích quyết định, mô phỏng đa tác tử, ablation study (**Khóa luận**).
- Event sourcing, outbox phân tán (**future work**).

---

## 6. NGHIỆP VỤ & THIẾT KẾ CHÍNH

### 6.1. Nghiệp vụ đặt cọc

| Tình huống | Kết quả |
| :--- | :--- |
| Strategy xác định được mức cọc | `APPROVED` + số tiền cọc → khách thanh toán cọc qua VNPay; phần còn lại thu khi checkout |
| Strategy không xác định được (giá trị lớn, khách có lịch sử bùng sân) hoặc strategy lỗi | `NEED_APPROVAL` → Manager duyệt kèm mức cọc hoặc từ chối |
| Cần duyệt nhưng không còn đủ thời gian trước giờ chơi | `APPROVED` với cọc 100% |
| Tắt feature flag hoặc TADSS không phản hồi | SmashCourt áp cọc 100% (như Đồ án 1) |
| Hết hạn duyệt | Hủy đơn, giải phóng sân — **không bao giờ tự động duyệt** |

Khách hàng không tự chọn mức cọc.

### 6.2. Luồng phê duyệt

```text
Khách đặt sân ─► SmashCourt ─► TADSS
                                 ├─ APPROVED      ─► VNPay (số tiền cọc)
                                 ├─ NEED_APPROVAL ─► Đơn chờ duyệt ─► Manager
                                 │                    ├─ Duyệt + mức cọc ─► VNPay
                                 │                    ├─ Từ chối         ─► Hủy đơn
                                 │                    └─ Hết hạn         ─► Hủy đơn
                                 └─ REJECTED      ─► Hủy đơn
TADSS tắt / lỗi ─► SmashCourt áp cọc 100%
Booking kết thúc ─► SmashCourt báo kết quả thực tế về TADSS
```

### 6.3. Pipeline chịu lỗi

```text
Outer Timeout 500ms (deadline phần thực thi strategy)
 └─ Fallback → FallbackDecisionStrategy (NEED_APPROVAL)
     └─ Retry 2 lần, delay 30ms (tổng 3 lần thử)
         └─ Circuit Breaker (tỷ lệ lỗi ≥ 50% trong 30s, tối thiểu 10 request, ngắt 15s)
             └─ Inner Timeout 120ms mỗi lần thử
                 └─ Strategy
```

Ngân sách cấu hình 3 × 120ms + 2 × 30ms = 420ms < 500ms. Ở v2, strategy được thực thi cách ly để timeout cắt được cả strategy không tôn trọng tín hiệu hủy.

### 6.4. Audit bất biến

Ba bảng append-only trong schema `tadss` — quyết định (kèm snapshot đầu vào), ghi đè, kết quả thực tế — được trigger PostgreSQL chặn sửa/xóa. Mỗi quyết định có tối đa một ghi đè (ràng buộc unique), chống hai Manager duyệt cùng lúc.

### 6.5. Cache theo request-hash

Đầu vào được chuẩn hóa canonical (thứ tự trường cố định, thời gian quy về UTC) rồi băm SHA-256; key cache gồm hash và tên/phiên bản strategy. Strategy tự khai báo có cho phép cache hay không và TTL, vì strategy phụ thuộc thời gian không nên dùng kết quả cũ.

Chi tiết kỹ thuật: [`SOFTWARE_ARCHITECTURE_SPECIFICATION.md`](../architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md), [`INTEGRATION_SPEC.md`](../contracts/INTEGRATION_SPEC.md), [`DECISION_PERSISTENCE_INVARIANTS.md`](../architecture/DECISION_PERSISTENCE_INVARIANTS.md).

---

## 7. CÔNG NGHỆ

| Nhóm | Công nghệ |
| :--- | :--- |
| Runtime | C# 12, .NET 8, ASP.NET Core Web API, EF Core 8 + Npgsql, PostgreSQL 16 |
| Chịu lỗi & cache | Polly v8, Redis 7 (StackExchange.Redis) + `IMemoryCache` |
| Audit | MediatR (ghi audit đồng bộ), trigger PostgreSQL |
| Observability | Serilog, OpenTelemetry, `grafana/otel-lgtm` (Grafana, Prometheus, Tempo, Loki) |
| Ứng dụng đối chứng | SmashCourt-BE (ASP.NET Core 8, Hangfire, SignalR, VNPay), smashcourt-fe (Next.js) |
| Kiểm thử | xUnit, FluentAssertions, Moq; **Testcontainers** (PostgreSQL, Redis); **Toxiproxy**; **WireMock.Net**; **JsonSchema.Net**; **Schemathesis**; **Stryker.NET**; coverlet + ReportGenerator; **k6**; **Playwright**; MSW |
| Phân tích minh chứng | Jupyter notebook (pandas, plotly) |

---

## 8. CHIẾN LƯỢC KIỂM THỬ & MINH CHỨNG

**Nguyên tắc:**

1. Minh chứng phải chạy trên **hạ tầng thật** (PostgreSQL, Redis thật; lỗi tiêm ở tầng mạng), không dùng cơ sở dữ liệu in-memory hay độ trễ giả lập.
2. **Mọi test case trong danh mục là bắt buộc**, phải chạy và đạt.
3. Công cụ kiểm thử và danh mục kiểm thử được hoàn thành **trước khi** phát triển phần tích hợp (Phase 5); giai đoạn cuối chỉ chạy kiểm thử và sửa lỗi.
4. Mọi số liệu báo cáo truy được về dữ liệu thô, commit và cấu hình (Evidence Pack).

**Danh mục kiểm thử** gồm các nhóm: unit (UT), integration trên hạ tầng thật (IT), contract (CT), mutation (MT), tiêm lỗi/chaos (FI), tải (LT), backend SmashCourt (BE), giao diện và E2E (FE, E2E), replay dữ liệu SmashCourt (RP), độc lập domain (PT), pilot người dùng thật (UAT). Danh mục đầy đủ: [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

**Ba nguồn dữ liệu minh chứng:**

- **215 booking hiện có của SmashCourt** (ẩn danh): replay qua TADSS để kiểm chứng hoạt động trên dữ liệu có hình dạng thực tế. Đây là dữ liệu môi trường phát triển nên chỉ dùng mô tả, không dùng làm nhãn hành vi.
- **Dataset công khai Hotel Booking Demand** (*Data in Brief*, 2019): kiểm chứng TADSS xử lý domain khác mà không sửa mã runtime.
- **Pilot với người dùng thật** (≥ 15 người, ≥ 50 booking, 2 tuần) trên môi trường staging: luồng tích hợp thật, thời gian duyệt, khảo sát khả dụng SUS.

---

## 9. KẾ HOẠCH THỰC HIỆN

| Phase | Thời gian | Nội dung |
| :--- | :--- | :--- |
| 1 — Foundation ✅ | 05/09 – 12/09 | Solution, schema `tadss`, contract nháp |
| 2 — Contract SDK v1 ✅ | 13/09 – 18/09 | SDK, đóng băng v1 |
| 3 — Runtime Engine ✅ | 19/09 – 22/09 | API, strategy, idempotency |
| 4 — Resilience & Audit ✅ | 23/09 – 01/10 | Polly, cache, audit, micro-benchmark lab |
| R — Re-baseline | 01/10 – 11/10 | Rà soát, đồng bộ tài liệu, dọn repo |
| 5 — Hoàn thiện TADSS + hạ tầng kiểm thử | 12/10 – 08/11 | Contract v2, sửa lõi, toàn bộ công cụ và danh mục kiểm thử |
| 6 — Backend ∥ Frontend | 09/11 – 06/12 | SmashCourt-BE (SV1) và smashcourt-fe (SV2) song song |
| 7 — Tích hợp | 07/12 – 13/12 | Ghép hệ thống, staging, đóng băng tính năng |
| 8 — Kiểm thử & sửa lỗi | 14/12 – 27/12 | Chạy toàn bộ danh mục, pilot, Evidence Pack |
| 9 — Đóng gói | 28/12/2026 – 03/01/2027 | Báo cáo, slide, video demo |

Chi tiết tuần, phân vai và gate: [`MASTER_EXECUTION_PLAN.md`](MASTER_EXECUTION_PLAN.md).

---

## 10. TIÊU CHÍ THÀNH CÔNG (KPI)

| Mã | Chỉ số | Mục tiêu thiết kế |
| :--- | :--- | :--- |
| SC1 | Độ trễ quyết định bình thường (PostgreSQL/Redis thật, 50 RPS) | p95 TADSS < 150ms; p95 API tạo booking E2E < 1000ms |
| SC2 | Độ trễ khi strategy lỗi | 100% request có thời gian pipeline ≤ 500ms; p99 endpoint ≤ 700ms |
| SC3 | Toàn vẹn & bất biến audit | 0 quyết định thiếu/thừa bản ghi; 0 lần sửa audit thành công |
| SC4 | Chống gửi trùng & ghi đè đồng thời | 0 bản ghi trùng; 0 ghi đè kép |
| SC5 | Feature flag | Có hiệu lực < 2 giây; khi tắt, 100% booking áp cọc 100% |
| SC6 | Không hỏng booking khi có sự cố | 0 vi phạm bất biến booking sau mọi kịch bản lỗi và trong pilot |
| SC7 | Độc lập domain | ≥ 10.000 bản ghi khách sạn xử lý được, 0 thay đổi mã runtime |
| SC8 | Chất lượng bộ kiểm thử | 100% test case đạt; mutation score ≥ 70% |
| SC9 | Khả dụng trong pilot | ≥ 15 người dùng, ≥ 50 booking; điểm SUS trung bình ≥ 68 |

Cách đo từng chỉ số: Mục 5 trong [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](../research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

---

## 11. ĐỊNH HƯỚNG KHÓA LUẬN

Sau Đồ án 2, TADSS là môi trường thực nghiệm cho Khóa luận *"Thiết kế và đánh giá kiến trúc quyết định tin cậy thích ứng cho các nền tảng đặt chỗ dịch vụ"*: điểm tin cậy có suy giảm theo thời gian, mô hình dự báo rủi ro no-show/hủy, ràng buộc công bằng Equal Opportunity ($|TPR_A - TPR_B| \le \epsilon$), đánh giá off-policy trên dữ liệu audit, shadow mode trên traffic thật và mô phỏng. Chi tiết: [`RESEARCH_THESIS_DEFENSE.md`](../research/RESEARCH_THESIS_DEFENSE.md).

---

## 12. THAY ĐỔI SO VỚI ĐỀ CƯƠNG BAN ĐẦU

| Nội dung | Đề cương ban đầu | Bản điều chỉnh v2.0 | Lý do |
| :--- | :--- | :--- | :--- |
| Thời lượng | 16 tuần | 18 tuần (31/08/2026 – 03/01/2027) | Theo tiến độ thực tế và khối lượng kiểm chứng |
| Inner timeout | 150ms | 120ms | Để 3 lần thử + 2 delay nằm trong 500ms |
| Fallback | Cọc tĩnh 100% bằng `DefaultDecisionStrategy` | `FallbackDecisionStrategy` → `NEED_APPROVAL`; cọc 100% là chính sách của SmashCourt khi TADSS không khả dụng | Tách chính sách nghiệp vụ khỏi cơ chế an toàn |
| Kết quả quyết định | Trạng thái + gợi ý dạng văn bản | Trạng thái + **số tiền cọc**; Manager duyệt kèm mức cọc | Contract cũ không biểu diễn được nghiệp vụ cọc |
| Chọn strategy | Một strategy theo cấu hình lúc khởi động; định tuyến cao điểm ở SmashCourt | Định tuyến strategy theo từng request trong TADSS + shadow mode | Thể hiện đúng "adaptive", phục vụ Khóa luận |
| Audit | Một bảng, override cập nhật trực tiếp | Ba bảng append-only, DB chặn sửa; lưu đầu vào và kết quả thực tế | Bảo đảm bất biến thật và có dữ liệu cho Khóa luận |
| Cache | TTL 15 phút | Strategy tự khai báo cho phép cache và TTL | Tránh quyết định cũ với strategy phụ thuộc thời gian |
| Phân luồng VIP bỏ qua TADSS | Có (ở SmashCourt) | Bỏ; hạng thành viên được xử lý trong strategy | TADSS là nơi duy nhất quyết định mức cọc |
| Kiểm thử | Unit/integration (in-memory), k6 và OpenTelemetry là tùy chọn | Bắt buộc hạ tầng thật, chaos, tải, E2E, mutation, observability, replay, dataset công khai, pilot | Yêu cầu minh chứng bằng dữ liệu chạy thật |
| Kiểm chứng độc lập domain | Mock request phòng họp | Dataset đặt phòng khách sạn công khai (≥ 10.000 bản ghi) | Dữ liệu thật thay cho dữ liệu mock |
