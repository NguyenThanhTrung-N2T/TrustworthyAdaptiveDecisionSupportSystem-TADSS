# Task 2.3: tải kết quả CI đúng nhánh của từng repo (AI đẩy CI từ master, BE/TADSS từ develop) và báo rõ lỗi của gh
import subprocess

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
