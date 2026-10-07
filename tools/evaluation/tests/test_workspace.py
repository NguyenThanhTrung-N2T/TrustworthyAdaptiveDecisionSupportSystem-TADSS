"""Thư mục làm việc: loại trừ bí mật và sản phẩm build (O-14)."""
from pathlib import Path

import pytest

from evaluator import workspace


def _build_tree(root: Path) -> None:
    (root / "src" / "App").mkdir(parents=True)
    (root / "src" / "App" / "Program.cs").write_text("code", encoding="utf-8")
    (root / "src" / "App" / "appsettings.json").write_text("{}", encoding="utf-8")
    (root / "src" / "App" / "appsettings.Development.json").write_text("SECRET", encoding="utf-8")
    (root / ".env").write_text("SECRET", encoding="utf-8")
    (root / ".env.production").write_text("SECRET", encoding="utf-8")
    for build_dir in ("bin", "obj", "TestResults", "logs", "evidence", ".vs", ".git"):
        (root / "src" / "App" / build_dir).mkdir()
        (root / "src" / "App" / build_dir / "junk.dll").write_text("x", encoding="utf-8")
    (root / "docs").mkdir()
    (root / "docs" / "readme.md").write_text("doc", encoding="utf-8")


def test_workspace_excludes_secrets(tmp_path: Path) -> None:
    source, work = tmp_path / "repo", tmp_path / "work"
    _build_tree(source)

    copied = workspace.prepare_workspace(source, work)

    names = sorted(str(path.relative_to(work)).replace("\\", "/") for path in work.rglob("*") if path.is_file())
    assert names == ["docs/readme.md", "src/App/Program.cs", "src/App/appsettings.json"]
    assert copied == 3
    assert workspace.find_forbidden(work) == []


def test_workspace_check_catches_leaked_secret(tmp_path: Path) -> None:
    source, work = tmp_path / "repo", tmp_path / "work"
    _build_tree(source)
    workspace.copy_workspace(source, work)
    (work / "src" / "App" / "appsettings.Development.json").write_text("LEAK", encoding="utf-8")

    assert [path.name for path in workspace.find_forbidden(work)] == ["appsettings.Development.json"]


def test_prepare_workspace_stops_when_a_secret_slips_through(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    source, work = tmp_path / "repo", tmp_path / "work"
    _build_tree(source)
    monkeypatch.setattr(workspace, "_is_secret", lambda name: False)  # mô phỏng bước copy lọc sót

    with pytest.raises(workspace.WorkspaceError, match="file bí mật"):
        workspace.prepare_workspace(source, work)


def test_copy_workspace_replaces_previous_copy(tmp_path: Path) -> None:
    source, work = tmp_path / "repo", tmp_path / "work"
    _build_tree(source)
    work.mkdir()
    (work / "stale.txt").write_text("old", encoding="utf-8")

    workspace.copy_workspace(source, work)

    assert not (work / "stale.txt").exists()


def test_read_git_info_reports_commit_and_dirty() -> None:
    def clean(command: list[str], cwd: Path) -> tuple[int, str]:
        return (0, "abc123\n") if command[1] == "rev-parse" else (0, "")

    def dirty(command: list[str], cwd: Path) -> tuple[int, str]:
        return (0, "abc123\n") if command[1] == "rev-parse" else (0, " M file.cs\n")

    def not_git(command: list[str], cwd: Path) -> tuple[int, str]:
        return 128, ""

    assert workspace.read_git_info(Path("."), clean) == workspace.GitInfo("abc123", False)
    assert workspace.read_git_info(Path("."), dirty) == workspace.GitInfo("abc123", True)
    assert workspace.read_git_info(Path("."), not_git) == workspace.GitInfo(None, None)
