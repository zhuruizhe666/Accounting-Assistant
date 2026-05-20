# Development Flow

## Phase 0: Project Skeleton

Goal: prove the C# WPF app can call the Python worker and display normalized JSON.

Done when:

- The WPF app starts.
- Users can select local JPG, JPEG, and PNG files.
- The app can call the Python worker in mock mode.
- The worker returns JSON that matches `shared/schemas/receipt-analysis.schema.example.json`.

## Phase 1: Receipt Queue

Goal: turn image selection into a stable review queue.

Done when:

- Multiple images can be loaded.
- Each image has an independent status.
- Bad image paths and unsupported files show errors without crashing the app.

## Phase 2: Primary OCR

Goal: replace mock OCR items with PaddleOCR output.

Done when:

- Each OCR item has text, confidence, and bbox.
- Clear receipts return usable text.
- OCR failure returns structured errors.

## Phase 3: Candidate Generation

Goal: extract likely dates, totals, tax amounts, merchant names, and receipt numbers.

Done when:

- Correct total appears in candidates for at least 85 percent of the initial receipt set.
- Correct date appears in candidates for at least 90 percent of the initial receipt set.
- Candidate lists stay short enough for human review.

## Phase 4: Review UI

Goal: make the app useful before adding advanced intelligence.

Done when:

- The image is shown next to extracted fields.
- Candidate fields can be selected or edited.
- Confirmed values are retained for export.

## Phase 5: Excel Export

Goal: write confirmed receipts to `.xlsx`.

Done when:

- Confirmed rows export correctly.
- Amounts are numeric Excel cells.
- Re-exporting does not create confusing duplicates.
