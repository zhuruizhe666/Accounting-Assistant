from __future__ import annotations

import argparse
import contextlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

from accounting_worker.candidate_extractor import extract_candidates
from accounting_worker.mock_analysis import build_mock_analysis
from accounting_worker.ocr import run_primary_ocr


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Accounting Assistant worker")
    subparsers = parser.add_subparsers(dest="command", required=True)

    analyze = subparsers.add_parser("analyze", help="Analyze one receipt image")
    analyze.add_argument("image_path", help="Path to the receipt image")
    analyze.add_argument("--mock", action="store_true", help="Return deterministic mock output")

    return parser.parse_args()


def main() -> int:
    args = parse_args()

    if args.command == "analyze":
        image_path = Path(args.image_path)
        if args.mock:
            result = build_mock_analysis(image_path)
        else:
            with contextlib.redirect_stdout(sys.stderr):
                ocr_items = run_primary_ocr(image_path)
            result = {
                "image_path": str(image_path),
                "status": "ok",
                "ocr_items": ocr_items,
                "candidates": extract_candidates(ocr_items),
            }

        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    print(f"Unknown command: {args.command}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
