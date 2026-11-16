# Phép thử trước của đề cương khóa luận

Thư mục này giữ chương trình và kết quả của các phép thử trước ở Mục 1.5 [đề cương khóa luận](../../docs/planning/PROPOSAL_KHOA_LUAN.md). Mọi con số của Mục 1.5 lấy từ bốn chương trình dưới đây, kết quả lưu trong `results/`.

Các phép thử trả lời những câu hỏi hẹp, trên dữ liệu công khai và với giả định khách cư xử như cũ khi mức cọc thay đổi:

- Chia tiền cọc theo rủi ro có làm phần bị hở nhỏ hơn một mức chung và các bảng luật không, và mô hình hơn một bảng tra đơn giản bao nhiêu.
- Lịch sử của khách đóng góp thêm bao nhiêu, và cách chia cọc có làm một nhóm khách chịu thiệt không.
- Một mức rủi ro chọn trên dữ liệu cũ có còn giữ trên dữ liệu sau không, kể cả khi hành vi khách xấu đi thật theo mùa, và tự điều chỉnh theo kết cục đến trễ giúp tới đâu.
- Cần bao nhiêu đơn học thì mô hình mới hơn bảng tra.

Chúng **không** cho biết một sân cầu lông sẽ bớt mất bao nhiêu tiền, và không đại diện cho thị trường sân cầu lông. Hiện không có dữ liệu sân thật nào có được.

## Các tệp

| Tệp | Là gì | Có trong git |
| :--- | :--- | :--- |
| `data/datasets/hotels.csv`, `data/datasets/medical_noshow.csv` (ở gốc repo) | Dữ liệu gốc, không sửa | Không. Tải theo địa chỉ và mã SHA-256 ghim sẵn |
| `tools/research/deposit_pretest.py` | Phép thử trên dữ liệu khách sạn: các chiến lược, nhãn chặt, độ nhạy, khoảng tin cậy, giữ mức rủi ro trên dữ liệu sau | Có |
| `tools/research/history_pretest.py` | Phép thử trên dữ liệu lịch khám: đóng góp của lịch sử khách, công bằng | Có |
| `tools/research/drift_pretest.py` | Dữ liệu trôi bất lợi thật theo mùa (khách sạn): giữ cố định so với tự điều chỉnh khi kết cục về trễ | Có |
| `tools/research/curve_pretest.py` | Đường học: bảng tra so với mô hình ở nhiều cỡ dữ liệu học, trên cả hai bộ | Có |
| `tools/research/floor_pretest.py` | Mục 2.4: phần sàn của tổn thất theo chính sách hoàn tiền của SmashCourt (nhiều cách quy đổi giờ sang ngày, nhiều giả định bán lại), và so hai dạng ràng buộc (trung bình, từng đơn) | Có |
| `tools/research/runtime_facts.py` | Các con số về Đồ án 2 dẫn ở Mục 1.4 của đề cương (lùi về mặc định, bộ nhớ đệm, số quyết định), đếm trên bộ minh chứng `evidence/EV-20261017-EVAL-full-r1` | Có |
| `evidence/EV-20261017-EVAL-full-r1/` | Bộ minh chứng của lượt đánh giá đầy đủ của Đồ án 2; `runtime_facts.py` kiểm SHA-256 hai tệp bản ghi quyết định theo `manifest.json` của nó | Không (thư mục `evidence/` nặng, nằm ngoài git); sinh lại bằng `bash tools/evaluation/run.sh full`, khoảng 3 giờ 25 phút |
| `tools/research/results/*.json` | Kết quả của lần chạy dùng trong đề cương, kèm nguồn dữ liệu và mọi tham số | Có |
| `tools/research/tests/` | Test của các hàm tính (dữ liệu nhỏ dựng tay, không cần tải) | Có |

Các chương trình không tạo tệp dữ liệu trung gian: mọi bước xử lý chạy trong bộ nhớ trên tệp gốc.

## Nguồn dữ liệu

| Mục | Khách sạn | Lịch khám |
| :--- | :--- | :--- |
| Bộ dữ liệu | Hotel Booking Demand: António, N., de Almeida, A., Nunes, L. (2019). *Data in Brief* 22, 41–49 | Medical Appointment No Shows: Hoppen, J., Aquarela Analytics (2016), [Kaggle](https://www.kaggle.com/datasets/joniarroba/noshowappointments) |
| Bản dùng | Bản TidyTuesday ngày 11/02/2020, ghim commit `d75aaa0d` | Bản sao trên GitHub `samhadd-ad/Medical-Appointment-noShow`, ghim commit `d2a909d1`. Kaggle cần đăng nhập nên không tải tự động được |
| Địa chỉ tải | `https://raw.githubusercontent.com/rfordatascience/tidytuesday/d75aaa0d31596ad6487ae0db20138067d301e031/data/2020/2020-02-11/hotels.csv` | `https://raw.githubusercontent.com/samhadd-ad/Medical-Appointment-noShow/d2a909d14903b8f2bad73582ec5827c4a5f996d6/KaggleV2-May-2016.csv` |
| SHA-256 | `7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06` | `9132d3e7d0246617df9041d3764f20ad6f08e7b0d9f0997fa254fc5e52eda27d` |
| Số dòng | 119.390 | 110.527 |
| Kiểm bản dùng | Mã SHA-256 trùng với bản đã dùng ở gói 5.6 của Đồ án 2 | Số dòng, số người (62.299) và số lượt không đến (22.319) khớp số đã công bố. Một bản sao khác (`prabhursingh/No-Show`) bị loại vì mã bệnh nhân bị Excel làm tròn |
| Giấy phép | CC BY 4.0, đã xác nhận 17/10/2026 ở gói 5.6 của Đồ án 2 | CC BY-NC-SA 4.0: được dùng cho nghiên cứu, không dùng thương mại, phải ghi nguồn. Bản sao trên GitHub không đổi giấy phép |
| Ngày tải | 13/11/2026; tải lại và kiểm khớp mã ngày 14/11/2026 | 15/11/2026 |

Repo không commit tệp gốc, theo quy ước đã có với bộ khách sạn (`tools/evidence/hotel_adapter.py`): tải theo địa chỉ ghim, kiểm SHA-256, lệch thì dừng.

## Chạy lại từ đầu

Cần Python 3.11 trở lên, không cần thư viện ngoài. Lần chạy trong đề cương dùng Python 3.13.15 trên Windows. Mỗi chương trình mất khoảng 5 phút, riêng `curve_pretest.py` khoảng 20 phút.

```bash
python -I tools/research/deposit_pretest.py          # tải dữ liệu nếu thiếu, chạy, ghi results/deposit_pretest.json
python -I tools/research/history_pretest.py          # như trên, ghi results/history_pretest.json
python -I tools/research/drift_pretest.py            # ghi results/drift_pretest.json
python -I tools/research/floor_pretest.py            # ghi results/floor_pretest.json
python -I tools/research/curve_pretest.py            # ghi results/curve_pretest.json
python -I tools/research/deposit_pretest.py --check  # chạy lại và so với tệp kết quả đã commit; khớp thì thoát 0
python -I tools/research/history_pretest.py --check
python -I tools/research/drift_pretest.py --check
python -I tools/research/floor_pretest.py --check
python -I tools/research/curve_pretest.py --check
python -I tools/research/runtime_facts.py --check     # cần bộ minh chứng EV-20261017-EVAL-full-r1 trong evidence/
python -m pytest -q tools/research                   # test các hàm tính
```

`-I` để Python không nạp mã từ thư mục hiện tại hay biến môi trường khi đọc tệp tải về. Các chương trình tất định: cùng dữ liệu, cùng tham số thì cùng kết quả (seed cố định cho mô hình và cho lấy mẫu lại).

## Cách đo

- **Phép đo chung.** Mỗi chiến lược gán một mức cọc d cho từng đơn. Mức cọc trung bình D và phần bị hở L đều tính theo giá trị đơn. Chiến lược có điểm rủi ro dùng quy tắc d = max(0, 1 − ε / điểm).
- **Hai cách so.** "Cùng mức cọc" chọn ε để D = 50 %, không dùng nhãn của tập thử. "Cùng mức hở" chọn ε để L bằng của mức chung 50 %; cách này có dùng nhãn, nên chỉ là cách đọc kết quả, không phải chính sách triển khai được.
- **Tham số.** Mọi thứ học được chỉ lấy từ tập học. Các tham số cố định (seed, số vòng học, độ kéo về tỷ lệ chung, mức cọc của các luật, ngày chia tập, chu kỳ quên) được chọn trước khi chạy và không chỉnh theo kết quả; danh sách đủ nằm ở mục `parameters` của tệp kết quả.
- **Khoảng tin cậy.** 500 lần lấy mẫu lại có hoàn lại trên tập thử, giữ nguyên mức cọc từng đơn; báo phân vị 2,5 % và 97,5 %.

**Khách sạn.** 102.681 đơn loại "không cọc" có giá trị lớn hơn 0. Hai kiểu nhãn: "mọi đơn bị hủy" (để đối chiếu) và "nhãn chặt K ngày" (không đến, hoặc hủy trong K ngày trước ngày đến, K = 1, 3, 7). Dữ liệu tính theo ngày còn sân tính theo giờ, nên không K nào là ngưỡng đúng cho sân. Chia tập chính: học trên đơn đến tới hết 2016, thử trên đơn đến năm 2017. Có thêm hai cách chia khác để kiểm độ nhạy. Phép kiểm "giữ mức rủi ro" chọn ε trên một giai đoạn hiệu chỉnh rồi áp lên giai đoạn sau, với ba cách cắt thời gian.

**Dữ liệu trôi (khách sạn, nhãn chặt 7 ngày).** Tỷ lệ bỏ sát ngày đổi theo mùa, nên chọn giai đoạn hiệu chỉnh ở mùa ít bỏ và giai đoạn thử ở mùa nhiều bỏ cho một lần dữ liệu trôi bất lợi thật. Ba giai đoạn ghi ở `parameters.windows`: xấu đi mạnh, xấu đi vừa, tốt lên (đối chứng). Đơn được duyệt theo ngày đặt; mức cọc quyết định lúc đặt; kết cục chỉ được biết vào ngày đến. Ba cách: mức chung 50 %; giữ cố định ε chọn trên giai đoạn hiệu chỉnh; tự điều chỉnh theo quy tắc của Gibbs và Candès (2021), mỗi kết cục về thì log ε cộng η × (mục tiêu − phần hở của đơn) × (giá trị đơn / giá trị trung bình), với ba tốc độ η. Mục tiêu là phần hở của mức chung 50 % ở giai đoạn hiệu chỉnh.

**Phần sàn và hai dạng ràng buộc (khách sạn).** Chính sách hủy của SmashCourt: hủy trước ít nhất 24 giờ hoàn 100 %, trước 2 tới 24 giờ hoàn 50 %, trong 2 giờ cuối và không đến thì không hoàn. Với cọc d, tổn thất của kết cục k là (1 − r_k)·(1 − κ_k·d), κ_k là phần cọc doanh nghiệp giữ, r_k là khả năng bán lại; phần sàn là tổn thất khi d = 100 %. Dữ liệu tính theo ngày nên thử bốn cách quy đổi mốc "2 giờ" và "24 giờ" sang ngày (`parameters.mappings_days`) và ba giả định bán lại (`parameters.resale`). So hai dạng ràng buộc ở cùng phần hở với mức chung 50 %: dạng ngưỡng (ràng buộc trung bình) và dạng d = max(0, 1 − ε/q) (ràng buộc từng đơn).

**Đường học.** Lấy mẫu con của tập học ở các cỡ 250 tới 40.000 đơn (mỗi cỡ ba lần, seed 1, 2, 3) và cỡ đầy đủ; huấn luyện bảng tra và mô hình trên cùng mẫu, đo trên cùng tập thử cố định.

**Lịch khám.** 110.521 lịch hẹn: bỏ 5 dòng ngày hẹn trước ngày đặt và 1 dòng tuổi âm. Học trên lịch hẹn trước 25/05/2016, thử từ ngày đó. Lịch hẹn không có giá tiền, nên mọi lịch hẹn được coi có cùng giá trị; con số ở đây là **chỉ số lịch hẹn bị bỏ mà chưa được bảo đảm**, không phải tiền. Lịch sử của một lịch hẹn chỉ gồm những lần hẹn có ngày hẹn trước ngày đặt, tức kết cục đã biết lúc đặt. Sổ có quên dần dùng chu kỳ 14 ngày. Phép đo công bằng xét người thật sự đến khám và tính tỷ lệ được cọc từ 20 % trở xuống theo nhóm. Chênh lệch giữa người đã có và chưa có lịch sử được tính hai cách: thô, và trong cùng nhóm thời gian đặt trước × tuổi.

## Kết quả nằm ở đâu trong tệp JSON

| Đề cương (Mục 1.5) | Tệp | Khóa trong `results` |
| :--- | :--- | :--- |
| Bảng "Kết quả" (nhãn mọi đơn bị hủy) | `deposit_pretest.json` | `all_cancellations` |
| Bảng nhãn chặt | `deposit_pretest.json` | `strict_labels`, `dataset.canceled_share_within_days` |
| Bảng so với bảng luật dùng đặc điểm của đơn | `deposit_pretest.json` | `rules_vs_model.strict_7d` |
| Bảng khoảng cách giữa mô hình và bảng tra | `deposit_pretest.json` | `sensitivity_strict_7d` |
| Giữ mức rủi ro trên dữ liệu sau, khi dữ liệu trôi theo chiều có lợi (không đưa vào đề cương, thay bằng `drift_pretest.json`) | `deposit_pretest.json` | `risk_level_on_later_data` |
| Bảng đường học | `curve_pretest.json` | `hotel_strict_7d`, `medical_noshow` |
| Câu 4: giữ mức rủi ro khi dữ liệu trôi | `drift_pretest.json` | `adverse_strong`, `adverse_moderate`, `favourable`; tỷ lệ bỏ theo tháng ở `monthly_abandon_value_share` |
| Mục 2.4: phần sàn, hai dạng ràng buộc | `floor_pretest.json` | `floor`, `floor_monthly`, `formulation_comparison_strict_7d` |
| Mốc ngân sách cố định tốt nhất nhìn lại | `drift_pretest.json` | `*.hindsight_fixed_eps` |
| Mục 1.4: lùi về mặc định, bộ nhớ đệm, số quyết định | `runtime_facts.json` | `decision_audit`, `decision_audit_portability`, `all` |
| Lịch sử của khách đóng góp bao nhiêu | `history_pretest.json` | `uncovered_index`, `bootstrap_95pct_reduction_at_same_deposit` |
| Công bằng | `history_pretest.json` | `fairness_at_same_deposit` |

## Giới hạn

- Kết quả đúng với giả định khách cư xử như cũ khi mức cọc thay đổi. Phép thử không thấy khách bỏ đi vì bị đòi cọc, cũng không thấy khách giữ lời hơn vì đã trả trước.
- Hủy phòng khách sạn và bỏ lịch khám không phải bỏ sân. Hai bộ dữ liệu là hai bối cảnh đánh giá độc lập, có nhãn, cấu trúc lịch sử và giá trị khác nhau; không gộp con số của hai bộ thành một mức lợi ích chung.
- Dữ liệu khách sạn gần như không có lịch sử khách. Dữ liệu lịch khám chỉ dài sáu tuần, nên lịch sử từng người ngắn.
- Dữ liệu trôi bất lợi thật mới có ở bộ khách sạn, theo mùa. Khách đổi hành vi vì chính mức cọc thì không bộ dữ liệu nào cho thấy; phần đó chỉ kiểm được bằng mô phỏng.
- Ngưỡng "bao nhiêu đơn thì mô hình hơn bảng tra" là của hai bộ dữ liệu này, không phải của sân cầu lông.
- Phép đo công bằng mới so theo giới, tuổi và việc có lịch sử hay không, trên bệnh nhân ở một thành phố của Brazil; nó không nói gì về khách đặt sân.
