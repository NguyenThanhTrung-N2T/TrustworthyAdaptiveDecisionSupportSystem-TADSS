# Task 2.10: compose perf chỉ nói chuyện với container trong stack (không Neon, Gmail, VNPay, Gemini thật), không nhận bí mật,
# mỗi service có trần CPU/RAM, và giữ đúng tên mà dashboard "Kiểm thử" lọc theo
import hashlib
import json
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
ALLOWED_VARIABLES = {"BE_TAG", "TADSS_TAG", "AI_TAG", "PERF_TADSS_DECISION", "PERF_TRACE_RATIO", "FAKE_LLM_DELAY_MS", "FAKE_LLM_JITTER_MS",
                     "PERF_CPUSET_SYSTEM", "PERF_CPUSET_TOOLS", "PERF_DB_POOL",
                     "PERF_DB_AUTO_PREPARE", "PERF_BE_CPUS", "PERF_TELEMETRY", "PERF_TADSS_URL"}
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


@pytest.mark.parametrize("service", sorted(SERVICES))
def test_system_and_load_generator_are_pinned_to_separate_cpu_sets(service):
    # B.7.2: k6 chạy cùng máy ⇒ cpuset tách CPU của hệ thống khỏi CPU của k6 + bộ quan sát (perf.sh tính theo số CPU của VM Docker)
    expected = "PERF_CPUSET_TOOLS" if service == "k6" else "PERF_CPUSET_SYSTEM"
    assert re.fullmatch(r"\$\{" + expected + r":-[0-9,-]+\}", str(SERVICES[service].get("cpuset", ""))), SERVICES[service].get("cpuset")


def test_be_pool_size_is_a_knob_defaulting_to_the_size_sp08_ran_with():
    # Thí nghiệm E3 (plan 6C 2C): chỉ đổi Maximum Pool Size; mặc định giữ 100 như SP-08 và SP-01
    assert "Maximum Pool Size=${PERF_DB_POOL:-100}" in _environment("be")["ConnectionStrings__DefaultConnection"]


def test_be_cpu_limit_is_a_knob_defaulting_to_two_cpus_like_every_earlier_run():
    # Thí nghiệm CPU cho Đăng nhập (05/11): chỉ BE đổi trần CPU, mọi container khác giữ nguyên
    assert SERVICES["be"]["cpus"] == "${PERF_BE_CPUS:-2}"
    assert all(not str(service.get("cpus", "")).startswith("${") for name, service in SERVICES.items() if name != "be")


def test_be_auto_prepare_is_a_knob_defaulting_to_off_like_the_deploy():
    # P6-54 (người dùng chốt 05/11): auto-prepare chỉ cho kiểm thử, đánh giá; bản deploy không bật ⇒ mặc định perf 0, lượt đánh giá --db-prepare 50.
    # Đi qua cấu hình của app, không qua chuỗi kết nối (chuỗi kết nối ghi rõ thì thắng cấu hình, sẽ che mất núm)
    be = _environment("be")
    assert be["Database__MaxAutoPrepare"] == "${PERF_DB_AUTO_PREPARE:-0}"
    assert "Auto Prepare" not in be["ConnectionStrings__DefaultConnection"]
    deploy = (STACK.parents[2] / "SmashCourt-BE" / "deploy" / "docker-compose.yml")
    if deploy.exists():  # workspace đủ 4 repo: bản deploy không bật thì mặc định perf (0) mới đúng là "như deploy"
        assert "MaxAutoPrepare" not in deploy.read_text(encoding="utf-8")


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


def test_telemetry_is_on_by_default_and_switched_by_one_knob_for_sp07():
    # SP-07 A/A (plan 6C B.7.4): cùng một biến tắt OpenTelemetry của cả ba app; mặc định bật như mọi lượt đo
    assert _environment("be")["Telemetry__Enabled"] == "${PERF_TELEMETRY:-true}"
    assert _environment("tadss")["Tadss__Telemetry__Enabled"] == "${PERF_TELEMETRY:-true}"
    assert _environment("ai")["OTEL_ENABLED"] == "${PERF_TELEMETRY:-true}"


def test_toxiproxy_runs_only_for_sp09_and_be_calls_tadss_directly_by_default():
    # SP-09 (người dùng chốt 07/11): Toxiproxy chỉ bật bằng profile faults; mọi lượt khác BE gọi thẳng TADSS như bản deploy
    assert SERVICES["toxiproxy"]["profiles"] == ["faults"]
    assert _environment("be")["TadssConfig__BaseUrl"] == "${PERF_TADSS_URL:-http://tadss:8080}"
    proxy = json.loads((STACK / "toxiproxy.json").read_text(encoding="utf-8"))
    assert proxy == [{"name": "tadss", "listen": "0.0.0.0:8666", "upstream": "tadss:8080", "enabled": True}]


def test_up_pulls_only_missing_images_so_a_registry_outage_does_not_abort_a_session():
    # Tag image là sha bất biến: đã có trên máy thì không kéo lại. 10/11 ghcr.io không kết nối được, `pull` luôn kéo
    # làm `perf.sh up` (set -e) dừng giữa chừng và lượt đo chạy trên môi trường dở dang
    script = (STACK.parent / "perf.sh").read_text(encoding="utf-8")
    pulls = [line.strip() for line in script.splitlines() if re.search(r"docker compose .* pull\b", line)]
    assert pulls, "perf.sh phải có bước pull image của be, tadss, ai"
    assert all("--policy missing" in line for line in pulls), pulls
