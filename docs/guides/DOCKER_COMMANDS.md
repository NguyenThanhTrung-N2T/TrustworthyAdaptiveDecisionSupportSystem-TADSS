# Lệnh Docker hằng ngày (BE, TADSS, AI)

Cập nhật 01/11/2026 (Epic 6C). CI build sẵn image và đẩy lên GHCR, nên trên máy chỉ cần **kéo về rồi chạy**, không phải build lại. Mục 0 là môi trường deploy chạy thường trực; mục 1–3 là cách chạy tay bằng compose ở gốc từng repo (khi cần thử riêng).

## 0. Bản deploy (chạy thường trực)

Một bản deploy của cả hệ thống trên máy: BE + TADSS + AI + PostgreSQL + Redis + ngrok của BE (FE trên Vercel gọi BE qua ngrok). Push `develop` hoặc `master` đều triển khai vào bản này (lần push gần nhất thắng; bình thường là `develop`). Dữ liệu BE chép từ Neon DB test `tadss-dev`; DB gốc không bị đụng tới.

Mọi thứ nằm ở **`SmashCourt-BE/deploy/`**: `docker-compose.yml` (cả hệ thống), `deploy.sh`, `seed-db.sh`, `cd.sh` (dùng cho CD), `runner/` (runner trong Docker), `.env.example`. Bí mật: **một** file `smashcourt-deploy.env` ở thư mục gốc workspace (`D:\SINH VIEN\HK6\DA1\SmashCourt\`, cạnh 4 repo; thư mục này không phải git repo nên file không bao giờ bị commit; mẫu là `.env.example`). Mở: `notepad smashcourt-deploy.env` từ thư mục gốc. Script tìm file theo `deploy/env-file.sh` (đặt `SMASHCOURT_ENV_FILE` nếu muốn để chỗ khác).

| Việc | Lệnh (trong `SmashCourt-BE/`) |
| :--- | :--- |
| Cập nhật một service lên bản mới nhất | `bash deploy/deploy.sh be` · `bash deploy/deploy.sh tadss` · `bash deploy/deploy.sh ai` |
| Bật cả hệ thống (lần đầu, hoặc sau khi tắt hẳn) | `bash deploy/deploy.sh all` |
| Chạy bản của `master` hoặc quay về bản cũ | `bash deploy/deploy.sh be master` · `bash deploy/deploy.sh be sha-xxxxxxx` |
| Nạp lại dữ liệu BE từ `tadss-dev` (**xóa sạch** dữ liệu của bản deploy rồi chép mới; gõ `smashcourt` để xác nhận) | `bash deploy/seed-db.sh` |
| Xem log | `docker logs -f smashcourt-be` · `docker logs -f tadss-runtime` · `docker logs -f smashcourt-ai` |

| Container | Địa chỉ | Ghi chú |
| :--- | :--- | :--- |
| `smashcourt-be` | http://localhost:8080/health/ready | Công khai qua `smashcourt-ngrok` (trang ngrok http://localhost:4040) |
| `tadss-runtime` | http://localhost:8081/health/ready | Chỉ BE gọi |
| `smashcourt-ai` | http://localhost:8000/health | |
| `smashcourt-postgres` | `127.0.0.1:15432` (DBeaver…) | Database `smashcourt`, `tadss`; tài khoản trong `.env` |
| `smashcourt-redis` | | Cache của TADSS |
| `smashcourt-alloy` | http://localhost:12345 | Gom số liệu đẩy lên Grafana Cloud; bật riêng bằng `bash deploy/deploy.sh obs` (mục "Quan sát" bên dưới) |

- Bản deploy và cách chạy tay ở mục 2 dùng chung cổng, tên container và tài khoản ngrok của BE. **Chỉ chạy một trong hai**: muốn chạy tay thì `docker stop smashcourt-be smashcourt-ngrok tadss-runtime smashcourt-ai` trước, xong thì `bash deploy/deploy.sh all`.

### CD tự động (push ⇒ deploy)

Push `develop` hoặc `master` của BE, TADSS, AI ⇒ CI (test, build, đẩy image `sha-<7>`) ⇒ job `deploy` cuối `ci.yml` chạy trên **runner của máy này** ⇒ `deploy/cd.sh <service> <sha>`: kéo đúng image của commit vừa push, kiểm container chạy đúng commit + health + smoke chỉ đọc (BE: `/api/cancel-policies`), hỏng thì **tự quay về bản đang chạy trước đó** và kiểm lại. Pull request không bao giờ deploy. Kết quả xem ở tab Actions của repo (job "Deploy (máy deploy)").

| `cd.sh` thoát | Nghĩa |
| :--- | :--- |
| 0 | Bản mới chạy, kiểm đạt |
| 1 | Bản mới hỏng, đã quay về bản trước (job đỏ để báo) |
| 2 | **UNSAFE**: quay về cũng hỏng, cần người xử lý |
| 3 | Từ chối, chưa đụng gì: sha sai hoặc image không có trên GHCR |

- **Runner chạy trong Docker:** 3 container `smashcourt-runner-be`, `-tadss`, `-ai` (runner chính thức của GitHub + Docker Compose, `SmashCourt-BE/deploy/runner/`), `restart: unless-stopped` nên **tự chạy khi mở Docker Desktop**, không cài gì vào Windows. Mỗi runner chỉ thấy Docker của máy (để deploy) và `smashcourt-deploy.env` (gắn chỉ đọc vào `~/.smashcourt-deploy/.env` trong container); chạy bằng user `runner`, không phải root. Runner tắt (Docker tắt) thì job deploy chờ tối đa 24 giờ, mở Docker là chạy.
- **Lệnh runner** (trong `SmashCourt-BE/`): bật/cập nhật `docker compose -f deploy/runner/docker-compose.yml up -d --build`; xem log `docker logs -f smashcourt-runner-be`. Đăng ký lại một runner (mất volume `smashcourt-runner-*`): `export RUNNER_TOKEN=$(gh api -X POST repos/NguyenThanhTrung-N2T/<repo>/actions/runners/registration-token -q .token)` rồi `docker compose -f deploy/runner/docker-compose.yml run --rm -e RUNNER_TOKEN runner-be register` (mã đăng ký chỉ dùng một lần, không lưu lại).
- **Dọn image:** mỗi lần deploy thành công, `cd.sh` chỉ giữ image đang chạy và bản ngay trước của service đó.
- **Bảo mật:** repo public nên pull request từ fork của **mọi** người ngoài phải được duyệt mới chạy workflow (Settings › Actions › General, đã đặt `all_external_contributors`); job deploy chỉ chạy khi push.
- **Quay về tay một bản cũ:** `bash deploy/cd.sh be <sha đầy đủ>` (có kiểm và tự quay về) hoặc `bash deploy/deploy.sh be sha-xxxxxxx` (chỉ chạy).

### Quan sát: Grafana Cloud (dashboard "Vận hành" + cảnh báo email)

Grafana nằm trên Grafana Cloud (gói Free, có link `https://<stack>.grafana.net` mở ở đâu cũng được); máy deploy chỉ chạy thêm **một** container nhỏ `smashcourt-alloy` gom số liệu rồi đẩy lên:

```text
smashcourt-be ─┐  OTLP (metric, trace, log; 60 giây/lần)
tadss-runtime ─┼──────────────► smashcourt-alloy ──► Grafana Cloud (Prometheus + Loki + Tempo + Grafana)
smashcourt-ai ─┘                 │ + CPU/RAM, image đang chạy của từng container (cAdvisor)        │
                                 └ + gọi health 3 service mỗi 60 giây (blackbox)          dashboard + email cảnh báo
```

- **Prometheus** lưu số liệu dạng chuỗi theo thời gian (request/giây, độ trễ, lỗi, CPU…); **Loki** lưu log; **Tempo** lưu trace (một request đi qua BE ⇒ DB ⇒ TADSS); **Grafana** là giao diện vẽ dashboard và gửi cảnh báo, đọc từ ba kho trên. Trên Grafana Cloud cả bốn có sẵn, không phải chạy trên máy.
- **Alloy** là bộ gom: nhận OTLP từ 3 service, tự đo thêm container và health, rồi đẩy hết lên Grafana Cloud. Alloy dừng hoặc máy tắt thì app vẫn chạy bình thường, chỉ mất số liệu (và cảnh báo "không sẵn sàng" sẽ gửi email).

**Cài một lần** (bạn tự làm; dán giá trị vào `smashcourt-deploy.env` ở thư mục gốc workspace, không dán vào chat):

1. Đăng ký <https://grafana.com> (gói Free, không cần thẻ), tạo stack, ví dụ tên `smashcourt`, vùng gần nhất (Singapore).
2. grafana.com › **My Account** › stack › ô **OpenTelemetry** › **Configure**: chép *OTLP Endpoint* vào `GRAFANA_CLOUD_OTLP_ENDPOINT`, *Instance ID* vào `GRAFANA_CLOUD_INSTANCE_ID`; bấm **Generate now** tạo token (quyền ghi metric, log, trace) và chép vào `GRAFANA_CLOUD_API_TOKEN`.
3. Mở Grafana của stack (`https://<stack>.grafana.net`) › **Administration › Users and access › Service accounts** › tạo service account vai trò **Admin** (cần để tạo điểm nhận email và luật cảnh báo) › **Add token**: chép vào `GRAFANA_SA_TOKEN`; `GRAFANA_URL=https://<stack>.grafana.net`.
4. `ALERT_EMAIL_TO=` địa chỉ nhận cảnh báo (nhiều địa chỉ: phân cách bằng `;`). Email do Grafana Cloud gửi, không cần Gmail hay mật khẩu ứng dụng.
5. Chạy (trong `SmashCourt-BE/`): `bash deploy/deploy.sh obs` (bật Alloy) rồi `python deploy/grafana/grafana_cloud.py push` (tạo thư mục "SmashCourt - Vận hành", 3 dashboard, điểm nhận email, 5 luật cảnh báo).

| Việc | Lệnh / nơi xem |
| :--- | :--- |
| Xem dashboard | Grafana › Dashboards › **SmashCourt - Vận hành** › BE / TADSS / AI (hàng nút đầu trang chuyển giữa 3 tab) |
| Đọc log của một service | Panel "Log Warning / Error" cuối mỗi tab; toàn bộ log: Explore › Loki › `service_name` = `smashcourt-be` / `tadss-runtime` / `smashcourt-ai` |
| Xem trace một request | Explore › Tempo › Search, Service Name = `smashcourt-be` |
| Sửa dashboard / cảnh báo | Sửa `SmashCourt-BE/deploy/grafana/dashboards.py` (hoặc `alert_rules` trong `grafana_cloud.py`), chạy `python deploy/grafana/grafana_cloud.py check` rồi `push` (ghi đè bản trên Grafana) |
| Alloy có lỗi không | <http://127.0.0.1:12345> (mọi thành phần phải "healthy"); `docker logs smashcourt-alloy` |
| Đổi token Grafana Cloud | Sửa `.env` rồi `bash deploy/deploy.sh obs` |

| Cảnh báo (email) | Khi nào |
| :--- | :--- |
| Service không sẵn sàng | Health của BE, TADSS hoặc AI không trả 2xx trong 2 phút; **hoặc mất số liệu** (máy deploy tắt, mất mạng, Alloy dừng) |
| BE / AI lỗi 5xx > 5 % | Trong 5 phút |
| TADSS đường lùi > 20 % | Trong 10 phút (chỉ có khi BE bật `TadssConfig:EnableTadssDecision`) |
| Gemini bị giới hạn (429) liên tục | 10 phút |
| CD thất bại / `UNSAFE` | Không qua Grafana: GitHub tự gửi email khi job "Deploy (máy deploy)" đỏ |

- **Giới hạn gói Free** (kiểm trên trang giá khi đăng ký): khoảng 10.000 chuỗi metric, 50 GB log, 50 GB trace, giữ 14 ngày. Để nằm trong giới hạn: gửi 60 giây/lần, histogram ít bucket, Alloy chỉ giữ metric container mà dashboard dùng. Xem mức dùng: Grafana › Administration › **Usage insights** (hoặc grafana.com › Billing).
- Số liệu cho báo cáo: số liệu chỉ giữ 14 ngày, nên xuất ngay (panel › Inspect › Data / Panel JSON). Bộ test hiệu năng có Grafana riêng chạy local, lưu kết quả lâu dài (không dùng Grafana Cloud).

## 1. Chạy bản nào? Biến `IMAGE_TAG` trong `.env`

Mỗi repo có một dòng `IMAGE_TAG` trong `.env` (mẫu ở `.env.example`). Compose tự đọc dòng này nên lệnh không cần thêm gì.

| `IMAGE_TAG` | Nghĩa |
| :--- | :--- |
| `develop` | Bản mới nhất của nhánh `develop` do CI build (mặc định cho BE, TADSS) |
| `master` | Bản mới nhất của nhánh `master` (mặc định cho AI) |
| `local` | Bản tự build từ code trên máy (`--build`) |
| `sha-xxxxxxx` | Đúng một commit cũ (7 ký tự đầu của mã commit), dùng khi muốn quay lại bản trước |

Nhánh khác (`feat/...`) **không có image**: code chưa merge thì tự build (mục 3).

## 2. Lệnh chính

Chạy trong thư mục của repo.

| Việc | BE (`SmashCourt-BE/`) | TADSS (`TADSS-Runtime/`) | AI (`SmashCourt-AI/`) |
| :--- | :--- | :--- | :--- |
| Kéo bản mới | `docker compose pull smashcourt-be` | `docker compose pull tadss-runtime` | `docker compose pull ai` |
| Chạy (kèm ngrok) | `docker compose up -d --no-build smashcourt-be ngrok` | `docker compose up -d --no-build tadss-runtime ngrok` | `docker compose up -d --no-build ai ngrok-ai` |
| Xem log | `docker compose logs -f smashcourt-be` | `docker compose logs -f tadss-runtime` | `docker compose logs -f ai` |
| Dừng | `docker compose stop smashcourt-be ngrok` | `docker compose stop tadss-runtime ngrok` | `docker compose stop ai ngrok-ai` |

- Cập nhật bản mới thì chạy lại hai lệnh **kéo** rồi **chạy**. Container cũ tự được thay.
- `Ctrl+C` khi xem log chỉ thoát màn hình log, container vẫn chạy.
- TADSS: luôn ghi tên service như bảng. `docker compose up -d --build` không ghi tên service sẽ chạy cả một lượt đánh giá (D-14, xem [EVALUATION_RUN_GUIDE.md](EVALUATION_RUN_GUIDE.md)). Muốn xem dashboard thì thêm `grafana` vào lệnh chạy (http://localhost:3001).
- Bí mật **không nằm trong image**. Compose lấy chúng từ `.env` và `appsettings.Development.json` (mount chỉ đọc) trên máy.

## 3. Tự build từ code đang sửa (chưa push)

```bash
IMAGE_TAG=local docker compose up -d --build <service>
```

Có `IMAGE_TAG=local` ở đầu lệnh thì image tự build mang tag `local`, không đè lên bản `develop`/`master` đã kéo về. Lần sau muốn quay lại bản của CI thì chạy lại lệnh **chạy** ở mục 2 (không có `IMAGE_TAG=local`).

Ở PowerShell, đặt biến bằng `$env:IMAGE_TAG="local"` trước lệnh, rồi `Remove-Item Env:IMAGE_TAG` sau khi xong.

## 4. Kiểm tra

```bash
docker ps                                             # container đang chạy, cột STATUS có (healthy)
docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' smashcourt-be   # đang chạy commit nào
```

| Service | Tên container | Health | Trang ngrok |
| :--- | :--- | :--- | :--- |
| BE | `smashcourt-be` | http://localhost:8080/health/ready | http://localhost:4040 |
| TADSS | `tadss-tadss-runtime-1` | http://localhost:8081/health/ready | http://localhost:4042 |
| AI | `smashcourt-ai` | http://localhost:8000/health | http://localhost:4041 |

## 5. Dọn image, container cũ

```bash
docker system df                   # xem dung lượng trước
docker container prune -f          # xoá container đã dừng
docker image prune -f              # xoá image không tên (<none>): bản cũ sau mỗi lần kéo bản mới
docker rmi <image>:<tag>           # xoá một image cụ thể, ví dụ ghcr.io/nguyenthanhtrung-n2t/smashcourt-be:local
docker builder prune -f            # xoá cache build (vài GB; lần --build sau chậm hơn)
```

**Không xoá** các image sau vì vẫn cần dùng:

| Image | Dùng cho |
| :--- | :--- |
| `ngrok/ngrok:latest`, `ngrok/ngrok:3.39.11-alpine` | ngrok của BE, AI (`latest`) và của TADSS (bản ghim) |
| `postgres:17.6-alpine`, `testcontainers/ryuk` | Integration test của BE |
| `postgres:16.15-alpine`, `redis:7.4-alpine`, `grafana/otel-lgtm:0.34.0`, `curlimages/curl`, `grafana/k6` | Test, đánh giá, kiểm expose/observability của TADSS |
| `axllent/mailpit` | E2E của BE (email giả) |
| `rhysd/actionlint` | Kiểm workflow GitHub Actions trước khi push |

**Không dùng** `docker system prune -a`, vì lệnh này xoá luôn các image trên. **Không dùng** `docker compose down --rmi local`, vì lệnh này xoá cả image do compose build.
