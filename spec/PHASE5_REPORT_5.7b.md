# Báo cáo gói 5.7b — Kinh tế mô phỏng chính sách cọc (PT-03) và shadow trong đánh giá (PT-04)

> **Trạng thái 15/10/2026:** phần mã và tài liệu **xong**, chưa commit (người dùng commit). **Chưa chạy lượt bản nhanh** nên chưa có số liệu PT-03/PT-04; Task 10 của plan 5.7b dừng ở bước bàn giao.

## 1. Đã làm (Task 1–9 của `PHASE5_PLAN_5.7b_TASKS.md`)

| Task | Kết quả | Nơi |
| :--- | :--- | :--- |
| 1 | Ảnh chụp chính sách hủy SmashCourt (24 giờ 100 %, 2 giờ 50 %, 0 giờ 0 %) + chọn mốc giống `CalculateRefundPercentAsync` | `tools/evidence/cancel_policy.py`, `config/smashcourt_cancel_policies.json` |
| 2 | Công thức `φ` hai biến thể hoàn tiền, `AS_IS_BE`, AUC có ties, `Δr`, percentile; đầu vào xấu → `ValueError` | `tools/evidence/policy_metrics.py` |
| 3 | `reservation_status(_date)` vào meta; D5 thêm 7 cột | `tools/evidence/hotel_adapter.py` |
| 4 | Ghép `PRIMARY`/`SHADOW` theo `correlation_id`, bốn trạng thái, kiểm toàn vẹn | `tools/evidence/shadow_pairing.py`, `dataset_analysis.py` |
| 5 | Mô phỏng, bootstrap ghép cặp, xếp hạng (cả hai quy ước `NEED_APPROVAL`), so sánh nhóm ghép/không ghép | `tools/evidence/policy_economics.py` |
| 6 | Bật shadow cho `eval-tadss-portability`; chụp counter, xả hàng đợi, nhãn `RECONCILED`/`UNKNOWN`/`FAILED` | `docker-compose.yml`, `tools/evaluation/evaluator/shadow_drain.py`, `dataset_step.py` |
| 7 | Bước (f): D5 kèm shadow, `raw/d5/policy_economics.json`, sao ảnh chụp vào `config/`, độ phủ PT-03/PT-04 | `evidence_step.py`, `dataset_analysis.py` |
| 8 | Mục PT-03/PT-04 trong `report.html`, biểu đồ phân tán có đoạn low→high | `tools/evidence/report/` |
| 9 | Methodology, DATASET_SCHEMA, guide, M-10 thu hẹp (Q-2) | `docs/`, `spec/PHASE5_PLAN_5.7_M5_FREEZE.md` |

Không đổi `src/`, contract 2.0.0, migration, SmashCourt-BE.

## 2. Kiểm tra cục bộ (15/10/2026, từng bộ riêng)

- `pytest tools/evidence`: 354 đạt. `pytest tools/evaluation`: 257 đạt + 3 bỏ qua (có sẵn). `pytest tools/docs`: 34 đạt.
- `check_docs.py`: DOC-1, DOC-2, DOC-3 đạt. `docker compose config -q`: im lặng. Catalog traceability (C#, 3 test): đạt khi thêm PT-03/PT-04. `git status src docs/contracts`: không đổi.
- Thời gian bootstrap đo: 26,5 s cho 10.000 dòng × 1.000 vòng (ngoại suy ~5 phút ở bản đầy đủ).
- Mỗi task qua review spec + chất lượng; các vòng sửa: Task 2 (1), 5 (2), 6 (1), 8 (2), cộng một đợt sửa cuối sau review toàn bộ; review cuối của đợt sửa: không còn mục mở.

## 3. Chưa làm / cần người dùng

1. Commit (gợi ý ở cuối), rồi chạy lượt bản nhanh: `docker compose up -d --build`; gửi `docker compose logs --tail 60 evaluator` và `docker compose ps -a`.
2. Phân tích lượt bản nhanh (cổng quyết định, spec 6): tỷ lệ `PAIRED` ≥ 99 %; nhãn đối soát `RECONCILED` và 0 vi phạm; SC1…SC8 không đổi kết luận; thời gian bước (e) (ghi vào methodology Mục 6 trước lượt đầy đủ); **xác nhận tên metric Prometheus** `tadss_shadow_*` (hằng `*_QUERY` ở `shadow_drain.py`; tên sai chỉ cho nhãn `UNKNOWN`).
3. Quyết định còn mở: Q-6 (gốc tính hoàn tiền khi chỉ trả cọc, Phase 6–7); B-2 (nghi lệch +7 giờ, trace sau); M-10 (PT-03 có cần `compared > 0` mới tính đạt không); `.gitattributes` `eol=lf` cho `tools/evidence/config/*.json` (SHA-256 ảnh chụp phụ thuộc kết thúc dòng).
4. **File đặc tả riêng đã gỡ** (15/10/2026, theo yêu cầu người dùng; bản trên đĩa vốn đã bị ghi đè về rev 3). Nội dung thiết kế rev 3.3 nằm trong `PHASE5_PLAN_5.7b_TASKS.md` và `docs/`.
5. Giấy phép (U-2) trước khi đưa D5, `pt_summary.json`, `policy_economics.json`, mục PT của báo cáo vào Evidence Pack chính thức.

## 4. Gợi ý commit (người dùng chạy; không stage `spec/`)

```text
feat(evaluation): simulate deposit policy economics and shadow mode on portability data

- PT-03: scenario-based economics on Hotel Booking (SmashCourt cancel policy, two refund variants, day-resolution bounds, both NEED_APPROVAL conventions, paired bootstrap)
- PT-04: shadow MockAlternative on the PT instance, per-run counter deltas, correlation_id pairing and integrity checks
- D5 gains reservation status and shadow columns; report section and trade-off chart
- docs: methodology PT-03/PT-04, dataset schema, run guide, phase 5 summary; licence gate for aggregated PT artifacts

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
```
