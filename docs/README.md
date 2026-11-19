# Tài liệu TADSS Runtime

Repo có ba file `README.md`: [`README.md`](../README.md) ở gốc (điểm vào của repo), file này (mục lục và bảng kiểm kê tài liệu) và [`src/Shared.DecisionContractSDK/README.md`](../src/Shared.DecisionContractSDK/README.md) (đóng gói vào NuGet của SDK qua `PackageReadmeFile`). Toàn bộ tài liệu của Đồ án 2 (kiến trúc, contract v2, kế hoạch, phương pháp thực nghiệm, hướng dẫn chạy) nằm ở `docs/`, cùng `CLAUDE.md` ở gốc repo. Bảng kiểm kê dưới đây là **danh sách tài liệu duy nhất** của repo: mỗi tài liệu có đúng một dòng, cho biết tài liệu thuộc nhóm nào và ai là nơi sở hữu chủ đề của nó. Bản đồ tài liệu theo chủ đề: Mục III của [`docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md`](planning/THESIS_DEVELOPMENT_PLAYBOOK.md).

Tài liệu cấp workspace SmashCourt (kiến trúc hệ thống, use case, API của BE) nằm ở repo SmashCourt-BE, không thuộc bảng kiểm kê của repo này: [`SmashCourt-BE/docs/3.1-kien-truc-he-thong.md`](../../SmashCourt-BE/docs/3.1-kien-truc-he-thong.md), [`SmashCourt-BE/docs/use-cases/`](../../SmashCourt-BE/docs/use-cases), [`SmashCourt-BE/docs/api/`](../../SmashCourt-BE/docs/api).

## Quy tắc "tài liệu sở hữu thắng"

- Mỗi chủ đề (kiến trúc, contract, schema DB, chịu lỗi, danh mục kiểm thử, cấu hình, cách chạy...) có **một** tài liệu sở hữu, là một tài liệu nhóm `hiện hành` trong bảng kiểm kê; cột *Ghi chú* của dòng đó mô tả ngắn nội dung và phạm vi của file (không phải danh sách chủ đề). Các tài liệu khác chỉ tóm tắt và dẫn link tới nơi sở hữu, không chép lại thông số.
- Khi hai tài liệu mâu thuẫn, nội dung của tài liệu sở hữu thắng; tài liệu còn lại phải sửa theo.
- Khi mã, contract hoặc kế hoạch đổi, sửa tài liệu sở hữu trong cùng đợt thay đổi.

## Hai nhóm tài liệu

| Nhóm | Ý nghĩa | Yêu cầu |
| :--- | :--- | :--- |
| `hiện hành` | Sở hữu một chủ đề và phải khớp mã, contract, kế hoạch hiện tại. | Không mô tả như đang dùng những thứ đã gỡ khỏi repo (DOC-2); link và anchor phải đúng (DOC-3). |
| `lịch sử` | Ảnh chụp tại một thời điểm, không cập nhật theo mã (bản đã nộp, khảo sát tiền đề...). | File `.md` mở đầu bằng banner `> **Tài liệu lịch sử** — ảnh chụp ngày dd/mm/yyyy, không cập nhật theo mã. Nguồn hiện hành: [...](../...)`; cột *Ghi chú* nêu tài liệu thay thế. Được miễn DOC-2. |

Một file chỉ là `lịch sử` khi **toàn bộ** nội dung là ảnh chụp cũ. Nếu chỉ một đoạn nói về quá khứ (ví dụ mục lưu trữ Phase 4), file vẫn là `hiện hành` và đoạn đó được viết ở thì quá khứ.

## Bảng kiểm kê

Cột *File* ghi đường dẫn tính từ gốc repo. Bảng này do `tools/docs/check_docs.py` đọc nên giữ nguyên tiêu đề cột `File | Nhóm` và đúng hai giá trị `hiện hành` / `lịch sử` ở cột *Nhóm*.

| File | Nhóm | Ghi chú |
| :--- | :--- | :--- |
| `README.md` | hiện hành | Điểm vào của repo: tổng quan, tính năng, kiến trúc, bắt đầu nhanh, API, kiểm thử, triển khai, tài liệu |
| `docs/README.md` | hiện hành | Mục lục tài liệu: quy tắc tài liệu sở hữu, hai nhóm tài liệu, bảng kiểm kê (DOC-1) và cách kiểm |
| `docs/guides/DEVELOPER_GUIDE.md` | hiện hành | Hướng dẫn nhà phát triển: build/test/chạy local, API key và bảo mật, runtime thích ứng (định tuyến, shadow, cache, tiêm lỗi), observability và chế độ đánh giá, Docker + ngrok, NG-4 |
| `CLAUDE.md` | hiện hành | Hướng dẫn làm việc với repo (quy ước, quyết định D-xx đang áp dụng) |
| `docs/PROJECT_BLUEPRINT_AND_SYSTEM_OVERVIEW.md` | hiện hành | Tổng quan hệ thống và luồng nghiệp vụ, trạng thái dự án |
| `docs/PROJECT_SUMMARY.md` | hiện hành | File tổng duy nhất: Phase 6 (6A BE, 6B FE, 6C DevOps + hiệu năng, việc còn mở) và Phase 5 (gói, kiến trúc, hạ tầng, kiểm thử, cách trình bày); thay `PHASE5_SUMMARY.md` và các báo cáo gói; tài liệu sở hữu vẫn thắng |
| `docs/PROPOSAL_DO_AN_2.docx` | lịch sử | Bản đề cương đã nộp (file sửa lần cuối 07/09/2026), mô tả kiến trúc v1. Thay thế: `docs/planning/PROPOSAL_DO_AN_2.md` |
| `docs/23521683_23521709_Design_and_Development_of_a_Fault_Tolerant_Decision_Runtime_Engine_for_Service_Reservation_Systems.docx` | lịch sử | Bản đề cương đã nộp (file sửa lần cuối 05/10/2026), một số thông số chưa theo mã hiện tại. Thay thế: `docs/planning/PROPOSAL_DO_AN_2.md` |
| `docs/architecture/SOFTWARE_ARCHITECTURE_SPECIFICATION.md` | hiện hành | Cấu trúc solution, các project và quy tắc phụ thuộc |
| `docs/architecture/DECISION_PERSISTENCE_INVARIANTS.md` | hiện hành | Schema `tadss` và bất biến lưu trữ quyết định |
| `docs/architecture/RESILIENCE_AND_FAULT_TAXONOMY.md` | hiện hành | Pipeline chịu lỗi và phân loại lỗi (FI-xx) |
| `docs/architecture/ARCHITECTURE_DECISION_RECORDS.md` | hiện hành | Bảng tổng hợp ADR-001…013 |
| `docs/architecture/adr/ADR-001-why-strategy-pattern.md` | hiện hành | ADR-001, Strategy Pattern (Accepted) |
| `docs/architecture/adr/ADR-002-why-sdk-first-approach.md` | hiện hành | ADR-002, SDK-first (Accepted) |
| `docs/architecture/adr/ADR-003-why-polly-resilience.md` | hiện hành | ADR-003, Polly v8 (Accepted) |
| `docs/architecture/adr/ADR-004-why-rich-domain-model.md` | hiện hành | ADR-004, Rich Domain Model (Accepted) |
| `docs/architecture/adr/ADR-005-why-catboost-ml.md` | hiện hành | ADR-005, định hướng Khóa luận (ngoài phạm vi Đồ án 2) |
| `docs/contracts/INTEGRATION_SPEC.md` | hiện hành | Contract v2, luồng tích hợp BE ↔ TADSS và bộ JSON schema v2 theo endpoint |
| `docs/contracts/openapi-v2.yaml` | hiện hành | OpenAPI v2 sinh từ mã, khóa bởi `OpenApiDriftTests` |
| `docs/contracts/baseline_public_api.txt` | hiện hành | Baseline bề mặt public của SDK, khóa bởi `PublicApiSurfaceTests` |
| `docs/contracts/schemas/v2/decision-detail.schema.json` | hiện hành | JSON schema v2, chi tiết quyết định |
| `docs/contracts/schemas/v2/decision-request.schema.json` | hiện hành | JSON schema v2, request đánh giá |
| `docs/contracts/schemas/v2/decision-result.schema.json` | hiện hành | JSON schema v2, kết quả quyết định |
| `docs/contracts/schemas/v2/outcome-request.schema.json` | hiện hành | JSON schema v2, request ghi outcome |
| `docs/contracts/schemas/v2/outcome-response.schema.json` | hiện hành | JSON schema v2, response outcome |
| `docs/contracts/schemas/v2/override-request.schema.json` | hiện hành | JSON schema v2, request override |
| `docs/contracts/schemas/v2/override-response.schema.json` | hiện hành | JSON schema v2, response override |
| `docs/contracts/schemas/v2/pending-approvals.schema.json` | hiện hành | JSON schema v2, hàng chờ duyệt |
| `docs/database/schema_smashcourt.sql` | hiện hành | Bản `pg_dump` schema DB SmashCourt (đầu vào của `tools/evidence/sql/verify-export.sh`) |
| `docs/guides/CONFIGURATION_REFERENCE.md` | hiện hành | Bảng tham chiếu khóa cấu hình `Tadss:*` (kiểu, mặc định, ràng buộc), khớp mã bởi test DOC-4 |
| `docs/guides/RUNBOOK.md` | hiện hành | Hướng dẫn chạy và vận hành duy nhất (11/11/2026, task 1.42): dev 4 repo, bản deploy + xem DB, CD và mã thoát `cd.sh`, Grafana Cloud, compose từng repo, lượt đánh giá TADSS (sáu bước, đọc kết quả, CSV RP), báo cáo tổng kết chất lượng (`python perf/board.py update` ⇒ `perf/results/BAO_CAO_MOI_NHAT.html`) và dashboard "Kiểm thử", đo hiệu năng `perf`, bảng sự cố, dọn Docker; thay `EVALUATION_RUN_GUIDE.md`, `DOCKER_COMMANDS.md`, `TEST_DASHBOARD.md`, `PERF_ENVIRONMENT.md` |
| `docs/planning/MASTER_EXECUTION_PLAN.md` | hiện hành | Kế hoạch v2.1, nguồn duy nhất về lịch trình |
| `docs/planning/BACKLOG_SPRINTS.md` | hiện hành | Backlog theo phase và trạng thái task |
| `docs/planning/PROJECT_WBS_MAPPING.md` | hiện hành | WBS, phụ thuộc và rủi ro |
| `docs/planning/PROPOSAL_DO_AN_2.md` | hiện hành | Phạm vi chính thức của Đồ án 2 |
| `docs/planning/PROPOSAL_KHOA_LUAN.md` | hiện hành | Đề cương Khóa luận (bản thảo 17/11/2026, viết lại sau đợt rà soát bảy vai, ngoài phạm vi Đồ án 2), viết cho người đọc không chuyên: lớp kiểm soát rủi ro có bảo đảm gắn lên bất kỳ cách chấm rủi ro nào (kể cả bảng luật 2.2.0), giữ phần tiền bị hở mà tiền cọc kiểm soát được trong mục tiêu khi hành vi khách thay đổi và kết cục đến trễ; bổ đề tất định mở rộng Lekeufack 2024 và Feldman 2023 cho trễ theo từng đơn, cập nhật theo ngày, trọng số theo giá, trần cọc; đánh giá ở quy mô sân trên cửa sổ trượt theo giao thức chốt trước; phép thử trước ở `tools/research/`; cam kết KL1–KL7; Mục 12 liệt kê các quyết định chờ chốt |
| `tools/research/README.md` | hiện hành | Bốn phép thử trước của đề cương Khóa luận (Mục 1.5): chia cọc theo rủi ro, đường học, lịch sử khách và công bằng, dữ liệu trôi với kết cục đến trễ; nguồn, giấy phép và SHA-256 của hai bộ dữ liệu; cách chạy lại và `--check`; cách đo và giới hạn |
| `docs/planning/THESIS_DEVELOPMENT_PLAYBOOK.md` | hiện hành | Thuật ngữ chuẩn, ranh giới phạm vi, bản đồ tài liệu |
| `docs/report/ARCHITECTURE_DIAGRAMS.md` | hiện hành | Sơ đồ cho báo cáo (Mermaid): ngữ cảnh, container, trước/sau khi có TADSS, cấu trúc bên trong TADSS; chỉ tóm tắt, tài liệu sở hữu thắng |
| `docs/report/USE_CASES.md` | hiện hành | Sơ đồ và đặc tả use case mới/thay đổi khi SmashCourt dùng TADSS (UC-DEPOSIT-01…12) và use case của TADSS (UC-TADSS-01…08) |
| `docs/report/SEQUENCE_DIAGRAMS.md` | hiện hành | Sơ đồ tuần tự cho báo cáo: đặt sân có cọc, duyệt/từ chối, xử lý quyết định bên trong TADSS, job hết hạn và báo outcome |
| `docs/report/ACTIVITY_AND_STATE_DIAGRAMS.md` | hiện hành | Sơ đồ hoạt động (quy trình đặt cọc, duyệt cọc, pipeline quyết định) và sơ đồ trạng thái (booking, quyết định) cho báo cáo |
| `docs/report/PERFORMANCE_REPORT.md` | hiện hành | Chương đánh giá hiệu năng SmashCourt + TADSS (Epic 6C, task 2.29): SLO, môi trường, mô hình tải, kết quả SP-01…10, nút cổ chai, trước / sau, đánh đổi, hạn chế; bằng chứng ở `perf/evidence/` |
| `docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md` | hiện hành | Danh mục kiểm thử, KPI SC1–SC8, quy tắc DOC-1…DOC-4 |
| `docs/research/E2E_TEST_SPECIFICATION.md` | hiện hành | Đặc tả từng bước các kịch bản E2E-01…E2E-14 (T5.27): dữ liệu thử, bước, quan sát kỳ vọng, minh chứng, truy vết |
| `docs/research/DATASET_SCHEMA_SPECIFICATION.md` | hiện hành | Cấu trúc dataset và Evidence Pack |
| `docs/research/RESEARCH_THESIS_DEFENSE.md` | lịch sử | Định hướng Khóa luận ban đầu (khung ATDA), ảnh chụp 13/11/2026. Thay thế: `docs/planning/PROPOSAL_KHOA_LUAN.md` |
| `docs/research/RESEARCH_PROBLEM_BACKLOG.md` | lịch sử | Khảo sát tiền đề trước khi khóa phạm vi, ảnh chụp 27/09/2026. Thay thế: `docs/planning/PROPOSAL_DO_AN_2.md` |
| `src/Shared.DecisionContractSDK/README.md` | hiện hành | Giới thiệu SDK `Shared.DecisionContractSDK` (đóng gói vào NuGet) |

## Cách kiểm

Từ gốc repo:

```bash
python tools/docs/check_docs.py
```

Lệnh thoát 0 khi cả ba mã đạt, ngược lại liệt kê từng vấn đề kèm đường dẫn và số dòng:

- **DOC-1:** mọi file dưới `docs/` và mọi `README.md` / `CLAUDE.md` có đúng một dòng trong bảng trên; dòng không trỏ tới file không tồn tại; file `lịch sử` dạng `.md` mở đầu bằng banner.
- **DOC-2:** tài liệu `hiện hành` không nhắc những thứ đã gỡ khỏi repo như đang dùng. Dòng nói về quá khứ hoặc điều cấm (ví dụ "đã gỡ", "không còn", "cấm") không bị tính.
- **DOC-3:** link nội bộ và anchor trong mọi file `.md` đều trỏ tới nơi tồn tại (link ra ngoài repo và `http` được bỏ qua, chỉ đếm; các link tới tài liệu workspace ở repo SmashCourt-BE nói trên thuộc loại này).

Không có cơ chế bỏ qua từng dòng: khi báo lỗi, sửa câu cho đúng hiện trạng hoặc phân loại lại file, không chèn từ khóa để lọt kiểm tra. Khi thêm tài liệu mới (hoặc README mới), thêm luôn một dòng vào bảng kiểm kê. Bộ kiểm này chạy trong CI và ở bước (a) của evaluator (`tools/evaluation/evaluator/steps/tests_step.py` chạy nó trên bản sao `/work`), nên tài liệu không được link tới file bị loại khỏi bản sao (`.env*`, `appsettings.Development.json`, `evidence/`, `logs/`).
