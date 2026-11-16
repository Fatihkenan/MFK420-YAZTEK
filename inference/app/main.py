from __future__ import annotations

import asyncio
import os
import time
from concurrent.futures import ThreadPoolExecutor
from contextlib import asynccontextmanager
from pathlib import Path
from tempfile import NamedTemporaryFile

from fastapi import FastAPI, File, Form, HTTPException, UploadFile

from .config import Settings
from .model import InvalidAudioError, ModelUnavailableError, SpeechEngine


settings = Settings()
engine = SpeechEngine(settings)
inference_executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="yaztek-asr")


@asynccontextmanager
async def lifespan(_: FastAPI):
    # PyTorch/Transformers model initialization can deadlock when it is first
    # imported from a worker thread on Windows. Startup is already a one-time,
    # blocking phase, so load on the main thread and reserve the executor for
    # actual inference requests.
    engine.load()
    try:
        yield
    finally:
        inference_executor.shutdown(wait=True, cancel_futures=True)


app = FastAPI(title="YAZTEK Turkish Speech Inference", version="1.0.0", lifespan=lifespan)


@app.get("/health")
async def health() -> dict:
    return {
        "status": "ok" if engine.ready else "degraded",
        "ready": engine.ready,
        "busy": engine.busy,
        "modelId": settings.model_id,
        "device": engine.device,
        "modelError": engine.load_error,
        "ffmpegReady": engine.ffmpeg_path is not None,
    }


@app.post("/analyze")
async def analyze(
    audio: UploadFile = File(...),
    reference_text: str = Form(...),
    consent: bool = Form(...),
) -> dict:
    if not consent:
        raise HTTPException(status_code=400, detail="Açık kayıt ve analiz izni olmadan ses işlenmez.")
    if not reference_text.strip():
        raise HTTPException(status_code=400, detail="Referans metin boş olamaz.")
    if len(reference_text) > 20_000:
        raise HTTPException(status_code=413, detail="Referans metin 20.000 karakter sınırını aşıyor.")
    if not audio.content_type or not audio.content_type.lower().startswith("audio/"):
        raise HTTPException(status_code=415, detail="Yalnızca ses içerik türleri kabul edilir.")
    if not engine.ready:
        raise HTTPException(status_code=503, detail=f"ASR modeli hazır değil: {engine.load_error or 'bilinmeyen hata'}")

    suffix_by_type = {
        "audio/webm": ".webm",
        "audio/ogg": ".ogg",
        "audio/mp4": ".m4a",
        "audio/mpeg": ".mp3",
        "audio/wav": ".wav",
        "audio/x-wav": ".wav",
    }
    normalized_type = audio.content_type.split(";", 1)[0].strip().lower()
    suffix = suffix_by_type.get(normalized_type, ".audio")
    temporary_path: str | None = None
    try:
        temp_root = Path(settings.temp_dir) if settings.temp_dir else None
        if temp_root:
            temp_root.mkdir(parents=True, exist_ok=True)
        with NamedTemporaryFile(prefix="yaztek-upload-", suffix=suffix, delete=False, dir=temp_root) as temporary:
            temporary_path = temporary.name
            total = 0
            while chunk := await audio.read(1024 * 1024):
                total += len(chunk)
                if total > settings.max_upload_bytes:
                    raise HTTPException(status_code=413, detail="Ses dosyası boyut sınırını aşıyor.")
                temporary.write(chunk)
        if total == 0:
            raise HTTPException(status_code=400, detail="Ses dosyası boş.")
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(
            inference_executor,
            engine.analyze,
            Path(temporary_path),
            reference_text,
        )
    except ModelUnavailableError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    except InvalidAudioError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    finally:
        await audio.close()
        if temporary_path:
            cleanup_error: OSError | None = None
            for attempt in range(3):
                try:
                    os.remove(temporary_path)
                    cleanup_error = None
                    break
                except FileNotFoundError:
                    cleanup_error = None
                    break
                except OSError as exc:
                    cleanup_error = exc
                    if attempt < 2:
                        await asyncio.to_thread(time.sleep, 0.05 * (attempt + 1))
            if cleanup_error is not None:
                raise HTTPException(
                    status_code=500,
                    detail="Geçici ses kaydı güvenli biçimde silinemedi; analiz sonucu yayımlanmadı.",
                ) from cleanup_error

