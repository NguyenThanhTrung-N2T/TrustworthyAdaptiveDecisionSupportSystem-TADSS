"""Báo cáo tổng kết chất lượng một file HTML cho người đọc không chuyên (người dùng chốt 19/11): sinh lại sau mỗi lần nạp kết quả.
Đọc đúng bảng đã gộp của dashboard "Kiểm thử" (state.json + danh mục), không thêm nguồn số liệu; bản kỹ thuật đầy đủ nằm ở phụ lục cuối file."""
from __future__ import annotations

import html
import re
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from testboard import board, paths
from testboard.catalog import CatalogEntry
from testboard.model import FAILED, LIMITED, PASSED, UNTESTED, SourceResult
from testboard.sources import manual_records

LATEST_NAME = "BAO_CAO_MOI_NHAT.html"
REPORTS_DIR_NAME = "reports"
KEEP_REPORTS = 30
_VN_OFFSET_HOURS = 7


@dataclass(frozen=True)
class Question:
    text: str
    why: str
    groups: tuple[str, ...] = ()
    codes: tuple[str, ...] = ()
    kpis: tuple[str, ...] = ()
    numbers_from: str = ""  # mã có số đo tốc độ để nói bằng lời trong câu trả lời (chỉ một mã, tránh dồn số của nhiều lượt)


# Câu hỏi người đọc thường quan tâm ⇒ nhóm / mã test trả lời câu đó (mã lấy từ tests/catalog.yaml)
QUESTIONS = (
    Question("Đặt sân, đặt cọc, duyệt đơn, thanh toán, hủy có chạy đúng không?",
             "Đây là các chức năng khách và nhân viên dùng hằng ngày; sai là mất tiền hoặc mất sân.", ("BE-UT", "BE-IT")),
    Question("Bộ quyết định mức cọc (TADSS) có tính đúng không?",
             "TADSS quyết định khách cọc bao nhiêu phần trăm; tính sai thì thu thiếu hoặc làm khó khách.", ("UT", "IT", "CT", "MT"), kpis=("SC3", "SC4", "SC8")),
    Question("Lúc lượng khách bình thường, đặt sân có nhanh không?",
             "Khách phải chờ lâu thì bỏ đi.", codes=("SP-01", "LT-01", "LT-03", "LT-04"), kpis=("SC1",),
             numbers_from="SP-01"),
    Question("Khi đông khách đột ngột hoặc quá tải thì sao?",
             "Giờ cao điểm, khuyến mãi có thể làm lượng khách tăng vọt.", codes=("SP-02", "SP-03", "SP-05", "SP-08", "LT-02")),
    Question("Chạy liên tục nhiều giờ có ổn định không?",
             "Hệ thống chạy cả ngày; nếu chậm dần hoặc tốn bộ nhớ dần thì sẽ hỏng sau một thời gian.", codes=("SP-04", "LT-05")),
    Question("TADSS gặp sự cố thì khách còn đặt được sân không?",
             "Bộ quyết định cọc là phần mới; hỏng nó không được làm hỏng việc đặt sân (khi đó thu cọc 100 %).",
             ("FI",), ("SP-09", "SP-06"), ("SC2", "SC5")),
    Question("Dữ liệu đặt sân và tiền cọc có bị sai không?",
             "Sai dữ liệu (thu thiếu tiền, giữ sân sai, trùng giờ) là lỗi nghiêm trọng nhất.", codes=("BE-IT-20", "OB-BE-5"), kpis=("SC6",)),
    Question("Hệ thống có an toàn không?",
             "Không để lộ khóa bí mật, không cho người lạ gọi vào phần nội bộ.", ("SEC", "SEC-BE", "SEC-AI", "NG")),
    Question("Trợ lý AI có hoạt động đúng không?", "Chatbot và gợi ý cho khách, quản lý.", ("AI-UT", "AI-CT"), ("SP-10",)),
    Question("Triển khai, theo dõi và báo động khi có sự cố có hoạt động không?",
             "Có sự cố thì nhóm phải biết ngay (email) và đưa bản cũ về được.", ("OPS", "OB", "OB-BE", "OB-AI", "DOC", "SCALE"), ("SP-07",)),
    Question("Áp dụng được cho lĩnh vực khác và dữ liệu thật không?", "Mục tiêu nghiên cứu của TADSS (đồ án 2).", ("RP", "PT"), kpis=("SC7",)),
    Question("Giao diện web (khách, nhân viên, quản lý) có chạy đúng không?",
             "Phần người dùng nhìn thấy; kiểm bằng cách bấm thử tự động trên trình duyệt.", ("FE", "E2E")),
)

# Tên dễ hiểu cho các mã hay xuất hiện ở mục "Cần lưu ý"; mã khác dùng tên trong danh mục
PLAIN_TITLES = {
    "SP-01": "Đo tốc độ với lượng khách bình thường (15 phút, 3 lần)",
    "SP-02": "Tăng dần lượng khách tới gấp 2,5 lần",
    "SP-03": "Lượng khách tăng vọt gấp 5 lần trong 1 phút",
    "SP-04": "Chạy liên tục 90 phút",
    "SP-05": "Tìm mức khách tối đa trước khi chậm",
    "SP-06": "So sánh có / không có TADSS",
    "SP-07": "Chi phí của việc theo dõi hệ thống",
    "SP-08": "Đo năng lực ban đầu",
    "SP-09": "TADSS hỏng trong lúc đông khách",
    "SP-10": "Trợ lý AI khi đông người dùng",
    "LT-01": "TADSS: 50 yêu cầu mỗi giây trong 10 phút",
    "LT-02": "TADSS: tăng dần tới quá tải",
    "LT-04": "Đặt sân qua máy chủ, 20 người cùng lúc",
    "LT-05": "TADSS: chạy liên tục 60 phút",
    "FI-11": "TADSS bị tắt hẳn",
    "FI-12": "TADSS trả lời rất chậm",
    "FI-13": "Tắt TADSS bằng công tắc khi đang chạy",
    "FI-14": "Đứt kết nối tới TADSS",
    "OPS-08": "Triển khai tự động từ chối bản sai",
    "OPS-09": "Triển khai tự động đúng phần vừa sửa",
    "OPS-10": "Bản mới hỏng thì tự quay về bản cũ",
    "SEC-AI-2": "Máy chủ gọi AI phải có khóa",
    "IT-07": "Nhiều quản lý cùng duyệt một đơn trong cùng lúc",
}
# Tên thường và "nếu không đạt thì sao" của từng nhóm test (khóa = nhóm trong tests/catalog.yaml)
GROUPS_PLAIN = {
    "UT": ("Kiểm tự động từng phần của TADSS", "TADSS có thể tính sai mức cọc"),
    "IT": ("Kiểm tự động TADSS với cơ sở dữ liệu thật", "TADSS có thể lưu hoặc trả lời sai khi chạy thật"),
    "CT": ("Thỏa thuận dữ liệu giữa máy chủ đặt sân và TADSS", "hai bên có thể hiểu sai dữ liệu của nhau"),
    "MT": ("Kiểm chất lượng của chính bộ test TADSS", "bộ test có thể bỏ sót lỗi"),
    "FI": ("Thử gây sự cố có chủ đích", "khi có sự cố khách có thể không đặt được sân"),
    "LT": ("Thử nhiều người dùng cùng lúc", "hệ thống có thể chậm khi đông người"),
    "BE-UT": ("Kiểm tự động từng phần của máy chủ đặt sân", "đặt sân, cọc, thanh toán có thể sai"),
    "BE-IT": ("Kiểm tự động máy chủ đặt sân với cơ sở dữ liệu thật", "đặt sân, cọc, thanh toán có thể sai khi chạy thật"),
    "FE": ("Kiểm giao diện web", "màn hình có thể hiển thị hoặc thao tác sai"),
    "E2E": ("Thử trọn luồng từ giao diện web tới máy chủ", "khách có thể gặp lỗi khi dùng thật"),
    "RP": ("Chạy lại trên dữ liệu đặt sân thật", "quyết định cọc có thể không hợp với khách thật"),
    "PT": ("Thử áp dụng TADSS cho lĩnh vực khác", "TADSS có thể chỉ dùng được cho sân cầu lông"),
    "SEC": ("Bảo mật TADSS", "người lạ có thể gọi vào TADSS hoặc làm lộ thông tin"),
    "NG": ("Mở TADSS ra Internet an toàn", "cấu hình mở ra Internet có thể không an toàn"),
    "SCALE": ("Chạy nhiều bản TADSS cùng lúc", "khó mở rộng khi đông khách"),
    "OB": ("Theo dõi TADSS", "TADSS có sự cố mà nhóm không thấy"),
    "DOC": ("Tài liệu", "tài liệu có thể sai hoặc thiếu"),
    "OPS": ("Đóng gói và triển khai tự động", "triển khai có thể hỏng hoặc không quay về bản cũ được"),
    "OB-BE": ("Theo dõi máy chủ đặt sân", "máy chủ có sự cố hoặc dữ liệu sai mà nhóm không thấy"),
    "OB-AI": ("Theo dõi trợ lý AI", "AI lỗi mà nhóm không thấy"),
    "SEC-BE": ("Bảo mật máy chủ đặt sân", "máy chủ có thể dùng thư viện có lỗ hổng"),
    "SEC-AI": ("Bảo mật trợ lý AI", "khóa AI có thể bị lộ hoặc AI bị gọi trái phép"),
    "AI-UT": ("Kiểm tự động trợ lý AI", "chatbot có thể trả lời lỗi"),
    "AI-CT": ("Thỏa thuận dữ liệu giữa máy chủ đặt sân và AI", "hai bên có thể hiểu sai dữ liệu"),
    "SP": ("Đo tốc độ toàn hệ thống", "khách có thể phải chờ lâu"),
}
# Hậu quả riêng của một số mã đo (thay câu chung của nhóm): nói theo tiêu chí, không theo số của một lượt
CODE_RISKS = {
    "SP-04": "bộ nhớ máy chủ có thể tăng dần khi chạy nhiều ngày và phải khởi động lại (tiêu chí: 90 phút không tăng quá 10 %)",
    "SP-05": "hệ thống có thể gãy ở lượng khách thấp hơn dự kiến",
    "LT-05": "TADSS có thể chậm dần khi chạy lâu",
    "SP-09": "khi TADSS hỏng khách có thể không đặt được sân",
}
# Tên các nhóm thao tác trong số đo k6 (perf/k6/slo.json)
_LOAD_GROUPS = {"read": "xem sân, lịch", "booking": "đặt sân", "login": "đăng nhập", "ipn": "nhận kết quả thanh toán", "write": "hủy, sửa đơn"}
_P95 = re.compile(r"\b(read|booking|login|ipn|write) p95 (\d+) ms")

_STATE_VIEW = {PASSED: ("ok", "Đạt"), LIMITED: ("warn", "Đạt, có lưu ý"), FAILED: ("bad", "Chưa đạt"), UNTESTED: ("none", "Chưa kiểm")}


def plain_numbers(detail: str) -> str:
    """'read p95 15 ms, booking p95 87 ms' ⇒ '95 % lượt xem sân, lịch xong trong 0,015 giây; …' (số đo k6 nói bằng lời)."""
    parts = [f"95 % lượt {_LOAD_GROUPS[name]} xong trong {int(ms) / 1000:.3f}".replace(".", ",") + " giây" for name, ms in _P95.findall(detail)]
    return "; ".join(dict.fromkeys(parts))


def _vn_time(moment: datetime) -> str:
    local = moment.astimezone(timezone.utc).timestamp() + _VN_OFFSET_HOURS * 3600
    return datetime.fromtimestamp(local, timezone.utc).strftime("%H:%M %d/%m/%Y")


def _items(question: Question, merged: dict[str, board.MergedCode]) -> list[board.MergedCode]:
    return [item for code, item in merged.items() if item.entry.group in question.groups or code in question.codes]


def _kpi_states(question: Question, sources: dict[str, SourceResult]) -> list[int]:
    return [max((source.kpis[kpi].state for source in sources.values() if kpi in source.kpis), default=UNTESTED) for kpi in question.kpis]


def answer(question: Question, merged: dict[str, board.MergedCode], sources: dict[str, SourceResult]) -> tuple[int, str]:
    """Trạng thái của câu hỏi (xấu nhất trong các mục đã kiểm) và câu trả lời bằng lời."""
    items = _items(question, merged)
    states = [item.state for item in items] + [state for state in _kpi_states(question, sources) if state != UNTESTED]
    tested = [state for state in states if state != UNTESTED]
    untested = sum(1 for item in items if item.state == UNTESTED)
    if not tested:
        return UNTESTED, f"Chưa kiểm ({len(items)} phép kiểm chưa chạy)."
    state = max(tested)
    done = sum(1 for item in items if item.state != UNTESTED)
    text = {PASSED: "Có.", LIMITED: "Có, nhưng còn điểm cần lưu ý (xem mục dưới).", FAILED: "Chưa: có phép kiểm không đạt (xem mục dưới)."}[state]
    text += f" Đã kiểm {done}/{len(items)} phép kiểm" + (f", còn {untested} chưa kiểm." if untested else ", đủ cả.")
    numbers = plain_numbers(merged[question.numbers_from].detail) if question.numbers_from in merged else ""
    return state, text + (f" Số đo: {numbers}." if numbers else "")


def meaning(item: board.MergedCode, manual_plain: dict[str, str] | None = None) -> str:
    """Một câu bằng lời thường cho mục "Cần lưu ý": vì sao chưa đạt trọn và nếu bỏ qua thì sao.
    manual_plain: câu `plain` của từng mã trong tests/manual-results.yaml (người kiểm tay tự viết cho người đọc thường)."""
    risk = CODE_RISKS.get(item.entry.code) or GROUPS_PLAIN.get(item.entry.group, ("", "có thể gây lỗi"))[1]
    if "KHÔNG ĐỦ DỮ LIỆU" in item.detail:
        return "Các chỉ tiêu chính đều đạt; riêng vài thao tác ít xảy ra có quá ít lần đo nên chưa đủ để kết luận chắc chắn."
    if item.source == "manual":
        plain = (manual_plain or {}).get(item.entry.code)
        return plain or "Kiểm tay; xem chi tiết kỹ thuật bên dưới."
    if item.state == FAILED and item.source.startswith(("ci-", "local-")):
        return f"Một phép kiểm tự động bị lỗi ở lượt chạy gần nhất. Nếu là lỗi thật thì {risk}; cần xem lại hoặc chạy lại."
    if item.state == FAILED:
        return f"Không đạt tiêu chí đã đặt trước. Nếu không xử lý thì {risk}."
    return f"Chạy được nhưng còn điều kiện chưa đủ; nếu bỏ qua thì {risk}."


def _title(entry: CatalogEntry) -> str:
    return PLAIN_TITLES.get(entry.code, entry.title)


def _badge(state: int) -> str:
    css, label = _STATE_VIEW[state]
    return f'<span class="b {css}">{label}</span>'


def render(catalog: dict[str, CatalogEntry], sources: dict[str, SourceResult], now: datetime | None = None) -> str:
    now = now or datetime.now(timezone.utc)
    manual_plain = {str(record.get("code")): str(record["plain"]) for record in manual_records(paths.MANUAL_FILE)
                    if isinstance(record, dict) and record.get("plain")} if paths.MANUAL_FILE.is_file() else {}
    merged = {item.entry.code: item for item in board.merge(catalog, sources)}
    counts = {state: sum(1 for item in merged.values() if item.state == state) for state in _STATE_VIEW}
    if counts[FAILED]:
        verdict, css = f"CHƯA ĐẠT HẾT: {counts[FAILED]} phép kiểm không đạt", "bad"
    elif counts[LIMITED]:
        verdict, css = "ĐẠT, CÓ ĐIỂM CẦN LƯU Ý", "warn"
    else:
        verdict, css = "ĐẠT", "ok"
    tested = counts[PASSED] + counts[LIMITED] + counts[FAILED]
    versions = ", ".join(f"{name.removeprefix('ci-').upper()} {sources[name].commit}" for name in ("ci-be", "ci-tadss", "ci-ai") if name in sources)

    rows = []
    for question in QUESTIONS:
        state, text = answer(question, merged, sources)
        rows.append(f"<tr><td><b>{html.escape(question.text)}</b><div class='why'>{html.escape(question.why)}</div></td>"
                    f"<td>{_badge(state)}</td><td>{html.escape(text)}</td></tr>")

    notes = [item for item in merged.values() if item.state in (FAILED, LIMITED)]
    notes.sort(key=lambda item: (-item.state, item.entry.code))
    note_rows = "".join(f"<tr><td>{_badge(item.state)}</td><td><b>{html.escape(_title(item.entry))}</b> <span class='code'>{item.entry.code}</span>"
                        f"<div>{html.escape(meaning(item, manual_plain))}</div><div class='why'>Chi tiết kỹ thuật: {html.escape(item.detail)}</div></td></tr>"
                        for item in notes)

    untested_groups: dict[str, list[CatalogEntry]] = {}
    for item in merged.values():
        if item.state == UNTESTED:
            untested_groups.setdefault(item.entry.group, []).append(item.entry)
    untested_rows = "".join(
        f"<li><b>{html.escape(GROUPS_PLAIN.get(group, (group,))[0])}</b>: {len(found)} phép kiểm"
        f"{' (' + html.escape(', '.join(PLAIN_TITLES[entry.code] for entry in found)) + ')' if all(entry.code in PLAIN_TITLES for entry in found) and len(found) <= 3 else ''}"
        f" <span class='code'>{html.escape(', '.join(entry.code for entry in found[:6]))}{'…' if len(found) > 6 else ''}</span></li>"
        for group, found in untested_groups.items())

    appendix = "".join(f"<tr><td class='code'>{item.entry.code}</td><td>{html.escape(item.entry.group_label)}</td><td>{_badge(item.state)}</td>"
                       f"<td>{html.escape(item.entry.title)}</td><td>{html.escape(item.detail)}</td><td>{html.escape(item.source)}</td></tr>"
                       for item in merged.values())
    loaded = "".join(f"<li>{html.escape(name)}: nạp lúc {html.escape(_vn_time(datetime.fromisoformat(source.loaded_at)) if source.loaded_at else '?')}"
                     f"{' · bản ' + html.escape(source.commit) if source.commit else ''}</li>" for name, source in sorted(sources.items()))

    return f"""<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>Báo cáo chất lượng SmashCourt</title>
<style>body{{font-family:Segoe UI,Arial,sans-serif;max-width:1100px;margin:24px auto;padding:0 16px;color:#222;line-height:1.5}}
h1{{margin-bottom:4px}} .meta{{color:#666}} .verdict{{font-size:22px;font-weight:bold;padding:14px 18px;border-radius:8px;margin:16px 0}}
.verdict.ok{{background:#e3f5e6;color:#17642a}} .verdict.warn{{background:#fff4d6;color:#7a5600}} .verdict.bad{{background:#fde4e2;color:#9b1c13}}
table{{border-collapse:collapse;width:100%;margin:8px 0 20px}} td,th{{border-bottom:1px solid #ddd;padding:8px;vertical-align:top;text-align:left}}
.b{{display:inline-block;padding:2px 10px;border-radius:12px;font-size:13px;white-space:nowrap}} .b.ok{{background:#e3f5e6;color:#17642a}}
.b.warn{{background:#fff4d6;color:#7a5600}} .b.bad{{background:#fde4e2;color:#9b1c13}} .b.none{{background:#eee;color:#555}}
.why{{color:#666;font-size:13px}} .code{{color:#888;font-size:12px;font-family:Consolas,monospace}} details{{margin-top:24px}}</style></head><body>
<h1>Báo cáo chất lượng SmashCourt</h1>
<div class="meta">Lập lúc {_vn_time(now)} (giờ Việt Nam){' · phiên bản đang kiểm: ' + html.escape(versions) if versions else ''}</div>
<div class="verdict {css}">Kết luận: {verdict}</div>
<p>Đã kiểm <b>{tested}</b> trên tổng <b>{len(merged)}</b> phép kiểm: <b>{counts[PASSED]}</b> đạt, <b>{counts[LIMITED]}</b> đạt có lưu ý,
<b>{counts[FAILED]}</b> không đạt; <b>{counts[UNTESTED]}</b> chưa kiểm.</p>
<h2>Trả lời nhanh</h2><table><tr><th>Câu hỏi</th><th>Kết quả</th><th>Giải thích</th></tr>{''.join(rows)}</table>
<h2>Cần lưu ý</h2>{f'<table>{note_rows}</table>' if notes else '<p>Không có.</p>'}
<h2>Chưa kiểm</h2>{f'<ul>{untested_rows}</ul>' if untested_rows else '<p>Không còn.</p>'}
<h2>Cách đọc</h2><ul><li><b>Đạt</b>: đúng tiêu chí đã đặt trước. <b>Đạt, có lưu ý</b>: chạy được nhưng có điều kiện chưa đủ (thiếu mẫu đo, sai lệch đã giải thích, còn một trường hợp chưa thử).
<b>Chưa đạt</b>: không đạt tiêu chí. <b>Chưa kiểm</b>: phép kiểm chưa chạy.</li>
<li>"95 % lượt đặt sân xong trong 0,087 giây" nghĩa là cứ 100 lần đặt sân thì 95 lần xong nhanh hơn 0,087 giây.</li>
<li>Tiêu chí từng phép kiểm ghi ở tài liệu phương pháp đánh giá (docs/research/EXPERIMENTAL_DESIGN_AND_BENCHMARK_METHODOLOGY.md).</li></ul>
<details><summary><b>Phụ lục kỹ thuật</b>: từng phép kiểm, số đo và nguồn</summary><p>Nguồn đã nạp:</p><ul>{loaded}</ul>
<table><tr><th>Mã</th><th>Nhóm</th><th>Kết quả</th><th>Nội dung</th><th>Chi tiết</th><th>Nguồn</th></tr>{appendix}</table></details>
</body></html>
"""


def write(catalog: dict[str, CatalogEntry], sources: dict[str, SourceResult] | None = None, results_dir: Path | None = None,
          now: datetime | None = None) -> Path:
    """Ghi báo cáo vào perf/results/reports/BAO_CAO_<thời điểm>.html và bản mới nhất perf/results/BAO_CAO_MOI_NHAT.html; trả đường dẫn bản mới nhất."""
    results_dir = results_dir or paths.RESULTS_DIR
    now = now or datetime.now(timezone.utc)
    content = render(catalog, sources if sources is not None else board.load_state(), now)
    dated = results_dir / REPORTS_DIR_NAME / f"BAO_CAO_{now.strftime('%Y%m%d-%H%M%S')}.html"
    dated.parent.mkdir(parents=True, exist_ok=True)
    dated.write_text(content, encoding="utf-8")
    for old in sorted(dated.parent.glob("BAO_CAO_*.html"))[:-KEEP_REPORTS]:   # chỉ giữ các bản gần nhất, thư mục không phình
        old.unlink()
    latest = results_dir / LATEST_NAME
    latest.write_text(content, encoding="utf-8")
    return latest
