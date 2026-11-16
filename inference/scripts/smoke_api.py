from __future__ import annotations

import asyncio
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from fastapi import HTTPException, UploadFile
from starlette.datastructures import Headers

from app.main import analyze, engine, inference_executor


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / "tests/data/evaluation_manifest.json").read_text(encoding="utf-8"))
    sample = manifest["samples"][0]
    audio_path = root / "tests/data" / sample["audio"]
    if not audio_path.is_file():
        print(f"Gerçek test kaydı bulunamadı: {audio_path}", file=sys.stderr)
        return 2

    async def exercise_endpoint() -> tuple[dict, int]:
        print("Model yükleniyor…", flush=True)
        loop = asyncio.get_running_loop()
        await loop.run_in_executor(inference_executor, engine.load)
        if not engine.ready:
            raise RuntimeError(f"Model hazır değil: {engine.load_error}")
        print("Model hazır; endpoint dosya akışı sınanıyor…", flush=True)
        with audio_path.open("rb") as audio:
            upload = UploadFile(
                file=audio,
                filename=audio_path.name,
                headers=Headers({"content-type": "audio/wav"}),
            )
            analysis = await analyze(upload, sample["referenceText"], True)

        denied_status = 0
        with audio_path.open("rb") as audio:
            upload = UploadFile(
                file=audio,
                filename=audio_path.name,
                headers=Headers({"content-type": "audio/wav"}),
            )
            try:
                await analyze(upload, sample["referenceText"], False)
            except HTTPException as exception:
                denied_status = exception.status_code
        return analysis, denied_status

    try:
        print("Endpoint testi başlatılıyor…", flush=True)
        analysis, denied_status = asyncio.run(exercise_endpoint())
    except RuntimeError as exception:
        print(str(exception), file=sys.stderr)
        return 3
    print("Endpoint analizi tamamlandı.", flush=True)
    if analysis.get("modelId") != "tgrhn/wav2vec2-turkish-300m-8" or not analysis.get("words"):
        print(f"Beklenmeyen analiz sözleşmesi: {analysis}", file=sys.stderr)
        return 4
    if denied_status != 400:
        print(f"İzinsiz kayıt reddedilmedi: HTTP {denied_status}", file=sys.stderr)
        return 5

    print(json.dumps({
        "health": {"ready": engine.ready, "modelId": engine.settings.model_id, "device": engine.device},
        "analysis": {
            "transcript": analysis["transcript"],
            "wer": analysis["sessionWer"],
            "cer": analysis["sessionCer"],
            "wordCount": len(analysis["words"]),
            "audioRetained": analysis["audioRetained"],
        },
        "consentDeniedStatus": denied_status,
    }, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        exit_code = main()
    finally:
        inference_executor.shutdown(wait=True, cancel_futures=True)
    raise SystemExit(exit_code)
