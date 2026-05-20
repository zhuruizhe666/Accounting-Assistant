from __future__ import annotations

from pathlib import Path
from typing import Any


def build_mock_analysis(image_path: Path) -> dict[str, Any]:
    """Return deterministic Phase 0 data that matches the shared JSON contract."""
    return {
        "image_path": str(image_path),
        "status": "mock",
        "ocr_items": [
            {
                "text": "Date",
                "confidence": 0.99,
                "bbox": [[40, 40], [120, 40], [120, 70], [40, 70]],
            },
            {
                "text": "2026-04-01",
                "confidence": 0.96,
                "bbox": [[130, 40], [270, 40], [270, 70], [130, 70]],
            },
            {
                "text": "Total",
                "confidence": 0.98,
                "bbox": [[40, 220], [120, 220], [120, 255], [40, 255]],
            },
            {
                "text": "128.00",
                "confidence": 0.95,
                "bbox": [[230, 220], [330, 220], [330, 255], [230, 255]],
            },
        ],
        "candidates": {
            "date": [
                {
                    "value": "2026-04-01",
                    "confidence": 0.92,
                    "source_text": "Date 2026-04-01",
                    "bbox_refs": [0, 1],
                }
            ],
            "total": [
                {
                    "value": "128.00",
                    "confidence": 0.9,
                    "source_text": "Total 128.00",
                    "bbox_refs": [2, 3],
                }
            ],
        },
    }
