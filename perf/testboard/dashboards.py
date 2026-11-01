"""Dashboard "Kiểm thử" (task 2.6–2.9, plan 6C Mục 5.3): 3 tab BE / TADSS / AI, sinh JSON vào perf/grafana/dashboards/ (Grafana local nạp bằng provisioning).
Tầng 1: KPI SC1–SC8 + một ô mỗi nhóm test (màu theo trạng thái). Tầng 2: bảng mã của nhóm, mặc định chỉ mã hỏng, có link methodology và báo cáo.
Tầng 3: báo cáo gốc (link lượt CI hoặc thư mục perf/results/). Hàng hiệu năng (SP): k6 phía client cạnh số liệu phía server.
Mỗi panel có mô tả 6 ý (P6-49); `check()` chặn panel thiếu ý, thiếu mã test hoặc dùng metric ngoài danh sách."""
from __future__ import annotations

import json
import re
from pathlib import Path

from testboard import paths
from testboard.model import FAILED, LIMITED, PASSED, UNTESTED

TAG = "smashcourt-kiem-thu"
DATASOURCE = {"type": "prometheus", "uid": "prometheus"}
# Metric được dùng: của testboard (board.py), k6 (OTLP), app (OTLP), cAdvisor (Alloy). Ngoài danh sách ⇒ check() đỏ.
KNOWN_METRICS = {
    "smashcourt_test_code_state", "smashcourt_test_group_state", "smashcourt_test_kpi_state", "smashcourt_test_source_loaded_seconds",
    "k6_http_reqs_total", "k6_http_req_duration_milliseconds", "k6_vus",
    "http_server_request_duration_seconds", "tadss_decision_duration_milliseconds",
    "container_cpu_usage_seconds_total", "container_memory_working_set_bytes",
}
_METRIC = re.compile(r"\b([a-z][a-z0-9_]*?)(?:_bucket|_count|_sum)?\s*[{\[]")
_STATE_MAPPING = [{"type": "value", "options": {
    str(PASSED): {"text": "ĐẠT", "color": "green"},
    str(UNTESTED): {"text": "CHƯA CHẠY", "color": "#5a5f6b"},
    str(LIMITED): {"text": "HẠN CHẾ", "color": "yellow"},
    str(FAILED): {"text": "HỎNG", "color": "red"},
}}]
_STATE_TEXT = [{"type": "value", "options": {
    "passed": {"text": "Đạt", "color": "green"}, "untested": {"text": "Chưa chạy", "color": "#5a5f6b"},
    "limited": {"text": "Hạn chế", "color": "yellow"}, "failed": {"text": "Hỏng", "color": "red"},
}}]
_SERVER_LATENCY = {
    "BE": ('histogram_quantile({q}, sum by (le) (rate(http_server_request_duration_seconds_bucket{{job="smashcourt-be",http_route!~"/health.*"}}[$__rate_interval])))', "BE"),
    "TADSS": ('histogram_quantile({q}, sum by (le) (rate(tadss_decision_duration_milliseconds_bucket{{endpoint="POST api/v2/decisions"}}[$__rate_interval]))) / 1000', "TADSS"),
    "AI": ('histogram_quantile({q}, sum by (le) (rate(http_server_request_duration_seconds_bucket{{job="smashcourt-ai",http_route!~"/health.*"}}[$__rate_interval])))', "AI"),
}


def describe(what: str, unit: str, read: str, threshold: str, source: str, tests: str) -> str:
    return "\n".join([f"**Đo gì:** {what}", f"**Đơn vị:** {unit}", f"**Cách đọc:** {read}", f"**Ngưỡng:** {threshold}", f"**Nguồn:** {source}", f"**Mã test:** {tests}"])


def _target(expr: str, legend: str = "", instant: bool = False, table: bool = False, ref: str = "A") -> dict:
    target = {"refId": ref, "datasource": DATASOURCE, "expr": expr, "legendFormat": legend, "instant": instant, "range": not instant}
    if table:
        target["format"] = "table"
    return target


def _panel(kind: str, title: str, description: str, targets: list[dict], pos: tuple[int, int, int, int], **extra: object) -> dict:
    x, y, w, h = pos
    return {"type": kind, "title": title, "description": description, "datasource": DATASOURCE, "targets": targets,
            "gridPos": {"x": x, "y": y, "w": w, "h": h}, **extra}


def _row(title: str, y: int) -> dict:
    return {"type": "row", "title": title, "collapsed": False, "gridPos": {"x": 0, "y": y, "w": 24, "h": 1}, "panels": []}


def _state_stat(title: str, description: str, expr: str, pos: tuple[int, int, int, int], link: str | None = None) -> dict:
    defaults: dict = {"mappings": _STATE_MAPPING, "color": {"mode": "thresholds"},
                      "thresholds": {"mode": "absolute", "steps": [{"value": None, "color": "#5a5f6b"}]}, "noValue": "Chưa nạp kết quả"}
    if link:
        defaults["links"] = [{"title": "Xem mã của nhóm này", "url": link}]
    return _panel("stat", title, description, [_target(expr, "{{tile}}", instant=True)], pos,
                  fieldConfig={"defaults": defaults, "overrides": []},
                  options={"textMode": "value_and_name", "colorMode": "background", "graphMode": "none", "justifyMode": "center",
                           "orientation": "auto", "text": {"titleSize": 12, "valueSize": 16},
                           "reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False}})


def _timeseries(title: str, description: str, targets: list[dict], pos: tuple[int, int, int, int], unit: str) -> dict:
    return _panel("timeseries", title, description, targets, pos,
                  fieldConfig={"defaults": {"unit": unit, "noValue": "Chưa có lượt đo (môi trường perf, gói 6C-4)",
                                            "custom": {"lineWidth": 2, "fillOpacity": 0, "showPoints": "never", "spanNulls": True, "axisSoftMin": 0}},
                               "overrides": []},
                  options={"legend": {"displayMode": "list", "placement": "bottom", "showLegend": True}, "tooltip": {"mode": "multi", "sort": "desc"}})


def build(system: str) -> dict:
    uid = f"smashcourt-kiem-thu-{system.lower()}"
    systems = f'systems=~".*,{system},.*"'
    panels: list[dict] = []

    panels.append(_state_stat("KPI SC1–SC8", describe(
        "Kết luận của 8 KPI đánh giá (methodology Mục 5), lấy từ Evidence Pack của evaluator và báo cáo SC6 của BE.",
        "ĐẠT / HỎNG / HẠN CHẾ (lượt INFRA_LIMITED, INCONCLUSIVE) / CHƯA CHẠY.",
        "KPI là của cả đề tài nên hiện giống nhau ở 3 tab; bấm ⓘ và xem bảng 'Nguồn kết quả' để biết lượt nào sinh ra kết quả.",
        "Theo định nghĩa từng KPI (methodology Mục 5).", "smashcourt_test_kpi_state (perf/testboard)", "SC1–SC8"),
        "smashcourt_test_kpi_state", (0, 0, 24, 4)))

    panels.append(_row("Tầng 1 · Nhóm test", 4))
    panels.append(_state_stat(f"Nhóm test của {system}", describe(
        f"Mỗi ô là một nhóm test có mã trong danh mục (tests/catalog.yaml) thuộc {system}: số mã đạt / tổng số mã.",
        "Xanh = đạt hết; đỏ = có mã hỏng; vàng = có mã hạn chế hoặc mới chạy một phần; xám = chưa chạy mã nào.",
        "Bấm một ô ⇒ bảng 'Chi tiết mã' bên dưới lọc theo nhóm đó và hiện các mã chưa đạt.",
        "Mã đạt khi mọi ca của mã đạt (một ca đỏ là đủ để mã đỏ); KPI SC8: 100 % test đạt.",
        "smashcourt_test_group_state (perf/testboard)", "SC8"),
        f'smashcourt_test_group_state{{system="{system}"}}', (0, 5, 24, 5),
        link=f"/d/{uid}?var-group=${{__field.labels.group}}&var-state=failed&var-state=limited&var-state=untested&${{__url_time_range}}"))

    panels.append(_row("Tầng 2 · Chi tiết mã (mặc định chỉ mã hỏng; đổi bộ lọc 'Trạng thái' ở đầu trang)", 10))
    hidden = {name: True for name in ("Time", "Value", "__name__", "job", "instance", "systems", "group", "link", "exported_job")}
    panels.append(_panel("table", "Chi tiết mã", describe(
        "Từng mã test của nhóm đang chọn: tên, trạng thái, ca đỏ đầu tiên, commit và lượt sinh ra kết quả.",
        "Một dòng một mã.",
        "Bấm mã ⇒ mở mô tả trong methodology; cột 'Báo cáo' là lượt CI (link) hoặc thư mục perf/results/ chứa báo cáo gốc (tầng 3).",
        "Như ô nhóm.", "smashcourt_test_code_state (perf/testboard)", "SC8"),
        [_target(f'smashcourt_test_code_state{{{systems},group=~"$group",state=~"$state"}}', instant=True, table=True)], (0, 11, 24, 11),
        transformations=[{"id": "organize", "options": {
            "excludeByName": hidden,
            "indexByName": {"code": 0, "title": 1, "group_label": 2, "state": 3, "detail": 4, "commit": 5, "source": 6, "report": 7, "tool": 8},
            "renameByName": {"code": "Mã", "title": "Nội dung", "group_label": "Nhóm", "state": "Trạng thái", "detail": "Ca đỏ / ghi chú",
                             "commit": "Commit", "source": "Nguồn", "report": "Báo cáo", "tool": "Công cụ"}}}],
        fieldConfig={"defaults": {"custom": {"align": "left", "filterable": True}, "noValue": "Không có mã nào ở trạng thái đã chọn"}, "overrides": [
            {"matcher": {"id": "byName", "options": "Mã"}, "properties": [{"id": "custom.width", "value": 90},
                {"id": "links", "value": [{"title": "Mô tả trong methodology", "url": "${__data.fields.link}", "targetBlank": True}]}]},
            {"matcher": {"id": "byName", "options": "Trạng thái"}, "properties": [{"id": "custom.width", "value": 100},
                {"id": "mappings", "value": _STATE_TEXT}, {"id": "custom.cellOptions", "value": {"type": "color-background"}}]},
            {"matcher": {"id": "byName", "options": "Nội dung"}, "properties": [{"id": "custom.width", "value": 420}]},
            {"matcher": {"id": "byName", "options": "Báo cáo"}, "properties": [
                {"id": "links", "value": [{"title": "Mở báo cáo", "url": "${__value.raw}", "targetBlank": True}]}]},
        ]},
        options={"showHeader": True, "cellHeight": "sm", "sortBy": [{"displayName": "Mã", "desc": False}]}))
    panels.append(_panel("table", "Nguồn kết quả", describe(
        "Lần nạp gần nhất của từng nguồn: CI ba repo, Evidence Pack, k6, SC6, Playwright.",
        "Thời gian tương đối kể từ lần nạp.",
        "Nguồn cũ ⇒ chạy lại lệnh nạp (docs/guides/TEST_DASHBOARD.md). Kết quả trên dashboard là gộp các nguồn này.",
        "Không có ngưỡng.", "smashcourt_test_source_loaded_seconds (perf/testboard)", "SC8"),
        [_target("smashcourt_test_source_loaded_seconds * 1000", instant=True, table=True)], (0, 22, 24, 7),
        transformations=[{"id": "organize", "options": {
            "excludeByName": {"Time": True, "__name__": True, "job": True, "instance": True, "exported_job": True},
            "renameByName": {"source": "Nguồn", "commit": "Commit", "run": "Lượt", "report": "Báo cáo", "Value": "Nạp lúc"}}}],
        fieldConfig={"defaults": {"custom": {"align": "left"}}, "overrides": [
            {"matcher": {"id": "byName", "options": "Nạp lúc"}, "properties": [{"id": "unit", "value": "dateTimeFromNow"}]},
            {"matcher": {"id": "byName", "options": "Báo cáo"}, "properties": [
                {"id": "links", "value": [{"title": "Mở báo cáo", "url": "${__value.raw}", "targetBlank": True}]}]},
        ]}, options={"showHeader": True, "cellHeight": "sm"}))

    panels.append(_row("Hiệu năng (SP) · k6 phía client cạnh server", 29))
    server_expr, server_name = _SERVER_LATENCY[system]
    tests = "SP-10" if system == "AI" else "SP-01…09"
    panels.append(_timeseries("Tải: request mỗi giây và VU", describe(
        "Số request k6 gửi mỗi giây và số người dùng ảo (VU) đang chạy.", "Request/giây; VU.",
        "Đường request phải đi theo kịch bản (ramp-up, giữ, spike); lệch nhiều ⇒ k6 hoặc máy đo bị nghẽn (INFRA_LIMITED).",
        "Theo kịch bản của mã SP (plan 6C Phụ lục B.7.4).", "k6_http_reqs_total, k6_vus (k6 --out opentelemetry)", tests),
        [_target("sum(rate(k6_http_reqs_total[$__rate_interval]))", "request/giây"), _target("max(k6_vus)", "VU", ref="B")], (0, 30, 8, 8), "short"))
    panels.append(_timeseries("Độ trễ p95 / p99: client (k6) cạnh server", describe(
        f"p95, p99 thời gian phản hồi đo ở k6 (gồm mạng) và đo trong {server_name}; chênh lệch là phần ngoài xử lý (mạng, hàng đợi).",
        "Giây.", "Client > server nhiều ⇒ nghẽn trước khi vào ứng dụng. Không cộng percentile của các tầng (P6-43).",
        "SLO theo hệ (P6-52): BE chốt ở task 2.18; TADSS SC1 p95 < 150 ms, SC2 p99 ≤ 700 ms; AI p95 < 300 ms (không gồm LLM).",
        f"k6_http_req_duration_milliseconds (k6), {'tadss_decision_duration_milliseconds' if system == 'TADSS' else 'http_server_request_duration_seconds'} (OTLP)", tests),
        [_target("histogram_quantile(0.95, sum by (le) (rate(k6_http_req_duration_milliseconds_bucket[$__rate_interval]))) / 1000", "client p95"),
         _target("histogram_quantile(0.99, sum by (le) (rate(k6_http_req_duration_milliseconds_bucket[$__rate_interval]))) / 1000", "client p99", ref="B"),
         _target(server_expr.format(q="0.95"), f"server p95 ({server_name})", ref="C"),
         _target(server_expr.format(q="0.99"), f"server p99 ({server_name})", ref="D")], (8, 30, 8, 8), "s"))
    panels.append(_timeseries("Tỉ lệ request lỗi (k6)", describe(
        "Phần trăm request k6 nhận 5xx hoặc lỗi mạng (status 0).", "Phần trăm.",
        "Tăng cùng lúc độ trễ tăng ⇒ đã gần điểm gãy (SP-05).", "Theo threshold của kịch bản (thường < 1 %).",
        "k6_http_reqs_total, nhãn status (k6)", tests),
        [_target('(sum(rate(k6_http_reqs_total{status=~"5..|0"}[$__rate_interval])) or vector(0)) / sum(rate(k6_http_reqs_total[$__rate_interval]))', "lỗi")], (16, 30, 8, 8), "percentunit"))
    panels.append(_timeseries("CPU theo container (perf)", describe(
        "CPU từng container của môi trường perf trong lượt đo.", "Số lõi.",
        "Container chạm giới hạn CPU trong khi độ trễ tăng ⇒ nút cổ chai tài nguyên.",
        "Giới hạn từng container ghi trong compose perf (plan 6C Mục 6).", "container_cpu_usage_seconds_total (cAdvisor qua Alloy)", tests),
        [_target('sum by (name) (rate(container_cpu_usage_seconds_total{container_label_com_docker_compose_project="smashcourt-perf"}[$__rate_interval]))', "{{name}}")],
        (0, 38, 12, 8), "short"))
    panels.append(_timeseries("RAM theo container (perf)", describe(
        "Bộ nhớ từng container của môi trường perf.", "Byte.", "Tăng đều không giảm suốt lượt soak (SP-04) ⇒ rò rỉ.",
        "Giới hạn từng container ghi trong compose perf.", "container_memory_working_set_bytes (cAdvisor qua Alloy)", tests),
        [_target('max by (name) (container_memory_working_set_bytes{container_label_com_docker_compose_project="smashcourt-perf"})', "{{name}}")],
        (12, 38, 12, 8), "bytes"))

    return {
        "uid": uid, "title": f"SmashCourt Kiểm thử - {system}", "tags": [TAG], "timezone": "browser", "schemaVersion": 39,
        "refresh": "1m", "time": {"from": "now-7d", "to": "now"}, "graphTooltip": 1,
        "links": [{"type": "dashboards", "tags": [TAG], "asDropdown": False, "includeVars": False, "keepTime": True, "title": "Kiểm thử"}],
        "templating": {"list": [
            {"name": "group", "label": "Nhóm", "type": "query", "datasource": DATASOURCE, "refresh": 2, "multi": True, "includeAll": True,
             "query": {"query": f"label_values(smashcourt_test_code_state{{{systems}}}, group)", "refId": "group"},
             "current": {"text": "All", "value": "$__all"}},
            {"name": "state", "label": "Trạng thái", "type": "custom", "multi": True, "includeAll": True,
             "query": "failed,limited,untested,passed", "current": {"text": "failed", "value": ["failed"]},
             "options": [{"text": s, "value": s, "selected": s == "failed"} for s in ("failed", "limited", "untested", "passed")]},
        ]},
        "panels": panels,
    }


def check() -> list[str]:
    """Panel nào cũng có mô tả đủ 6 ý và mã test; PromQL chỉ dùng metric trong KNOWN_METRICS (task 2.9)."""
    errors = []
    for system in ("BE", "TADSS", "AI"):
        for panel in build(system)["panels"]:
            if panel["type"] == "row":
                continue
            where = f"{system} / {panel['title']}"
            description = panel.get("description", "")
            for part in ("Đo gì", "Đơn vị", "Cách đọc", "Ngưỡng", "Nguồn", "Mã test"):
                if not re.search(rf"\*\*{part}:\*\* \S", description):
                    errors.append(f"{where}: mô tả thiếu ý '{part}'")
            if not re.search(r"(SC\d|[A-Z]+-\d)", description.split("**Mã test:**")[-1]):
                errors.append(f"{where}: thiếu mã test")
            for target in panel["targets"]:
                for name in set(_METRIC.findall(target["expr"])) - KNOWN_METRICS - {"by", "sum", "max", "rate", "avg"}:
                    errors.append(f"{where}: metric '{name}' không có trong KNOWN_METRICS")
    return errors


def render(system: str) -> str:
    return json.dumps(build(system), ensure_ascii=False, indent=2) + "\n"


def write(directory: Path = paths.DASHBOARD_DIR) -> list[Path]:
    directory.mkdir(parents=True, exist_ok=True)
    written = []
    for system in ("BE", "TADSS", "AI"):
        path = directory / f"testboard-{system.lower()}.json"
        path.write_text(render(system), encoding="utf-8", newline="\n")
        written.append(path)
    return written
