# Task 2.10: compose perf chỉ nói chuyện với container trong stack (không Neon, Gmail, VNPay, Gemini thật), không nhận bí mật,
# mỗi service có trần CPU/RAM, và giữ đúng tên mà dashboard "Kiểm thử" lọc theo
import hashlib
import re
from pathlib import Path
from urllib.parse import urlparse

import pytest
import yaml

STACK = Path(__file__).resolve().parents[1]
COMPOSE = yaml.safe_load((STACK / "docker-compose.yml").read_text(encoding="utf-8"))
SERVICES = COMPOSE["services"]
# Container ngoài stack được phép gọi: Alloy của bộ quan sát kiểm thử (cùng mạng smashcourt-test)
ALLOWED_HOSTS = set(SERVICES) | {"smashcourt-test-alloy", "127.0.0.1", "localhost"}
# Biến được phép truyền vào: tag image và núm chỉnh lượt đo; không biến nào mang bí mật
ALLOWED_VARIABLES = {"BE_TAG", "TADSS_TAG", "AI_TAG", "PERF_TADSS_DECISION", "PERF_TRACE_RATIO", "FAKE_LLM_DELAY_MS", "FAKE_LLM_JITTER_MS"}
TEXT = (STACK / "docker-compose.yml").read_text(encoding="utf-8")


def _environment(service: str) -> dict[str, str]:
    env = SERVICES[service].get("environment") or {}
    return {key: str(value) for key, value in env.items()}


def test_project_and_container_names_match_dashboard_filters():
    assert COMPOSE["name"] == "smashcourt-perf"  # panel CPU/RAM lọc container_label_com_docker_compose_project="smashcourt-perf"
    assert all(service["container_name"].startswith("perf-") for service in SERVICES.values())
    assert COMPOSE["networks"]["default"] == {"name": "smashcourt-test", "external": True}


@pytest.mark.parametrize("service", sorted(SERVICES))
def test_every_service_has_cpu_and_memory_ceiling(service):
    assert "cpus" in SERVICES[service] and "mem_limit" in SERVICES[service]


def test_only_known_variables_are_interpolated_and_no_env_file_is_read():
    variables = set(re.findall(r"\$\{([A-Z0-9_]+)", TEXT))
    assert variables <= ALLOWED_VARIABLES, variables - ALLOWED_VARIABLES
    assert all("env_file" not in service for service in SERVICES.values())


@pytest.mark.parametrize("service", sorted(SERVICES))
def test_urls_and_connection_strings_stay_inside_the_stack(service):
    for key, value in _environment(service).items():
        for url in re.findall(r"https?://[^\s,;\"']+", value):
            host = urlparse(url).hostname or ""
            assert host in ALLOWED_HOSTS or host.endswith(".invalid"), f"{service}.{key} trỏ ra ngoài: {host}"
        for host in re.findall(r"Host=([^;]+)", value):
            assert host in SERVICES, f"{service}.{key} trỏ DB ra ngoài: {host}"
        if key.endswith("RedisConnectionString"):
            assert value.split(":")[0] in SERVICES


def test_no_real_provider_appears_anywhere():
    for marker in ("neon.tech", "gmail.com", "vnpayment.vn", "googleapis.com", "ngrok"):
        assert marker not in TEXT.lower(), marker


def test_tadss_key_hash_matches_the_key_be_sends():
    key = _environment("be")["TadssConfig__ApiKey"]
    assert hashlib.sha256(key.encode()).hexdigest() == _environment("tadss")["Tadss__Security__ApiKeys__0__KeySha256"]


def test_ai_uses_fake_llm():
    assert urlparse(_environment("ai")["GEMINI_BASE_URL"]).hostname == "fake-llm"
