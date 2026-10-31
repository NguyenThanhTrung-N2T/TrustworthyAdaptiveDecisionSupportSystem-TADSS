# Lệnh Docker hằng ngày (BE, TADSS, AI)

Cập nhật 01/11/2026 (Epic 6C). CI build sẵn image và đẩy lên GHCR, nên trên máy chỉ cần **kéo về rồi chạy**, không phải build lại. Khi có CD tự động (gói 6C-2b), runner sẽ tự làm việc này.

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
