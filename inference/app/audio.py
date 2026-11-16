from __future__ import annotations

import shutil
import subprocess
import wave
from dataclasses import dataclass
from pathlib import Path

import numpy as np


class AudioDecodeError(RuntimeError):
    pass


@dataclass(frozen=True)
class DecodedAudio:
    samples: np.ndarray
    sample_rate: int
    duration_seconds: float
    speech_ratio: float


def resolve_ffmpeg(configured_path: str | None = None) -> str:
    if configured_path:
        path = Path(configured_path)
        if path.is_file():
            return str(path)
        raise AudioDecodeError(f"YAZTEK_FFMPEG_PATH bulunamadı: {configured_path}")

    system_ffmpeg = shutil.which("ffmpeg")
    if system_ffmpeg:
        return system_ffmpeg

    try:
        import imageio_ffmpeg

        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception as exc:  # pragma: no cover - depends on host installation
        raise AudioDecodeError("FFmpeg bulunamadı. imageio-ffmpeg kurun veya YAZTEK_FFMPEG_PATH ayarlayın.") from exc


def _speech_ratio(samples: np.ndarray, sample_rate: int) -> float:
    frame_size = max(1, int(sample_rate * 0.03))
    usable = samples[: len(samples) - (len(samples) % frame_size)]
    if usable.size == 0:
        return 0.0
    frames = usable.reshape(-1, frame_size)
    rms = np.sqrt(np.mean(np.square(frames), axis=1) + 1e-12)
    noise_floor = float(np.percentile(rms, 20))
    threshold = max(0.008, noise_floor * 3.0)
    return float(np.mean(rms >= threshold))


def decode_to_mono_16khz(source_path: Path, wav_path: Path, ffmpeg_path: str, timeout_seconds: float) -> DecodedAudio:
    command = [
        ffmpeg_path,
        "-hide_banner",
        "-loglevel",
        "error",
        "-y",
        "-i",
        str(source_path),
        "-ac",
        "1",
        "-ar",
        "16000",
        "-c:a",
        "pcm_s16le",
        str(wav_path),
    ]
    try:
        completed = subprocess.run(command, capture_output=True, check=False, timeout=timeout_seconds)
    except subprocess.TimeoutExpired as exc:
        raise AudioDecodeError("Ses dönüştürme zaman aşımına uğradı.") from exc
    if completed.returncode != 0:
        detail = completed.stderr.decode("utf-8", errors="replace")[-600:]
        raise AudioDecodeError(f"Ses dosyası çözümlenemedi: {detail}")

    try:
        with wave.open(str(wav_path), "rb") as wav_file:
            if wav_file.getnchannels() != 1 or wav_file.getframerate() != 16000 or wav_file.getsampwidth() != 2:
                raise AudioDecodeError("FFmpeg beklenen 16 kHz mono PCM çıktısını üretmedi.")
            frames = wav_file.readframes(wav_file.getnframes())
            samples = np.frombuffer(frames, dtype="<i2").astype(np.float32) / 32768.0
    except (wave.Error, OSError) as exc:
        raise AudioDecodeError("Dönüştürülen WAV dosyası okunamadı.") from exc

    duration_seconds = len(samples) / 16000.0
    return DecodedAudio(
        samples=samples,
        sample_rate=16000,
        duration_seconds=duration_seconds,
        speech_ratio=_speech_ratio(samples, 16000),
    )

