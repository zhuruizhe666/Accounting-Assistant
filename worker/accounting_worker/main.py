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

    subparsers.add_parser("serve", help="Run a persistent JSON-lines worker")

    return parser.parse_args()


def analyze_image(image_path: Path, *, use_mock: bool) -> dict:
    if use_mock:
        return build_mock_analysis(image_path)

    with contextlib.redirect_stdout(sys.stderr):
        ocr_items = run_primary_ocr(image_path)

    return {
        "image_path": str(image_path),
        "status": "ok",
        "ocr_items": ocr_items,
        "candidates": extract_candidates(ocr_items),
    }


def serve() -> int:
    for line in sys.stdin:
        try:
            request = json.loads(line)
            command = request.get("command")

            if command != "analyze":
                raise ValueError(f"Unknown serve command: {command}")

            result = analyze_image(
                Path(request["image_path"]),
                use_mock=bool(request.get("mock", False)),
            )
        except Exception as exc:
            result = {
                "image_path": "",
                "status": "error",
                "ocr_items": [],
                "candidates": {},
                "error": str(exc),
            }

        print(json.dumps(result, ensure_ascii=False, separators=(",", ":")), flush=True)

    return 0


def main() -> int:
    args = parse_args()

    if args.command == "analyze":
        image_path = Path(args.image_path)
        result = analyze_image(image_path, use_mock=args.mock)

        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    if args.command == "serve":
        return serve()

    print(f"Unknown command: {args.command}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
