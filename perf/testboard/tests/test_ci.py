# Task 2.3: tải kết quả CI đúng nhánh của từng repo (AI đẩy CI từ master, BE/TADSS từ develop) và báo rõ lỗi của gh
import subprocess
from pathlib import Path

import pytest

from testboard import ci


@pytest.mark.parametrize(("key", "branch"), [("be", "develop"), ("tadss", "develop"), ("ai", "master")])
def test_latest_run_defaults_to_the_branch_each_repo_runs_ci_on(monkeypatch, key, branch):
    calls = []
    monkeypatch.setattr(ci, "_gh", lambda *args: calls.append(args) or "[]")

    with pytest.raises(RuntimeError, match=branch):
        ci.latest_run(ci.REPOS[key], None)

    assert calls[0][calls[0].index("-b") + 1] == branch


def test_explicit_branch_overrides_the_default(monkeypatch):
    calls = []
    monkeypatch.setattr(ci, "_gh", lambda *args: calls.append(args) or "[]")

    with pytest.raises(RuntimeError):
        ci.latest_run(ci.REPOS["ai"], "develop")

    assert calls[0][calls[0].index("-b") + 1] == "develop"


def test_gh_failure_surfaces_the_message_of_gh(monkeypatch):
    def fail(*args, **kwargs):
        raise subprocess.CalledProcessError(1, args[0], output="", stderr="no valid artifacts found to download\n")

    monkeypatch.setattr(ci.subprocess, "run", fail)

    with pytest.raises(RuntimeError, match="no valid artifacts found to download"):
        ci._gh("run", "download", "1")


def test_download_adds_ci_step_codes_next_to_test_codes(monkeypatch, tmp_path):
    # plan 6C B4: bước kiểm không phải test (quét gói, kiểm image) lên bảng từ kết luận step của cùng lượt CI
    import json

    def fake_gh(*args):
        if args[:2] == ("run", "list"):
            return json.dumps([{"databaseId": 7, "headSha": "abcdef1234", "conclusion": "success", "url": "u", "createdAt": "t"}])
        if args[:2] == ("run", "download"):
            target = Path(args[args.index("-D") + 1])
            (target / "junit.xml").write_text("<testsuites><testsuite/></testsuites>", encoding="utf-8")
            return ""
        return json.dumps({"jobs": [{"name": "image", "steps": [{"name": "Build, check and push image", "conclusion": "success"},
                                                               {"name": "Check vulnerable packages (pip-audit)", "conclusion": "failure"}]}]})

    monkeypatch.setattr(ci, "_gh", fake_gh)
    monkeypatch.setattr(ci.tempfile, "mkdtemp", lambda prefix: str(tmp_path))

    source, directory = ci.download(ci.REPOS["ai"], None, {"OPS-01", "OPS-02", "SEC-AI-3", "SEC-AI-4"})

    assert {code: result.state for code, result in source.codes.items()} == {"OPS-01": 1, "OPS-02": 1, "SEC-AI-3": 1, "SEC-AI-4": 4}
    assert (directory / "ci-jobs.json").is_file()
