"""Kiểm mã của SC7 (Q-11): commit chạm adapter khách sạn không được chạm `src/TADSS.Runtime.Api` hay `src/Shared.DecisionContractSDK`; cây làm việc ở hai thư mục đó sạch; adapter đã commit."""
from pathlib import Path

import pytest

from evaluator import code_check as module

ADAPTER = "tools/evidence/hotel_adapter.py"
REPO = Path("/repo")


def _options(command: list[str]) -> list[str]:
    """Các cặp `-c khóa=giá_trị` đứng trước lệnh con."""
    options, index = [], 1
    while command[index] == "-c":
        options.append(command[index + 1])
        index += 2
    return options


def _subcommand(command: list[str]) -> list[str]:
    return command[1 + 2 * len(_options(command)):]


class FakeGit:
    """Lịch sử git giả: `log` là đầu ra `git log` (mỗi dòng một sha), `tree[sha]` là danh sách file mỗi commit, `merge_tree[sha]` chỉ hiện khi `diff-tree` có `-m` (commit merge),
    status theo từng nhóm đường dẫn (kèm mã thoát)."""

    def __init__(self, log: str, tree: dict[str, str], merge_tree: dict[str, str], runtime_status: str, adapter_status: str, head: int, runtime_status_code: int, adapter_status_code: int) -> None:
        self.calls: list[list[str]] = []
        self._log, self._tree, self._merge_tree = log, tree, merge_tree
        self._runtime_status, self._adapter_status = runtime_status, adapter_status
        self._head, self._runtime_code, self._adapter_code = head, runtime_status_code, adapter_status_code

    def __call__(self, command: list[str], cwd: Path) -> tuple[int, str]:
        self.calls.append(command)
        assert cwd == REPO
        args = _subcommand(command)
        if args[:2] == ["rev-parse", "HEAD"]:
            return self._head, "deadbeef\n"
        if args[:2] == ["status", "--porcelain"]:
            return (self._adapter_code, self._adapter_status) if ADAPTER in args else (self._runtime_code, self._runtime_status)
        if args[0] == "log":
            return 0, self._log
        if args[0] == "diff-tree":
            sha = args[-1]
            return 0, self._tree.get(sha, "") + (self._merge_tree.get(sha, "") if "-m" in args else "")
        raise AssertionError(f"lệnh git ngoài dự kiến: {command}")


def fake_git(
    log: str = "", tree: dict[str, str] | None = None, runtime_status: str = "", adapter_status: str = "", head: int = 0,
    merge_tree: dict[str, str] | None = None, runtime_status_code: int = 0, adapter_status_code: int = 0,
) -> FakeGit:
    return FakeGit(log, tree or {}, merge_tree or {}, runtime_status, adapter_status, head, runtime_status_code, adapter_status_code)


def test_code_check_passes_when_no_commit_touches_runtime_or_sdk() -> None:
    run = fake_git(log="aaaaaaa1111\nbbbbbbb2222\n", tree={"aaaaaaa1111": f"{ADAPTER}\ntools/evidence/tests/test_hotel_adapter.py\n", "bbbbbbb2222": f"{ADAPTER}\ndocs/x.md\n"})

    result = module.check_adapter_isolation(REPO, run)

    assert result.status == "PASS" and result.commits == ["aaaaaaa", "bbbbbbb"]
    assert "2 commit" in result.reason


def test_code_check_fails_when_an_adapter_commit_touches_runtime_api() -> None:
    run = fake_git(log="aaaaaaa1111\nbbbbbbb2222\n", tree={"aaaaaaa1111": f"{ADAPTER}\n", "bbbbbbb2222": f"{ADAPTER}\nsrc/TADSS.Runtime.Api/Program.cs\n"})

    result = module.check_adapter_isolation(REPO, run)

    assert result.status == "FAIL" and "bbbbbbb" in result.reason and "src/TADSS.Runtime.Api/Program.cs" in result.reason
    assert result.commits == ["aaaaaaa", "bbbbbbb"], "ghi cả commit đã xét"


def test_code_check_fails_when_an_adapter_commit_touches_the_sdk() -> None:
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\nsrc/Shared.DecisionContractSDK/Domain/Reservation.cs\n"})

    assert module.check_adapter_isolation(REPO, run).status == "FAIL"


def test_code_check_fails_on_dirty_working_tree_in_sdk() -> None:
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\n"}, runtime_status=" M src/Shared.DecisionContractSDK/Domain/Reservation.cs\n")

    result = module.check_adapter_isolation(REPO, run)

    assert result.status == "FAIL" and "chưa commit" in result.reason and "Reservation.cs" in result.reason


def test_code_check_is_insufficient_when_adapter_is_not_committed() -> None:
    result = module.check_adapter_isolation(REPO, fake_git(log="", adapter_status="?? tools/evidence/hotel_adapter.py\n"))

    assert result.status == "INSUFFICIENT_DATA" and "adapter chưa commit" in result.reason and result.commits == []


def test_code_check_is_insufficient_when_adapter_has_uncommitted_changes() -> None:
    result = module.check_adapter_isolation(REPO, fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\n"}, adapter_status=f" M {ADAPTER}\n"))

    assert result.status == "INSUFFICIENT_DATA" and "chưa commit" in result.reason


def test_code_check_is_insufficient_when_git_is_unreadable() -> None:
    result = module.check_adapter_isolation(REPO, fake_git(head=128))

    assert result.status == "INSUFFICIENT_DATA" and "git" in result.reason


@pytest.mark.parametrize("scenario", ["pass", "dirty"])
def test_code_check_only_runs_read_only_git_commands(scenario: str) -> None:
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\n"}, runtime_status="" if scenario == "pass" else " M src/TADSS.Runtime.Api/Program.cs\n")

    module.check_adapter_isolation(REPO, run)

    allowed = {"rev-parse", "status", "log", "diff-tree"}
    assert {_subcommand(command)[0] for command in run.calls} <= allowed and all(command[0] == "git" for command in run.calls)


def test_code_check_normalizes_line_endings_like_the_host_checkout() -> None:
    """Repo Windows mount vào container Linux: file CRLF so với index LF sẽ hiện "đã sửa" nếu không chuẩn hóa dòng như máy chủ (core.autocrlf=true)."""
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\n"})

    module.check_adapter_isolation(REPO, run)

    statuses = [command for command in run.calls if _subcommand(command)[0] == "status"]
    assert statuses and all("core.autocrlf=true" in _options(command) for command in statuses)


def test_code_check_to_dict_is_serializable() -> None:
    result = module.CodeCheck("PASS", "ok", ["abc1234"])

    assert result.to_dict() == {"status": "PASS", "reason": "ok", "commits": ["abc1234"], "adapter": ADAPTER}


def test_code_check_is_insufficient_when_runtime_status_cannot_be_read() -> None:
    """`git status` lỗi (VD không đọc được index) không được coi là cây sạch."""
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\n"}, runtime_status_code=128)

    result = module.check_adapter_isolation(REPO, run)

    assert result.status == "INSUFFICIENT_DATA" and "git status" in result.reason


def test_code_check_is_insufficient_when_adapter_status_cannot_be_read() -> None:
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\n"}, adapter_status_code=128)

    result = module.check_adapter_isolation(REPO, run)

    assert result.status == "INSUFFICIENT_DATA" and "git status" in result.reason


def test_code_check_path_prefix_is_directory_bounded() -> None:
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": f"{ADAPTER}\nsrc/TADSS.Runtime.ApiX/Program.cs\nsrc/Shared.DecisionContractSDK.Tests/A.cs\n"})

    assert module.check_adapter_isolation(REPO, run).status == "PASS", "thư mục anh em có tên bắt đầu giống nhau không phải runtime/SDK"


def test_code_check_sees_files_changed_by_a_merge_commit() -> None:
    run = fake_git(log="aaaaaaa1111\n", tree={"aaaaaaa1111": ""}, merge_tree={"aaaaaaa1111": f"{ADAPTER}\nsrc/TADSS.Runtime.Api/Program.cs\n"})

    result = module.check_adapter_isolation(REPO, run)

    assert result.status == "FAIL" and "src/TADSS.Runtime.Api/Program.cs" in result.reason
    assert any("-m" in command for command in run.calls if _subcommand(command)[0] == "diff-tree")
