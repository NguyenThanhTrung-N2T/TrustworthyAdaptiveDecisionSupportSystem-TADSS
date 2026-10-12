"""modes.json khớp bảng hồ sơ tải của Task 8 / methodology 4.6."""
import json
from pathlib import Path

from evaluator.steps.load_step import duration_seconds

MODES = json.loads((Path(__file__).resolve().parents[3] / "tests" / "load" / "config" / "modes.json").read_text(encoding="utf-8"))


def test_modes_config_quick() -> None:
    quick = MODES["quick"]

    assert set(quick) == {"lt01", "lt02", "lt03", "scale2"}, "bản nhanh không có LT-05"
    lt01 = quick["lt01"]
    assert (lt01["executor"], lt01["rate"], lt01["warmup"], lt01["duration"], lt01["runs"]) == ("constant-arrival-rate", 50, "30s", "2m", 1)
    lt02 = quick["lt02"]
    assert lt02["executor"] == "ramping-arrival-rate"
    assert (lt02["steps"][0], lt02["steps"][-1]) == (10, 200)
    assert len(lt02["steps"]) * duration_seconds(lt02["hold"]) == 180, "10 → 200 RPS trong 3 phút"


def test_modes_config_full() -> None:
    full = MODES["full"]

    assert set(full) == {"lt01", "lt02", "lt05", "lt03", "scale2"}
    lt01 = full["lt01"]
    assert (lt01["rate"], lt01["warmup"], lt01["duration"], lt01["runs"]) == (50, "1m", "10m", 3)
    lt02 = full["lt02"]
    assert (lt02["steps"][0], lt02["steps"][-1]) == (10, 400)
    total_minutes = len(lt02["steps"]) * (duration_seconds(lt02["hold"]) + 1) / 60
    assert 13 <= total_minutes <= 16, "khoảng 15 phút"
    lt05 = full["lt05"]
    assert (lt05["rate"], lt05["warmup"], lt05["duration"]) == (50, "1m", "60m")


def test_lt02_preallocates_enough_vus_to_not_drop_iterations_in_main_phase() -> None:
    """Lượt r1 dùng tối đa 317 VU và bỏ ~184 lượt ở pha chính vì k6 cấp VU giữa chừng không kịp ⇒ cấp sẵn 400 ở cả hai chế độ."""
    for mode in ("quick", "full"):
        assert MODES[mode]["lt02"]["preAllocatedVUs"] == 400
        assert MODES[mode]["lt02"]["maxVUs"] >= 400


def test_every_profile_has_enough_vus_to_avoid_dropping_iterations() -> None:
    for mode in MODES.values():
        for profile in mode.values():
            assert profile["maxVUs"] >= profile["preAllocatedVUs"] >= 100
