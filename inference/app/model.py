from __future__ import annotations

import math
import threading
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import Any

import numpy as np

from .alignment import (
    RecognizedWord,
    build_reading_analysis,
    detect_reference_language,
    normalize_word,
)
from .audio import AudioDecodeError, decode_to_mono_16khz, resolve_ffmpeg
from .config import Settings


class ModelUnavailableError(RuntimeError):
    pass


class InvalidAudioError(RuntimeError):
    pass


class SpeechEngine:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.processor: Any = None
        self.model: Any = None
        self.device = "cpu"
        self.ffmpeg_path: str | None = None
        self.load_error: str | None = None
        self._load_lock = threading.Lock()
        self._inference_lock = threading.Lock()

    @property
    def ready(self) -> bool:
        return self.processor is not None and self.model is not None and self.ffmpeg_path is not None

    @property
    def busy(self) -> bool:
        return self._inference_lock.locked()

    def load(self) -> None:
        if self.ready:
            return
        with self._load_lock:
            if self.ready:
                return
            try:
                import torch
                from transformers import AutoModelForCTC, AutoProcessor

                self.ffmpeg_path = resolve_ffmpeg(self.settings.ffmpeg_path)
                if self.settings.device == "auto":
                    self.device = "cuda" if torch.cuda.is_available() else "cpu"
                else:
                    self.device = self.settings.device
                self.processor = AutoProcessor.from_pretrained(
                    self.settings.model_id,
                    local_files_only=self.settings.local_files_only,
                )
                self.model = AutoModelForCTC.from_pretrained(
                    self.settings.model_id,
                    local_files_only=self.settings.local_files_only,
                )
                self.model.to(self.device)
                self.model.eval()
                self.load_error = None
            except Exception as exc:  # keep health endpoint honest and available
                self.processor = None
                self.model = None
                self.load_error = f"{type(exc).__name__}: {exc}"

    def _decode_words(self, logits: Any, duration_seconds: float, language: str) -> list[RecognizedWord]:
        import torch

        probabilities = torch.softmax(logits, dim=-1)
        frame_confidence, frame_ids = torch.max(probabilities, dim=-1)
        ids = frame_ids.detach().cpu().tolist()
        confidences = frame_confidence.detach().cpu().tolist()
        blank_id = self.model.config.pad_token_id
        tokenizer = self.processor.tokenizer
        delimiter = tokenizer.word_delimiter_token
        frame_seconds = duration_seconds / max(1, len(ids))

        token_segments: list[tuple[str, int, int, float]] = []
        run_id = ids[0] if ids else blank_id
        run_start = 0
        run_confidences: list[float] = []

        def flush(end_frame: int) -> None:
            if run_id == blank_id or not run_confidences:
                return
            token = tokenizer.convert_ids_to_tokens(run_id)
            if token in tokenizer.all_special_tokens and token != delimiter:
                return
            confidence = float(sum(run_confidences) / len(run_confidences))
            token_segments.append((token, run_start, end_frame, confidence))

        for index, (token_id, confidence) in enumerate(zip(ids, confidences)):
            if token_id != run_id:
                flush(index)
                run_id = token_id
                run_start = index
                run_confidences = [confidence]
            else:
                run_confidences.append(confidence)
        flush(len(ids))

        words: list[RecognizedWord] = []
        characters: list[str] = []
        starts: list[int] = []
        ends: list[int] = []
        token_confidences: list[float] = []

        def flush_word() -> None:
            if not characters:
                return
            surface = normalize_word("".join(characters), language)
            if surface:
                geometric_confidence = math.exp(sum(math.log(max(value, 1e-8)) for value in token_confidences) / len(token_confidences))
                words.append(RecognizedWord(
                    text=surface,
                    start_seconds=starts[0] * frame_seconds,
                    end_seconds=ends[-1] * frame_seconds,
                    confidence=float(geometric_confidence),
                ))
            characters.clear()
            starts.clear()
            ends.clear()
            token_confidences.clear()

        for token, start, end, confidence in token_segments:
            if token == delimiter:
                flush_word()
                continue
            characters.append(token)
            starts.append(start)
            ends.append(end)
            token_confidences.append(confidence)
        flush_word()
        return words

    def analyze(self, source_path: Path, reference_text: str) -> dict[str, Any]:
        if not self.ready:
            raise ModelUnavailableError(self.load_error or "ASR modeli yüklenmedi.")
        reference_language = detect_reference_language(reference_text)
        configured_language = self.settings.model_language
        if configured_language not in {"tr", "en", "multilingual"}:
            raise ModelUnavailableError(
                "YAZTEK_ASR_MODEL_LANGUAGE yalnızca tr, en veya multilingual olabilir."
            )
        if configured_language != "multilingual" and reference_language != configured_language:
            language_name = "İngilizce" if reference_language == "en" else "Türkçe"
            raise ModelUnavailableError(
                f"Metin {language_name}, ancak yüklü ASR modeli '{configured_language}' dili için yapılandırılmış. "
                "Uygun modeli YAZTEK_ASR_MODEL ve YAZTEK_ASR_MODEL_LANGUAGE ile seçmeden analiz yapılmadı."
            )
        temp_root = Path(self.settings.temp_dir) if self.settings.temp_dir else None
        if temp_root:
            temp_root.mkdir(parents=True, exist_ok=True)
        with self._inference_lock, TemporaryDirectory(prefix="yaztek-audio-", dir=temp_root) as temp_directory:
            wav_path = Path(temp_directory) / "decoded.wav"
            try:
                audio = decode_to_mono_16khz(
                    source_path,
                    wav_path,
                    self.ffmpeg_path,
                    self.settings.inference_timeout_seconds,
                )
            except AudioDecodeError as exc:
                raise InvalidAudioError(str(exc)) from exc

            if audio.duration_seconds <= 0.15:
                raise InvalidAudioError("Kayıt analiz için çok kısa.")
            if audio.duration_seconds > self.settings.max_audio_seconds:
                raise InvalidAudioError(f"Kayıt {self.settings.max_audio_seconds:.0f} saniye sınırını aşıyor.")
            if audio.speech_ratio < self.settings.min_speech_ratio:
                raise InvalidAudioError("Kayıtta yeterli konuşma kanıtı bulunamadı; analiz üretilmedi.")

            import torch

            inputs = self.processor(audio.samples, sampling_rate=audio.sample_rate, return_tensors="pt")
            input_values = inputs.input_values.to(self.device)
            with torch.inference_mode():
                logits = self.model(input_values).logits[0]
            recognized_words = self._decode_words(logits, audio.duration_seconds, reference_language)
            if not recognized_words:
                raise InvalidAudioError("ASR hiçbir kelime çözemedi; analiz üretilmedi.")

            analysis = build_reading_analysis(
                reference_text=reference_text,
                recognized_words=recognized_words,
                audio_duration_seconds=audio.duration_seconds,
                confidence_threshold=self.settings.confidence_threshold,
                pause_threshold_seconds=self.settings.pause_threshold_seconds,
                sentence_pause_threshold_seconds=self.settings.sentence_pause_threshold_seconds,
                min_assessable_ratio=self.settings.min_assessable_ratio,
            )
            analysis.update({
                "modelId": self.settings.model_id,
                "modelArchitecture": "Wav2Vec2 XLS-R + CTC greedy decode",
                "modelLanguage": configured_language,
                "sampleRate": audio.sample_rate,
                "speechRatio": round(audio.speech_ratio, 4),
                "confidenceThreshold": self.settings.confidence_threshold,
                "pauseThresholdSeconds": self.settings.pause_threshold_seconds,
                "sentencePauseThresholdSeconds": self.settings.sentence_pause_threshold_seconds,
                "liveWordAnalysis": False,
                "audioRetained": False,
            })
            return analysis

