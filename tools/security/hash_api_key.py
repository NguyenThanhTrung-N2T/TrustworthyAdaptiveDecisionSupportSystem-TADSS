"""
Băm API key trong appsettings.Development.json (SEC-1/SEC-4): TADSS chỉ lưu SHA-256 của key (`Tadss:Security:ApiKeys[i].KeySha256`), key gốc thuộc về bên gọi (SmashCourt-BE).

    python tools/security/hash_api_key.py                         # băm key gốc đang nằm trong KeySha256; giá trị đã là mã băm thì bỏ qua
    python tools/security/hash_api_key.py --new                   # sinh key mới cho client đầu tiên (đổi key), thay key cũ
    python tools/security/hash_api_key.py --new --client <id>     # sinh key mới cho đúng client đó

Key gốc được ghi vào `.env` của repo thành biến `TADSS_API_KEY_<CLIENTID>` (ví dụ `smashcourt-be` → `TADSS_API_KEY_SMASHCOURT_BE`) để cấu hình cho BE;
`.env` đã bị loại khỏi git, image Docker và thư mục làm việc của evaluator, và không service compose nào dùng biến này.
Chỉ thay đúng chuỗi giá trị KeySha256 trong file JSON và đúng một dòng của `.env` (giữ nguyên định dạng, BOM, kiểu xuống dòng, các dòng khác như NGROK_AUTHTOKEN);
không bao giờ in key hay mã băm. Thoát 0 khi xong, 1 khi lỗi (không file nào bị sửa).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import secrets
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = ROOT / "src" / "TADSS.Runtime.Api" / "appsettings.Development.json"
DEFAULT_ENV = ROOT / ".env"
ENV_PREFIX = "TADSS_API_KEY_"
# Cùng quy tắc với SecurityOptionsValidator: mã băm là 64 ký tự hex, ClientId chỉ gồm chữ, số, '.', '_', '-'
HASH_PATTERN = re.compile(r"\A[0-9a-fA-F]{64}\Z")
CLIENT_ID_PATTERN = re.compile(r"\A[A-Za-z0-9._-]{1,64}\Z")
# Giá trị ghi trần được vào .env (không cần nháy); key khác dạng này được bọc nháy đơn
PLAIN_ENV_VALUE = re.compile(r"\A[A-Za-z0-9._-]+\Z")
BOM = b"\xef\xbb\xbf"
NEW_KEY_BYTES = 32  # 64 ký tự hex


class ApiKeyFileError(RuntimeError):
    """Không xử lý được an toàn; không file nào bị sửa."""


def sha256_hex(key: str) -> str:
    return hashlib.sha256(key.encode("utf-8")).hexdigest()


def env_variable(client_id: str) -> str:
    return ENV_PREFIX + re.sub(r"[^A-Za-z0-9]", "_", client_id).upper()


def load_api_keys(text: str) -> list[dict]:
    try:
        data = json.loads(text)
    except json.JSONDecodeError as error:
        raise ApiKeyFileError(f"file cấu hình không phải JSON hợp lệ (dòng {error.lineno})") from error
    keys = data.get("Tadss", {}).get("Security", {}).get("ApiKeys")
    if not isinstance(keys, list) or not keys:
        raise ApiKeyFileError('chưa có Tadss:Security:ApiKeys; thêm [{ "ClientId": "smashcourt-be", "KeySha256": "" }] rồi chạy lại với --new')
    for index, entry in enumerate(keys):
        client = entry.get("ClientId") if isinstance(entry, dict) else None
        if not isinstance(client, str) or not CLIENT_ID_PATTERN.match(client):
            raise ApiKeyFileError(f"ApiKeys[{index}].ClientId thiếu hoặc không hợp lệ (chỉ chữ, số, '.', '_', '-', tối đa 64 ký tự)")
    return keys


def replace_value(text: str, old: str, new: str) -> str:
    """Thay đúng một lần chuỗi giá trị của KeySha256 (so theo dạng JSON đã mã hóa) để giữ nguyên phần còn lại của file."""
    pattern = re.compile(r'("KeySha256"\s*:\s*)' + re.escape(json.dumps(old, ensure_ascii=False)))
    matches = pattern.findall(text)
    if len(matches) != 1:
        raise ApiKeyFileError(f"tìm thấy {len(matches)} chỗ KeySha256 mang giá trị cần thay (cần đúng 1); sửa tay hoặc chạy --new")
    return pattern.sub(lambda match: match.group(1) + json.dumps(new), text, count=1)


def _env_value(raw: str) -> str:
    value = raw.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
        return value[1:-1]
    return value


def set_env_variable(text: str, name: str, key: str, overwrite: bool) -> str:
    """Đặt `name=key` trong nội dung .env: thay dòng đã có (giữ kiểu xuống dòng) hoặc thêm vào cuối; có key KHÁC mà không overwrite thì dừng."""
    lines = text.splitlines(keepends=True)
    newline = "\r\n" if "\r\n" in text else "\n"
    if PLAIN_ENV_VALUE.match(key):
        rendered = f"{name}={key}"
    elif "'" not in key and "\n" not in key and "\r" not in key:
        rendered = f"{name}='{key}'"
    else:
        raise ApiKeyFileError("key chứa nháy đơn hoặc xuống dòng, không ghi an toàn vào .env; đổi key bằng --new")
    pattern = re.compile(rf"\A\s*(?:export\s+)?{re.escape(name)}\s*=(?P<value>.*?)(?P<end>\r?\n)?\Z", re.DOTALL)
    found = [index for index, line in enumerate(lines) if pattern.match(line)]
    if len(found) > 1:
        raise ApiKeyFileError(f".env có {len(found)} dòng {name}; giữ lại một dòng rồi chạy lại")
    if found:
        match = pattern.match(lines[found[0]])
        if not overwrite and _env_value(match.group("value")) not in ("", key):
            raise ApiKeyFileError(f".env đã có {name} mang key KHÁC; không ghi đè. Xóa dòng đó nếu chắc chắn, hoặc dùng --new để đổi key")
        lines[found[0]] = rendered + (match.group("end") or "")
        return "".join(lines)
    prefix = "" if not text or text.endswith(("\n", "\r")) else newline
    return text + prefix + rendered + newline


def run(config: Path, env_file: Path, new: bool, client: str | None) -> tuple[list[str], bool]:
    """Trả (thông báo, đã sửa file hay chưa)."""
    if not config.exists():
        raise ApiKeyFileError(f"không thấy {config}")
    raw = config.read_bytes()
    has_bom = raw.startswith(BOM)
    text = raw[len(BOM):].decode("utf-8") if has_bom else raw.decode("utf-8")
    keys = load_api_keys(text)

    messages: list[str] = []
    pending: list[tuple[str, str, bool]] = []  # (ClientId, key gốc, ghi đè)
    updated = text
    if new:
        targets = [entry for entry in keys if client is None or entry["ClientId"] == client][:1]
        if not targets:
            raise ApiKeyFileError(f"không có ClientId '{client}' trong ApiKeys")
        entry = targets[0]
        key = secrets.token_hex(NEW_KEY_BYTES)
        updated = replace_value(updated, entry.get("KeySha256", ""), sha256_hex(key))
        pending.append((entry["ClientId"], key, True))
        messages.append(f"{entry['ClientId']}: đã sinh key mới và ghi mã băm vào KeySha256 (key cũ hết hiệu lực khi TADSS khởi động lại)")
    else:
        for entry in keys:
            value = entry.get("KeySha256") or ""
            if HASH_PATTERN.match(value):
                messages.append(f"{entry['ClientId']}: KeySha256 đã là mã băm, bỏ qua")
                continue
            if not value.strip():
                raise ApiKeyFileError(f"{entry['ClientId']}: KeySha256 đang trống; chạy lại với --new để sinh key")
            if value != value.strip():
                raise ApiKeyFileError(f"{entry['ClientId']}: key có khoảng trắng ở đầu/cuối, có thể dán nhầm; sửa tay rồi chạy lại")
            updated = replace_value(updated, value, sha256_hex(value))
            pending.append((entry["ClientId"], value, False))
            messages.append(f"{entry['ClientId']}: đã băm key gốc và ghi mã băm vào KeySha256")

    if not pending:
        return messages, False
    # Dựng và kiểm mọi thứ trong bộ nhớ trước khi ghi: JSON mới hợp lệ, mã băm khớp key, .env nhận được key
    by_client = {client_id: key for client_id, key, _ in pending}
    for entry in load_api_keys(updated):
        if entry["ClientId"] in by_client and entry.get("KeySha256") != sha256_hex(by_client[entry["ClientId"]]):
            raise ApiKeyFileError("kiểm lại thất bại: mã băm sau khi thay không khớp key; không file nào bị sửa")
    env_raw = env_file.read_bytes() if env_file.exists() else b""
    env_text = env_raw.decode("utf-8-sig")
    for client_id, key, overwrite in pending:
        env_text = set_env_variable(env_text, env_variable(client_id), key, overwrite)
    env_file.write_bytes((BOM if env_raw.startswith(BOM) else b"") + env_text.encode("utf-8"))
    config.write_bytes((BOM if has_bom else b"") + updated.encode("utf-8"))
    messages += [f"key gốc lưu trong .env, biến {env_variable(client_id)} (không commit; dùng để cấu hình SmashCourt-BE)" for client_id, _, _ in pending]
    return messages, True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Băm API key trong appsettings.Development.json của TADSS.")
    parser.add_argument("--new", action="store_true", help="sinh key mới (đổi key) thay vì băm key đang có")
    parser.add_argument("--client", help="ClientId cần sinh key mới (mặc định: phần tử đầu tiên)")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG, help=argparse.SUPPRESS)
    parser.add_argument("--env-file", type=Path, default=DEFAULT_ENV, help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    try:
        messages, changed = run(args.config, args.env_file, args.new, args.client)
        for message in messages:
            print(f">> {message}")
    except (ApiKeyFileError, OSError, UnicodeDecodeError) as error:
        print(f"LỖI: {error}", file=sys.stderr)
        return 1
    print(">> Xong. Build lại để image dùng cấu hình mới: docker compose up -d --build" if changed else ">> Không có gì cần đổi.")
    return 0


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    sys.exit(main())
