const pipeline = [
  { no: '01', title: 'Kayıt ve ön işleme', text: 'Tarayıcıdan mikrofon akışı; gürültü önleyici mikrofon, telefon ve tablet varyasyonlarıyla eğitilmiş model.', tags: ['WebRTC', '16 kHz'] },
  { no: '02', title: 'Akustik çözümleme', text: 'Sesi doğrudan harflere çevirir; kelimenin sözlükte olması gerekmez, bu yüzden “geliyorum → geliyom” yakalanır. Whisper düzeltme eğilimi nedeniyle dışlandı.', tags: ['Wav2Vec2 / XLSR', 'Türkçe fine-tune'] },
  { no: '03', title: 'Çoklu ASR + dil modeli', text: 'Aynı ses hem dil modelsiz hem dil modelli çözümlenir. Okunacak metin bilindiği için harf/hece/kelime seviyesinde n-gram çıkarılır.', tags: ['n-gram LM', 'CTC decode'] },
  { no: '04', title: 'Hipotez birleştirme', text: 'Her hipotez orijinal metinle hizalanır, kelime başına en düşük düzenleme mesafeli aday seçilir. Alternatif olarak BERTurk ön temizliği ve Llama-3 ile en iyi hipotez üretimi.', tags: ['Viterbi', 'edit distance', 'BERTurk', 'Llama-3 + LoRA'] },
  { no: '05', title: 'Güvenilirlik ve süre analizi', text: 'a/e, i/ı gibi telaffuz kaymalarında çözümlemenin ne kadar güvenilir olduğu ölçülür. Hizalamadan çıkan milisaniye aralıklarıyla beklenen kelime süresinden sapma hesaplanır.', tags: ['Tsallis entropi', 'kelime süresi sapması'] },
  { no: '06', title: 'Rapor ve kişiselleştirme', text: 'Harf/kelime/cümle hataları, tekrar, duraksama ve başa dönme sayıları raporlanır; performans verisi metin üretimine girdi olur.', tags: ['Yerel Ollama · Qwen3', 'kural + kriter tabanlı üretim'] },
];

const technologies = [
  ['Ön yüz', <><code>ReactJS</code> · duyarlı, bileşen tabanlı</>],
  ['Arka yüz', <><code>.NET Core</code> · katmanlı mimari, REST</>],
  ['Veritabanı', <code>PostgreSQL</code>],
  ['Bulut', <><code>AWS + Kubernetes</code></>],
  ['Ölçekleme', 'GPU otomatik ölçekleme, load balancing'],
  ['Eşzamanlılık', <>Dağıtık güvenli <code>Queue</code></>],
  ['Tasarım', <><code>Figma</code> · UX → UI</>],
];

export default function TechnicalPanel() {
  return (
    <section className="izgara iki" aria-label="Teknik mimari paneli">
      <div className="kart">
        <h3 className="kart-baslik">Ses → hata raporu hattı</h3>
        <p className="kart-alt">Başvuru formu, Yöntem 2.1/B’de tanımlanan iki seviyeli mimari</p>
        <div className="hat">
          {pipeline.map((step, index) => (
            <div className="hat-grup" key={step.no}>
              <div className="hat-adim">
                <div className="hat-no">{step.no}</div>
                <div><h4>{step.title}</h4><p>{step.text}</p>{step.tags.map((tag) => <span className="tek" key={tag}>{tag}</span>)}</div>
              </div>
              {index < pipeline.length - 1 && <div className="ok-isaret" aria-hidden="true">▼</div>}
            </div>
          ))}
        </div>
      </div>
      <div className="izgara yan-kolon">
        <div className="kart">
          <h3 className="kart-baslik">Teknoloji yığını</h3>
          <table><thead><tr><th>Katman</th><th>Seçim</th></tr></thead><tbody>{technologies.map(([layer, choice]) => <tr key={layer}><td>{layer}</td><td>{choice}</td></tr>)}</tbody></table>
        </div>
        <div className="kart">
          <h3 className="kart-baslik">Başarı eşikleri</h3>
          <p className="kart-alt">İş paketlerinde sayısallaştırılmış kabul kriterleri</p>
          <table><tbody><tr><td>Kelime hata oranı</td><td className="kpi">&lt; %5</td></tr><tr><td>Harf hata oranı</td><td className="kpi">&lt; %10</td></tr><tr><td>Hata tespit oranı</td><td className="kpi">&gt; %95</td></tr><tr><td>Ses analizi doğruluğu</td><td className="kpi">&gt; %95</td></tr><tr><td>Kullanılabilirlik memnuniyeti</td><td className="kpi">≥ %90</td></tr></tbody></table>
        </div>
        <div className="kart">
          <h3 className="kart-baslik">Güvenlik ve KVKK</h3>
          <p className="kart-alt">İP-6 · GDPR ilkeleriyle uyum</p>
          <div className="cip-sira">{['Token kimlik doğrulama', 'SSL şifreleme', 'Veri maskeleme', 'Rol tabanlı erişim', 'Penetrasyon testi', 'Erişim loglama', 'Veri minimizasyonu', 'Depolama sınırlaması'].map((item) => <span className="cip" key={item}>{item}</span>)}</div>
          <p className="aciklama">Öğrenci izin vermediği sürece mentör, öğrencinin bilgilerini ve raporlarını göremez — bu kural arayüzde bir izin anahtarı olarak somutlaştırılmıştır.</p>
        </div>
      </div>
    </section>
  );
}
