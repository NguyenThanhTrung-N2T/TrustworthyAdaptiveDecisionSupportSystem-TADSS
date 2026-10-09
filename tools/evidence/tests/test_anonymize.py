"""Ẩn danh hóa định danh (DATASET_SCHEMA Mục 4)."""
from evidence.anonymize import Anonymizer, uuid_from_hash


def test_anonymize_deterministic_within_run() -> None:
    anonymizer = Anonymizer(b"khoa-1")

    first = anonymizer.hash("reservation", "11111111-aaaa")
    assert first == anonymizer.hash("reservation", "11111111-aaaa")
    assert first != anonymizer.hash("reservation", "22222222-bbbb")
    assert first != anonymizer.hash("branch", "11111111-aaaa"), "khác loại định danh thì khác mã dù cùng giá trị"
    assert "11111111" not in first and len(first) == 32 and all(c in "0123456789abcdef" for c in first)


def test_different_keys_give_different_codes_and_random_key_by_default() -> None:
    assert Anonymizer(b"khoa-1").hash("actor", "manager-01") != Anonymizer(b"khoa-2").hash("actor", "manager-01")
    assert Anonymizer().hash("actor", "x") != Anonymizer().hash("actor", "x"), "mỗi lượt chạy một khóa ngẫu nhiên"


def test_empty_values_stay_empty() -> None:
    anonymizer = Anonymizer(b"k")

    assert anonymizer.hash("actor", None) == "" and anonymizer.hash("actor", "") == ""


def test_uuid_from_hash_is_valid_v4_and_deterministic() -> None:
    import uuid

    code = Anonymizer(b"khoa-1").hash("reservation", "x")
    value = uuid_from_hash(code)

    assert value == uuid_from_hash(code)
    parsed = uuid.UUID(value)
    assert parsed.version == 4 and parsed.variant == uuid.RFC_4122
    assert value != uuid_from_hash(Anonymizer(b"khoa-1").hash("reservation", "y"))
