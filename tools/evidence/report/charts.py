"""Biểu đồ SVG nội tuyến cho báo cáo (không phụ thuộc thư viện hay tài nguyên ngoài): đường theo thời gian với vùng tô màu cho pha sự cố."""
from __future__ import annotations

from dataclasses import dataclass
from html import escape

PALETTE = ("#1f6feb", "#d1242f", "#1a7f37", "#9a6700", "#8250df", "#0a7e8c")
BAND_COLORS = ("#ffd8d3", "#fff1c2", "#d7ecff", "#e6d9ff")
WIDTH, HEIGHT = 760, 260
MARGIN_LEFT, MARGIN_RIGHT, MARGIN_TOP, MARGIN_BOTTOM = 56, 12, 14, 34


@dataclass(frozen=True)
class Series:
    """Một đường: tên và các điểm `(x, y)`; điểm có y là None bị bỏ (đứt đoạn)."""

    name: str
    points: list[tuple[float, float | None]]


@dataclass(frozen=True)
class Band:
    """Vùng tô màu theo trục x (ví dụ khoảng thời gian một lỗi được bật)."""

    label: str
    start: float
    end: float


def _nice_max(value: float) -> float:
    if value <= 0:
        return 1.0
    for step in (1, 2, 2.5, 5, 10):
        scale = 10 ** (len(str(int(value))) - 1) if value >= 1 else 1
        candidate = step * scale
        if candidate >= value:
            return float(candidate)
    return float(value)


def line_chart(series: list[Series], x_label: str, y_label: str, bands: list[Band] | None = None, y_max: float | None = None) -> str:
    """Trả `<svg>` hoàn chỉnh. Không có điểm dữ liệu nào ⇒ chuỗi rỗng (nơi gọi ghi lý do thiếu dữ liệu)."""
    points = [(x, y) for item in series for x, y in item.points if y is not None]
    if not points:
        return ""
    x_min, x_max = min(x for x, _ in points), max(x for x, _ in points)
    top = y_max if y_max is not None else _nice_max(max(y for _, y in points))
    span_x = (x_max - x_min) or 1.0
    plot_w, plot_h = WIDTH - MARGIN_LEFT - MARGIN_RIGHT, HEIGHT - MARGIN_TOP - MARGIN_BOTTOM

    def px(x: float) -> float:
        return MARGIN_LEFT + (x - x_min) / span_x * plot_w

    def py(y: float) -> float:
        return MARGIN_TOP + plot_h - min(y, top) / top * plot_h

    parts = [f'<svg viewBox="0 0 {WIDTH} {HEIGHT}" role="img" aria-label="{escape(y_label)} theo {escape(x_label)}" class="chart">']
    for index, band in enumerate(bands or []):
        left, right = px(max(band.start, x_min)), px(min(band.end, x_max))
        if right > left:
            color = BAND_COLORS[index % len(BAND_COLORS)]
            parts.append(f'<rect x="{left:.1f}" y="{MARGIN_TOP}" width="{right - left:.1f}" height="{plot_h}" fill="{color}"/>')
            parts.append(f'<text x="{left + 3:.1f}" y="{MARGIN_TOP + 11}" font-size="10" fill="#57606a">{escape(band.label)}</text>')
    for tick in range(5):
        value = top * tick / 4
        y = py(value)
        parts.append(f'<line x1="{MARGIN_LEFT}" y1="{y:.1f}" x2="{WIDTH - MARGIN_RIGHT}" y2="{y:.1f}" stroke="#d0d7de" stroke-width="0.6"/>')
        parts.append(f'<text x="{MARGIN_LEFT - 6}" y="{y + 3:.1f}" font-size="10" text-anchor="end" fill="#57606a">{value:g}</text>')
    for tick in range(6):
        value = x_min + span_x * tick / 5
        parts.append(f'<text x="{px(value):.1f}" y="{HEIGHT - MARGIN_BOTTOM + 14}" font-size="10" text-anchor="middle" fill="#57606a">{value:g}</text>')
    parts.append(f'<text x="{MARGIN_LEFT + plot_w / 2:.0f}" y="{HEIGHT - 4}" font-size="11" text-anchor="middle" fill="#24292f">{escape(x_label)}</text>')
    parts.append(f'<text x="12" y="{MARGIN_TOP + plot_h / 2:.0f}" font-size="11" text-anchor="middle" fill="#24292f" transform="rotate(-90 12 {MARGIN_TOP + plot_h / 2:.0f})">{escape(y_label)}</text>')
    for index, item in enumerate(series):
        color = PALETTE[index % len(PALETTE)]
        segments: list[list[tuple[float, float]]] = [[]]
        for x, y in item.points:
            if y is None:
                if segments[-1]:
                    segments.append([])
            else:
                segments[-1].append((x, y))
        for segment in segments:
            if len(segment) == 1:
                parts.append(f'<circle cx="{px(segment[0][0]):.1f}" cy="{py(segment[0][1]):.1f}" r="1.8" fill="{color}"/>')
            elif segment:
                path = " ".join(f"{px(x):.1f},{py(y):.1f}" for x, y in segment)
                parts.append(f'<polyline fill="none" stroke="{color}" stroke-width="1.6" points="{path}"/>')
    legend = "".join(
        f'<span class="legend"><i style="background:{PALETTE[index % len(PALETTE)]}"></i>{escape(item.name)}</span>' for index, item in enumerate(series)
    )
    parts.append("</svg>")
    return "".join(parts) + f'<div class="legends">{legend}</div>'


def bar_chart(categories: list[str], series: list[Series], y_label: str, y_max: float | None = None) -> str:
    """Biểu đồ cột nhóm SVG: mỗi `category` là một nhóm, mỗi `Series` một cột trong nhóm (điểm `(chỉ_số_nhóm, giá_trị)`; giá trị None bị bỏ). Không có giá trị nào ⇒ chuỗi rỗng."""
    values = [value for item in series for _, value in item.points if value is not None]
    if not values or not categories:
        return ""
    top = y_max if y_max is not None else _nice_max(max(values))
    plot_w, plot_h = WIDTH - MARGIN_LEFT - MARGIN_RIGHT, HEIGHT - MARGIN_TOP - MARGIN_BOTTOM
    group_w = plot_w / len(categories)
    bar_w = group_w * 0.8 / max(1, len(series))

    def py(value: float) -> float:
        return MARGIN_TOP + plot_h - min(value, top) / top * plot_h

    parts = [f'<svg viewBox="0 0 {WIDTH} {HEIGHT}" role="img" aria-label="{escape(y_label)}" class="chart">']
    for tick in range(5):
        value = top * tick / 4
        y = py(value)
        parts.append(f'<line x1="{MARGIN_LEFT}" y1="{y:.1f}" x2="{WIDTH - MARGIN_RIGHT}" y2="{y:.1f}" stroke="#d0d7de" stroke-width="0.6"/>')
        parts.append(f'<text x="{MARGIN_LEFT - 6}" y="{y + 3:.1f}" font-size="10" text-anchor="end" fill="#57606a">{value:g}</text>')
    for index, name in enumerate(categories):
        parts.append(f'<text x="{MARGIN_LEFT + group_w * (index + 0.5):.1f}" y="{HEIGHT - MARGIN_BOTTOM + 14}" font-size="10" text-anchor="middle" fill="#24292f">{escape(name)}</text>')
    parts.append(f'<text x="12" y="{MARGIN_TOP + plot_h / 2:.0f}" font-size="11" text-anchor="middle" fill="#24292f" transform="rotate(-90 12 {MARGIN_TOP + plot_h / 2:.0f})">{escape(y_label)}</text>')
    for s_index, item in enumerate(series):
        color = PALETTE[s_index % len(PALETTE)]
        for group, value in item.points:
            if value is None:
                continue
            x = MARGIN_LEFT + group_w * int(group) + group_w * 0.1 + bar_w * s_index
            y = py(value)
            parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w - 2:.1f}" height="{MARGIN_TOP + plot_h - y:.1f}" fill="{color}"/>')
            parts.append(f'<text x="{x + (bar_w - 2) / 2:.1f}" y="{y - 3:.1f}" font-size="9" text-anchor="middle" fill="#24292f">{value:.3g}</text>')
    legend = "".join(f'<span class="legend"><i style="background:{PALETTE[index % len(PALETTE)]}"></i>{escape(item.name)}</span>' for index, item in enumerate(series))
    parts.append("</svg>")
    return "".join(parts) + f'<div class="legends">{legend}</div>'
