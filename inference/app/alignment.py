from __future__ import annotations

import math
import unicodedata
from dataclasses import dataclass
from typing import Any


TURKISH_MARKERS = frozenset("çğıöşüâîûÇĞİÖŞÜÂÎÛ")
TURKISH_COMMON_WORDS = frozenset({
    "ve", "bir", "bu", "için", "ile", "olan", "olarak", "çok", "daha",
    "sonra", "ama", "gibi", "kadar", "de", "da", "mi", "mı",
})


def normalize_word(value: str, language: str = "auto") -> str:
    """Normalize a word without discarding valid Turkish or English letters.

    Turkish dotted/dotless I is handled explicitly. Punctuation and apostrophes
    are removed, while Unicode letters and digits are kept. This avoids the old
    behavior that silently deleted English q/w/x characters.
    """
    normalized = unicodedata.normalize("NFC", value).replace("’", "'")
    if language in {"auto", "tr"}:
        normalized = normalized.replace("I", "ı").replace("İ", "i")
    normalized = normalized.casefold()
    return "".join(
        character
        for character in normalized
        if unicodedata.category(character)[0] in {"L", "N"}
    )


def detect_reference_language(value: str) -> str:
    if any(character in TURKISH_MARKERS for character in value):
        return "tr"
    words = {
        normalize_word(part, "tr")
        for part in value.split()
        if normalize_word(part, "tr")
    }
    if words & TURKISH_COMMON_WORDS:
        return "tr"
    return "en"


def edit_distance(left: list[str] | str, right: list[str] | str) -> int:
    previous = list(range(len(right) + 1))
    for left_index, left_value in enumerate(left, start=1):
        current = [left_index]
        for right_index, right_value in enumerate(right, start=1):
            current.append(min(
                current[-1] + 1,
                previous[right_index] + 1,
                previous[right_index - 1] + (left_value != right_value),
            ))
        previous = current
    return previous[-1]


def is_near_asr_variant(reference: str, hypothesis: str) -> bool:
    """Identify a plausible ASR spelling drift, never an automatic correct."""
    longest = max(len(reference), len(hypothesis))
    if longest < 5:
        return False
    allowed_distance = 2 if longest >= 9 else 1
    return edit_distance(reference, hypothesis) <= allowed_distance


@dataclass(frozen=True)
class RecognizedWord:
    text: str
    start_seconds: float
    end_seconds: float
    confidence: float


@dataclass(frozen=True)
class AlignmentOperation:
    kind: str
    reference_index: int | None
    hypothesis_index: int | None


def _hypothesis_position(
    index: int,
    word_count: int,
    recognized_words: list[RecognizedWord] | None,
    audio_duration_seconds: float | None,
) -> float:
    if recognized_words and audio_duration_seconds and audio_duration_seconds > 0:
        word = recognized_words[index]
        return min(1.0, max(0.0, ((word.start_seconds + word.end_seconds) / 2) / audio_duration_seconds))
    return (index + 0.5) / max(1, word_count)


def align_sequences(
    reference: list[str],
    hypothesis: list[str],
    recognized_words: list[RecognizedWord] | None = None,
    audio_duration_seconds: float | None = None,
    confidence_threshold: float = 0.72,
) -> list[AlignmentOperation]:
    """Globally align words using lexical, confidence and temporal evidence.

    The dynamic program keeps order monotonic, so one insertion/deletion cannot
    shift every following word. Exact words are strong anchors. Time contributes
    only a small penalty and therefore cannot override real lexical evidence.
    """
    rows = len(reference) + 1
    columns = len(hypothesis) + 1
    scores = [[math.inf] * columns for _ in range(rows)]
    back: list[list[str | None]] = [[None] * columns for _ in range(rows)]
    scores[0][0] = 0.0

    for row in range(1, rows):
        scores[row][0] = scores[row - 1][0] + 1.0
        back[row][0] = "delete"
    for column in range(1, columns):
        scores[0][column] = scores[0][column - 1] + 1.0
        back[0][column] = "insert"

    for row in range(1, rows):
        reference_position = (row - 0.5) / max(1, len(reference))
        for column in range(1, columns):
            reference_word = reference[row - 1]
            hypothesis_word = hypothesis[column - 1]
            exact = reference_word == hypothesis_word
            near = not exact and is_near_asr_variant(reference_word, hypothesis_word)
            confidence = (
                recognized_words[column - 1].confidence
                if recognized_words and column - 1 < len(recognized_words)
                else 1.0
            )
            base_substitution = 0.0 if exact else 0.45 if near else 0.85 if confidence < confidence_threshold else 1.15
            hypothesis_position = _hypothesis_position(
                column - 1,
                len(hypothesis),
                recognized_words,
                audio_duration_seconds,
            )
            position_penalty = abs(reference_position - hypothesis_position) * (0.12 if exact else 0.30)
            diagonal_score = scores[row - 1][column - 1] + base_substitution + position_penalty
            delete_score = scores[row - 1][column] + 1.0
            insert_score = scores[row][column - 1] + 1.0

            # On ties, preserve exact/near anchors; for unrelated words prefer
            # a real gap over forcing a substitution that can cascade.
            if exact:
                candidates = [(diagonal_score, 0, "equal"), (delete_score, 1, "delete"), (insert_score, 2, "insert")]
            elif near:
                candidates = [(diagonal_score, 0, "substitute"), (delete_score, 1, "delete"), (insert_score, 2, "insert")]
            else:
                candidates = [(delete_score, 0, "delete"), (insert_score, 1, "insert"), (diagonal_score, 2, "substitute")]
            best_score, _, best_kind = min(candidates, key=lambda item: (round(item[0], 9), item[1]))
            scores[row][column] = best_score
            back[row][column] = best_kind

    operations: list[AlignmentOperation] = []
    row = len(reference)
    column = len(hypothesis)
    while row > 0 or column > 0:
        kind = back[row][column]
        if kind in {"equal", "substitute"}:
            operations.append(AlignmentOperation(kind, row - 1, column - 1))
            row -= 1
            column -= 1
        elif kind == "insert":
            operations.append(AlignmentOperation("insert", None, column - 1))
            column -= 1
        else:
            operations.append(AlignmentOperation("delete", row - 1, None))
            row -= 1
    operations.reverse()
    return operations


def _is_reliable_anchor(
    operation: AlignmentOperation | None,
    recognized_words: list[RecognizedWord],
    confidence_threshold: float,
) -> bool:
    return (
        operation is not None
        and operation.kind == "equal"
        and operation.hypothesis_index is not None
        and recognized_words[operation.hypothesis_index].confidence >= confidence_threshold
    )


def _neighboring_reliable_anchors(
    operation: AlignmentOperation,
    operations: list[AlignmentOperation],
    recognized_words: list[RecognizedWord],
    confidence_threshold: float,
) -> tuple[bool, bool]:
    """Return whether an operation is bounded by two trustworthy exact words.

    A CTC frame maximum is not a calibrated word confidence.  Requiring exact
    lexical anchors on both sides prevents a confident decoder mistake from
    being presented as a definite student error.
    """
    position = operations.index(operation)
    previous = next(
        (candidate for candidate in reversed(operations[:position]) if candidate.hypothesis_index is not None),
        None,
    )
    following = next(
        (candidate for candidate in operations[position + 1:] if candidate.hypothesis_index is not None),
        None,
    )
    return (
        _is_reliable_anchor(previous, recognized_words, confidence_threshold),
        _is_reliable_anchor(following, recognized_words, confidence_threshold),
    )


def _boundary_equivalent_groups(
    operations: list[AlignmentOperation],
    reference: list[str],
    hypothesis: list[str],
    max_group_words: int = 4,
) -> list[tuple[list[int], list[int]]]:
    """Find CTC word-boundary shifts with an identical character stream.

    Turkish CTC models can decode ``erik ağacına`` as ``eri kağacına`` or
    ``cıvıl cıvıl`` as one token.  There is no acoustic evidence for a reading
    error when the normalized character stream is identical; only the model's
    delimiter moved.  Runs are deliberately small so unrelated text cannot be
    absorbed into a broad match.
    """
    groups: list[tuple[list[int], list[int]]] = []
    run: list[AlignmentOperation] = []

    def flush() -> None:
        if not run:
            return
        reference_indexes = sorted({
            operation.reference_index
            for operation in run
            if operation.reference_index is not None
        })
        hypothesis_indexes = sorted({
            operation.hypothesis_index
            for operation in run
            if operation.hypothesis_index is not None
        })
        if (
            reference_indexes
            and hypothesis_indexes
            and len(reference_indexes) <= max_group_words
            and len(hypothesis_indexes) <= max_group_words
            and "".join(reference[index] for index in reference_indexes)
            == "".join(hypothesis[index] for index in hypothesis_indexes)
        ):
            groups.append((reference_indexes, hypothesis_indexes))
        run.clear()

    for operation in operations:
        if operation.kind == "equal":
            flush()
        else:
            run.append(operation)
    flush()
    return groups


def build_reading_analysis(
    reference_text: str,
    recognized_words: list[RecognizedWord],
    audio_duration_seconds: float,
    confidence_threshold: float,
    pause_threshold_seconds: float,
    sentence_pause_threshold_seconds: float,
    min_assessable_ratio: float,
) -> dict[str, Any]:
    language = detect_reference_language(reference_text)
    reference_surface_all = reference_text.split()
    normalized_all = [normalize_word(word, language) for word in reference_surface_all]
    reference_indexes = [index for index, word in enumerate(normalized_all) if word]
    reference_surface = [reference_surface_all[index] for index in reference_indexes]
    reference = [normalized_all[index] for index in reference_indexes]
    hypothesis = [normalize_word(word.text, language) for word in recognized_words]
    operations = align_sequences(
        reference,
        hypothesis,
        recognized_words,
        audio_duration_seconds,
        confidence_threshold,
    )
    operation_positions = {id(operation): index for index, operation in enumerate(operations)}

    results: list[dict[str, Any]] = [
        {
            "index": original_index,
            "reference": surface,
            "recognized": None,
            "normalizedExpected": normalized,
            "normalizedRecognized": None,
            "status": "uncertain",
            "confidence": None,
            "startSeconds": None,
            "endSeconds": None,
            "durationSeconds": None,
            "effectiveDurationSeconds": None,
            "pauseBeforeSeconds": 0.0,
            "repeatCount": 0,
            "isCorrect": False,
            "evidence": "ASR hizalaması bu kelime için yeterli kanıt üretmedi.",
        }
        for original_index, surface, normalized in zip(reference_indexes, reference_surface, reference)
    ]
    result_by_reference = {reference_index: results[index] for index, reference_index in enumerate(range(len(reference)))}

    aligned_operations = [
        operation
        for operation in operations
        if operation.reference_index is not None and operation.hypothesis_index is not None
    ]
    for operation in aligned_operations:
        assert operation.reference_index is not None and operation.hypothesis_index is not None
        word = recognized_words[operation.hypothesis_index]
        result = result_by_reference[operation.reference_index]
        normalized_recognized = hypothesis[operation.hypothesis_index]
        result.update({
            "recognized": word.text,
            "normalizedRecognized": normalized_recognized,
            "confidence": round(word.confidence, 4),
            "startSeconds": round(word.start_seconds, 3),
            "endSeconds": round(word.end_seconds, 3),
            "durationSeconds": round(max(0.0, word.end_seconds - word.start_seconds), 3),
            "effectiveDurationSeconds": round(max(0.0, word.end_seconds - word.start_seconds), 3),
        })
        if word.confidence < confidence_threshold:
            result["status"] = "uncertain"
            result["evidence"] = "ASR kelime güveni eşik altında; kesin doğru veya hata sayılmadı."
        elif operation.kind == "equal":
            result["status"] = "correct"
            result["isCorrect"] = True
            result["evidence"] = "Yüksek güvenli ASR çıktısı beklenen kelimeyle eşleşti."
        elif is_near_asr_variant(reference[operation.reference_index], normalized_recognized):
            result["status"] = "uncertain"
            result["evidence"] = "Algılanan biçim beklenen kelimeye çok yakın; ASR yazım sapması olabileceği için kesin hata sayılmadı."
        else:
            previous_anchor, following_anchor = _neighboring_reliable_anchors(
                operation,
                operations,
                recognized_words,
                confidence_threshold,
            )
            if previous_anchor and following_anchor:
                result["status"] = "error"
                result["evidence"] = "Farklı algılanan kelime, iki yüksek güvenli doğru komşu arasında kaldığı için kesin hata kanıtı sayıldı."
            else:
                result["status"] = "uncertain"
                result["evidence"] = "ASR farklı bir kelime üretti ancak iki güvenilir doğru komşuyla doğrulanamadı; kesin hata sayılmadı."

    boundary_groups = _boundary_equivalent_groups(operations, reference, hypothesis)
    boundary_reference_indexes = {
        index
        for reference_indexes, _ in boundary_groups
        for index in reference_indexes
    }
    boundary_hypothesis_indexes = {
        index
        for _, hypothesis_indexes in boundary_groups
        for index in hypothesis_indexes
    }
    for group_reference_indexes, group_hypothesis_indexes in boundary_groups:
        group_words = [recognized_words[index] for index in group_hypothesis_indexes]
        group_confidence = min(word.confidence for word in group_words)
        group_start = min(word.start_seconds for word in group_words)
        group_end = max(word.end_seconds for word in group_words)
        total_characters = sum(len(reference[index]) for index in group_reference_indexes)
        consumed_characters = 0
        recognized_surface = " ".join(word.text for word in group_words)
        normalized_recognized = "".join(hypothesis[index] for index in group_hypothesis_indexes)
        for reference_index in group_reference_indexes:
            result = result_by_reference[reference_index]
            start_ratio = consumed_characters / max(1, total_characters)
            consumed_characters += len(reference[reference_index])
            end_ratio = consumed_characters / max(1, total_characters)
            start_seconds = group_start + (group_end - group_start) * start_ratio
            end_seconds = group_start + (group_end - group_start) * end_ratio
            result.update({
                "recognized": recognized_surface,
                "normalizedRecognized": normalized_recognized,
                "confidence": round(group_confidence, 4),
                "startSeconds": round(start_seconds, 3),
                "endSeconds": round(end_seconds, 3),
                "durationSeconds": round(max(0.0, end_seconds - start_seconds), 3),
                "effectiveDurationSeconds": round(max(0.0, end_seconds - start_seconds), 3),
            })
            if group_confidence >= confidence_threshold:
                result["status"] = "correct"
                result["isCorrect"] = True
                result["evidence"] = "ASR yalnızca kelime sınırını farklı yerleştirdi; birleşik normalize karakter dizisi beklenen ifadeyle aynı."
            else:
                result["status"] = "uncertain"
                result["isCorrect"] = False
                result["evidence"] = "Kelime sınırı eşleşiyor ancak ASR güveni düşük; kesin sınıflandırılmadı."

    deletion_operations = [
        operation
        for operation in operations
        if operation.kind == "delete" and operation.reference_index is not None
    ]
    first_hypothesis_position = next(
        (index for index, operation in enumerate(operations) if operation.hypothesis_index is not None),
        None,
    )
    last_hypothesis_position = next(
        (index for index in range(len(operations) - 1, -1, -1) if operations[index].hypothesis_index is not None),
        None,
    )
    possible_start_clipping = False
    possible_end_clipping = False
    for operation in deletion_operations:
        assert operation.reference_index is not None
        if operation.reference_index in boundary_reference_indexes:
            continue
        position = operation_positions[id(operation)]
        previous = next((candidate for candidate in reversed(operations[:position]) if candidate.hypothesis_index is not None), None)
        following = next((candidate for candidate in operations[position + 1:] if candidate.hypothesis_index is not None), None)
        result = result_by_reference[operation.reference_index]
        if first_hypothesis_position is not None and position < first_hypothesis_position:
            possible_start_clipping = recognized_words[0].start_seconds <= 0.25
            result["evidence"] = "Kayıt ilk algılanan kelimeyle hemen başladığı için metnin başlangıcı kesilmiş veya kelime atlanmış olabilir."
        elif last_hypothesis_position is not None and position > last_hypothesis_position:
            trailing_silence = max(0.0, audio_duration_seconds - recognized_words[-1].end_seconds)
            possible_end_clipping = trailing_silence <= 0.25
            result["evidence"] = "Kayıt son algılanan kelimeden hemen sonra bittiği için metnin sonu kesilmiş veya kelime atlanmış olabilir."
        elif _is_reliable_anchor(previous, recognized_words, confidence_threshold) and _is_reliable_anchor(following, recognized_words, confidence_threshold):
            result["status"] = "missing"
            result["evidence"] = "İki doğrulanmış komşu kelime arasında beklenen kelime için ASR kanıtı bulunamadı."

    extra_words = 0
    extra_word_details: list[dict[str, Any]] = []
    for operation in operations:
        if operation.kind != "insert" or operation.hypothesis_index is None:
            continue
        if operation.hypothesis_index in boundary_hypothesis_indexes:
            continue
        inserted = recognized_words[operation.hypothesis_index]
        if inserted.confidence < confidence_threshold:
            extra_word_details.append({
                "recognized": inserted.text,
                "status": "uncertain",
                "confidence": round(inserted.confidence, 4),
                "startSeconds": round(inserted.start_seconds, 3),
                "endSeconds": round(inserted.end_seconds, 3),
            })
            continue
        position = operation_positions[id(operation)]
        previous_reference = next((candidate.reference_index for candidate in reversed(operations[:position]) if candidate.reference_index is not None), None)
        following_reference = next((candidate.reference_index for candidate in operations[position + 1:] if candidate.reference_index is not None), None)
        normalized_inserted = hypothesis[operation.hypothesis_index]
        repeat_reference = next((
            candidate
            for candidate in (previous_reference, following_reference)
            if candidate is not None and normalized_inserted == reference[candidate]
        ), None)
        if repeat_reference is None:
            previous_anchor = next(
                (candidate for candidate in reversed(operations[:position]) if candidate.reference_index is not None),
                None,
            )
            following_anchor = next(
                (candidate for candidate in operations[position + 1:] if candidate.reference_index is not None),
                None,
            )
            is_definite_extra = (
                _is_reliable_anchor(previous_anchor, recognized_words, confidence_threshold)
                and _is_reliable_anchor(following_anchor, recognized_words, confidence_threshold)
            )
            if is_definite_extra:
                extra_words += 1
            extra_word_details.append({
                "recognized": inserted.text,
                "status": "extra" if is_definite_extra else "uncertain",
                "confidence": round(inserted.confidence, 4),
                "startSeconds": round(inserted.start_seconds, 3),
                "endSeconds": round(inserted.end_seconds, 3),
                "evidence": (
                    "Fazladan algılanan kelime iki güvenilir doğru komşu arasında doğrulandı."
                    if is_definite_extra
                    else "Fazladan ASR çıktısı iki güvenilir doğru komşuyla doğrulanamadı; kesin hata sayılmadı."
                ),
            })
            continue
        repeated_result = result_by_reference[repeat_reference]
        repeated_result["repeatCount"] += 1
        if repeated_result["isCorrect"]:
            repeated_result["status"] = "repeat"
            repeated_result["startSeconds"] = round(min(repeated_result["startSeconds"], inserted.start_seconds), 3)
            repeated_result["endSeconds"] = round(max(repeated_result["endSeconds"], inserted.end_seconds), 3)
            repeated_result["effectiveDurationSeconds"] = round(repeated_result["endSeconds"] - repeated_result["startSeconds"], 3)
            repeated_result["evidence"] = "Aynı kelime yüksek güvenle art arda yeniden algılandı."

    pause_events: list[dict[str, Any]] = []
    previous_aligned: AlignmentOperation | None = None
    for operation in aligned_operations:
        if previous_aligned is not None:
            assert previous_aligned.hypothesis_index is not None
            assert previous_aligned.reference_index is not None
            assert operation.hypothesis_index is not None
            assert operation.reference_index is not None
            previous_word = recognized_words[previous_aligned.hypothesis_index]
            current_word = recognized_words[operation.hypothesis_index]
            gap = max(0.0, current_word.start_seconds - previous_word.end_seconds)
            previous_surface = reference_surface[previous_aligned.reference_index]
            threshold = sentence_pause_threshold_seconds if previous_surface.rstrip().endswith((".", "!", "?")) else pause_threshold_seconds
            result = result_by_reference[operation.reference_index]
            result["pauseBeforeSeconds"] = round(gap, 3)
            if result["durationSeconds"] is not None:
                result["effectiveDurationSeconds"] = round(result["durationSeconds"] + gap, 3)
            if gap > threshold:
                pause_events.append({
                    "afterReferenceIndex": reference_indexes[previous_aligned.reference_index],
                    "beforeReferenceIndex": reference_indexes[operation.reference_index],
                    "durationSeconds": round(gap, 3),
                    "thresholdSeconds": round(threshold, 3),
                    "afterSentencePunctuation": previous_surface.rstrip().endswith((".", "!", "?")),
                })
                if result["isCorrect"] and result["status"] != "repeat":
                    result["status"] = "pause"
                    result["evidence"] = f"Önceki kelimeden sonra {gap:.2f} sn sessizlik ölçüldü (eşik {threshold:.2f} sn)."
        previous_aligned = operation

    substitutions = sum(
        operation.kind == "substitute"
        and operation.reference_index not in boundary_reference_indexes
        and operation.hypothesis_index not in boundary_hypothesis_indexes
        for operation in operations
    )
    deletions = sum(
        operation.kind == "delete" and operation.reference_index not in boundary_reference_indexes
        for operation in operations
    )
    insertions = sum(
        operation.kind == "insert" and operation.hypothesis_index not in boundary_hypothesis_indexes
        for operation in operations
    )
    correct = sum(bool(result["isCorrect"]) for result in results)
    errors = sum(result["status"] == "error" for result in results)
    missing = sum(result["status"] == "missing" for result in results)
    uncertain = sum(result["status"] == "uncertain" for result in results)
    repeats = sum(int(result["repeatCount"]) for result in results)
    pauses = sum(result["status"] == "pause" for result in results)
    assessable = correct + errors + missing
    assessable_ratio = assessable / len(reference) if reference else 0.0
    accuracy = round(correct / len(reference) * 100, 1) if reference and assessable_ratio >= min_assessable_ratio else None
    assessable_accuracy = round(correct / assessable * 100, 1) if assessable else None
    correct_wpm = round(correct / (audio_duration_seconds / 60), 1) if audio_duration_seconds > 0 and assessable_ratio >= min_assessable_ratio else None
    reference_characters = "".join(reference)
    hypothesis_characters = "".join(hypothesis)
    leading_silence = max(0.0, recognized_words[0].start_seconds) if recognized_words else audio_duration_seconds
    trailing_silence = max(0.0, audio_duration_seconds - recognized_words[-1].end_seconds) if recognized_words else audio_duration_seconds

    warnings: list[str] = []
    if uncertain:
        warnings.append(f"{uncertain} kelime düşük veya çelişkili ASR kanıtı nedeniyle kesin sınıflandırılmadı.")
    if possible_start_clipping:
        warnings.append("Kaydın başlangıcı kesilmiş olabilir; ilk kelimeler kesin hata sayılmadı.")
    if possible_end_clipping:
        warnings.append("Kaydın sonu kesilmiş olabilir; son kelimeler kesin hata sayılmadı.")
    if accuracy is None:
        warnings.append("Değerlendirilebilir kelime oranı yetersiz; doğruluk ve hız yayımlanmadı.")
    warnings.append("Kelime ve cümle seviyesinde geri dönüş bu sürümde güvenilir biçimde ölçülmüyor.")

    return {
        "alignmentVersion": "3.1-word-boundary-anchor-precision",
        "referenceLanguage": language,
        "referenceWordCount": len(reference),
        "transcript": " ".join(word.text for word in recognized_words),
        "durationSeconds": round(audio_duration_seconds, 3),
        "accuracyPercent": accuracy,
        "assessableAccuracyPercent": assessable_accuracy,
        "assessableWordRatio": round(assessable_ratio, 4),
        "correctWordsPerMinute": correct_wpm,
        "speedBasis": "Tüm kayıt süresi içindeki doğrulanmış doğru kelime sayısı.",
        "definiteErrorPolicy": "Bir ASR farkı yalnızca iki güvenilir doğru komşu veya iki güvenilir atlama çapasıyla desteklenirse kesin hata sayılır.",
        "sessionWer": round((substitutions + deletions + insertions) / max(1, len(reference)), 4),
        "sessionCer": round(edit_distance(reference_characters, hypothesis_characters) / max(1, len(reference_characters)), 4),
        "recordingBoundary": {
            "leadingSilenceSeconds": round(leading_silence, 3),
            "trailingSilenceSeconds": round(trailing_silence, 3),
            "possibleStartClipping": possible_start_clipping,
            "possibleEndClipping": possible_end_clipping,
        },
        "counts": {
            "correct": correct,
            "errors": errors,
            "missing": missing,
            "extra": extra_words,
            "repeats": repeats,
            "pauses": pauses,
            "uncertain": uncertain,
            "wordLevelReturns": None,
            "sentenceLevelReturns": None,
        },
        "words": results,
        "extraWords": extra_word_details,
        "pauseEvents": pause_events,
        "warnings": warnings,
    }
