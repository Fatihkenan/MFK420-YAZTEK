from pathlib import Path

import pytest

from app.config import Settings
from app.model import ModelUnavailableError, SpeechEngine


def ready_engine(language: str) -> SpeechEngine:
    engine = SpeechEngine(Settings(model_language=language))
    engine.processor = object()
    engine.model = object()
    engine.ffmpeg_path = "ffmpeg"
    return engine


def test_english_text_is_not_scored_with_turkish_acoustic_model():
    engine = ready_engine("tr")
    with pytest.raises(ModelUnavailableError, match="İngilizce"):
        engine.analyze(Path("not-used.wav"), "This is a short reading text.")


def test_turkish_text_is_not_scored_with_english_acoustic_model():
    engine = ready_engine("en")
    with pytest.raises(ModelUnavailableError, match="Türkçe"):
        engine.analyze(Path("not-used.wav"), "Bu bir kısa okuma metnidir.")
