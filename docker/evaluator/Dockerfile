# Image evaluator của chế độ đánh giá (D-12, O-14): .NET 8 SDK (Debian) + Python 3.11 + k6 + Docker CLI (kèm compose) + git.
# Image KHÔNG chứa mã nguồn của dự án: repo được mount chỉ đọc tại /repo lúc chạy, evaluator copy sang /work loại trừ file bí mật (appsettings.Development.json, .env*) rồi build/test trên bản copy.
# Chạy bằng root: cần ghi vào /evidence (thư mục của người dùng) và dùng /var/run/docker.sock (Testcontainers cho integration test, đọc digest image, verify-expose.sh); quyền socket
# vốn tương đương root trên máy chủ nên hạ xuống user thường không tăng an toàn (xem README: evaluator có quyền điều khiển Docker của máy).
FROM grafana/k6:2.3.0 AS k6
FROM docker:29.1-cli AS dockercli

FROM mcr.microsoft.com/dotnet/sdk:8.0.425
ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PATH="/opt/venv/bin:${PATH}" \
    PYTHONPATH=/opt/evaluation

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-venv git bash curl openssl tar coreutils ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=k6 /usr/bin/k6 /usr/local/bin/k6
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=dockercli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose

WORKDIR /opt/evaluation
COPY tools/evaluation/requirements.txt ./requirements.txt
RUN python3 -m venv /opt/venv && pip install --no-cache-dir -r requirements.txt
COPY tools/evaluation/evaluator ./evaluator

# Mã nguồn dự án và dữ liệu lượt chạy đến từ mount: /repo (chỉ đọc), /evidence (ghi), /work (bản copy), /faults (file tiêm lỗi)
WORKDIR /work
ENTRYPOINT ["python", "-m", "evaluator"]
