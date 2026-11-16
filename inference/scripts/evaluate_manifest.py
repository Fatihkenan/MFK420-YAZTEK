from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.config import Settings
from app.model import SpeechEngine


def safe_divide(numerator: int, denominator: int) -> float | None:
    return round(numerator / denominator, 4) if denominator else None


def main() -> int:
    parser = argparse.ArgumentParser(description="Run YAZTEK ASR evaluation on consented, manually labelled audio.")
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    if not args.manifest.is_file():
        print(f"Manifest bulunamadı: {args.manifest}", file=sys.stderr)
        return 2
    payload = json.loads(args.manifest.read_text(encoding="utf-8"))
    samples = payload.get("samples", [])
    if not samples:
        print("Manifestte gerçek, elle doğrulanmış örnek yok; başarı metriği üretilmedi.", file=sys.stderr)
        return 3

    engine = SpeechEngine(Settings())
    engine.load()
    if not engine.ready:
        print(f"Model hazır değil: {engine.load_error}", file=sys.stderr)
        return 4

    root = args.manifest.parent
    reports = []
    total_ref_words = 0
    total_wer_errors = 0.0
    total_ref_chars = 0
    total_cer_errors = 0.0
    expected_correct = 0
    false_positive_errors = 0
    expected_pauses = 0
    predicted_pauses = 0
    true_positive_pauses = 0
    status_labelled_samples = 0
    pause_labelled_samples = 0

    for sample in samples:
        audio_path = (root / sample["audio"]).resolve()
        if not audio_path.is_file():
            print(f"Ses dosyası bulunamadı: {audio_path}", file=sys.stderr)
            return 5
        result = engine.analyze(audio_path, sample["referenceText"])
        expected_statuses = sample.get("expectedWordStatuses")
        if expected_statuses is not None:
            if len(expected_statuses) != result["referenceWordCount"]:
                print(f"Elle etiket sayısı kelime sayısıyla eşleşmiyor: {audio_path.name}", file=sys.stderr)
                return 6
            status_labelled_samples += 1
            for expected, predicted in zip(expected_statuses, result["words"]):
                if expected == "correct":
                    expected_correct += 1
                    if predicted["status"] in {"error", "missing"}:
                        false_positive_errors += 1

        expected_pause_values = sample.get("expectedPauseAfterWordIndexes")
        if expected_pause_values is not None:
            pause_labelled_samples += 1
            expected_pause_indexes = set(expected_pause_values)
            predicted_pause_indexes = {item["index"] - 1 for item in result["words"] if item["status"] == "pause" and item["index"] > 0}
            expected_pauses += len(expected_pause_indexes)
            predicted_pauses += len(predicted_pause_indexes)
            true_positive_pauses += len(expected_pause_indexes & predicted_pause_indexes)
        total_ref_words += result["referenceWordCount"]
        total_wer_errors += result["sessionWer"] * result["referenceWordCount"]
        normalized_reference_chars = len("".join(item["normalizedExpected"] for item in result["words"]))
        total_ref_chars += normalized_reference_chars
        total_cer_errors += result["sessionCer"] * normalized_reference_chars
        reports.append({"audio": sample["audio"], "analysis": result})

    summary = {
        "sampleCount": len(reports),
        "modelId": engine.settings.model_id,
        "wer": round(total_wer_errors / max(1, total_ref_words), 4),
        "cer": round(total_cer_errors / max(1, total_ref_chars), 4),
        "statusLabelledSampleCount": status_labelled_samples,
        "pauseLabelledSampleCount": pause_labelled_samples,
        "falsePositiveErrorRate": safe_divide(false_positive_errors, expected_correct) if status_labelled_samples else None,
        "pausePrecision": safe_divide(true_positive_pauses, predicted_pauses) if pause_labelled_samples else None,
        "pauseRecall": safe_divide(true_positive_pauses, expected_pauses) if pause_labelled_samples else None,
        "notes": [
            "WER/CER, manifestte verilen veri kümesi referans transkriptlerine göre hesaplanır.",
            "Yanlış pozitif ve duraksama metrikleri yalnızca ilgili elle doğrulanmış etiketler varsa üretilir.",
            "Çocuk sesi ayrı bir kohort olarak raporlanmadıkça çocuk performansı iddia edilmez.",
        ],
    }
    output = {"summary": summary, "samples": reports}
    rendered = json.dumps(output, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
