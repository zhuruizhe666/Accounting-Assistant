from __future__ import annotations

import os
import tempfile
from pathlib import Path
from typing import Any, NamedTuple

from PIL import Image

os.environ.setdefault("FLAGS_use_mkldnn", "0")
os.environ.setdefault("FLAGS_use_onednn", "0")
os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")

OCR_MAX_SIDE = 960
_PRIMARY_OCR: Any | None = None


class PreparedImage(NamedTuple):
    path: Path
    scale_to_original: float
    temporary_path: Path | None


def run_primary_ocr(image_path: Path) -> list[dict[str, Any]]:
    """Run PaddleOCR and normalize its verbose result into our UI contract."""
    if not image_path.exists():
        raise FileNotFoundError(f"Image not found: {image_path}")

    prepared = prepare_image_for_ocr(image_path)
    try:
        raw_results = get_primary_ocr().predict(str(prepared.path))
        return normalize_paddleocr_results(raw_results, prepared.scale_to_original)
    finally:
        if prepared.temporary_path is not None:
            prepared.temporary_path.unlink(missing_ok=True)


def get_primary_ocr() -> Any:
    global _PRIMARY_OCR

    if _PRIMARY_OCR is None:
        from paddleocr import PaddleOCR

        _PRIMARY_OCR = PaddleOCR(
            lang="ch",
            ocr_version="PP-OCRv4",
            text_detection_model_name="PP-OCRv4_mobile_det",
            text_recognition_model_name="PP-OCRv4_mobile_rec",
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
            text_det_limit_side_len=OCR_MAX_SIDE,
            text_det_limit_type="max",
        )

    return _PRIMARY_OCR


def prepare_image_for_ocr(image_path: Path) -> PreparedImage:
    with Image.open(image_path) as image:
        image.load()
        width, height = image.size
        longest_side = max(width, height)

        if longest_side <= OCR_MAX_SIDE:
            return PreparedImage(image_path, scale_to_original=1.0, temporary_path=None)

        scale_to_ocr = OCR_MAX_SIDE / longest_side
        resized_size = (
            max(1, int(round(width * scale_to_ocr))),
            max(1, int(round(height * scale_to_ocr))),
        )

        resized = image.convert("RGB").resize(resized_size, Image.Resampling.LANCZOS)
        temp_file = tempfile.NamedTemporaryFile(prefix="accounting_ocr_", suffix=".jpg", delete=False)
        temp_path = Path(temp_file.name)
        temp_file.close()
        resized.save(temp_path, format="JPEG", quality=92, optimize=True)

    return PreparedImage(temp_path, scale_to_original=1 / scale_to_ocr, temporary_path=temp_path)


def normalize_paddleocr_results(raw_results: list[dict[str, Any]], scale_to_original: float = 1.0) -> list[dict[str, Any]]:
    normalized: list[dict[str, Any]] = []

    for page_result in raw_results:
        texts = page_result.get("rec_texts") or []
        scores = page_result.get("rec_scores") or []
        polygons = page_result.get("rec_polys") or page_result.get("dt_polys") or []

        for text, score, polygon in zip(texts, scores, polygons):
            clean_text = str(text).strip()
            if not clean_text:
                continue

            normalized.append(
                {
                    "text": clean_text,
                    "confidence": round(float(score), 6),
                    "bbox": polygon_to_bbox(polygon, scale_to_original),
                }
            )

    return normalized


def polygon_to_bbox(polygon: Any, scale_to_original: float = 1.0) -> list[list[int]]:
    if hasattr(polygon, "tolist"):
        points = polygon.tolist()
    else:
        points = polygon

    return [
        [int(round(point[0] * scale_to_original)), int(round(point[1] * scale_to_original))]
        for point in points
    ]
