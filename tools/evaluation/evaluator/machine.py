"""Thông số máy và Docker của lượt chạy (cho manifest và mục "giới hạn" của báo cáo), cùng digest image đã dùng."""
from __future__ import annotations

import json
import os
import platform
import subprocess
from pathlib import Path
from typing import Callable

Runner = Callable[[list[str]], tuple[int, str]]
# Image mà lượt đánh giá dùng: digest của chúng được ghi vào manifest (EV-5)
IMAGES = [
    "mcr.microsoft.com/dotnet/sdk:8.0.425", "docker:29.1-cli", "grafana/k6:2.3.0", "grafana/otel-lgtm:0.34.0",
    "postgres:16.15-alpine", "redis:7.4-alpine", "ghcr.io/shopify/toxiproxy:2.12.0", "nginx:1.29.8-alpine", "tadss-eval-runtime:local",
]


def _run(command: list[str]) -> tuple[int, str]:
    try:
        completed = subprocess.run(command, capture_output=True, text=True, check=False, timeout=60)
    except (OSError, subprocess.TimeoutExpired):
        return 1, ""
    return completed.returncode, completed.stdout


def memory_total_gb(meminfo: str | None = None) -> float | None:
    """RAM khả dụng cho container (GB), đọc `MemTotal` từ /proc/meminfo; không có (không phải Linux) ⇒ None."""
    if meminfo is None:
        path = Path("/proc/meminfo")
        if not path.exists():
            return None
        meminfo = path.read_text(encoding="utf-8")
    for line in meminfo.splitlines():
        if line.startswith("MemTotal:"):
            return round(int(line.split()[1]) / 1024 / 1024, 1)
    return None


def os_description(os_release: str | None = None) -> str:
    """Tên hệ điều hành của container (PRETTY_NAME) hoặc `platform.platform()`."""
    if os_release is None:
        path = Path("/etc/os-release")
        os_release = path.read_text(encoding="utf-8") if path.exists() else ""
    for line in os_release.splitlines():
        if line.startswith("PRETTY_NAME="):
            return line.split("=", 1)[1].strip().strip('"')
    return platform.platform()


def docker_info(run: Runner = _run) -> dict[str, object]:
    """Phiên bản server Docker, số CPU và RAM mà Docker cấp (máy thật hoặc VM của Docker Desktop)."""
    code, output = run(["docker", "info", "--format", "{{json .}}"])
    if code != 0 or not output.strip():
        return {}
    info = json.loads(output)
    return {
        "server_version": info.get("ServerVersion"),
        "cpus": info.get("NCPU"),
        "memory_gb": round(info.get("MemTotal", 0) / 1024**3, 1) if info.get("MemTotal") else None,
        "operating_system": info.get("OperatingSystem"),
    }


def image_digest(image: str, run: Runner = _run) -> str | None:
    """Digest (`repo@sha256:...`) hoặc ID của image cục bộ; không có ⇒ None."""
    code, output = run(["docker", "image", "inspect", "--format", "{{if .RepoDigests}}{{index .RepoDigests 0}}{{else}}{{.Id}}{{end}}", image])
    return output.strip() or None if code == 0 else None


def collect(images: list[str], run: Runner = _run) -> dict[str, object]:
    """Thông số máy + Docker + digest của các image được liệt kê (không có ⇒ ghi None, không làm hỏng lượt chạy)."""
    return {
        "cpu_count": os.cpu_count(),
        "memory_gb": memory_total_gb(),
        "operating_system": os_description(),
        "docker": docker_info(run),
        "images": {image: image_digest(image, run) for image in images},
    }
