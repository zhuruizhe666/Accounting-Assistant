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
from accounting_worker.ocr import warmup_primary_ocr
from accounting_worker.semantic_parser import parse_semantic_fields


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Accounting Assistant worker")
    subparsers = parser.add_subparsers(dest="command", required=True)

    analyze = subparsers.add_parser("analyze", help="Analyze one receipt image")
    analyze.add_argument("image_path", help="Path to the receipt image")
    analyze.add_argument("--mock", action="store_true", help="Return deterministic mock output")

    subparsers.add_parser("serve", help="Run a persistent JSON-lines worker")
    subparsers.add_parser("warmup", help="Initialize OCR models without analyzing an image")

    return parser.parse_args()


def analyze_image(image_path: Path, *, use_mock: bool) -> dict:
    if use_mock:
        log(f"mock analyze requested: {image_path}")
        return build_mock_analysis(image_path)

    with contextlib.redirect_stdout(sys.stderr):
        log(f"analyze started: {image_path}")
        log("ocr step started")
        ocr_items = run_primary_ocr(image_path)
        log(f"ocr step completed: {len(ocr_items)} item(s)")

    return {
        "image_path": str(image_path),
        "status": "ok",
        "ocr_items": ocr_items,
        "candidates": extract_candidates(ocr_items),
        "semantic_fields": {},
        "semantic_status": {
            "engine": "ollama",
            "status": "pending_ocr_review",
            "reason": "semantic parsing runs after OCR review is confirmed",
        },
    }


def parse_semantics_for_reviewed_ocr(ocr_items: list[dict], locked_fields: dict | None = None) -> dict:
    with contextlib.redirect_stdout(sys.stderr):
        log(f"semantic step started after OCR review: ocr_items={len(ocr_items)}")
        semantic_fields, semantic_status = parse_semantic_fields(ocr_items, locked_fields or {})
        log(f"semantic step completed: {semantic_status.get('status')}")

    return {
        "status": "ok",
        "semantic_fields": semantic_fields,
        "semantic_status": semantic_status,
    }


def warmup_worker() -> dict:
    with contextlib.redirect_stdout(sys.stderr):
        log("warmup started: initializing OCR model")
        warmup_primary_ocr()
        log("warmup completed: OCR model ready")

    return {
        "status": "ok",
        "worker_status": "ready",
    }


def serve() -> int:
    log("serve started: waiting for JSON-lines commands")
    for line in sys.stdin:
        try:
            request = json.loads(line)
            command = request.get("command")
            log(f"serve command received: {command}")

            if command == "warmup":
                result = warmup_worker()
            elif command == "analyze":
                result = analyze_image(
                    Path(request["image_path"]),
                    use_mock=bool(request.get("mock", False)),
                )
            elif command == "semantic":
                result = parse_semantics_for_reviewed_ocr(
                    request.get("ocr_items", []),
                    request.get("locked_fields", {}),
                )
            else:
                raise ValueError(f"Unknown serve command: {command}")
        except Exception as exc:
            log(f"serve command failed: {exc}")
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

    if args.command == "warmup":
        result = warmup_worker()
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0

    print(f"Unknown command: {args.command}", file=sys.stderr)
    return 2


def log(message: str) -> None:
    print(f"[worker] {message}", file=sys.stderr, flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
