"""Ngữ cảnh một lượt chạy: run_id, thư mục minh chứng, chế độ, bước được chọn và địa chỉ các thành phần của stack đánh giá (O-14, O-21)."""
from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

MODES = ("quick", "full")
ALL_STEPS = ("a", "b", "c", "d", "e")
_RUN_ID = re.compile(r"^EV-(?P<date>\d{8})-EVAL-(?:quick|full)-r(?P<n>\d+)$")


class ConfigurationError(ValueError):
    """Biến môi trường của evaluator không hợp lệ."""


def next_run_id(evidence_root: Path, mode: str, now: datetime) -> str:
    """`EV-{yyyyMMdd}-EVAL-{quick|full}-r{n}`; n là số lần kế tiếp trong ngày (mọi chế độ) dựa trên các thư mục đã có trong `evidence/`."""
    if mode not in MODES:
        raise ConfigurationError(f"EVAL_MODE phải là một trong {MODES}, nhận {mode!r}")
    date = now.strftime("%Y%m%d")
    taken = [
        int(match.group("n"))
        for child in (evidence_root.iterdir() if evidence_root.is_dir() else [])
        if (match := _RUN_ID.match(child.name)) and match.group("date") == date
    ]
    return f"EV-{date}-EVAL-{mode}-r{(max(taken) + 1) if taken else 1}"


def parse_steps(value: str | None) -> tuple[str, ...]:
    """Tập con của `a,b,c,d,e` (giữ thứ tự chuẩn); rỗng → tất cả."""
    if not value or not value.strip():
        return ALL_STEPS
    wanted = {part.strip().lower() for part in value.split(",") if part.strip()}
    unknown = wanted - set(ALL_STEPS)
    if unknown:
        raise ConfigurationError(f"EVAL_STEPS có bước lạ: {sorted(unknown)}")
    return tuple(step for step in ALL_STEPS if step in wanted)


@dataclass(frozen=True)
class Endpoints:
    """Địa chỉ các thành phần trong mạng của stack đánh giá (đặt trong compose)."""

    tadss: str = "http://eval-tadss-1:8080"
    tadss2: str = "http://eval-tadss-2:8080"
    proxy: str = "http://eval-proxy:8080"
    grafana: str = "http://eval-grafana:3000"
    prometheus: str = "http://eval-grafana:3000/api/datasources/proxy/uid/prometheus"
    toxiproxy: str = "http://eval-toxiproxy:8474"
    api_key: str = ""

    @classmethod
    def from_env(cls, env: dict[str, str]) -> "Endpoints":
        defaults = cls()
        return cls(
            tadss=env.get("EVAL_TADSS_URL", defaults.tadss),
            tadss2=env.get("EVAL_TADSS2_URL", defaults.tadss2),
            proxy=env.get("EVAL_PROXY_URL", defaults.proxy),
            grafana=env.get("EVAL_GRAFANA_URL", defaults.grafana),
            prometheus=env.get("EVAL_PROMETHEUS_URL", defaults.prometheus),
            toxiproxy=env.get("EVAL_TOXIPROXY_URL", defaults.toxiproxy),
            api_key=env.get("EVAL_API_KEY", ""),
        )


@dataclass
class RunContext:
    """Mọi thứ một bước cần biết về lượt chạy; thư mục con của `evidence/<run_id>/` tạo khi truy cập."""

    run_id: str
    mode: str
    steps: tuple[str, ...]
    started_at: datetime
    repo_dir: Path
    work_dir: Path
    evidence_root: Path
    endpoints: Endpoints
    env: dict[str, str] = field(default_factory=dict)
    # Kết quả các bước đã chạy trong lượt này (run_steps nối thêm sau mỗi bước) để bước (e) tổng hợp
    outcomes: list = field(default_factory=list)

    @property
    def evidence_dir(self) -> Path:
        return self.evidence_root / self.run_id

    def raw_dir(self, *parts: str) -> Path:
        """`evidence/<run_id>/raw/<parts>`, tạo nếu chưa có."""
        path = self.evidence_dir.joinpath("raw", *parts)
        path.mkdir(parents=True, exist_ok=True)
        return path


def build_context(env: dict[str, str], now: datetime) -> RunContext:
    """Dựng ngữ cảnh từ biến môi trường: `EVAL_MODE` (mặc định quick), `EVAL_STEPS`, `EVAL_REPO` (/repo), `EVAL_WORK` (/work), `EVAL_EVIDENCE` (/evidence)."""
    mode = env.get("EVAL_MODE", "quick").strip().lower() or "quick"
    evidence_root = Path(env.get("EVAL_EVIDENCE", "/evidence"))
    return RunContext(
        run_id=next_run_id(evidence_root, mode, now),
        mode=mode,
        steps=parse_steps(env.get("EVAL_STEPS")),
        started_at=now,
        repo_dir=Path(env.get("EVAL_REPO", "/repo")),
        work_dir=Path(env.get("EVAL_WORK", "/work")),
        evidence_root=evidence_root,
        endpoints=Endpoints.from_env(env),
        env=dict(env),
    )
