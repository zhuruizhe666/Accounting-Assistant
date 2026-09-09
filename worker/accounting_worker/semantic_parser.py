from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from typing import Any

FIELD_ORDER = [
    "document_type",
    "document_number",
    "issue_date",
    "counterparty_name",
    "total_amount",
    "tax_amount",
    "expense_category",
    "project_name",
    "department",
    "handler",
    "summary",
    "notes",
]

DEFAULT_OLLAMA_BASE_URL = "http://localhost:11434"
DEFAULT_OLLAMA_MODEL = "qwen2.5:7b-instruct"


def parse_semantic_fields(
    ocr_items: list[dict[str, Any]],
    locked_fields: dict[str, Any] | None = None,
) -> tuple[dict[str, list[dict[str, Any]]], dict[str, Any]]:
    locked_fields = locked_fields or {}
    log(f"semantic parse requested: ocr_items={len(ocr_items)}")
    if not ocr_items:
        log("semantic parse skipped: no OCR items")
        return empty_semantic_fields(), {"engine": "ollama", "status": "skipped", "reason": "no_ocr_items"}

    base_url = os.environ.get("OLLAMA_BASE_URL", DEFAULT_OLLAMA_BASE_URL).rstrip("/")
    model = os.environ.get("OLLAMA_MODEL", DEFAULT_OLLAMA_MODEL)
    log(f"semantic engine configured: base_url={base_url} model={model}")

    prompt = build_prompt(ocr_items, locked_fields)
    log(f"semantic prompt built: chars={len(prompt)}")
    request_body = {
        "model": model,
        "stream": False,
        "format": "json",
        "prompt": prompt,
        "options": {
            "temperature": 0,
            "num_ctx": 8192,
        },
    }

    try:
        log("ollama request started")
        response = post_json(f"{base_url}/api/generate", request_body, timeout_seconds=90)
        log("ollama request completed")
        model_text = response.get("response", "")
        parsed = parse_model_json(model_text)
        log("ollama JSON parsed")
        return normalize_semantic_fields(parsed, ocr_items, locked_fields), {
            "engine": "ollama",
            "model": model,
            "status": "ok",
        }
    except Exception as exc:
        log(f"semantic parse failed: {exc}")
        return empty_semantic_fields(), {
            "engine": "ollama",
            "model": model,
            "status": "error",
            "reason": str(exc),
        }


def build_prompt(ocr_items: list[dict[str, Any]], locked_fields: dict[str, Any]) -> str:
    compact_items = [
        {
            "index": index,
            "text": item.get("corrected_text") or item.get("text", ""),
            "original_text": item.get("text", ""),
            "was_corrected": bool(item.get("corrected_text")),
            "confidence": item.get("confidence", 0),
            "bbox": item.get("bbox", []),
        }
        for index, item in enumerate(ocr_items)
    ]
    compact_locked_fields = {
        field_name: {
            "value": str(field.get("value", "")),
            "ocr_refs": field.get("ocr_refs", []),
            "source": field.get("source", "human"),
        }
        for field_name, field in locked_fields.items()
        if field_name in FIELD_ORDER and str(field.get("value", "")).strip()
    }

    return (
        "You are a receipt OCR semantic field classifier.\n"
        "Return ONLY valid JSON. Do not use markdown. Do not explain.\n"
        "Do not invent values that are not present in OCR items.\n"
        "Human prefilled fields may be wrong. Use them only as context.\n"
        "You must still infer each field independently from OCR items.\n"
        "If OCR evidence disagrees with a human prefilled field, return the OCR-supported value.\n"
        "Use English field IDs only.\n"
        "Every field item must reference source OCR indexes in ocr_refs.\n"
        "Allowed field IDs, in required order:\n"
        f"{json.dumps(FIELD_ORDER, ensure_ascii=False)}\n"
        "Return exactly this JSON shape:\n"
        "{\n"
        '  "fields": [\n'
        '    {"field":"issue_date","value":"string","ocr_refs":[0],"confidence":0.0,"reason":"short reason"}\n'
        "  ]\n"
        "}\n"
        "Use document_number for invoice numbers, receipt numbers, and bill numbers.\n"
        "Use counterparty_name for the visible trading party most relevant to accounting review.\n"
        "Use summary for a short business description based only on OCR text.\n"
        "If unsure, omit the field.\n"
        "Human prefilled fields:\n"
        f"{json.dumps(compact_locked_fields, ensure_ascii=False)}\n"
        "OCR items:\n"
        f"{json.dumps(compact_items, ensure_ascii=False)}"
    )


def post_json(url: str, body: dict[str, Any], timeout_seconds: int) -> dict[str, Any]:
    data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.URLError as exc:
        raise RuntimeError(f"Ollama request failed: {exc}") from exc


def parse_model_json(model_text: str) -> dict[str, Any]:
    text = model_text.strip()
    if text.startswith("```"):
        text = text.strip("`").strip()
        if text.startswith("json"):
            text = text[4:].strip()

    return json.loads(text)


def normalize_semantic_fields(
    parsed: dict[str, Any],
    ocr_items: list[dict[str, Any]],
    locked_fields: dict[str, Any] | None = None,
) -> dict[str, list[dict[str, Any]]]:
    output = empty_semantic_fields()
    ocr_count = len(ocr_items)
    for raw_field in parsed.get("fields", []):
        field_name = str(raw_field.get("field", "")).strip()
        if field_name not in output:
            continue

        value = str(raw_field.get("value", "")).strip()
        if not value:
            continue

        refs = [
            ref
            for ref in raw_field.get("ocr_refs", [])
            if isinstance(ref, int) and 0 <= ref < ocr_count
        ]
        if not refs:
            continue

        output[field_name].append(
            {
                "value": value,
                "confidence": clamp_float(raw_field.get("confidence", 0.0)),
                "ocr_refs": refs,
                "reason": str(raw_field.get("reason", "")).strip(),
            }
        )

    return output


def empty_semantic_fields() -> dict[str, list[dict[str, Any]]]:
    return {field: [] for field in FIELD_ORDER}


def clamp_float(value: Any) -> float:
    try:
        parsed = float(value)
    except (TypeError, ValueError):
        return 0.0

    return max(0.0, min(1.0, parsed))


def log(message: str) -> None:
    print(f"[semantic] {message}", file=os.sys.stderr, flush=True)
