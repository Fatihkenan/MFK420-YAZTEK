# YAZTEK V2 — gerçek sesli okuma analizi ve dinamik anlama soruları

Bu sürüm, `yaztek-arayuz-tasarimi.html` görselini koruyarak tarayıcıdan gerçek ses kaydı alır. Canlı aşamada yalnızca Web Audio ses seviyesi gösterilir; kelime durumları ve metrikler kayıt bittikten ve sunucu gerçek bir ASR yanıtı ürettikten sonra görünür. Model, mikrofon veya servis kullanılamıyorsa başarılı analiz taklit edilmez.

## Mimari

- `frontend`: React + Vite. `MediaRecorder` ile Opus/WebM (tarayıcıya göre Ogg/MP4), `AnalyserNode` ile gerçek 26 çubuklu waveform.
- `backend`: ASP.NET Core 10. Dosya/izin/boyut/tür denetimi, çıkarım servisine akış, isteğe bağlı JWT ve PostgreSQL rapor saklama.
- `inference`: FastAPI + PyTorch + Transformers + FFmpeg. 16 kHz mono PCM dönüşümü, enerji tabanlı konuşma varlığı denetimi, Türkçe XLS-R/CTC çözümleme ve kanıta dayalı kelime hizalama.
- `comprehension`: ASP.NET Core içinden yerel Ollama `/api/chat` API'si ve `qwen3:4b`. JSON Schema ile soru üretimi, soru başına ayrı kalite kararı ve üç açık uçlu yanıtın tek toplu çağrıda anlamsal değerlendirilmesi.

Seçilen model `tgrhn/wav2vec2-turkish-300m-8` modelidir. Model kartında Apache-2.0 lisanslı, `facebook/wav2vec2-xls-r-300m` tabanlı ve Common Voice 17 Türkçe ile ince ayarlı olarak yayınlanmıştır. Model kartındaki WER 0,1949 yayıncının kendi sonucudur; bu projenin testi değildir. Ağırlık yaklaşık 1,26 GB'dır. CPU'da çalışır; gecikme için CUDA önerilir. Model çocuk sesi için doğrulanmış değildir.

## Kurulum

Gerekenler:

- Node.js ve npm
- .NET 10 SDK
- Python 3.11 veya 3.12
- PostgreSQL (dinamik anlama denemeleri için zorunlu; ses raporu saklama ayrıca `Analysis:PersistReports` ile yönetilir)
- Windows üzerinde çalışan Ollama ve yerel `qwen3:4b` modeli
- İlk açılışta model indirmek için Hugging Face erişimi
- En az yaklaşık 4 GB boş RAM; üretimde GPU tercih edilir

### 1. Python çıkarım servisi

```powershell
cd inference
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
$env:YAZTEK_TEMP_DIR=(Join-Path (Resolve-Path '.').Path '.runtime-temp')
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8090
```

İlk açılış seçilen modeli indirir ve süreç boyunca bellekte tek kez tutar. İnternetsiz sonraki çalıştırmada önbellek hazırsa:

```powershell
$env:YAZTEK_MODEL_LOCAL_ONLY="true"
$env:HF_HUB_OFFLINE="1"
$env:TRANSFORMERS_OFFLINE="1"
```

Sistem FFmpeg'i yoksa `imageio-ffmpeg` paketinin ikilisi kullanılır. Haricî bir ikili için `YAZTEK_FFMPEG_PATH` verilebilir. Tüm eşikler [inference/.env.example](inference/.env.example) içinde listelenmiştir.

Sağlık kontrolü: `GET http://127.0.0.1:8090/health`. Yalnızca `ready: true` gerçek analize hazır olduğunu gösterir.

Yerel geliştirmede `dotnet run` mevcut `inference/.venv` ve indirilmiş model önbelleğini kullanarak bu servisi kapalıysa otomatik başlatır, `ready: true` yanıtını bekledikten sonra API'yi açar ve backend kapanırken yalnızca kendi başlattığı ASR sürecini kapatır. Üretim yapılandırmasında otomatik süreç başlatma kapalıdır. Python servisini ayrı terminalde çalıştırmak teşhis ve bağımsız kullanım için hâlâ desteklenir.

Model yükleme ve inference aynı kalıcı, tek iş parçacıklı worker üzerinde çalışır; böylece model her istekte yeniden yüklenmez ve eşzamanlı büyük kayıtlar sınırsız paralel bellek tüketmez. Üretimde Uvicorn/Gunicorn worker sayısını model belleğine göre belirleyin; her işletim sistemi süreci kendi model kopyasını taşır.

### 2. Ollama ve ASP.NET Core API

Ollama'yı başlatın ve modeli yalnızca ilk kurulumda indirin:

```powershell
ollama serve
ollama pull qwen3:4b
ollama list
```

API anahtarı gerekmez. Backend, çalışan Ollama sürecine bağlanır; modeli her istekte indirmez veya ayrı bir model süreci başlatmaz.

```powershell
cd backend
dotnet tool update --global dotnet-ef --version 10.0.4
$env:ConnectionStrings__PostgreSql="Host=localhost;Port=5432;Database=yaztek;Username=yaztek;Password=PAROLANIZ"
$env:ComprehensionAi__Provider="Ollama"
$env:ComprehensionAi__BaseUrl="http://127.0.0.1:11434/"
$env:ComprehensionAi__Model="qwen3:4b"
# 16 GB RAM için proje varsayılanları:
$env:ComprehensionAi__TimeoutSeconds="300"
$env:ComprehensionAi__NumContext="16384"
$env:ComprehensionAi__NumPredict="1536"
$env:ComprehensionAi__QuestionBatchSize="2"
$env:ComprehensionAi__MaxOutputRetries="2"
$env:ComprehensionAi__QualityReviewBatchSize="4"
$env:ComprehensionAi__QualityReviewNumPredict="768"
$env:ComprehensionAi__EvaluationNumPredict="1536"
$env:ComprehensionAi__KeepAlive="30m"
dotnet ef database update --project src/YAZTEK.Infrastructure/YAZTEK.Infrastructure.csproj --startup-project src/YAZTEK.Api/YAZTEK.Api.csproj
dotnet run --project src/YAZTEK.Api/YAZTEK.Api.csproj
```

- Genel sağlık: `GET http://localhost:5080/api/health`
- Model sağlığı: `GET http://localhost:5080/api/reading-analysis/health`
- Analiz: `POST http://localhost:5080/api/reading-analysis` (`multipart/form-data`)
- Soru servisi sağlığı: `GET http://localhost:5080/api/comprehension/health`
- Yeni test: `POST http://localhost:5080/api/comprehension/attempts`
- Yanıt gönderimi: `POST http://localhost:5080/api/comprehension/attempts/{attemptId}/submit`

Geliştirme varsayılanında `PersistReports=false` ve `RequireAuthentication=false` değerleri yerel prototipi çalıştırır. Üretimde ikisini de ihtiyaca göre etkinleştirin; `Authentication:Authority` ve `Authentication:Audience` alanlarını gerçek OpenID Connect sağlayıcısıyla doldurmadan API'yi internete açmayın. Ses raporu tablosu yalnızca analiz raporunu, metin özetini ve ASR transkriptini saklar; ham ses saklanmaz. `RetentionDays` dolunca ses raporu temizleme görevi çalışır. Anlama denemeleri ise önceki öğrenme geçmişi korunacağı için ayrı tablolarda kalıcı tutulur.

Anlama sistemi için ücretli bulut çağrısı ve `ComprehensionAi__ApiKey` yoktur. Sağlık endpoint'i hem Ollama'ya erişimi hem de yapılandırılan modelin yüklü olduğunu denetler; erişim/model yoksa 503 ve açıklayıcı `unreachable`/`model_missing` durumu döner. Çağrılar `stream=false`, `think=false`, JSON Schema `format`, `keep_alive=30m` ve yapılandırılabilir bağlam/çıktı sınırlarıyla yapılır. Dört çoktan seçmeli ve üç açık uçlu soru, aynı türden en fazla ikişer soruluk bağımsız gruplarda üretilir; grup başına `num_predict=1536` kullanılır. Kalite denetimi en fazla dörder soru ve 768 tokenlık gruplara ayrılır; üç açık uçlu yanıt değerlendirmesi en çok 1536 token kullanır. `done_reason=length` yanıtı eksik JSON olarak kabul edilmez. Kesilen, geçmişle çakışan veya aynı grupta yinelenen yalnızca eksik soru yeniden istenir; gruptaki geçerli sorular korunur. Tek soru için en fazla iki yeniden deneme yapılır ve token sınırı yükseltilmez. Geçici 408/429/5xx hataları da en fazla bir kez kısa geri çekilmeyle denenir. Sahte veya sabit soru fallback'i yoktur.

## Dinamik okuduğunu anlama sistemi

1. Öğrenci ve metin kimliği, metin başlığı/içeriği ve istemcinin tek kullanımlık idempotency anahtarı backend'e gider. `(student_id, text_id, idempotency_key)` benzersiz indeksi çift tıklama veya ağ tekrarında ikinci set üretimini engeller.
2. Dil, Türkçe karakterler ve sık kullanılan sözcüklerden belirlenir. Model aynı dilde tam 7 soru üretir: 4 adet dört seçenekli çoktan seçmeli ve 3 kısa yanıt.
3. Prompt; ana fikir, önemli bilgi, neden-sonuç, çıkarım, karakter/olay ilişkisi, bağlamdan anlam ve genel değerlendirme havuzundan metne uygun en az dört beceriyi ister. Önceki düşük puanlı beceriler önceliklendirilir, ancak çeşitlilik zorunlu kalır.
4. .NET doğrulayıcı; tam sayı/tip dağılımını, dört benzersiz seçeneği, tek doğru indeksini, metinde bulunan kanıtı, rubriği, set içi tekrarları ve önceki denemelerle birebir soru tekrarını reddeder. Kanıt alanı JSON Schema içinde doğrudan kaynak cümlelerle sınırlandırılır. Ardından en fazla dörder soruluk yapılandırılmış model çağrıları her soru için tam bir `pass/reject` kararı vererek belirsizlik, metin dışı bilgi, birden fazla doğru seçenek ve anlamsız çeldiricileri inceler. Yedi benzersiz karar veya açıklayıcı ret gerekçesi gelmezse yanıt geçersizdir. Bir tam üretim ve en fazla iki hedefli onarımda da geçemezse 422 döner; soru uydurulmaz.
5. Doğru cevap, açıklama, kanıt ve rubrik PostgreSQL'de tutulur; test gönderilene kadar öğrenci DTO'suna dahil edilmez. Çoktan seçmeli puanı sunucuda kesin hesaplanır. Üç kısa yanıt; metin, referans, kanıt ve rubrikle tek çağrıda anlamsal değerlendirilir. Eş anlamlı doğru ifade kabul edilir; yüzeysel metin benzerliği kullanılmaz. Güven `<0,65` ise kesin yanlış yerine `ReviewRequired` olur.
6. Her soru 0, 0,5 veya 1 puandır; toplam 7 üzerinden hesaplanır. Deneme, soru, öğrenci cevabı, puan, güven, açıklama ve model kimliği silinmeden ayrı tablolarda saklanır. “Tekrar dene” yeni idempotency anahtarıyla yeni deneme oluşturur.

Tekrar onarımı sırasında PostgreSQL'deki aynı öğrenci + metin + kaynak içerik geçmişinin tamamı doğrulamada kalır. Modele bağlamı taşırmamak için en son 35 benzersiz ifade gösterilir; daha eski bir geçmiş soruyla çakışma yakalanırsa reddedilen ifade hedefli onarım istemine ayrıca eklenir. Geçerli sorular korunur, yalnızca hatalı soru indeksleri aynı soru tipiyle yeniden istenir. Her gereken tip için bir küçük ek alternatif üretilir; backend geçmişe, kabul edilen sorulara, dile, seçenek/cevap ve kanıta karşı geçerli ilk adayı seçer. Salt `acaba`, `sizce`, sözcük sırası veya benzeri dolgu değişiklikleri hem geçmişe karşı hem aynı set içinde çeşitlilik sayılmaz. Model kaynakta bulunan fakat ilgisiz bir kanıt seçerse backend soru+cevapla ilişkili gerçek kaynak cümlesini deterministik olarak arar; güvenilir cümle yoksa soru yine reddedilir. Kabul edilen kanıtlar modele bildirilerek metnin başka bölümleri tercih ettirilir. Bütçe bir tam set ve en fazla iki küçük hedefli onarım çağrısıdır. Bütçe sonunda sayı, tip, dil, kanıt, cevap veya kalite kontrolü hâlâ geçmiyorsa deneme `question_quality_failed` olur ve son doğrulama nedenleri kullanıcıya gösterilir.

### Açık uçlu yanıt güvenlik kuralları

- Boş yanıt, `Bilmiyorum`/`I don't know`, yalnızca noktalama, soru metninin tekrarı ve belirgin rastgele karakter dizileri model çağrısından önce backend tarafından `Incorrect / 0` olarak işaretlenir.
- Diğer kısa yanıtlar Qwen3'e üçlü tip kontrollü kanıt alanlarıyla gönderilir: yanıtın soruyla ilgisi, metin kanıtıyla desteklenmesi ve referansla çelişmesi. `Correct`, `Partial`, `Incorrect` ve `ReviewRequired` durumlarının puanı backend tarafından sırasıyla `1`, `0,5`, `0`, `0` olarak belirlenir; modelden gelen serbest puana güvenilmez.
- Model JSON'u veya tip alanları geçersizse en fazla bir kontrollü yeniden deneme yapılır. Son toplu yanıtta yalnızca bütünlük denetiminden geçen soru sonuçları kullanılır; eksik, yinelenen, yanlış soru kimliğine bağlı veya kanıtı karışmış öğeler `ReviewRequired` olur. En az bir yanıt inceleme gerektiriyorsa toplam puan kesinleştirilmez (`totalScore: null`, `isFinal: false`).
- Kanıt hem kaynak metinde birebir bulunmalı hem de soru/referans cevapla anlamlı sözcük örtüşmesi taşımalıdır. Bu doğrulamayı geçmeyen kanıt öğrenciye gösterilmez. Kayıtlı eski denemeler okunurken de dil, puan-durum ve kanıt denetimleri yeniden uygulanır; geçmiş kayıt silinmez veya değiştirilmez.
- Öğrenciye gösterilen değerlendirme metni modelin serbest açıklaması değildir; kaynak diline göre backend'in güvenli Türkçe/İngilizce mesajlarından üretilir. Böylece teknik hata metni veya başka dilde model çıktısı arayüze sızmaz.

PostgreSQL tabloları `comprehension_test_attempts`, `comprehension_questions` ve `comprehension_answers`; migration ise `AddDynamicComprehension` adındadır. Bu kayıtlar Karnem/Mentör servislerine güvenli bir sorgu katmanı eklemek için hazırdır; mevcut ekran yerleşimleri değiştirilmemiştir.

### 3. React

```powershell
cd frontend
npm install
npm run dev
```

Arayüz `http://localhost:5173`, API varsayılan olarak `http://localhost:5080` adresindedir. Farklı adres için [frontend/.env.example](frontend/.env.example) dosyasını `.env.local` olarak kopyalayın.

Mikrofon erişimi tarayıcı güvenlik kuralları gereği `localhost` veya HTTPS ister. Kullanıcı açık izin kutusunu işaretlemeden kayıt başlamaz.

## Analiz mantığı ve sınırlar

1. Kayıt FFmpeg ile 16 kHz mono PCM'e çevrilir; boyut, 180 saniye üst sınırı ve konuşma/sessizlik oranı doğrulanır.
2. XLS-R/Wav2Vec2 CTC logitleri greedy olarak çözülür. CTC karelerinden kelime başlangıç/bitiş zamanı ve token olasılıklarından yaklaşık kelime güveni üretilir.
3. Referans ve ASR kelimeleri Unicode harflerini koruyarak dile duyarlı normalleştirilir. Türkçe `I/İ/ı/i`, apostrof ve noktalama doğru işlenir; İngilizce `q/w/x` artık silinmez.
4. Küresel dinamik hizalama; kelime sırası, CTC zaman damgası, sözcük benzerliği ve güveni birlikte kullanır. Bir ekleme/silme sonraki doğru kelimeleri kaydırmaz. Eşik altı veya küçük ASR yazım sapması kesin hata değil `uncertain` olur. Eksik kelime yalnızca iki yüksek güvenli ve gerçekten eşleşen komşu arasında kanıtlanır.
5. Yüksek güvenli aynı kelime eklemesi tekrar sayılır. Kelimeler arası CTC zaman boşluğu yapılandırılabilir eşiği aşarsa duraksamadır; `. ! ?` sonrasında ayrı ve daha uzun eşik kullanılır.
6. Değerlendirilebilir kelime oranı %70'in altındaysa doğruluk ve doğru kelime/dakika yayınlanmaz. Ana doğruluk yüzdesi tüm referans kelimeler içindeki doğrulanmış doğru kelime oranıdır; belirsiz kelime hata sayılmaz ama yüzdeyi yapay olarak yükseltmez.
7. Başlangıç/son kelimeleri yokken ASR kanıtı kayıt sınırına bitişikse kesik kayıt olasılığı raporlanır ve bu kelimeler kesin hata sayılmaz. Arayüzde beklenen kelime, model çıktısı, güven ve zaman aralığı açılabilir tabloda görülebilir.

Akustik model dili `YAZTEK_ASR_MODEL_LANGUAGE` ile `tr`, `en` veya `multilingual` olarak açıkça belirtilir. Varsayılan model Türkçedir. İngilizce metin Türkçe modele gönderilmez; uygun ücretsiz yerel model `YAZTEK_ASR_MODEL` ile kurulup dil `en` seçilmeden sistem sahte analiz üretmez.

Gerçekten ölçülen alanlar: transkript, kelime zamanları, yaklaşık CTC güveni, doğru/hatalı/eksik/tekrar/duraksama/emin-değil durumları, kayıt süresi, doğru kelime/dakika, tüm referans içinde doğrulanmış doğru kelime oranı, değerlendirilebilir kapsama ve oturum WER/CER.

Henüz güvenilir ölçülmeyen alanlar: kelime/cümle seviyesinde geri dönüş (`null` döner), fonem düzeyinde telaffuz kalitesi, klinik akıcılık, konuşmacı kimliği ve çocuk performansı. Enerji VAD'i üretim sınıfı bir VAD değildir. CTC token olasılıkları kalibre edilmiş pedagojik hata olasılığı sayılmaz.

## Testler ve gerçek sonuç

Dinamik anlama iş kuralları:

```powershell
dotnet test backend/YAZTEK.slnx
```

Testler iki Türkçe denemede her seferinde tam 7 soru, 4+3 tip dağılımı, birebir soru tekrarı olmaması, ilk denemenin yedi cevabının korunması ve 7/7 puanı; ayrıca İngilizce metin/dil akışını doğrular. Aynı idempotency anahtarının modeli ikinci kez çağırmadığı ayrıca sınanır. Bunlar kontrollü test doubles ile iş kuralı testleridir; gerçek yerel model kalite ölçümü değildir.

18 Ekim 2026'da açık uçlu değerlendirme kuralları ayrıca izole PostgreSQL 16 veritabanında sınandı. `wnqjfknqe`, `Bilmiyorum` ve boş yanıt gerçek API üzerinden gönderildi; üçü de `Incorrect / 0` oldu, toplam yalnızca dört doğru çoktan seçmeli cevap için **4/7** kaydedildi ve Ollama gereksiz yere çağrılmadı. Kontrollü .NET testlerinde doğru, kısmi, çelişkili, ilgisiz, boş/rastgele yanıt, düşük güven ve servis hatası akışları doğrulandı.

18 Ekim 2026'da soru tekrar onarımı için tek kontrollü gerçek uçtan uca test, izole PostgreSQL 16 ve yerel `qwen3:4b` ile yaklaşık 165 saniyede tamamlandı. Önceden kaydedilmiş yedi yaygın soruya karşı yeni deneme `Ready` oldu; PostgreSQL'de 7 soru, 4+3 dağılımı ve **0 birebir geçmiş çakışması** doğrulandı. Çıktı incelemesinde iki açık uçlu sorunun aynı set içinde anlamsal olarak fazla benzer olduğu görüldü; modelin bağımsız kalite kontrolü bunu kaçırmıştı. Bunun ardından aynı-set yüzeysel tekrar denetimi deterministik doğrulayıcıya eklendi ve test edildi; gerçek Ollama testi kullanıcı tarafından istenen tek çağrı sınırı nedeniyle yeniden çalıştırılmadı.

Aynı gün `qwen3:4b` ile tek sınırlı gerçek anlamsal değerlendirme çalıştırıldı. Model iki şema kontrollü denemede de üçlü toplu sonucu iç kurallarla tamamen tutarlı veremedi (yaklaşık 49 saniye model süresi). Sistem bu başarısızlığı doğru cevap gibi göstermedi: üç kısa yanıt `ReviewRequired / 0` olarak saklandı, `totalScore` boş ve `isFinal=false` kaldı. Bu test, yerel 4B modelin serbest anlamsal puanlamasının henüz güvenilir başarı olarak raporlanamayacağını; mentör incelemesi geri dönüşünün çalıştığını gösterir.

17 Ekim 2026'da gerçek `qwen3:4b` ve izole PostgreSQL 16 üzerinde yapılan entegrasyon kontrolünde sağlık endpoint'i `ready` döndü. Açık neden-sonuçlar içeren Türkçe metin için ilk deneme 93,6 saniyede `Ready` oldu ve tam 7 soru (4+3) üretti. Üç açık uçlu yanıtın gerçek model değerlendirmesi ile dört çoktan seçmelinin backend puanlaması 13,9 saniyede tamamlandı; 7 cevap kalıcı kayda geçti ve bilerek verilen üç yanlış seçenekle puan 4/7 oldu. Aynı öğrenci/metin için ikinci gerçek deneme yaklaşık 153 saniye sonra kalite denetleyicinin açıklamasız `tr` ret gerekçesi nedeniyle `question_quality_failed` döndü; ikinci set ve gerçek model üzerinde tekrar-yok sonucu bu nedenle **başarıyla doğrulanmadı**. Kod bu durumu sahte soruyla gizlemez. `qwen3:4b` küçük ve yerel olduğu için özellikle yeniden üretim gereken isteklerde birkaç dakikalık gecikme ve kalite değişkenliği beklenmelidir.

Gerçek entegrasyonu yeniden sınamak için Ollama ve PostgreSQL çalışırken iki farklı UUID `idempotencyKey` ile aynı `studentId` + `textId` gövdesini `POST /api/comprehension/attempts` adresine gönderin. Her başarılı yanıtta 7 soru ve 4+3 dağılımı bekleyin; ikinci deneme kalite filtresinden geçmezse bunu başarı saymayın. Gönderimde yedi sorunun tamamını `POST /api/comprehension/attempts/{attemptId}/submit` adresine verin.

Deterministik hizalama testleri:

```powershell
cd inference
.\.venv\Scripts\python.exe -m pytest -q
```

Gerçek kayıt değerlendirmesi:

```powershell
.\.venv\Scripts\python.exe scripts\evaluate_manifest.py tests\data\evaluation_manifest.json --output tests\data\evaluation-results.json
```

Normal bir yerel Python ortamında model yükleme, gerçek `UploadFile` akışı ve izin reddini birlikte sınamak için `python scripts\smoke_api.py` kullanılabilir.

17 Ekim 2026'da `Codyfederer/tr-full-dataset` veri kümesinden üç CC-BY-4.0 olarak işaretli yetişkin Türkçe kayıtla alınan sonuç:

- 3 kayıt, toplam 27 referans kelime
- WER: **0,1852 (%18,52)**
- CER: **0,0361 (%3,61)**
- Yanlış pozitif hata oranı: **ölçülmedi**
- Duraksama precision/recall: **ölçülmedi**
- Çocuk sesi sonucu: **ölçülmedi**

WER/CER, veri kümesinin sağladığı transkripte göredir. Bu çalışmada sesler insan tarafından yeniden dinlenip kelime hatası/duraksama etiketiyle işaretlenmedi; bu nedenle yanlış pozitif ve duraksama başarısı iddia edilmez. Ayrıntı [inference/tests/data/evaluation-results.json](inference/tests/data/evaluation-results.json), tekrar üretim girdisi [inference/tests/data/evaluation_manifest.json](inference/tests/data/evaluation_manifest.json) dosyasındadır. Kendi izinli test kayıtlarınızı eklerken şablon olarak [inference/tests/data/evaluation_manifest.example.json](inference/tests/data/evaluation_manifest.example.json) kullanın.

13 Kasım 2026 hizalama onarımında aynı üç kayıt yeniden çalıştırıldı. WER değişmedi (**0,1852**); CER normalleştirme düzeltmesiyle **0,0361** oldu. `karşınızdayım → karşısızdayım` ve `geride → gerize` gibi küçük fakat yüksek CTC güvenli model sapmaları kesin öğrenci hatası yerine `uncertain` sınıfına alındı. Konservatif doğrulanmış-doğru yüzdeleri sırasıyla **%71,4**, **%75,0** ve **%93,8** kaldı; belirsiz kelimeler otomatik doğru sayılmadığı için sahte yüksek sonuç üretilmedi. Yanlış/atlama/tekrar/duraksama senaryoları deterministik testlerde doğrulandı, ancak bu beş senaryo için izinli ve elle etiketlenmiş gerçek öğrenci kayıtları bulunmadığından gerçek hata tespit başarısı ölçülmüş sayılmaz.

Frontend sözleşme/sunum testi, gerçek mikrofon API'lerini kontrollü taklit eder; akustik model testi değildir:

```powershell
cd frontend
npm run build
npm run smoke:ui
```

Bu test masaüstü/mobil yerleşimi, kayıt sırasında metrik üretilmemesini, analiz sonrası API durumlarının CSS'e aktarılmasını ve normal cümle/uzun paragraf/96 karakterlik boşluksuz kelimenin sağ metrik kolonuna veya viewport dışına taşmamasını denetler.

## Gizlilik ve üretime geçiş

- Açık kayıt izni zorunludur; ham yükleme hem API hem inference katmanında sınırlandırılır ve işlem sonunda geçici dosya silinir.
- Rapor saklama varsayılan olarak kapalıdır. Açılırsa PostgreSQL erişimini en az yetkiyle, disk/backup şifrelemesiyle ve gerçek öğrenci/mentör yetkilendirmesiyle koruyun.
- Frontend geliştirme adaptörü bearer tokenı `localStorage` üzerinden okuyabilir; üretimde XSS etkisini azaltmak için kurumun kimlik mimarisine uygun BFF veya `HttpOnly`, `Secure`, `SameSite` cookie yaklaşımı tercih edilmelidir.
- `RetentionDays` tek başına KVKK uyumu sağlamaz. Aydınlatma/açık rıza metni, veli onayı, silme/erişim talepleri, veri işleyen sözleşmeleri, yurt dışı aktarım ve olay kayıtları kurumun hukuk ve güvenlik ekiplerince belirlenmelidir.
- Üretim öncesi izinli çocuk sesi, farklı konuşmacılar, gürültü, sessizlik, yanlış telaffuz, tekrar ve duraksama içeren kör ve elle etiketli bir değerlendirme kümesi hazırlanmalıdır. Eşikler ancak bu kümede yanlış pozitif oranı, WER/CER ve duraksama precision/recall birlikte incelendikten sonra sabitlenmelidir.
