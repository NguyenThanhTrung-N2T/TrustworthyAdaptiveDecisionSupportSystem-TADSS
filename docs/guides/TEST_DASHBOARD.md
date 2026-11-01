# Dashboard "Kiểm thử" (Epic 6C, gói 6C-3b)

Cập nhật 01/11/2026. Một màn hình cho biết **mọi nhóm test của BE, TADSS, AI đang đạt hay hỏng**, bấm vào nhóm đỏ là ra mã hỏng, từ mã mở được mô tả trong methodology và báo cáo gốc. Chạy **trên máy** (Grafana local), tách khỏi dashboard "Vận hành" trên Grafana Cloud. Kế hoạch: [plan 6C](../../spec/PHASE6_PLAN_6C_DEVOPS_OBSERVABILITY_PERF.md) Mục 5.3, task 2.1–2.9.

## 1. Thành phần

```text
docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md  (Mục 4: bảng mã test — nguồn chi tiết duy nhất)
        │ python perf/board.py catalog
        ▼
tests/catalog.yaml (189 mã: nhóm, tab BE/TADSS/AI, phạm vi, tiêu chí, công cụ, link)
        │
CI ba repo · Evidence Pack · k6 · SC6 · Playwright ──► python perf/board.py <nguồn> ──► perf/results/<thời điểm>-<nguồn>/ (bản gốc)
                                                              │ gộp theo mã (một nguồn đỏ ⇒ mã đỏ)
                                                              ▼
                                       Pushgateway ⇒ Prometheus ⇒ Grafana "Kiểm thử" (3 tab BE / TADSS / AI)
```

| Thư mục / file | Vai trò |
| :--- | :--- |
| `perf/docker-compose.yml` | Bộ quan sát test: Prometheus, Pushgateway, Alloy (OTLP cho k6 và app của môi trường perf, CPU/RAM container), Grafana 13 |
| `perf/board.py` + `perf/testboard/` | Sinh danh mục, sinh dashboard, nạp kết quả, kiểm (`check`, chạy trong CI TADSS) |
| `perf/grafana/dashboards/*.json` | Dashboard sinh từ `perf/testboard/dashboards.py` (không sửa trên giao diện) |
| `perf/results/` | Bản gốc từng lần nạp + `state.json` (gitignore, chỉ trên máy) |

## 2. Bật và xem

```bash
docker compose -f perf/docker-compose.yml up -d      # trong TADSS-Runtime/
pip install -r perf/requirements.txt                 # một lần (pyyaml, pytest)
```

Mở <http://127.0.0.1:3003> (không cần đăng nhập) ⇒ Dashboards ⇒ **SmashCourt - Kiểm thử** ⇒ BE / TADSS / AI. Tắt: `docker compose -f perf/docker-compose.yml down` (giữ dữ liệu; thêm `-v` để xóa sạch).

## 3. Nạp kết quả

| Nguồn | Lệnh (trong `TADSS-Runtime/`) | Mã được cập nhật |
| :--- | :--- | :--- |
| CI gần nhất trên GitHub (cần `gh` đã đăng nhập) | `python perf/board.py ci all` (hoặc `be`, `tadss`, `ai`; mặc định nhánh CI của từng repo: BE, TADSS `develop`, AI `master`; đổi bằng `--branch`) | BE-UT, BE-IT, UT, IT, CT, OPS-03/04, OB-BE-1/2, AI-UT, OB-AI-1, AI-CT-01… |
| Test chạy trên máy | BE/TADSS: chạy `dotnet test ... --logger trx --results-directory <dir>` với `SMASHCOURT_CATALOG_INDEX_DIR=<dir>` (BE) hoặc `TADSS_CATALOG_INDEX_DIR=<dir>` (TADSS), rồi `python perf/board.py trx be <dir>`; AI: `pytest --junitxml=<file>` rồi `python perf/board.py junit <file>` | như CI |
| Evidence Pack của evaluator | `python perf/board.py evidence evidence/<run_id>` | MT, FI, LT, RP, PT, SEC, NG, SCALE, OB, DOC + KPI SC1–SC8 |
| Một lượt k6 | `python perf/board.py k6 SP-01 <summary.json>`; lượt `INFRA_LIMITED`/`ABORTED` thêm `--limited "lý do"` (vàng, không tính đạt, P6-47) | SP-xx |
| Bất biến booking (BE, `SC6_OUTPUT`) | `python perf/board.py sc6 <file.json>` | KPI SC6, BE-IT-20 |
| Playwright (E2E, FE) | `python perf/board.py playwright <report.json>` (mã lấy từ tên test, vd `E2E-03 …`) | E2E-xx, FE-xx |
| Đẩy lại bảng (sau khi bật lại Grafana) | `python perf/board.py push` | — |

- Mỗi lần nạp chỉ thay kết quả của **đúng nguồn đó**; nguồn khác giữ nguyên. Mã lấy kết quả "xấu" nhất trong các nguồn.
- Chưa bật Grafana mà vẫn muốn lưu: thêm `--no-push` (chỉ ghi `perf/results/`), bật xong chạy `push`.
- Mã không có trong danh mục (vd trait mã quyết định `P6-18`) được liệt kê "bỏ qua" khi nạp, không âm thầm mất.
- Gắn mã cho test mới: BE `[Trait("Catalog", "BE-IT-27")]`, TADSS `[Trait("CatalogId", "IT-16")]`, AI `@pytest.mark.catalog("AI-UT-06")`. Mã mới phải có trong methodology Mục 4 rồi chạy `python perf/board.py catalog`.

## 4. Đọc dashboard

| Tầng | Ở đâu | Cách đọc |
| :--- | :--- | :--- |
| KPI | Hàng đầu: SC1–SC8 | ĐẠT / HỎNG / HẠN CHẾ / CHƯA CHẠY (giống nhau ở 3 tab) |
| 1 | "Nhóm test của …": mỗi ô một nhóm, `đạt/tổng` | Xanh đạt hết; đỏ có mã hỏng; vàng có mã hạn chế hoặc mới chạy một phần; xám chưa chạy. **Bấm ô** ⇒ bảng tầng 2 lọc theo nhóm |
| 2 | "Chi tiết mã" | Mặc định chỉ mã **hỏng** (đổi "Trạng thái" ở đầu trang). Bấm mã ⇒ mô tả trong methodology; cột "Ca đỏ" là test đỏ đầu tiên |
| 3 | Cột "Báo cáo" + bảng "Nguồn kết quả" | Link lượt CI, hoặc thư mục `perf/results/…` chứa file gốc (trx, JUnit, manifest, summary k6) |
| SP | Hàng "Hiệu năng" | k6 phía client cạnh số liệu server (p95/p99, RPS, lỗi, CPU/RAM container) trong lúc đo; chỉnh khung thời gian về lượt đo |

Mọi panel có ⓘ với mô tả 6 ý (đo gì, đơn vị, cách đọc, ngưỡng, nguồn, mã test); `python perf/board.py check` chặn panel thiếu mô tả hoặc dùng metric không khai báo.

## 5. Cho báo cáo

- Bản gốc mỗi lần nạp nằm ở `perf/results/<thời điểm>-<nguồn>/` (`summary.json` + file gốc): chép thư mục cần dùng ra ngoài trước khi xóa.
- Số liệu Prometheus giữ 365 ngày trong volume `smashcourt-test-prometheus-data`. Không lưu ảnh dashboard (quy ước dự án); cần bảng thì dùng panel ⇒ Inspect ⇒ Data ⇒ tải CSV.

## 6. Môi trường đo

Môi trường `perf` (BE, TADSS, AI theo image `sha-…`, DB riêng, LLM giả, k6) nối vào mạng `smashcourt-test` và gửi OTLP tới Alloy của bộ này: [PERF_ENVIRONMENT.md](PERF_ENVIRONMENT.md). Bộ quan sát có thêm Tempo (trace của lượt phân tích) và exporter PostgreSQL/Redis của môi trường perf (chạy trong Alloy).
