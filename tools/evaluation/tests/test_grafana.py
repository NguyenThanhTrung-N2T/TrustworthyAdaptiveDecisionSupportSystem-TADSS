"""OB-2 trên Grafana của stack đánh giá (không gọi mạng thật)."""
import json

from evaluator.clients import grafana


def _ok_body(uid: str = "tadss-runtime", folder: str = "TADSS") -> str:
    return json.dumps({"dashboard": {"uid": uid}, "meta": {"folderTitle": folder}})


def test_dashboard_found_first_try() -> None:
    check = grafana.check_dashboard("http://g:3000/", fetch=lambda url: (200, _ok_body()), sleep=lambda _: None)

    assert check.passed and "TADSS" in check.detail


def test_dashboard_retries_until_provisioned() -> None:
    answers = iter([(404, "nf"), (404, "nf"), (200, _ok_body())])
    urls: list[str] = []

    def fetch(url: str) -> tuple[int, str]:
        urls.append(url)
        return next(answers)

    check = grafana.check_dashboard("http://g:3000", fetch=fetch, sleep=lambda _: None)

    assert check.passed and len(urls) == 3 and urls[0] == "http://g:3000/api/dashboards/uid/tadss-runtime"


def test_dashboard_never_appears() -> None:
    check = grafana.check_dashboard("http://g:3000", fetch=lambda url: (404, "nf"), wait_seconds=0.0, sleep=lambda _: None)

    assert not check.passed and "404" in check.detail


def test_dashboard_wrong_folder_or_uid_fails() -> None:
    assert not grafana.check_dashboard("http://g", fetch=lambda url: (200, _ok_body(folder="General")), sleep=lambda _: None).passed
    assert not grafana.check_dashboard("http://g", fetch=lambda url: (200, _ok_body(uid="other")), sleep=lambda _: None).passed
    assert not grafana.check_dashboard("http://g", fetch=lambda url: (200, "<html>"), sleep=lambda _: None).passed
