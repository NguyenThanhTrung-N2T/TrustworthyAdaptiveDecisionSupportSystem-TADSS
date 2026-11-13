# RUNBOOK — CHẠY VÀ VẬN HÀNH SMASHCOURT + TADSS

| | |
| :--- | :--- |
| **Dành cho** | Người chạy, vận hành và kiểm thử hệ thống trên máy Windows (Git Bash + Docker Desktop): dev, bản deploy, CD, quan sát, lượt đánh giá TADSS, dashboard "Kiểm thử", đo hiệu năng, xử lý sự cố |
| **Cập nhật** | 11/11/2026 (Epic 6C, task 1.42). Thay cho bốn hướng dẫn cũ `DOCKER_COMMANDS.md`, `EVALUATION_RUN_GUIDE.md`, `PERF_ENVIRONMENT.md`, `TEST_DASHBOARD.md` và mục "Lệnh quan trọng" của bản tổng hợp; lệnh đã đối chiếu với script hiện tại |
| **Không có ở đây** | Ý nghĩa từng khóa cấu hình `Tadss:*`: [CONFIGURATION_REFERENCE.md](CONFIGURATION_REFERENCE.md). Lý do thiết kế: spec / plan ở `spec/`. Kết luận hiệu năng: [PERFORMANCE_REPORT.md](../report/PERFORMANCE_REPORT.md) |

Lệnh chạy nguyên văn trong Git Bash (lệnh `docker compose` chạy được cả trong PowerShell). Thư mục chạy ghi ở đầu mỗi mục. Bí mật **không bao giờ** nằm trong image hay repo: dev dùng `.env` + `appsettings.Development.json` của từng repo, bản deploy dùng một file `smashcourt-deploy.env` ở thư mục gốc workspace.

## 0. Tra nhanh

| Muốn | Lệnh | Mục |
| :--- | :--- | :--- |
| Build + test trước khi commit | BE `dotnet build SmashCourt-BE.slnx -c Release` + `dotnet test …`; TADSS `dotnet build TADSS.slnx -c Release --warnaserror` | 1 |
| Cập nhật một service của bản deploy | `bash deploy/deploy.sh be` (hoặc `tadss`, `ai`) trong `SmashCourt-BE/` | 2 |
| Xem dữ liệu của bản deploy | pgAdmin / DBeaver tới `127.0.0.1:15432` | 2.3 |
| Bật / tắt TADSS trên bản deploy (không khởi động lại) | `bash deploy/tadss-flag.sh on` · `off` · `status` trong `SmashCourt-BE/` | 2.4 |
| TADSS treo mà đặt sân vẫn nhanh (mạch ngắt) | Panel "Mạch gọi TADSS" tab BE; log `Mạch gọi TADSS` | 2.5 |
| Xem job deploy của CI, mã thoát `cd.sh` | tab Actions của repo, job "Deploy (máy deploy)" | 3 |
| Xem dashboard vận hành, log, trace | Grafana Cloud › SmashCourt - Vận hành | 4 |
| Chạy một lượt đánh giá TADSS | `docker compose up -d --build` trong `TADSS-Runtime/` | 6 |
| Xem mọi nhóm test đạt / hỏng | `docker compose -f perf/docker-compose.yml up -d` ⇒ <http://127.0.0.1:3003> | 7 |
| Đo hiệu năng một kịch bản | `bash perf/perf.sh up` ⇒ `bash perf/perf.sh run load --rate 19 --duration 15m --code SP-01` | 8 |
| Một lỗi đã gặp | bảng sự cố | 9 |
| Trả dung lượng đĩa | `docker image prune -f`, `bash tools/evaluation/run.sh --clean-only` | 10 |

---

## 1. Dev hằng ngày (từng repo)

| Repo | Chạy | Build / kiểm (= CI) |
| :--- | :--- | :--- |
| `SmashCourt-BE/` | `dotnet run` (<http://localhost:5179>, Swagger `/swagger`) | `dotnet build SmashCourt-BE.slnx -c Release -nologo -v q` (0 warning) · `dotnet test SmashCourt-BE.slnx -c Release --no-build --filter "Category!=Integration"` (unit) · bỏ filter để chạy cả integration + HTTP trên PostgreSQL thật (cần Docker, khoảng 5 phút, 2676 test) |
| `TADSS-Runtime/` | `dotnet run --project src/TADSS.Runtime.Api` (<http://localhost:5296/swagger>, mọi `/api/*` cần `X-Api-Key`) | `dotnet build TADSS.slnx -c Release -nologo -v q --warnaserror` · `dotnet test tests/TADSS.UnitTests -c Release --no-build` (1915) · `dotnet test tests/TADSS.IntegrationTests -c Release --no-build` (244, Testcontainers) · `python tools/docs/check_docs.py` (DOC-1…3) |
| `SmashCourt-AI/` | `.venv/Scripts/python.exe -m uvicorn app.main:app --reload --port 8000` | `.venv/Scripts/python.exe -m ruff check .` · `-m compileall -q app` · `-m pytest -q` (LLM giả, không gọi Gemini) |
| `smashcourt-fe/` | `npm run dev` (<http://localhost:3000>) | `npm run lint` · `npx tsc --noEmit -p .` · `npm run build` |

**Pytest của TADSS** (Python 3.11+, venv một lần: `python -m venv .venv && source .venv/Scripts/activate && pip install -r tools/evaluation/requirements.txt -r tools/evidence/requirements.txt -r perf/requirements.txt`). Chạy **riêng từng bộ**, không gộp một lệnh:

```bash
python -m pytest -q tools/evaluation   # 269 đạt (Docker tắt: 266 đạt + 3 bỏ qua)
python -m pytest -q tools/evidence     # 355
python -m pytest -q tools/docs         # 34
python -m pytest -q perf/stack perf/testboard   # 189 (môi trường đo + dashboard Kiểm thử)
```

**Kiểm bằng Docker thật** (đã nằm trong bước (a) của lượt đánh giá, chỉ chạy tay khi sửa phần đó): `bash tools/expose/verify-expose.sh` (NG-1/2/3/5, SEC-6), `bash tools/observability/verify-observability.sh` (OB-2, OB-4), `bash tools/evidence/sql/verify-export.sh` (câu xuất SmashCourt). Mỗi script tự dọn container khi thoát.

**API key TADSS:** `python tools/security/hash_api_key.py` băm khóa trong `appsettings.Development.json` (ô `KeySha256` chỉ nhận mã băm), key gốc vào `.env`; `--new` sinh khóa mới (`--client <ClientId>` để chọn client).

**Migration EF của TADSS (chỉ đọc; `dotnet ef` cài global: `dotnet tool install --global dotnet-ef`):**

```bash
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations list --no-connect --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
ASPNETCORE_ENVIRONMENT=Testing ConnectionStrings__DefaultConnection="Host=localhost;Database=x" dotnet ef migrations has-pending-model-changes --project src/TADSS.Infrastructure --startup-project src/TADSS.Runtime.Api
```

Kỳ vọng: 4 ID migration (3 của v1 giữ để lịch sử khớp Neon + `20261002101457_DecisionPersistenceV2`) và "No changes have been made to the model since the last migration.". Chuỗi kết nối là giả, không mở kết nối. `migrations add`, `database update` chỉ chạy khi người dùng yêu cầu. Schema SmashCourt không dùng migration: script ở `SmashCourt-BE/docs/database/`, người dùng tự áp.

---

## 2. Bản deploy (chạy thường trực trên máy nhóm)

Một bản của cả hệ thống: BE + TADSS + AI + PostgreSQL + Redis + ngrok của BE (FE trên Vercel gọi BE qua ngrok) + Alloy. Push `develop` hoặc `master` của BE, TADSS, AI đều triển khai vào bản này (lần push gần nhất thắng; AI triển khai từ `master`). Không dùng Neon: gói miễn phí không đủ giờ chạy cho Hangfire chạy liên tục (P6-48); dữ liệu BE chép một lần từ Neon `tadss-dev` (staging).

Mọi thứ nằm ở **`SmashCourt-BE/deploy/`**: `docker-compose.yml` (cả hệ thống), `deploy.sh`, `seed-db.sh`, `cd.sh` (CD), `runner/` (runner trong Docker), `grafana/` (dashboard + cảnh báo), `.env.example`. Bí mật: **một** file `smashcourt-deploy.env` ở thư mục gốc workspace (`D:\SINH VIEN\HK6\DA1\SmashCourt\`, không phải git repo; mẫu là `.env.example`); `deploy/env-file.sh` tìm file này (đặt `SMASHCOURT_ENV_FILE` nếu để chỗ khác).

### 2.1. Lệnh (trong `SmashCourt-BE/`)

| Việc | Lệnh |
| :--- | :--- |
| Cập nhật một service lên bản mới nhất của nhánh | `bash deploy/deploy.sh be` · `bash deploy/deploy.sh tadss` · `bash deploy/deploy.sh ai` |
| Bật cả hệ thống (lần đầu, hoặc sau khi tắt hẳn) | `bash deploy/deploy.sh all` |
| Chạy bản của `master`, hoặc đúng một commit | `bash deploy/deploy.sh be master` · `bash deploy/deploy.sh be sha-xxxxxxx` (tag `sha-` đã có trên máy thì không kéo lại) |
| Quay về một bản cũ **có kiểm** (health, smoke, tự quay về nếu hỏng) | `bash deploy/cd.sh be <sha đầy đủ 40 ký tự>` |
| Bật / làm mới bộ gom số liệu (Alloy) | `bash deploy/deploy.sh obs` |
| Nạp lại dữ liệu BE từ `tadss-dev` (**xóa sạch** dữ liệu bản deploy rồi chép mới; gõ `smashcourt` để xác nhận) | `bash deploy/seed-db.sh` |
| Xem log | `docker logs -f smashcourt-be` · `docker logs -f tadss-runtime` · `docker logs -f smashcourt-ai` |
| Đang chạy commit nào | `docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' smashcourt-be` |

### 2.2. Container

| Container | Địa chỉ | Ghi chú |
| :--- | :--- | :--- |
| `smashcourt-be` | <http://localhost:8080/health/ready> | Công khai qua `smashcourt-ngrok` (trang ngrok <http://localhost:4040>) |
| `tadss-runtime` | <http://localhost:8081/health/ready> | Chỉ BE gọi |
| `smashcourt-ai` | <http://localhost:8000/health> | |
| `smashcourt-postgres` | `127.0.0.1:15432` | Database `smashcourt` (BE), `tadss` (TADSS) |
| `smashcourt-redis` | | Cache của TADSS; từ P6-58 cache quyết định của strategy quy tắc tắt (`CacheTtlMinutes` = 0 trong compose deploy), Redis để sẵn cho strategy cần cache sau này |
| `smashcourt-alloy` | <http://localhost:12345> | Gom số liệu lên Grafana Cloud (Mục 4) |
| `smashcourt-runner-be`, `-tadss`, `-ai` | | Runner CI (Mục 3) |

Bản deploy và cách chạy tay ở Mục 5 dùng chung cổng, tên container và tài khoản ngrok của BE: **chỉ chạy một trong hai**. Muốn chạy tay thì `docker stop smashcourt-be smashcourt-ngrok tadss-runtime smashcourt-ai` trước, xong thì `bash deploy/deploy.sh all`.

### 2.3. Xem dữ liệu (thay cho Neon Console)

pgAdmin: Servers › Register › Server…; **Connection**: Host `127.0.0.1`, Port `15432`, Maintenance database `postgres`, Username / Password là `POSTGRES_USER` / `POSTGRES_PASSWORD` trong `smashcourt-deploy.env`; SSL `disable` hoặc `prefer`. Thấy hai database `smashcourt` (bảng BE ở schema `public`) và `tadss` (schema `tadss`). Nhanh bằng terminal: `docker exec -it smashcourt-postgres psql -U <POSTGRES_USER> -d smashcourt`.

- Đây là DB **đang chạy thật**: chỉ xem; sửa / xóa là ảnh hưởng ngay hệ thống.
- Cổng `15432` dùng chung với DB của môi trường đo (Mục 8): trong lúc đo bản deploy tắt và cổng trỏ vào DB perf.
- Dữ liệu ở volume `pgdata`: xóa container không mất, `docker compose down -v` thì mất hết.

### 2.4. Bật / tắt TADSS khi đang chạy (cờ `EnableTadssDecision`)

Một lệnh trong `SmashCourt-BE/`, **không khởi động lại BE**, nên việc đặt sân không bị gián đoạn:

| Việc | Lệnh | Tác dụng |
| :--- | :--- | :--- |
| Bật | `bash deploy/tadss-flag.sh on` | Đơn online tạo **sau** lúc bật hỏi TADSS mức cọc (`APPROVED` kèm số tiền, `NEED_APPROVAL` chờ Manager duyệt, `REJECTED` từ chối) |
| Tắt (van an toàn) | `bash deploy/tadss-flag.sh off` | Đơn online tạo sau lúc tắt cọc 100 %, BE không gọi TADSS |
| Xem | `bash deploy/tadss-flag.sh status` | Giá trị trong file cờ và các dòng log gần nhất của BE về cờ |

- **Cách hoạt động:** script ghi `{"TadssConfig":{"EnableTadssDecision":true|false}}` vào `/app/runtime-config/feature-flags.json` trong container (file tạm rồi `mv`, BE không đọc phải file ghi dở). BE đọc file này làm nguồn cấu hình cuối, tự nạp lại khi file đổi (`Extensions/RuntimeConfigExtensions.cs`) và dùng cờ qua `IOptionsMonitor`, nên nhận giá trị mới trong khoảng 1 giây; mỗi lần cờ đổi BE ghi một dòng Warning `Cờ EnableTadssDecision đổi khi đang chạy: … ⇒ …` (thấy cả trên Grafana Cloud). Script đợi dòng đó rồi mới báo xong.
- **Không gián đoạn:** request đang chạy dùng giá trị đọc lúc bắt đầu; đơn đang chờ duyệt, thao tác duyệt / từ chối của Manager, job hết hạn duyệt và job báo kết quả về TADSS không phụ thuộc cờ nên vẫn chạy khi tắt.
- **Giữ trạng thái:** file nằm trong volume `smashcourt-deploy-be-runtime-config`, nên CD tạo lại container BE vẫn giữ cờ. Chưa có file thì dùng `appsettings.json` của image (mặc định **tắt**). Không đặt `TadssConfig__EnableTadssDecision` trong `deploy/docker-compose.yml` (file là nguồn quyết định).
- **Điều kiện bật:** TADSS chạy và `TADSS_API_KEY` có trong `smashcourt-deploy.env` (bắt buộc, compose deploy đã đòi biến này từ 6C-2b; `TadssSettingsValidator` không chấp nhận cờ bật mà thiếu khóa hay URL). Image BE phải có tính năng này (từ commit thêm `RuntimeConfigExtensions`); image cũ thì script báo lỗi và không đổi gì.
- Mức test: `RuntimeConfigExtensionsTests` (nạp lại trong 2 giây khi ghi đè, khi `mv`, khi xóa file), `TadssFlagChangeLoggerTests` (log khi đổi), BE-IT-19 (`HttpTadssIntegrationTests`, booking sau lúc đổi dùng giá trị mới).

### 2.5. Mạch ngắt khi TADSS không trả lời (P6-57)

Cờ bật nhưng TADSS treo hoặc mạng tới TADSS mất gói: trước P6-57 mỗi đơn online chờ 800 ms × 2 rồi mới cọc 100 %. Bản deploy bật mạch ngắt (`TadssConfig__CircuitEnabled: "true"` trong `deploy/docker-compose.yml`), chỉ cho lệnh đánh giá cọc lúc đặt sân:

| Trạng thái | Vào khi | Đơn online |
| :--- | :--- | :--- |
| Đóng | Bình thường; đơn thử thành công | Gọi TADSS như cũ (800 ms, thử lại 1 lần) |
| Mở | 1 lần gọi lỗi **và** `/health/ready` của TADSS lỗi (300 ms), hoặc 3 lần gọi lỗi liên tiếp | Cọc 100 % ngay, không gọi TADSS |
| Nửa mở | BE kiểm `/health/ready` mỗi 10 s, thấy chuyển từ lỗi sang tốt; hoặc tới mốc 60 → 120 → 240 → 300 s | Một đơn đi thử (một lần gọi); các đơn khác vẫn cọc 100 % |

- **Xem:** panel "Mạch gọi TADSS" ở tab BE của dashboard Vận hành (0 đóng, 1 mở, 2 nửa mở); log Warning `Mạch gọi TADSS: … ⇒ …` của `smashcourt-be`; `bash deploy/tadss-flag.sh status` in lần chuyển gần nhất. Trạng thái khác đóng quá 2 phút thì có email "Mạch gọi TADSS của BE không đóng".
- **Xử lý:** không cần làm gì để đóng lại: TADSS hồi phục thì mạch tự đóng sau một đơn thử. Muốn ngừng hẳn TADSS: `bash deploy/tadss-flag.sh off` (cờ đổi thì mạch về đóng). Lệnh duyệt / từ chối của Manager không đi qua mạch: TADSS lỗi thì Manager nhận "thử lại sau" như trước.
- **Tắt riêng mạch ngắt:** bỏ dòng `TadssConfig__CircuitEnabled` trong compose deploy rồi `bash deploy/deploy.sh be` (tạo lại container BE). Tham số khác ở `TadssConfig` của `appsettings.json` (`CircuitFailureThreshold`, `CircuitProbeIntervalSeconds`, `CircuitProbeTimeoutMs`, `CircuitBreakSeconds`, `CircuitMaxBreakSeconds`).
- **Đo:** `bash perf/perf.sh up --circuit-off` cho lượt đối chứng mạch tắt; `python perf/stack/faults.py latency <lượt>` tính độ trễ đặt sân trong khoảng lỗi (`fault-latency.json`).
- **E-STAGING** cũng bật mạch. TADSS dev dùng Neon (ngủ khi vắng): trước đợt E2E / LT-04 gọi `/health/ready` của TADSS tới khi 200 rồi chờ 15 giây.

---

## 3. CD tự động (push ⇒ deploy)

Push `develop` hoặc `master` ⇒ CI (test, build, đẩy image `ghcr.io/nguyenthanhtrung-n2t/<service>:sha-<7>`, bất biến) ⇒ job **"Deploy (máy deploy)"** cuối `ci.yml` chạy trên runner của máy này ⇒ lấy bộ deploy từ `SmashCourt-BE` nhánh `develop` ⇒ `deploy/cd.sh <service> <sha>`: kéo image (thử lại 5 lần khi lỗi mạng), triển khai, kiểm container chạy đúng commit (nhãn `revision`) + health + smoke chỉ đọc (BE `/api/cancel-policies`), hỏng thì **tự quay về bản đang chạy trước đó** và kiểm lại. Pull request không bao giờ deploy.

| `cd.sh` thoát | Nghĩa | Làm gì |
| :---: | :--- | :--- |
| 0 | Bản mới chạy, kiểm đạt | — |
| 1 | Bản mới hỏng, đã quay về bản trước (job đỏ để báo) | Đọc log job, sửa, push lại |
| 2 | **UNSAFE**: quay về cũng hỏng, hoặc không có bản trước | Người xử lý ngay: `docker ps`, `docker logs`, `bash deploy/deploy.sh <service> sha-<bản tốt>` |
| 3 | Từ chối, chưa đụng gì: sha sai dạng hoặc image không có trên GHCR | Kiểm job "Docker Image" của cùng lượt CI |
| 4 | Chưa đụng gì: máy deploy không tới được GHCR sau 5 lần thử (khoảng 2,5 phút) | Image vẫn có (job "Docker Image" xanh) ⇒ mạng ổn thì **Re-run failed jobs** trên tab Actions, hoặc `gh run rerun <id> --failed` |

- **Runner chạy trong Docker** (`SmashCourt-BE/deploy/runner/`): runner chính thức của GitHub + Docker Compose, `restart: unless-stopped` nên **tự chạy khi mở Docker Desktop**; chỉ thấy Docker của máy và `smashcourt-deploy.env` (gắn chỉ đọc); chạy bằng user `runner`. Docker tắt thì job deploy chờ tối đa 24 giờ.
- **Lệnh runner** (trong `SmashCourt-BE/`): bật / cập nhật `docker compose -f deploy/runner/docker-compose.yml up -d --build`; log `docker logs -f smashcourt-runner-be`. Đăng ký lại (mất volume `smashcourt-runner-*`): `export RUNNER_TOKEN=$(gh api -X POST repos/NguyenThanhTrung-N2T/<repo>/actions/runners/registration-token -q .token)` rồi `docker compose -f deploy/runner/docker-compose.yml run --rm -e RUNNER_TOKEN runner-be register` (mã dùng một lần).
- `cd.sh` chỉ giữ image đang chạy và bản ngay trước của mỗi service.
- Repo public: pull request từ fork của mọi người ngoài phải được duyệt mới chạy workflow (`all_external_contributors`).

---

## 4. Quan sát vận hành: Grafana Cloud

```text
smashcourt-be ─┐  OTLP (metric, trace, log; 60 giây/lần)
tadss-runtime ─┼──────────────► smashcourt-alloy ──► Grafana Cloud (Prometheus + Loki + Tempo + Grafana)
smashcourt-ai ─┘                 │ + CPU/RAM, image đang chạy của từng container (cAdvisor)        │
                                 └ + gọi health 3 service mỗi 60 giây (blackbox)          dashboard + email cảnh báo
```

Alloy dừng hoặc máy tắt thì app vẫn chạy, chỉ mất số liệu (và cảnh báo "không sẵn sàng" gửi email).

**Cài một lần** (dán giá trị vào `smashcourt-deploy.env`, không dán vào chat): (1) đăng ký <https://grafana.com> gói Free, tạo stack (vd `smashcourt`, Singapore); (2) My Account › stack › **OpenTelemetry › Configure**: chép *OTLP Endpoint* vào `GRAFANA_CLOUD_OTLP_ENDPOINT`, *Instance ID* vào `GRAFANA_CLOUD_INSTANCE_ID`, **Generate now** token ghi metric / log / trace vào `GRAFANA_CLOUD_API_TOKEN`; (3) Grafana của stack › Administration › Users and access › **Service accounts**: vai trò Admin › Add token vào `GRAFANA_SA_TOKEN`, `GRAFANA_URL=https://<stack>.grafana.net`; (4) `ALERT_EMAIL_TO=` (nhiều địa chỉ phân cách `;`); (5) trong `SmashCourt-BE/`: `bash deploy/deploy.sh obs` rồi `python deploy/grafana/grafana_cloud.py push` (thư mục "SmashCourt - Vận hành", 3 dashboard, điểm nhận email, 5 luật).

| Việc | Lệnh / nơi xem |
| :--- | :--- |
| Dashboard | Grafana › Dashboards › **SmashCourt - Vận hành** › BE / TADSS / AI |
| Log | Panel "Log Warning / Error" cuối mỗi tab; toàn bộ: Explore › Loki › `service_name` = `smashcourt-be` / `tadss-runtime` / `smashcourt-ai`. Cảnh báo khởi động của BE (DataProtection-Keys, https port) là vô hại |
| Trace | Explore › Tempo › Search, Service Name = `smashcourt-be` |
| Sửa dashboard / cảnh báo | Sửa `deploy/grafana/dashboards.py` (hoặc `alert_rules` trong `grafana_cloud.py`) ⇒ `python deploy/grafana/grafana_cloud.py check` ⇒ `push` |
| Alloy có lỗi không | <http://127.0.0.1:12345> (mọi thành phần "healthy"); `docker logs smashcourt-alloy` |
| Đổi token | Sửa `smashcourt-deploy.env` ⇒ `bash deploy/deploy.sh obs` |

| Cảnh báo (email) | Khi nào |
| :--- | :--- |
| Service không sẵn sàng | Health của BE, TADSS hoặc AI không 2xx trong 2 phút; **hoặc mất số liệu** (máy tắt, mất mạng, Alloy dừng) |
| BE / AI lỗi 5xx > 5 % | 5 phút |
| TADSS đường lùi > 20 % | 10 phút (chỉ có khi bật `TadssConfig:EnableTadssDecision`) |
| Gemini 429 liên tục | 10 phút |
| CD thất bại / `UNSAFE` | GitHub tự gửi email khi job "Deploy (máy deploy)" đỏ |

Giới hạn gói Free: khoảng 10.000 chuỗi metric, 50 GB log, 50 GB trace, giữ **14 ngày** (xem mức dùng ở Administration › Usage insights). Số liệu cho báo cáo xuất ngay (panel › Inspect › Data); số liệu đo hiệu năng dùng Grafana local (Mục 7–8), giữ 365 ngày.

---

## 5. Chạy tay từng repo bằng compose (không qua bản deploy)

Chạy trong thư mục của repo. Compose dùng image `ghcr.io/nguyenthanhtrung-n2t/<service>:${IMAGE_TAG:-local}`: **không đặt `IMAGE_TAG` thì là `local`** (image tự build). Muốn bản của CI thì thêm dòng `IMAGE_TAG=develop` (BE, TADSS) / `IMAGE_TAG=master` (AI) vào `.env` của repo, hoặc `IMAGE_TAG=sha-xxxxxxx` cho đúng một commit; nhánh `feat/...` không có image.

| Việc | BE (`SmashCourt-BE/`) | TADSS (`TADSS-Runtime/`) | AI (`SmashCourt-AI/`) |
| :--- | :--- | :--- | :--- |
| Kéo bản mới | `docker compose pull smashcourt-be` | `docker compose pull tadss-runtime` | `docker compose pull ai` |
| Chạy (kèm ngrok) | `docker compose up -d --no-build smashcourt-be ngrok` | `docker compose up -d --no-build tadss-runtime ngrok` | `docker compose up -d --no-build ai ngrok-ai` |
| Tự build từ code đang sửa | `IMAGE_TAG=local docker compose up -d --build smashcourt-be` | `IMAGE_TAG=local docker compose up -d --build tadss-runtime` | `IMAGE_TAG=local docker compose up -d --build ai` |
| Log / dừng | `docker compose logs -f smashcourt-be` / `stop smashcourt-be ngrok` | `docker compose logs -f tadss-runtime` / `stop tadss-runtime ngrok` | `docker compose logs -f ai` / `stop ai ngrok-ai` |
| Container, health, trang ngrok | `smashcourt-be`, <http://localhost:8080/health/ready>, <http://localhost:4040> | `tadss-tadss-runtime-1`, <http://localhost:8081/health/ready>, <http://localhost:4042> | `smashcourt-ai`, <http://localhost:8000/health>, <http://localhost:4041> |

- TADSS: luôn ghi tên service. `docker compose up -d --build` **không** ghi tên service chạy cả một lượt đánh giá (Mục 6); thêm `grafana` để có dashboard <http://localhost:3001>. Cần mạng dùng chung: `docker network create smashcourt-network` (một lần).
- BE kèm Mailpit cho E2E / LT-04: `docker compose -f docker-compose.yml -f docker-compose.mailpit.yml up -d --build` (email ở <http://localhost:8025>).
- PowerShell: `$env:IMAGE_TAG="local"` trước lệnh, `Remove-Item Env:IMAGE_TAG` sau khi xong.

---

## 6. Lượt đánh giá TADSS (Evidence Pack cho hội đồng)

Trong `TADSS-Runtime/`. Stack đánh giá chạy cô lập (mạng riêng, PostgreSQL / Redis dùng một lần), **không chạm Neon** dù khởi động cùng app (D-14).

### 6.1. Chuẩn bị (một lần)

Docker Desktop đang chạy (`docker info`), RAM cho Docker ≥ 8 GB (`%UserProfile%\.wslconfig`), trống ≥ 15 GB ổ đĩa; tắt chế độ ngủ khi cắm điện; đóng ứng dụng nặng; có mạng `smashcourt-network`, `.env` có `NGROK_AUTHTOKEN`, `appsettings.Development.json` có API key thật (thiếu hai thứ sau thì chỉ phần vận hành lỗi, lượt đánh giá vẫn chạy). Lần đầu tải / build khoảng 6–8 GB image.

### 6.2. Chạy

```bash
docker compose up -d --build                      # app + ngrok + Grafana VÀ một lượt bản nhanh (~30 phút)
docker compose logs -f evaluator                  # im lặng tới cuối là bình thường; Ctrl+C chỉ thoát theo dõi
EVAL_MODE=full docker compose up -d --build       # bản đầy đủ (~3 giờ 25 phút: thêm LT-05, Stryker, CT-03 500 ví dụ, PT ~118 nghìn request)
bash tools/evaluation/run.sh [quick|full]         # chỉ stack đánh giá, không bật app
bash tools/evaluation/run.sh quick --steps e,f    # chạy lại vài bước (muốn có report.html phải kèm f)
```

PowerShell bản đầy đủ: `$env:EVAL_MODE="full"; docker compose up -d --build`, xong thì đóng cửa sổ (hoặc `Remove-Item Env:EVAL_MODE`), nếu không lần `up` sau lại chạy bản đầy đủ. Mỗi lần `up` hoặc `run.sh` tạo một thư mục `evidence/<run_id>/` mới; `run.sh` từ chối chạy khi lượt do `up` khởi động còn chạy.

**Trước lượt đầy đủ chính thức:** xuất CSV SmashCourt (6.5) để RP có dữ liệu; **commit mọi thay đổi** để SC7 và `manifest.json` ghi đúng commit (thay đổi chưa commit ở `src/TADSS.Runtime.Api/**`, `src/Shared.DecisionContractSDK/**` làm SC7 không đạt; `tools/evidence/hotel_adapter.py` chưa commit thì SC7 `INSUFFICIENT_DATA`); máy cắm điện, tắt ngủ.

### 6.3. Sáu bước

| Bước | Làm gì | Mã | Thời lượng (bản nhanh / đầy đủ) |
| :--- | :--- | :--- | :--- |
| (a) test | Build `--warnaserror`, unit + integration, `verify-expose`, `verify-observability`, `verify-export`, `check_docs.py`, 3 bộ pytest của `tools/`, kiểm dashboard | UT, IT, NG, SEC, OB-2, OB-4, DOC | ~11,5 phút / 9 phút |
| (b) tải | k6 LT-01, LT-02 (LT-05 chỉ bản đầy đủ) trên instance 1 | LT-01/02/05 | ~7 phút / 1 giờ 51 |
| (c) sự cố | SCALE-2 qua nginx hai instance, rồi k6 50 RPS khi tiêm lỗi (strategy chậm / lỗi, cắt PostgreSQL, Redis chậm) | LT-03, FI-02/03/07/08, SCALE-2, OB-3 | ~8,5 phút / 18 phút |
| (d) hợp đồng | Schemathesis trên `openapi-v2.yaml` (25 / 500 ví dụ), coverage; Stryker chỉ bản đầy đủ | CT-03, MT-01…03 | ~1 phút / 45 phút |
| (e) dữ liệu thực tế | RP: replay booking SmashCourt (CSV, 6.5); PT: dataset khách sạn (ghim SHA-256) tới `eval-tadss-portability`; shadow + kinh tế chính sách cọc (PT-03/04) | RP-01/02, PT-01…04 | ~1,5 phút / 13 phút |
| (f) Evidence Pack | Xuất audit D1, dựng D2/D4/D5, SC1…SC8, EV-1…EV-5, `report.html`, `manifest.json` | SC, EV | ~1 phút / 9 phút |

Kịch bản k6 ở `tests/load/` (`scenarios/lt01.js`…`scale2.js`, hồ sơ tải `config/modes.json`); giao diện biến `--env`: `BASE_URL`, `API_KEY`, `RUN_ID`, `SCENARIO`, `MODE` (`quick`/`full`), `SUMMARY_PATH`, `SAMPLE_ONLY=N` (chỉ in N payload). In 5 payload mẫu không gửi:

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W)/tests/load:/load:ro" -v "$(pwd -W):/out" -w /load grafana/k6:2.3.0 \
  run -e SAMPLE_ONLY=5 --console-output /out/samples.log --quiet scenarios/lt01.js
```

Trong Git Bash phải giữ `MSYS_NO_PATHCONV=1` và `$(pwd -W)` (nếu không, đường dẫn `/load…` bị đổi thành đường dẫn Windows).

### 6.4. Đọc kết quả

| Mã thoát | Nghĩa | Làm gì |
| :---: | :--- | :--- |
| 0 | Đạt mọi tiêu chí | Mở báo cáo |
| 1 | Chạy hết, có tiêu chí không đạt | Báo cáo mục KPI + các bước `failed` |
| 2 | Lỗi hạ tầng, tham số sai, chưa chạy hết | Cột "Ghi chú" của bảng tóm tắt, rồi Mục 9 |

`explorer.exe evidence` ⇒ bấm đúp `evidence/<run_id>/report.html` (tự chứa). Đọc: mục 1 **Kết luận KPI** (SC1…SC8) ⇒ mục 2 các bước ⇒ mục 10 toàn vẹn (EV-1…EV-5 phải toàn "đạt") ⇒ mục 11 giới hạn. Bình thường ở bản nhanh: SC5 / SC6 `NOT_APPLICABLE` (đo ở môi trường tích hợp), SC8 `INSUFFICIENT_DATA` (bản nhanh bỏ Stryker). PT-03/04 là **chỉ số mô phỏng theo kịch bản, không phải kết quả tài chính**: đọc nhãn đối soát shadow (`RECONCILED` là đạt), ghép cặp (`PAIRED` ≥ 99 %), hai biến thể hoàn tiền (`REFUND_OF_PAID` là chính sách), hai cận `S-SC`. Dựng lại báo cáo từ pack không gửi lại request: `docker compose run --rm --no-deps --entrypoint python evaluator -m evidence.report /evidence/<run_id>`. Grafana của stack đánh giá: <http://127.0.0.1:3002>.

Nhận biết treo: `docker ps --filter name=tadss-evaluator` phải `Up` và `ls -R "$(ls -1td evidence/EV-* | head -1)/raw" | tail -5` phải có file mới; quá 10 phút không thêm file thì xem Mục 9; dừng hẳn `docker compose stop evaluator`.

### 6.5. Dữ liệu SmashCourt cho RP (tùy chọn)

```bash
export SMASHCOURT_DATABASE_URL='postgresql://<user>:<mật khẩu>@<host>:5432/<db>?sslmode=require'   # chỉ trong cửa sổ này, không ghi file
mkdir -p data/private
docker run --rm -i postgres:16.15-alpine psql "$SMASHCOURT_DATABASE_URL" -X -q --csv -v ON_ERROR_STOP=1 \
  < tools/evidence/sql/export_smashcourt_bookings.sql > data/private/smashcourt_bookings.csv
wc -l data/private/smashcourt_bookings.csv   # số booking + 1 dòng tiêu đề
bash tools/evaluation/run.sh quick --steps e,f
```

`data/private/` nằm trong `.gitignore` / `.dockerignore`: **không commit, không gửi cho ai**; evaluator băm HMAC mọi định danh trước khi ghi vào `evidence/`. DB ở chính máy thì đổi `localhost` trong URL thành `host.docker.internal`.

---

## 7. Dashboard "Kiểm thử" (mọi nhóm test đạt / hỏng)

Trong `TADSS-Runtime/`. Chạy trên máy (Grafana local), tách khỏi Grafana Cloud.

```text
methodology Mục 4 (bảng mã test) ──python perf/board.py catalog──► tests/catalog.yaml (189 mã)
CI ba repo · Evidence Pack · k6 · SC6 · Playwright ──python perf/board.py <nguồn>──► perf/results/<thời điểm>-<nguồn>/ (bản gốc)
                                                     │ gộp theo mã (một nguồn đỏ ⇒ mã đỏ)
                                                     ▼
                              Pushgateway ⇒ Prometheus (giữ 365 ngày) ⇒ Grafana "SmashCourt - Kiểm thử" (BE / TADSS / AI)
```

Bật: `docker compose -f perf/docker-compose.yml up -d` (một lần `pip install -r perf/requirements.txt`) ⇒ <http://127.0.0.1:3003> (không đăng nhập) ⇒ Dashboards ⇒ **SmashCourt - Kiểm thử**. Tắt: `docker compose -f perf/docker-compose.yml down` (thêm `-v` để xóa sạch). Prometheus <http://127.0.0.1:9095>, Pushgateway `:9096`, Alloy OTLP `:14317` / `:14318`, Tempo (trace của lượt phân tích).

| Nạp từ | Lệnh |
| :--- | :--- |
| CI gần nhất (cần `gh` đăng nhập) | `python perf/board.py ci all` (hoặc `be`, `tadss`, `ai`; `--branch` để đổi nhánh) |
| Test chạy trên máy | BE / TADSS: `dotnet test … --logger trx --results-directory <dir>` với `SMASHCOURT_CATALOG_INDEX_DIR=<dir>` (BE) / `TADSS_CATALOG_INDEX_DIR=<dir>` (TADSS) ⇒ `python perf/board.py trx be <dir>`; AI: `pytest --junitxml=<file>` ⇒ `python perf/board.py junit <file>` |
| Evidence Pack | `python perf/board.py evidence evidence/<run_id>` |
| Một lượt k6 | `python perf/board.py k6 SP-01 <summary.json>` (lượt hạn chế thêm `--limited "lý do"`) |
| SC6 | `python perf/board.py sc6 <file.json>` |
| Playwright | `python perf/board.py playwright <report.json>` (mã lấy từ tên test, vd `E2E-03 …`) |
| Đẩy lại bảng sau khi bật lại Grafana | `python perf/board.py push` (nạp mà chưa bật Grafana: thêm `--no-push`) |
| Sinh lại danh mục / dashboard; kiểm (= CI) | `python perf/board.py catalog` · `dashboards` · `check` |

Đọc: hàng KPI SC1–SC8; tầng 1 mỗi ô một nhóm `đạt/tổng` (xanh / đỏ / vàng hạn chế / xám chưa chạy), **bấm ô** ⇒ tầng 2 bảng mã (mặc định chỉ mã hỏng) ⇒ cột "Báo cáo" mở file gốc; hàng "Hiệu năng" đặt k6 phía client cạnh số liệu server. Sửa bảng mã trong methodology thì **phải** chạy `python perf/board.py catalog` rồi `check` (CI job Testboard đỏ nếu quên). Gắn mã cho test mới: BE `[Trait("Catalog", "BE-IT-27")]`, TADSS `[Trait("CatalogId", "IT-16")]`, AI `@pytest.mark.catalog("AI-UT-06")`.

---

## 8. Đo hiệu năng (`perf`)

Trong `TADSS-Runtime/`. Chạy BE, TADSS, AI **đúng image `sha-…` của bản deploy** cùng DB, Redis, Mailpit, LLM giả riêng; k6 đóng vai người dùng theo 7 hành trình. Thiết kế và SLO: plan 6C Phụ lục B.6–B.7; kết quả: [PERFORMANCE_REPORT.md](../report/PERFORMANCE_REPORT.md).

### 8.1. Lệnh

```bash
bash perf/perf.sh up          # bật bộ quan sát + perf, chờ healthy (tag = bản đang deploy; image sha đã có thì không kéo lại)
bash perf/perf.sh seed        # sau up, mỗi khi datagen đổi: dữ liệu benchmark (2.000 khách, seed 42) + database mẫu
bash perf/perf.sh run load --rate 19 --duration 15m --pg-sample activity --cooldown 120 --code SP-01 --tag r1
bash perf/perf.sh pool <lượt> <lượt> <lượt>   # p95/p99 trên mẫu thô ghép 3 lượt cùng cấu hình
python perf/stack/bottleneck.py perf/results/k6/<lượt> [--traces]   # phân rã DB / TADSS / BE (Mục 9 của báo cáo)
python perf/stack/perf_evidence.py collect <mã bộ> <lượt>… | verify perf/evidence/<mã bộ>   # bằng chứng + SHA-256
bash perf/perf.sh down        # tắt perf, xóa dữ liệu của nó (kết quả giữ nguyên)
```

| Lệnh | Tùy chọn chính |
| :--- | :--- |
| `up` | `--be/--tadss/--ai TAG` (đo bản khác), `--tadss-off` (SP-06 bên B), `--telemetry-off` (SP-07 bên B), `--trace` (trace 100 %, chỉ lượt phân tích), `--faults` (BE ⇒ Toxiproxy ⇒ TADSS, SP-09), `--llm-delay MS --llm-jitter MS` (mặc định 800 / 200; SP-10A `0 0`), `--db-prepare N` (auto-prepare, mặc định 0 như deploy), `--db-pool N`, `--be-cpus N` (mặc định 2), `--cpu-layout physical\|legacy`, `--pg-locks on\|off`. Mỗi lần `up` đặt lại mọi tùy chọn |
| `run <profile>` | Profile: `smoke`, `load` (SP-01), `stress` (SP-02), `spike` (SP-03), `soak` (SP-04), `breakpoint` (SP-05), `ai` (SP-10), `closed`. `--rate N` (hành trình/s; danh nghĩa 19), `--duration`, `--warmup 2m`, `--code SP-xx` (nạp dashboard), `--tag`, `--cooldown 120`, `--pg-sample activity\|locks`, `--fault stop-tadss\|latency\|reset` (cần `up --faults`), `--limited "lý do"`, `--selftest`, `--allow-battery` (lượt không dùng để kết luận) |
| `reset` / `status` | Khôi phục hai DB từ mẫu + xóa Redis + kiểm checksum (`run` tự gọi) / tag, commit, trạng thái |

| Container | Vai trò | Cổng |
| :--- | :--- | :--- |
| `perf-be`, `perf-tadss`, `perf-ai` | Image GHCR theo tag, cấu hình toàn hằng không bí mật | BE `127.0.0.1:18080`, TADSS `18081` |
| `perf-postgres` (17), `perf-tadss-postgres` (16), `perf-redis` | DB dùng một lần + database mẫu để khôi phục | SmashCourt `127.0.0.1:15432` (trùng cổng DB bản deploy) |
| `perf-mailpit`, `perf-fake-llm` | Email, LLM giả (trễ cấu hình được) | Mailpit `18025` |
| `perf-k6`, `perf-toxiproxy` | k6 khi `run`; Toxiproxy khi `up --faults` | Toxiproxy API `18474` |

Hành trình (`perf/k6/lib/journeys.js`): J1 xem sân / lịch trống 55 % · J2 đăng nhập 5 % · J3 đặt ⇒ J4 IPN 80 % / hủy 10 % / bỏ dở 10 % (15 %) · J5 lịch sử 15 % · J6 nhân viên 7 % · J7 duyệt cọc 3 %; tài khoản `perf-*@smashcourt.perf`, mật khẩu `Perf@12345`. Giờ đặt và đơn check-in / check-out theo **giờ thật** ⇒ tỷ lệ request ghi phụ thuộc giờ chạy.

Kết quả mỗi lượt `perf/results/k6/<lượt>/` (gitignore; mục lục `perf/results/INDEX.md`): `verdict.json` (**kết luận**: ĐẠT / KHÔNG ĐẠT / KHÔNG ĐỦ DỮ LIỆU theo nhóm, p95 ≥ 100 mẫu, p99 ≥ 1000 mẫu), `metadata.json` (image, commit, seed, cpuset, trần), `resources.json` (theo phút), `raw.csv.gz` (mẫu thô), `cpu-clock.json` (xung nhịp), `faults.json` + `sc6.json` (SP-09), `bottleneck.json`. k6 thoát 99 chỉ là threshold trên mọi mẫu; kết luận theo `verdict.json`.

### 8.2. Lượt chính thức trên laptop

1. **Tắt bản deploy + runner** (hỏi người dùng); `run` tự dừng nếu RAM VM Docker vượt 95 %. Script buổi đo (`perf/results/session-lib.sh`) tắt, đo, luôn bật lại deploy khi xong và có khóa `.session.lock` chống chạy chồng.
2. Cắm sạc (preflight dừng nếu chạy pin); giữ **cùng chế độ nguồn** cho các lượt cần so (mọi lượt từ 01/11 ở "Best power efficiency").
3. Máy đã bật **khoảng 1,5 giờ** (30 phút chưa đủ: xung nhịp thấp làm Đăng nhập không đạt); trước lượt đầu **đo CPU từng tiến trình** vài giây, tắt ứng dụng dùng CPU (10/11 một trình duyệt dùng 26 % CPU ⇒ lượt bị loại); PC Manager, IDE thứ hai.
4. Giữ máy thức: `powershell -NoProfile -ExecutionPolicy Bypass -File perf/stack/keepawake.ps1 -Log <log>` ở nền; khóa màn hình (Win + L) được, **không** gập nắp / nút nguồn / rút sạc. Sau mỗi lượt kiểm sự kiện ngủ Kernel-Power 42/506/507.
5. `--cooldown 120` giữa các lượt; buổi ≤ khoảng 2 giờ 15; xem `cpu-clock.json` (`effectiveMhz.p10`) của từng lượt trước khi dùng số; so hai cấu hình thì chạy **xen kẽ** A B B A A B.
6. Xong: `perf.sh up` (về mặc định) ⇒ `docker stop perf-…` ⇒ bật lại bản deploy.

---

## 9. Sự cố thường gặp

| Triệu chứng | Nguyên nhân | Xử lý |
| :--- | :--- | :--- |
| Job Deploy thoát 3 "không có image" dù job Docker Image xanh | (trước 11/11) mạng máy deploy tới ghcr.io chập chờn | Từ 11/11 `cd.sh` thử lại và thoát 4; mạng ổn thì Re-run failed jobs |
| Job Deploy thoát 1 / 2 | Bản mới hỏng (1, đã quay về) / quay về cũng hỏng (2) | Mục 3 |
| CI BE đỏ đúng một test `OnlineBookingTadssFailureTests.NetworkError_IsRetriedOnce_ThenFullPayment` (đếm 1 kết nối thay vì 2) | Test chập chờn khi máy CI bận (11/11: lần chạy lại và 15/15 lượt trên máy đều đạt) | Re-run failed jobs; lặp lại thì ghi lý do từng lần gọi trong test trước khi sửa |
| `perf.sh up` dừng ở bước kéo image | (trước 11/11) `pull` luôn kéo lại | Từ 11/11 dùng `--policy missing`; image chưa có thì cần mạng tới GHCR |
| Hai chuỗi đo chạy chồng / bản deploy tự bật giữa buổi đo | Dừng script cha bằng TaskStop / tín hiệu thường: buổi con, k6 vẫn chạy; `trap` bật lại deploy | Giết **cả cây** bằng `kill -9` (`day*.sh`, `session*.sh`, mọi `perf.sh`), `docker stop perf-k6`, dừng `keepawake.ps1`; cất lượt dở vào `perf/results/k6/_aborted/` |
| Lượt đo có đợt nghẽn, Đăng nhập chậm, xung nhịp p10 < 3000 MHz | Máy vừa khởi động / ứng dụng nền dùng CPU | Mục 8.2 bước 3; lượt đó ghi là nhiễu hoặc chạy bù |
| `tadss-runtime` khởi động lại liên tục, log `KeySha256 phải là chuỗi hex 64 ký tự` | Ô `KeySha256` chứa key gốc | `python tools/security/hash_api_key.py` rồi `docker compose up -d --build` |
| `network smashcourt-network declared as external, but could not be found` | Chưa tạo mạng | `docker network create smashcourt-network` |
| `Cannot connect to the Docker daemon` | Docker Desktop chưa bật | Mở Docker Desktop, chờ "Engine running", `docker info` |
| Container dừng đột ngột, test / k6 chậm bất thường | Docker thiếu RAM (< 8 GB) | Tăng RAM, đóng ứng dụng khác |
| Thời lượng bước dài gấp nhiều lần, FI-xx báo sai | Máy ngủ / khựng giữa lượt | Lượt đó **không dùng được**; tắt ngủ, chạy lại |
| `port is already allocated` (3002 / 15432 / 8080) | Stack lần trước còn chạy, hoặc bản deploy và perf / chạy tay cùng bật | `bash tools/evaluation/run.sh --clean-only` (3002); chỉ bật một trong hai (15432, 8080) |
| Lượt đánh giá mã thoát 2 | Hạ tầng lỗi, tham số sai | Ghi chú của bảng tóm tắt; `docker compose logs eval-tadss-1`; chạy lại bước lỗi (`--steps`) |
| (a) đỏ đúng một test `ErrorHandlingOptionsTests…InvalidRetryAfter…` `ObjectDisposedException` | Test chập chờn có sẵn (~1/10 khi máy bận) | `bash tools/evaluation/run.sh quick --steps a` |
| (b) `k6 bỏ N lượt` | Thiếu VU | Chỉ xử lý khi bỏ ở **pha chính**: tắt ứng dụng nặng, `--steps b,f` |
| (c) FI-07 thiếu 2/1262 request; SCALE-2 vài `503` sau LT-03 | Máy khựng; instance 2 nối lại PostgreSQL | `--steps c,f` |
| (e) PT `skipped` / `failed` "sai SHA-256" / RP `skipped` | Không tải được dataset / nguồn đổi / chưa có CSV | Kiểm mạng; `docker volume rm tadss_eval-datasets` (sai SHA: **không** sửa SHA, báo lại); Mục 6.5 |
| SC7 `INSUFFICIENT_DATA` dù PT 0 lỗi | `tools/evidence/hotel_adapter.py` chưa commit | Commit rồi `--steps e,f` |
| `run.sh` báo evaluator của `up` vẫn chạy | Lượt tự chạy chưa xong | Chờ `docker compose logs -f evaluator` xong |
| Log BE có cảnh báo DataProtection-Keys / "Failed to determine the https port" | Cảnh báo lúc khởi động, vô hại (BE không dùng khóa DataProtection, HTTPS do ngrok xử lý) | Không cần làm gì |

---

## 10. Dọn Docker

```bash
docker system df                            # dung lượng trước
docker container prune -f                   # container đã dừng
docker image prune -f                       # image không tên (bản cũ sau mỗi lần kéo)
docker rmi <image>:<tag>                    # một image cụ thể
docker builder prune -f                     # cache build (vài GB)
docker compose down                         # (TADSS-Runtime/) dừng app + stack đánh giá, giữ image và cache dataset
bash tools/evaluation/run.sh --clean-only   # chỉ phần đánh giá: 9 container, volume, image, build cache ~6 GB
```

**Không xoá:** `ngrok/ngrok:*` (ngrok BE, AI, TADSS), `postgres:17.6-alpine` + `testcontainers/ryuk` (integration BE), `postgres:16.15-alpine`, `redis:7.4-alpine`, `grafana/otel-lgtm`, `grafana/k6:2.3.0`, `curlimages/curl` (TADSS), `axllent/mailpit` (E2E BE), image của bản deploy đang chạy và bản ngay trước (`cd.sh` tự giữ). **Không dùng** `docker system prune` / `-a` (xóa cả mạng `smashcourt-network` và image cần dùng) hay `docker compose down --rmi local`.

**Trả dung lượng cho Windows** (ổ ảo không tự co): Docker Desktop › Troubleshoot › Clean / Purge data (xóa **mọi** image, container, volume), hoặc tắt Docker Desktop ⇒ PowerShell Administrator `wsl --shutdown` ⇒ thu nhỏ `D:\DockerData\disk\docker_data.vhdx` bằng `diskpart` (`select vdisk file="D:\DockerData\disk\docker_data.vhdx"`, `attach vdisk readonly`, `compact vdisk`, `detach vdisk`, `exit`).
