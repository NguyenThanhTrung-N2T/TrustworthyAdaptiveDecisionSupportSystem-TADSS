# ĐỊNH HƯỚNG KHÓA LUẬN: KIẾN TRÚC QUYẾT ĐỊNH TIN CẬY THÍCH ỨNG (ATDA) `[NGOÀI PHẠM VI ĐỒ ÁN 2]`

> Tài liệu định hướng cho Khóa luận tốt nghiệp (Lớp 3 — strategy TADS và khung ATDA). Không thuộc kế hoạch 18 tuần của Đồ án 2 ([`MASTER_EXECUTION_PLAN.md`](../planning/MASTER_EXECUTION_PLAN.md)).

- **Tên đề tài dự kiến:** Thiết kế và đánh giá kiến trúc quyết định tin cậy thích ứng cho các nền tảng đặt chỗ dịch vụ (*Design and Evaluation of an Adaptive Trust Decision Architecture for Service Reservation Platforms*).
- **Thời lượng dự kiến:** 12 tuần sau khi bảo vệ Đồ án 2.

---

## I. KẾ THỪA TỪ ĐỒ ÁN 2

| Kế thừa | Vai trò trong Khóa luận |
| :--- | :--- |
| TADSS runtime (pipeline chịu lỗi, cách ly strategy, audit fail-closed) | Môi trường chạy strategy TADS an toàn; strategy học máy lỗi/chậm vẫn có fallback |
| Contract v2 (lịch sử khách, nhiều sân, giờ chi nhánh) | Đặc trưng đầu vào cho mô hình mà không cần đổi contract |
| Audit bất biến: snapshot request + quyết định + override + outcome | Dataset có nhãn kết quả thực tế; override của Manager là tín hiệu phản hồi của con người |
| Định tuyến theo request + **shadow mode** | So sánh TADS với baseline trên cùng traffic mà không ảnh hưởng khách |
| Evidence Pack, notebook, k6, Toxiproxy, OpenTelemetry | Tái sử dụng để đo chi phí độ trễ và độ bền của strategy học máy |

---

## II. CÂU HỎI NGHIÊN CỨU

- **RQ chính:** Strategy quyết định thích ứng (TADS) chạy trên TADSS có cải thiện *Expected Business Utility* so với chính sách cọc tĩnh (baseline của Đồ án 2) mà vẫn thỏa ràng buộc công bằng và ràng buộc độ bền hay không?
- **Sub-RQ1 — Trust & Fairness:** Mô hình hóa điểm tin cậy thích ứng với suy giảm theo thời gian ($e^{-\lambda \Delta t}$) và mô hình dự báo rủi ro no-show/hủy như thế nào để đề xuất mức cọc, đồng thời thỏa **Equal Opportunity**: $|TPR_{A} - TPR_{B}| \le \epsilon$ giữa các nhóm khách (ví dụ khách mới và khách thân thiết)?
- **Sub-RQ2 — Đánh giá an toàn:** Có thể đánh giá một chính sách cọc mới trước khi áp dụng thật bằng cách kết hợp đánh giá off-policy trên dữ liệu audit và shadow mode trên traffic thật hay không?
- **Ràng buộc độ bền (kế thừa Đồ án 2):** strategy TADS phải giữ SLA của pipeline (SC1, SC2) và không làm giảm tính liên tục của luồng quyết định.

**Hàm mục tiêu tham khảo:**
$$\max \mathbb{E}[Utility] = \alpha \cdot \frac{Revenue}{Rev_{max}} + \beta \cdot Retention - \gamma \cdot Risk - \delta \cdot \frac{Complaint}{Complaint_{max}}$$

---

## III. NGUỒN DỮ LIỆU

| Nguồn | Nội dung | Ghi chú |
| :--- | :--- | :--- |
| Audit TADSS từ pilot/vận hành thật (bộ D1) | Đặc trưng request, quyết định, override, outcome thật | Nhãn hành vi thật; cỡ mẫu phụ thuộc thời gian vận hành |
| Hotel Booking Demand (Antonio, Almeida & Nunes, *Data in Brief*, 2019) | ~119 nghìn đơn đặt phòng, nhãn `is_canceled` | Proxy cho bài toán hủy; đã có adapter từ Đồ án 2 |
| Medical Appointment No-Shows (Kaggle) | ~110 nghìn lịch hẹn, nhãn no-show | Proxy cho bài toán no-show |
| Mô phỏng (agent-based) | Hành vi đặt/hủy của các nhóm khách | Hiệu chỉnh tham số theo các nguồn trên; dùng cho ablation |

Dữ liệu 215 booking hiện có trong SmashCourt là dữ liệu môi trường phát triển (nhãn do job tự gán) nên **không** dùng làm nhãn huấn luyện.

---

## IV. PHƯƠNG PHÁP

1. **Mô hình rủi ro:** baseline hồi quy logistic; so sánh CatBoost / LightGBM (ADR-005); đánh giá AUC, độ hiệu chỉnh xác suất (calibration), chỉ số công bằng theo nhóm.
2. **Điểm tin cậy thích ứng:** tổng hợp lịch sử đúng hẹn/no-show/hủy có trọng số suy giảm theo thời gian.
3. **Ánh xạ chính sách:** rủi ro + điểm tin cậy → mức cọc hoặc `NEED_APPROVAL`; hiện thực thành `AdaptiveTrustDecisionStrategy` theo contract v2.
4. **Đánh giá off-policy:** ước lượng giá trị chính sách mới trên dữ liệu audit (ví dụ inverse propensity scoring), nêu rõ giả định.
5. **Shadow mode:** chạy TADS song song với baseline trên traffic thật, so sánh phân bố quyết định và độ trễ.
6. **Ablation trong mô phỏng:** Baseline → + mô hình rủi ro → + ràng buộc công bằng → + human-in-the-loop → + fallback.

---

## V. LỘ TRÌNH 12 TUẦN

| Tuần | Nội dung |
| :--- | :--- |
| 1–3 | Tổng quan tài liệu; chuẩn bị dữ liệu (D1, hai dataset công khai); thiết kế đặc trưng |
| 4–6 | Mô hình rủi ro, điểm tin cậy, ràng buộc công bằng; đánh giá offline |
| 7–8 | Hiện thực `AdaptiveTrustDecisionStrategy`; đo chi phí độ trễ trên TADSS; shadow mode |
| 9–10 | Đánh giá off-policy; mô phỏng và ablation |
| 11–12 | Viết luận văn, chuẩn bị bảo vệ |

Ngưỡng mục tiêu cho các chỉ số mô hình được chốt sau khi khảo sát dữ liệu ở tuần 1–3.
