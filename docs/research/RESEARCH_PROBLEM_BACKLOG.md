# [OUT-OF-SCOPE ĐỒ ÁN 2 — TÀI LIỆU KHẢO SÁT TIỀN ĐỀ] KHUNG NGHIÊN CỨU & KHẢO SÁT VẤN ĐỀ (ENTERPRISE AI OS RESEARCH BACKLOG)

> **📌 Khóa Phạm vi & Ghi chú Lịch sử (`[OUT-OF-SCOPE ĐỒ ÁN 2]`):** Tài liệu này lưu trữ kết quả khảo sát diện rộng (Brainstorming & Problem Exploration) ở giai đoạn tiền đề **trước khi khóa phạm vi đề tài**. Toàn bộ các cấu phần AI/ML, Agentic AI, MCP, GraphRAG, ClickHouse CDC trong tài liệu này **KHÔNG thuộc phạm vi triển khai của Đồ án 2** (Đồ án 2 chỉ xây dựng hạ tầng thực thi quyết định `TADSS Runtime Engine` không có AI/ML). Xem định nghĩa phạm vi chính thức tại [`PROPOSAL_DO_AN_2.md`](../planning/PROPOSAL_DO_AN_2.md) và [`EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md`](./EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).

*(Nội dung nguyên bản giai đoạn khảo sát tiền đề dưới đây được giữ lại để lưu vết lịch sử hình thành ý tưởng)*

Tài liệu này được phác thảo ở giai đoạn ý tưởng ban đầu nhằm khảo sát các bài toán cho **Đồ án 2** và **Khóa luận tốt nghiệp**, lấy **Enterprise AI OS (Hệ điều hành trí tuệ nhân tạo doanh nghiệp)** làm chủ đề khảo sát. 

Chúng ta khảo sát ý tưởng về **Tầng AI độc lập (AI Layer)** có khả năng cắm/rút (pluggable) vào bất kỳ phân hệ nghiệp vụ nào (Booking, CRM, Clinic, Spa, HR...), giải quyết các bài toán thông qua mô hình: **Pain Point ➔ Research Question ➔ Experiment ➔ Evaluation.**

---

## I. MÔ HÌNH KIẾN TRÚC HỆ THỐNG: ENTERPRISE AI OS

Kiến trúc tách biệt hoàn toàn giữa các phân hệ nghiệp vụ truyền thống (CRUD) và Tầng xử lý/Quyết định AI (AI Layer). Khi thay đổi nghiệp vụ (từ đặt sân cầu lông sang quản lý phòng gym, phòng khám), Tầng AI vẫn giữ nguyên cấu trúc cốt lõi.

```
┌────────────────────────────────────────────────────────────────────────┐
│                      TẦNG NGHIỆP VỤ (Business Modules)                 │
│   ┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌──────────┐   │
│   │    Booking    │ │      CRM      │ │   Inventory   │ │   ...    │   │
│   └───────────────┘ └───────────────┘ └───────────────┘ └──────────┘   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ (Giao tiếp qua MCP / API Gateway)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   HỆ ĐIỀU HÀNH ENTERPRISE AI OS (AI Layer)             │
│                                                                        │
│   ┌───────────────────────────┐           ┌────────────────────────┐   │
│   │    Agent Orchestrator     │ ◄───────► │   Knowledge Service    │   │
│   │ (Lập kế hoạch & Điều phối)│           │  (GraphRAG / Vector)   │   │
│   └─────────────┬─────────────┘           └───────────┬────────────┘   │
│                 │                                     │                │
│                 ▼                                     ▼                │
│   ┌───────────────────────────┐           ┌────────────────────────┐   │
│   │     Prediction Engine     │           │   Decision Engine      │   │
│   │    (ML Demand/No-show)    │           │ (Dynamic Pricing/Repo) │   │
│   └─────────────┬─────────────┘           └───────────┬────────────┘   │
│                 │                                     │                │
│                 ▼                                     ▼                │
│   ┌───────────────────────────┐           ┌────────────────────────┐   │
│   │    Evaluation Service     │           │ Explainability Service │   │
│   │ (LLM-as-a-judge / RAGAS)  │           │   (Giải thích mô hình) │   │
│   └───────────────────────────┘           └────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────┘
```

---

## II. QUY TẮC ĐÁNH GIÁ ĐỀ TÀI NGHIÊN CỨU (RESEARCH PLAYBOOK)

Mỗi bài toán hay ý tưởng nghiên cứu khi đưa vào Backlog bắt buộc phải đi qua bộ lọc 8 câu hỏi kiểm định sau:

1.  **Pain Point là gì?** (Mô tả vấn đề thực tế cụ thể).
2.  **Ai đang gặp vấn đề?** (Đối tượng chịu ảnh hưởng trong doanh nghiệp).
3.  **Vấn đề có phổ biến không?** (Tính thực tiễn và quy mô của bài toán).
4.  **Tại sao giải pháp hiện tại chưa tốt?** (Chỉ ra nhược điểm của cách làm truyền thống).
5.  **Phần nghiên cứu khoa học của đề tài (Research Contribution) là gì?** (Không làm lại cái cũ, chỉ ra phần cải tiến giải thuật/mô hình).
6.  **Công nghệ là phương tiện hay mục tiêu?** (Bắt buộc phải là phương tiện để giải quyết bài toán).
7.  **Đo lường hiệu quả bằng chỉ số cụ thể nào?** (Chỉ số kỹ thuật và chỉ số doanh nghiệp).
8.  **Có thể phát triển tiếp thành khóa luận tốt nghiệp không?** (Độ rộng và độ sâu học thuật).

---

## III. PHÂN LOẠI CÁC BÀI TOÁN (PROBLEM CLASSIFICATION & EVALUATION BACKLOG)

Chúng ta phân loại bài toán thành 3 cấp độ để xác định rõ phần nào là **kỹ thuật**, phần nào là **nghiệp vụ** và phần nào là **nghiên cứu khoa học**.

### Cấp độ 1: Engineering Problems (Bài toán Kỹ thuật & Kiến trúc)
*Đây là nền tảng hạ tầng, thể hiện năng lực lập trình và thiết kế hệ thống của kỹ sư phần mềm.*

*   **EP-01: Race Conditions trong đặt tài nguyên thời gian thực**
    *   *Hiện trạng:* Dùng database lock làm nghẽn hệ thống khi lượng truy cập lớn.
    *   *Nhược điểm:* Latency tăng cao, dễ deadlock.
    *   *Giải pháp:* Tối ưu hóa khóa phân tán **Distributed Lock (Redis Redlock)** kết hợp cơ chế Optimistic Concurrency Control (OCC) và kiến trúc CQRS.
    *   *Đo lường (Evaluation):* Throughput (RPS - Requests Per Second), Average Response Time (Latency - ms), Tỷ lệ lỗi (Error Rate %).
*   **EP-02: Đồng bộ dữ liệu phân tích thời gian thực**
    *   *Hiện trạng:* Chạy các câu lệnh JOIN/GROUP BY nặng trực tiếp trên database giao dịch chính (OLTP).
    *   *Nhược điểm:* Làm sập hệ thống đặt sân/đặt phòng của khách hàng.
    *   *Giải pháp:* Triển khai **CDC (Change Data Capture)** qua Debezium/Kafka để đồng bộ sang OLAP Database (ClickHouse).
    *   *Đo lường (Evaluation):* Thời gian đồng bộ dữ liệu (Sync Delay - ms), CPU/RAM Load của DB chính khi chạy báo cáo.

### Cấp độ 2: Business Problems (Bài toán Nghiệp vụ Doanh nghiệp)
*Đây là các nghiệp vụ thực tế phát sinh trong quá trình vận hành dòng tiền và dịch vụ.*

*   **BP-01: Rủi ro bùng hẹn (No-show Risk)**
    *   *Hiện trạng:* Thu tiền cọc cố định cao gây cản trở khách hàng mới, cọc thấp không có tác dụng răn đe.
    *   *Nhược điểm:* Thất thoát doanh thu, lãng phí khung giờ hoạt động của sân/phòng dịch vụ.
    *   *Giải pháp:* Thiết lập chính sách đặt cọc động dựa trên dự báo xác suất no-show của từng khách hàng.
    *   *Đo lường (Evaluation):* Tỷ lệ No-show (%), Doanh thu giữ lại từ việc phạt bùng lịch (VND), Tỷ lệ đặt sân thành công của khách hàng mới.
*   **BP-02: Phân ca trực nhân sự**
    *   *Hiện trạng:* Phân ca trực cố định qua Excel.
    *   *Nhược điểm:* Lãng phí nhân sự vào ca vắng, thiếu nhân sự hỗ trợ vào ca đông đột xuất.
    *   *Giải pháp:* Dự báo lưu lượng khách hàng và lập lịch ca trực tự động theo ca gãy.
    *   *Đo lường (Evaluation):* Chi phí nhân sự hoạt động hàng tháng (VND), Tỷ lệ thời gian nhàn rỗi của nhân viên (%), Thời gian chờ trung bình của khách hàng khi check-in (phút).

### Cấp độ 3: Research Problems (Bài toán Nghiên cứu Khoa học - Trọng tâm Khóa luận)
*Đây là các đề tài có chiều sâu học thuật, yêu cầu khảo sát, đề xuất giải thuật, thử nghiệm và đánh giá khoa học.*

#### 🔬 RP-01: Hệ thống hỗ trợ ra quyết định thông minh (Enterprise AI Decision Platform / Copilot)
*   **Research Question**: *Làm thế nào để xây dựng một AI Agent có khả năng lập kế hoạch (planning) và tích hợp các công cụ nghiệp vụ doanh nghiệp nhằm hỗ trợ nhà quản lý ra quyết định vận hành tối ưu?*
*   **Phương pháp đề xuất**: Xây dựng Agent điều phối (Agent Orchestrator) ứng dụng giao thức **Model Context Protocol (MCP)** để kết nối trực tiếp mô hình ngôn ngữ lớn (LLM) vào các API dữ liệu nội bộ. Tích hợp cơ chế **Human-in-the-loop (HITL)** để AI chỉ đề xuất và con người phê duyệt.
*   **Đánh giá (Evaluation)**:
    *   *Độ chính xác lập kế hoạch (Planning Accuracy %):* AI gọi đúng các API/Tool cần thiết để trả lời câu hỏi của Manager.
    *   *Thời gian ra quyết định (Decision Time - phút):* So sánh thời gian từ lúc phát hiện vấn đề đến lúc Manager phê duyệt giữa phương pháp thủ công và dùng AI Copilot.
    *   *Tỷ lệ phê duyệt đề xuất (Approval Rate %):* Số lượng khuyến nghị của AI được Manager chấp nhận thực tế.

#### 🔬 RP-02: Thuật toán định giá động tối ưu hóa công suất và doanh thu (Dynamic Pricing Optimization)
*   **Research Question**: *Làm thế nào để cân bằng giữa doanh thu ngắn hạn và mức độ hài lòng dài hạn của khách hàng thông qua giải thuật định giá động?*
*   **Phương pháp đề xuất**: Khảo sát và so sánh hiệu quả giữa các thuật toán **Học tăng cường (Deep Q-Network / PPO)** và giải thuật heuristic truyền thống. Nghiên cứu tích hợp yếu tố ràng buộc "Độ nhạy cảm về giá của khách hàng (Price Elasticity of Demand)" vào mô hình tối ưu hóa.
*   **Đánh giá (Evaluation)**:
    *   *Tổng doanh thu tăng trưởng (Revenue Growth %):* So sánh doanh thu thu được từ mô phỏng định giá động so với định giá cố định.
    *   *Tỷ lệ lấp đầy (Occupancy Rate %):* Mức độ phân bổ đều của khách hàng trên các khung giờ thấp điểm.
    *   *Chỉ số hài lòng khách hàng (Retention Rate / Customer Satisfaction Index %):* Đo lường xem định giá động có làm khách hàng rời bỏ hệ thống hay không.

#### 🔬 RP-03: Tri thức vận hành thông tin đa chiều (Knowledge RAG & Reasoning)
*   **Research Question**: *Làm thế nào để giải quyết vấn đề ảo tưởng (Hallucination) và cải thiện khả năng suy luận nguyên nhân (Root Cause Reasoning) khi AI phân tích dữ liệu hoạt động của doanh nghiệp?*
*   **Phương pháp đề xuất**: Thay thế Vector RAG truyền thống bằng **GraphRAG (kết hợp cơ sở dữ liệu đồ thị tri thức)** để nắm bắt mối quan hệ phi tuyến tính giữa các thực thể (Khách hàng, Chi nhánh, Nhân sự, Thời tiết, Khuyến mãi). Sử dụng **Hybrid Search** và hệ thống chấm điểm **RAGAS**.
*   **Đánh giá (Evaluation)**:
    *   *Tỷ lệ ảo tưởng (Hallucination Rate %):* Số câu trả lời của AI chứa thông tin tự bịa ra.
    *   *Độ chính xác trích dẫn nguồn (Citation Accuracy %):* AI chỉ ra đúng nguồn gốc dữ liệu phục vụ phân tích.
    *   *Độ chính xác suy luận nguyên nhân (Reasoning Accuracy %):* So sánh kết luận của AI về lý do sụt giảm doanh thu/khách hàng với kết luận thực tế của chuyên gia.

---

## IV. BẢN ĐỒ XU HƯỚNG CÔNG NGHỆ 2026 TRONG ENTERPRISE AI

Để đảm bảo đề tài nghiên cứu tiệm cận với công nghiệp thực tế năm 2026, chúng ta lập bản đồ đối chiếu các công nghệ cốt lõi:

| Xu hướng công nghệ (2026) | Khái niệm & Ứng dụng trong Enterprise | Khả năng tích hợp vào Đồ án 2 / Khóa luận tốt nghiệp |
| :--- | :--- | :--- |
| **Agentic AI & Planning** | Hệ thống AI không chỉ trả lời câu hỏi mà tự động chia nhỏ mục tiêu phức tạp thành các bước nhỏ, gọi công cụ (APIs), tự sửa lỗi (Self-reflection) và hoàn thành tác vụ. | • Tích hợp vào **Agent Orchestrator** để xử lý các yêu cầu điều phối từ Manager (Ví dụ: Tự động khóa sân, xuất hóa đơn hoàn tiền, gửi email cảnh báo). |
| **Model Context Protocol (MCP)** | Giao thức mở do Anthropic phát triển để chuẩn hóa cách LLM kết nối an toàn với tài nguyên cục bộ (file, database, APIs). | • Sử dụng để xây dựng một cổng giao tiếp duy nhất giữa Tầng AI (AI Layer) và Tầng Nghiệp vụ (Booking, CRM, Finance) giúp LLM tự động đọc schema database và gọi API. |
| **GraphRAG (Knowledge Graph RAG)** | Kết hợp Retrieval-Augmented Generation với đồ thị tri thức (Neo4j / AgensGraph) để cung cấp khả năng suy luận ngữ nghĩa toàn diện. | • Ứng dụng giải quyết bài toán: Phân tích nguyên nhân sụt giảm doanh thu của chi nhánh dựa trên mối quan hệ phức tạp giữa khách hàng - khuyến mãi - đối thủ - thời tiết. |
| **AI Evaluation Frameworks** | Các framework đánh giá tự động (như Ragas, Phoenix, TruLens) sử dụng các chỉ số: Faithfulness, Answer Relevance, Context Recall để đo lường chất lượng hệ thống AI. | • Xây dựng **Evaluation Service** độc lập để đánh giá trực tiếp chất lượng các câu trả lời của AI Chatbot/AI Copilot trước khi đưa ra màn hình cho người dùng. |
| **Human-in-the-loop (HITL) & Guardrails** | Đặt con người làm lớp kiểm duyệt cuối cùng cho các quyết định của AI, kết hợp các thư viện kiểm soát nội dung (NeMo Guardrails) để ngăn ngừa hành vi sai lệch của LLM. | • Áp dụng cho phân hệ **Dynamic Pricing** và **Staff Scheduling** — AI chỉ có vai trò đề xuất kế hoạch tối ưu, Manager là người nhấn nút "Approve" để hệ thống thực thi. |

---

## V. ĐỀ XUẤT LỘ TRÌNH BRAINSTORMING TIẾP THEO

Chúng ta sẽ tiếp tục thảo luận dựa trên khung tư duy này:
1.  **Lựa chọn 1-2 chủ đề nghiên cứu sâu (Research Themes)** từ phần III mà bạn cảm thấy có nhiều hứng thú học thuật nhất và phù hợp với năng lực kỹ thuật của mình.
2.  **Thiết kế thử nghiệm (Experiment Design):** Với chủ đề được chọn, chúng ta sẽ mô tả chi tiết: *Chúng ta sẽ thu thập/sinh dữ liệu (dataset) như thế nào để train/test mô hình? Chúng ta sẽ chạy thử nghiệm trên nền tảng giả lập nào?*

*Tài liệu này đã được lưu vào hệ thống để tiếp tục hoàn thiện.*
