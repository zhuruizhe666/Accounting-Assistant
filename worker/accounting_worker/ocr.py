from __future__ import annotations

import os
from pathlib import Path
from typing import Any

os.environ.setdefault("FLAGS_use_mkldnn", "0")
os.environ.setdefault("FLAGS_use_onednn", "0")
os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")


def run_primary_ocr(image_path: Path) -> list[dict[str, Any]]:
    """Run PaddleOCR and normalize its verbose result into our UI contract."""
    if not image_path.exists():
        raise FileNotFoundError(f"Image not found: {image_path}")

    from paddleocr import PaddleOCR

    ocr = PaddleOCR(lang="ch")
    raw_results = ocr.predict(str(image_path))
    return normalize_paddleocr_results(raw_results)


def normalize_paddleocr_results(raw_results: list[dict[str, Any]]) -> list[dict[str, Any]]:
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
                    "bbox": polygon_to_bbox(polygon),
                }
            )

    return normalized


def polygon_to_bbox(polygon: Any) -> list[list[int]]:
    if hasattr(polygon, "tolist"):
        points = polygon.tolist()
    else:
        points = polygon

    return [[int(point[0]), int(point[1])] for point in points]
