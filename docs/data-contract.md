# Data Contract

The Python worker writes a single JSON object to stdout for each analyzed image.

## Receipt Analysis Result

Required fields:

- `image_path`: original image path.
- `status`: worker status such as `mock`, `ok`, or `error`.
- `ocr_items`: ordered OCR text blocks.
- `candidates`: candidate field values grouped by field name.

## OCR Item

- `text`: recognized text.
- `confidence`: OCR confidence from 0 to 1.
- `bbox`: four image-space points, clockwise from top-left.

## Candidate

- `value`: normalized candidate value.
- `confidence`: candidate confidence from 0 to 1.
- `source_text`: nearby OCR text that explains the candidate.
- `bbox_refs`: zero-based indexes into `ocr_items`.

The UI should treat `bbox_refs` as the source of image highlights.
