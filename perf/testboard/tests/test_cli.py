# plan 6C B4: lệnh nạp mới của `python perf/board.py` chạy trọn (đọc ⇒ state.json ⇒ bản gốc), không đẩy Pushgateway
import json

from testboard import board, cli
from testboard.model import LIMITED, PASSED


def _fault_run(path, kind):
    path.mkdir(parents=True)
    (path / "faults.json").write_text(json.dumps({"kind": kind, "check": {"bookings": 10, "notFallback": 0, "awaitingApproval": 0,
                                                                          "duplicateDecisions": 0, "status": "PASS"}}), encoding="utf-8")
    (path / "sc6.json").write_text(json.dumps({"status": "PASS", "checkedBookings": 5}), encoding="utf-8")
    (path / "verdict.json").write_text(json.dumps({"validity": "OK", "status": "PASS"}), encoding="utf-8")
    return path


def test_faults_command_loads_every_run_as_one_source(tmp_path):
    runs = [str(_fault_run(tmp_path / name, kind)) for name, kind in (("a", "stop-tadss"), ("b", "latency"), ("c", "reset"))]

    assert cli.main(["--no-push", "faults", *runs]) == 0

    codes = board.load_state()["faults"].codes
    assert {code: result.state for code, result in codes.items()} == {"FI-11": PASSED, "FI-12": PASSED, "FI-14": PASSED}


def test_k6_command_takes_several_pooled_files_for_one_code(tmp_path):
    files = []
    for name, status in (("pooled-A1-3runs.json", "PASS"), ("pooled-B2-3runs.json", "INSUFFICIENT")):
        files.append(tmp_path / name)
        files[-1].write_text(json.dumps({"groups": {}, "status": status, "selftest": False, "runs": [1, 2, 3]}), encoding="utf-8")

    assert cli.main(["--no-push", "k6", "SP-06", *map(str, files)]) == 0

    result = board.load_state()["k6-SP-06"].codes["SP-06"]
    assert result.state == LIMITED and "pooled-A1-3runs: gộp 3 lượt" in result.detail


def test_manual_command_loads_the_repository_file():
    assert cli.main(["--no-push", "manual"]) == 0
    assert board.load_state()["manual"].codes["SEC-AI-1"].state == PASSED


def test_ci_download_folder_is_removed_after_its_copy_is_archived(tmp_path, monkeypatch):
    # bản tải CI nằm trong thư mục tạm của hệ thống: sau khi chép vào perf/results thì xóa, không để tồn đọng trong TEMP
    from testboard import ci
    from testboard.model import SourceResult

    downloaded = tmp_path / "testboard-be-x"
    downloaded.mkdir()
    (downloaded / "unit.trx").write_text("x", encoding="utf-8")
    monkeypatch.setattr(ci, "download", lambda repo, branch, known: (SourceResult("ci-be", commit="abc1234"), downloaded))

    assert cli.main(["--no-push", "ci", "be"]) == 0

    assert not downloaded.exists()
    archived = board.load_state()["ci-be"].report
    assert archived and (board.paths.REPO_ROOT / archived / "testboard-be-x" / "unit.trx").is_file()


def test_update_loads_three_ci_repos_and_manual_then_writes_one_report(tmp_path, monkeypatch, isolated_results):
    from testboard import ci
    from testboard.model import SourceResult

    def fake_download(repo, branch, known):
        folder = tmp_path / f"dl-{repo.key}"
        folder.mkdir()
        return SourceResult(f"ci-{repo.key}", commit="abc1234"), folder

    monkeypatch.setattr(ci, "download", fake_download)

    assert cli.main(["--no-push", "update"]) == 0

    assert set(board.load_state()) == {"ci-be", "ci-tadss", "ci-ai", "manual"}
    assert (isolated_results / "BAO_CAO_MOI_NHAT.html").is_file()
    assert len(list((isolated_results / "reports").iterdir())) == 1
    assert (paths_manual := board.paths.MANUAL_FILE).is_file()   # file ghi nhận tay của repo không bị xóa như thư mục tải CI
