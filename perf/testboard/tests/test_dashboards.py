# Task 2.6–2.9: 3 dashboard "Kiểm thử" khớp file JSON đã sinh, mọi panel có mô tả 6 ý, chỉ dùng metric khai báo, lọc đúng tab
import json

import pytest
from testboard import dashboards, paths


@pytest.mark.parametrize("system", ["BE", "TADSS", "AI"])
def test_generated_json_is_up_to_date(system):
    path = paths.DASHBOARD_DIR / f"testboard-{system.lower()}.json"

    assert path.read_text(encoding="utf-8") == dashboards.render(system), "chạy `python perf/board.py dashboards`"


def test_every_panel_has_six_point_description_and_known_metrics():
    assert dashboards.check() == []


def test_check_reports_missing_description_part_and_unknown_metric(monkeypatch):
    original = dashboards.build

    def broken(system):
        board = original(system)
        panel = next(p for p in board["panels"] if p["type"] == "timeseries")
        panel["description"] = panel["description"].replace("**Ngưỡng:**", "**Nguong:**")
        panel["targets"][0]["expr"] = "sum(rate(khong_ton_tai_total[1m]))"
        return board

    monkeypatch.setattr(dashboards, "build", broken)

    errors = dashboards.check()

    assert any("Ngưỡng" in error for error in errors) and any("khong_ton_tai_total" in error for error in errors)


@pytest.mark.parametrize("system", ["BE", "TADSS", "AI"])
def test_tier_one_and_two_are_filtered_to_the_tab_system(system):
    board = dashboards.build(system)
    exprs = {p["title"]: p["targets"][0]["expr"] for p in board["panels"] if p["type"] != "row"}

    assert exprs[f"Nhóm test của {system}"] == f'smashcourt_test_group_state{{system="{system}"}}'
    assert f',{system},' in exprs["Chi tiết mã"] and '$state' in exprs["Chi tiết mã"]
    state_var = next(v for v in board["templating"]["list"] if v["name"] == "state")
    assert state_var["current"]["value"] == ["failed"]  # tầng 2 mặc định chỉ hiện mã hỏng


def test_dashboards_link_to_each_other_by_tag():
    uids = {dashboards.build(system)["uid"] for system in ("BE", "TADSS", "AI")}
    tags = {json.dumps(dashboards.build(system)["links"]) for system in ("BE", "TADSS", "AI")}

    assert len(uids) == 3 and len(tags) == 1
