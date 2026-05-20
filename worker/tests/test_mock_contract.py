from pathlib import Path

from accounting_worker.mock_analysis import build_mock_analysis


def test_mock_analysis_contract_contains_required_top_level_fields() -> None:
    result = build_mock_analysis(Path("sample.jpg"))

    assert result["image_path"] == "sample.jpg"
    assert result["status"] == "mock"
    assert isinstance(result["ocr_items"], list)
    assert isinstance(result["candidates"], dict)


def test_mock_analysis_candidate_refs_point_to_ocr_items() -> None:
    result = build_mock_analysis(Path("sample.jpg"))
    ocr_count = len(result["ocr_items"])

    for candidates in result["candidates"].values():
        for candidate in candidates:
            assert all(0 <= ref < ocr_count for ref in candidate["bbox_refs"])
