from __future__ import annotations

import os
from dataclasses import dataclass


def _as_bool(value: str | None, default: bool) -> bool:
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


@dataclass(frozen=True)
class Settings:
    model_id: str = os.getenv("YAZTEK_ASR_MODEL", "tgrhn/wav2vec2-turkish-300m-8")
    model_language: str = os.getenv("YAZTEK_ASR_MODEL_LANGUAGE", "tr").strip().lower()
    device: str = os.getenv("YAZTEK_ASR_DEVICE", "auto")
    local_files_only: bool = _as_bool(os.getenv("YAZTEK_MODEL_LOCAL_ONLY"), False)
    ffmpeg_path: str | None = os.getenv("YAZTEK_FFMPEG_PATH")
    temp_dir: str | None = os.getenv("YAZTEK_TEMP_DIR")
    max_upload_bytes: int = int(os.getenv("YAZTEK_MAX_AUDIO_BYTES", str(25 * 1024 * 1024)))
    max_audio_seconds: float = float(os.getenv("YAZTEK_MAX_AUDIO_SECONDS", "180"))
    inference_timeout_seconds: float = float(os.getenv("YAZTEK_INFERENCE_TIMEOUT_SECONDS", "240"))
    confidence_threshold: float = float(os.getenv("YAZTEK_WORD_CONFIDENCE_THRESHOLD", "0.72"))
    pause_threshold_seconds: float = float(os.getenv("YAZTEK_PAUSE_THRESHOLD_SECONDS", "1.50"))
    sentence_pause_threshold_seconds: float = float(os.getenv("YAZTEK_SENTENCE_PAUSE_THRESHOLD_SECONDS", "2.50"))
    min_speech_ratio: float = float(os.getenv("YAZTEK_MIN_SPEECH_RATIO", "0.025"))
    min_assessable_ratio: float = float(os.getenv("YAZTEK_MIN_ASSESSABLE_RATIO", "0.70"))

