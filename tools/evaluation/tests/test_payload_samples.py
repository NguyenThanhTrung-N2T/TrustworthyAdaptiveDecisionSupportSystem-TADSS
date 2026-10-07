"""Bộ sinh payload của k6 (lib/payload.js): mẫu hợp lệ theo JSON schema v2 và phủ mọi ô của bảng trộn. Cần k6 (binary hoặc image grafana/k6); không có thì bỏ qua."""
import json
import shutil
import subprocess
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator, FormatChecker

from evaluator import d2

LOAD_DIR = Path(__file__).resolve().parents[3] / "tests" / "load"
SCHEMA = json.loads((Path(__file__).resolve().parents[3] / "docs" / "contracts" / "schemas" / "v2" / "decision-request.schema.json").read_text(encoding="utf-8"))
SAMPLES = 500
K6_IMAGE = "grafana/k6:2.3.0"
REQUIRED_CELLS = {
    "PEAK_WEEKDAY", "OFFPEAK_WEEKDAY", "PEAK_WEEKEND", "OFFPEAK_WEEKEND",
    "GUEST", "STANDARD", "SILVER", "GOLD", "PLATINUM", "DIAMOND",
    "NORMAL", "HIGH_VALUE", "ONE", "TWO", "THREE",
}


def _generate_samples(count: int) -> list[dict]:
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "samples.log"
        if shutil.which("k6"):
            command = ["k6", "run", "-e", f"SAMPLE_ONLY={count}", "--console-output", str(out), "--quiet", str(LOAD_DIR / "scenarios" / "lt01.js")]
        elif shutil.which("docker"):
            command = [
                "docker", "run", "--rm", "-v", f"{LOAD_DIR}:/load:ro", "-v", f"{tmp}:/out", "-w", "/load", K6_IMAGE,
                "run", "-e", f"SAMPLE_ONLY={count}", "--console-output", "/out/samples.log", "--quiet", "scenarios/lt01.js",
            ]
        else:
            pytest.skip("cần k6 (binary hoặc docker) để sinh mẫu payload")
        completed = subprocess.run(command, capture_output=True, text=True, check=False, timeout=300)
        if completed.returncode != 0:
            pytest.skip(f"không chạy được k6 ({completed.returncode}): {completed.stderr[:200]}")
        return [entry for entry in d2.read_entries(out) if "body" in entry]


@pytest.fixture(scope="module")
def samples() -> list[dict]:
    return _generate_samples(SAMPLES)


def test_payload_samples_valid_schema(samples: list[dict]) -> None:
    assert len(samples) == SAMPLES
    validator = Draft202012Validator(SCHEMA, format_checker=FormatChecker())
    invalid = [(index, [error.message for error in validator.iter_errors(sample["body"])][:2]) for index, sample in enumerate(samples)]
    assert [item for item in invalid if item[1]] == [], "mọi mẫu phải hợp lệ schema v2"


def test_payload_play_times_are_in_the_future_and_items_valid(samples: list[dict]) -> None:
    now = datetime.now(timezone.utc)
    for sample in samples:
        body = sample["body"]
        requested = datetime.fromisoformat(body["requestedAt"])
        assert abs(requested - now) < timedelta(minutes=5), "requestedAt là thời điểm sinh"
        for item in body["items"]:
            start, end = datetime.fromisoformat(item["startTime"]), datetime.fromisoformat(item["endTime"])
            assert start - requested >= timedelta(days=6), "thời gian chơi đủ xa để cửa sổ duyệt tồn tại"
            assert timedelta(minutes=30) <= end - start <= timedelta(minutes=720), "thời lượng item trong giới hạn P-01"
        assert len({item["resourceId"] for item in body["items"]}) == len(body["items"]), "mỗi item một sân khác nhau"
        assert body["totalPrice"] == sum(item["price"] for item in body["items"])
    assert len({sample["body"]["reservationId"] for sample in samples}) == SAMPLES, "mỗi payload một đơn mới"


def test_payload_mix_covers_every_cell(samples: list[dict]) -> None:
    seen = {cell for sample in samples for cell in sample["cells"]}
    assert REQUIRED_CELLS <= seen, f"thiếu ô: {sorted(REQUIRED_CELLS - seen)}"
    for sample in samples:
        body = sample["body"]
        if "HIGH_VALUE" in sample["cells"]:
            assert body["totalPrice"] > 2_000_000
        else:
            assert body["totalPrice"] <= 2_000_000
        if "GUEST" in sample["cells"]:
            assert body["customer"]["isGuest"] is True and "customerId" not in body["customer"]
        local_start = datetime.fromisoformat(body["items"][0]["startTime"])
        weekend = local_start.weekday() >= 5
        assert ("PEAK_WEEKEND" in sample["cells"] or "OFFPEAK_WEEKEND" in sample["cells"]) == weekend, "nhãn ô khớp thứ trong tuần theo giờ chi nhánh"
