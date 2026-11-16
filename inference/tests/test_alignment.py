from app.alignment import (
    RecognizedWord,
    build_reading_analysis,
    detect_reference_language,
    normalize_word,
)


def word(text: str, start: float, end: float, confidence: float = 0.95) -> RecognizedWord:
    return RecognizedWord(text, start, end, confidence)


def analyze(reference: str, recognized: list[RecognizedWord]):
    return build_reading_analysis(reference, recognized, 6.0, 0.72, 1.5, 2.5, 0.7)


def test_exact_words_are_correct():
    result = analyze("Yapay zekâ günlük yaşamda kullanılır.", [
        word("yapay", 0.2, 0.6), word("zekâ", 0.7, 1.1), word("günlük", 1.2, 1.7),
        word("yaşamda", 1.8, 2.3), word("kullanılır", 2.4, 3.0),
    ])
    assert result["counts"]["correct"] == 5
    assert result["accuracyPercent"] == 100.0


def test_low_confidence_substitution_is_uncertain_not_error():
    result = analyze("Kuşlar neşeyle öttü.", [
        word("kuşlar", 0.1, 0.5), word("başka", 0.6, 1.0, 0.41), word("öttü", 1.1, 1.5),
    ])
    assert result["words"][1]["status"] == "uncertain"
    assert result["counts"]["errors"] == 0


def test_high_confidence_substitution_is_error():
    result = analyze("Kuşlar neşeyle öttü.", [
        word("kuşlar", 0.1, 0.5), word("başka", 0.6, 1.0), word("öttü", 1.1, 1.5),
    ])
    assert result["words"][1]["status"] == "error"
    assert result["counts"]["errors"] == 1


def test_near_high_confidence_asr_variant_is_uncertain_not_error():
    result = analyze("Dört Temmuz'u geride bıraktık.", [
        word("dört", 0.1, 0.4), word("temmuzu", 0.5, 0.9),
        word("gerize", 1.0, 1.4), word("bıraktık", 1.5, 2.0),
    ])
    assert result["words"][2]["status"] == "uncertain"
    assert result["counts"]["errors"] == 0
    assert result["counts"]["uncertain"] == 1


def test_normal_sentence_pause_uses_larger_threshold():
    result = analyze("Kuşlar öttü. Yavru uyandı.", [
        word("kuşlar", 0.1, 0.5), word("öttü", 0.6, 1.0), word("yavru", 3.1, 3.5), word("uyandı", 3.6, 4.1),
    ])
    assert result["words"][2]["status"] == "correct"
    assert result["counts"]["pauses"] == 0


def test_long_non_punctuation_pause_is_detected():
    result = analyze("Kuşlar neşeyle öttü.", [
        word("kuşlar", 0.1, 0.5), word("neşeyle", 2.2, 2.7), word("öttü", 2.8, 3.2),
    ])
    assert result["words"][1]["status"] == "pause"
    assert result["counts"]["pauses"] == 1


def test_repeated_word_is_separate_from_generic_extra_word():
    result = analyze("Kuşlar öttü.", [
        word("kuşlar", 0.1, 0.5), word("kuşlar", 0.6, 1.0),
        word("fazla", 1.1, 1.4), word("öttü", 1.5, 2.0),
    ])
    assert result["words"][0]["status"] == "repeat"
    assert result["words"][0]["effectiveDurationSeconds"] == 0.9
    assert result["counts"]["repeats"] == 1
    assert result["counts"]["extra"] == 1


def test_turkish_and_english_normalization_preserves_real_letters():
    assert normalize_word("İSTANBUL'da", "tr") == "istanbulda"
    assert normalize_word("IŞIK", "tr") == "ışık"
    assert normalize_word("Quick, Wavy Fox!", "en") == "quickwavyfox"
    assert detect_reference_language("This quick fox jumps.") == "en"
    assert detect_reference_language("Bu bir okuma metnidir.") == "tr"


def test_one_wrong_word_does_not_shift_following_correct_words():
    result = analyze("Kuşlar bugün bahçede neşeyle öttü.", [
        word("kuşlar", 0.1, 0.5),
        word("yarın", 0.6, 0.95),
        word("bahçede", 1.0, 1.45),
        word("neşeyle", 1.5, 2.0),
        word("öttü", 2.1, 2.5),
    ])
    assert [item["status"] for item in result["words"]] == [
        "correct", "error", "correct", "correct", "correct",
    ]
    assert result["counts"]["errors"] == 1


def test_skipped_internal_word_requires_two_reliable_anchors():
    result = analyze("Kuşlar bugün bahçede neşeyle öttü.", [
        word("kuşlar", 0.1, 0.5),
        word("bugün", 0.6, 1.0),
        word("neşeyle", 1.1, 1.6),
        word("öttü", 1.7, 2.1),
    ])
    assert result["words"][2]["status"] == "missing"
    assert result["counts"]["missing"] == 1
    assert result["words"][3]["status"] == "correct"


def test_low_confidence_neighbor_does_not_prove_missing_word():
    result = analyze("Kuşlar bugün bahçede neşeyle öttü.", [
        word("kuşlar", 0.1, 0.5),
        word("bugün", 0.6, 1.0, 0.42),
        word("neşeyle", 1.1, 1.6),
        word("öttü", 1.7, 2.1),
    ])
    assert result["words"][2]["status"] == "uncertain"
    assert result["counts"]["missing"] == 0


def test_cut_recording_edges_are_uncertain_and_reported():
    start_cut = analyze("Kuşlar bugün bahçede öttü.", [
        word("bugün", 0.01, 0.4), word("bahçede", 0.5, 0.9), word("öttü", 1.0, 1.4),
    ])
    assert start_cut["words"][0]["status"] == "uncertain"
    assert start_cut["recordingBoundary"]["possibleStartClipping"] is True

    end_cut = analyze("Kuşlar bugün bahçede öttü.", [
        word("kuşlar", 0.1, 0.5), word("bugün", 0.6, 1.0), word("bahçede", 5.7, 5.98),
    ])
    assert end_cut["words"][3]["status"] == "uncertain"
    assert end_cut["recordingBoundary"]["possibleEndClipping"] is True


def test_speed_uses_verified_words_and_full_audio_duration():
    result = build_reading_analysis(
        "Bir iki üç dört.",
        [word("bir", 1.0, 1.4), word("iki", 2.0, 2.4), word("üç", 3.0, 3.4), word("dört", 4.0, 4.4)],
        10.0,
        0.72,
        1.5,
        2.5,
        0.7,
    )
    assert result["correctWordsPerMinute"] == 24.0
    assert result["recordingBoundary"]["leadingSilenceSeconds"] == 1.0
    assert result["recordingBoundary"]["trailingSilenceSeconds"] == 5.6


def test_shifted_word_boundary_is_not_counted_as_two_errors():
    result = analyze("Bahçedeki erik ağacına iki kırlangıç yuva yapmış.", [
        word("bahçedeki", 0.1, 0.6),
        word("eri", 0.7, 0.95),
        word("kağacına", 0.96, 1.5),
        word("iki", 1.6, 1.85),
        word("kırlangıç", 1.9, 2.4),
        word("yuva", 2.5, 2.8),
        word("yapmış", 2.9, 3.3),
    ])
    assert result["words"][1]["status"] == "correct"
    assert result["words"][2]["status"] == "correct"
    assert result["counts"]["errors"] == 0
    assert result["counts"]["missing"] == 0
    assert result["counts"]["extra"] == 0


def test_collapsed_repeated_phrase_boundary_is_correct():
    result = analyze("Yavrular her sabah cıvıl cıvıl ötüyor.", [
        word("yavrular", 0.1, 0.5),
        word("her", 0.6, 0.8),
        word("sabah", 0.9, 1.2),
        word("cıvılcıvıl", 1.3, 1.9),
        word("ötüyor", 2.0, 2.4),
    ])
    assert result["words"][3]["status"] == "correct"
    assert result["words"][4]["status"] == "correct"
    assert result["counts"]["errors"] == 0
    assert result["counts"]["missing"] == 0


def test_unanchored_confident_phrase_difference_is_uncertain_not_false_error():
    result = analyze("Kuşlar bugün bahçede neşeyle ötüyordu.", [
        word("kuşlar", 0.1, 0.5),
        word("yarın", 0.6, 0.9),
        word("evde", 1.0, 1.3),
        word("neşeyle", 1.4, 1.9),
        word("ötüyordu", 2.0, 2.5),
    ])
    assert result["words"][1]["status"] == "uncertain"
    assert result["words"][2]["status"] == "uncertain"
    assert result["counts"]["errors"] == 0


def test_isolated_wrong_word_still_requires_and_uses_two_anchors():
    result = analyze("Kuşlar bugün bahçede neşeyle ötüyordu.", [
        word("kuşlar", 0.1, 0.5),
        word("bugün", 0.6, 0.9),
        word("okulda", 1.0, 1.4),
        word("neşeyle", 1.5, 2.0),
        word("ötüyordu", 2.1, 2.6),
    ])
    assert result["words"][2]["status"] == "error"
    assert result["counts"]["errors"] == 1

