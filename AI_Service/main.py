from fastapi import FastAPI, File, UploadFile, Form
from transformers import Wav2Vec2Processor, Wav2Vec2ForCTC
import torch
import librosa
import shutil
import os
import difflib
import re

app = FastAPI()

print("Yapay zeka modeli yükleniyor...")
MODEL_ID = "mpoyraz/wav2vec2-xls-r-300m-cv7-turkish"
processor = Wav2Vec2Processor.from_pretrained(MODEL_ID)
model = Wav2Vec2ForCTC.from_pretrained(MODEL_ID)
print("Model başarıyla yüklendi!")

def metni_temizle(metin):
    metin = metin.lower()
    metin = re.sub(r'[^\w\s]', '', metin)
    metin = re.sub(r'\s+', ' ', metin).strip()
    return metin

def hatalari_bul(referans, okunan):
    ref_kelimeler = referans.split()
    oku_kelimeler = okunan.split()
    
    matcher = difflib.SequenceMatcher(None, ref_kelimeler, oku_kelimeler)
    hatalar = []
    
    for islem, i1, i2, j1, j2 in matcher.get_opcodes():
        if islem == 'replace':
            hatalar.append({"hata_turu": "Yanlış Okuma", "beklenen": " ".join(ref_kelimeler[i1:i2]), "okunan": " ".join(oku_kelimeler[j1:j2])})
        elif islem == 'delete':
            hatalar.append({"hata_turu": "Kelime Atlama", "beklenen": " ".join(ref_kelimeler[i1:i2]), "okunan": "-"})
        elif islem == 'insert':
            hatalar.append({"hata_turu": "Fazladan Kelime Ekleme", "beklenen": "-", "okunan": " ".join(oku_kelimeler[j1:j2])})
            
    return hatalar

@app.get("/")
def read_root():
    return {"mesaj": "YAZTEK Yapay Zeka Servisi Calisiyor!"}

@app.post("/ses-analiz")
async def ses_analiz_yap(
    file: UploadFile = File(...),
    referans_metin: str = Form(...)
):
    temiz_referans = metni_temizle(referans_metin)

    temp_file_path = f"temp_{file.filename}"
    with open(temp_file_path, "wb") as buffer:
        shutil.copyfileobj(file.file, buffer)
        
    try:
        ses_verisi, _ = librosa.load(temp_file_path, sr=16000)
        inputs = processor(ses_verisi, sampling_rate=16000, return_tensors="pt", padding=True)
        
        with torch.no_grad():
            logits = model(inputs.input_values).logits
            
        predicted_ids = torch.argmax(logits, dim=-1)
        
        # 1. YENİ EKLENEN KISIM: decode fonksiyonuna "zaman damgalarını (offsets) da ver" diyoruz.
        ciktilar = processor.decode(predicted_ids[0], output_word_offsets=True)
        
        # 2. YENİ EKLENEN KISIM: Modelden gelen zaman damgalarını saniyeye çeviriyoruz.
        zaman_damgalari = []
        if ciktilar.word_offsets:
            for kelime_bilgisi in ciktilar.word_offsets:
                zaman_damgalari.append({
                    "kelime": kelime_bilgisi["word"],
                    # Wav2Vec2 her bir kareyi ~0.02 saniye olarak işler.
                    "baslama_saniyesi": round(kelime_bilgisi["start_offset"] * 0.02, 2),
                    "bitis_saniyesi": round(kelime_bilgisi["end_offset"] * 0.02, 2)
                })

        transcription = metni_temizle(ciktilar.text)
        analiz_sonucu = hatalari_bul(temiz_referans, transcription)
        
    finally:
        if os.path.exists(temp_file_path):
            os.remove(temp_file_path)
            
    return {
        "dosya_adi": file.filename,
        "orijinal_referans": referans_metin,
        "temizlenmis_referans": temiz_referans,
        "yapay_zeka_ciktisi": transcription,
        "hata_sayisi": len(analiz_sonucu),
        "detayli_hatalar": analiz_sonucu,
        "kelime_sureleri": zaman_damgalari # 3. YENİ EKLENEN KISIM: JSON çıktımıza süreleri de ekliyoruz.
    }