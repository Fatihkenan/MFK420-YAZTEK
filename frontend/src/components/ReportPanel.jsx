const reportMetrics = [
  ['Doğruluk', '94', ' %', 'Öğretimsel düzey'],
  ['Hız', '86', ' k/dk', 'Norm içinde'],
  ['Anlama', '3', ' / 7', 'Geliştirilebilir'],
  ['Güvenilirlik', '0.91', '', 'Entropi skoru'],
];

export default function ReportPanel({ studentProfile }) {
  return (
    <div className="izgara iki">
      <div className="izgara yan-kolon">
        <div className="kart">
          <h3 className="kart-baslik">Okuma raporun</h3>
          <p className="kart-alt">Kırlangıçlar · 14 Ağustos · 3 dk 42 sn</p>
          <div className="izgara dort mini-aralik">
            {reportMetrics.map(([title, value, suffix, note]) => (
              <dl className="metrik" key={title}><dt>{title}</dt><dd>{value}<small>{suffix}</small></dd><p className="norm">{note}</p></dl>
            ))}
          </div>
        </div>
        <div className="kart">
          <h3 className="kart-baslik">Hata dökümü</h3>
          <p className="kart-alt">Harf, kelime ve cümle seviyesinde otomatik tespit</p>
          <div className="hata-satir"><span className="hata-tur">Harf</span><span><code>sallayınca</code> → <code>salayınca</code> · çift ünsüz atlandı</span><button className="ses-btn" type="button">▶ Doğru okunuşu dinle</button></div>
          <div className="hata-satir"><span className="hata-tur">Kelime</span><span><code>cıvıl</code> iki kez tekrar edildi</span><button className="ses-btn" type="button">▶ Doğru okunuşu dinle</button></div>
          <div className="hata-satir"><span className="hata-tur">Süre</span><span><code>kırlangıç</code> beklenenden 640 ms uzun sürdü</span><button className="ses-btn" type="button">▶ Doğru okunuşu dinle</button></div>
          <div className="hata-satir"><span className="hata-tur">Cümle</span><span>3. cümlede bir kez başa dönüldü</span><button className="ses-btn" type="button">▶ Kaydı dinle</button></div>
        </div>
        <div className="kart">
          <h3 className="kart-baslik">Sıradaki metinlerin</h3>
          <p className="kart-alt">Performansına göre üretildi · tekrar kelimelerin metne yerleştirildi</p>
          <div className="izgara kompakt">
            <div className="gorev"><div className="gorev-ikon">🌬️</div><div><h4>Rüzgârlı Bir Gün</h4><p><span className="etiket orta">Orta</span> · içinde <b>rüzgâr</b>, <b>sallayınca</b> geçiyor</p></div></div>
            <div className="gorev"><div className="gorev-ikon">🪺</div><div><h4>Yuvadaki Misafir</h4><p><span className="etiket kolay">Kolay</span> · kısa cümlelerle tekrarlı okuma</p></div></div>
          </div>
        </div>
      </div>
      <div className="izgara yan-kolon">
        <div className="kart">
          <h3 className="kart-baslik">Gelişimin</h3>
          <p className="kart-alt">Son 8 okuma · kelime/dk</p>
          <svg className="grafik" viewBox="0 0 300 170" role="img" aria-label="Okuma hızı son sekiz oturumda 61’den 86’ya yükseldi">
            <line x1="0" y1="60" x2="300" y2="60" stroke="#D7E0F0" strokeDasharray="4 4" />
            <text x="4" y="54" fontSize="9" fill="#5A6788" fontFamily="Figtree">hedef 95</text>
            <polyline points="10,140 51,132 92,120 133,124 174,104 215,96 256,88 290,80" fill="none" stroke="#3B36D9" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="290" cy="80" r="5" fill="#3B36D9" />
            <text x="230" y="70" fontSize="11" fill="#131F45" fontFamily="JetBrains Mono" fontWeight="600">86</text>
            <text x="4" y="160" fontSize="9" fill="#5A6788" fontFamily="Figtree">61 kel/dk</text>
          </svg>
        </div>
        <div className="kart"><h3 className="kart-baslik">Tekrar listen</h3><p className="kart-alt">Bir sonraki metinde karşına çıkacak</p><div className="cip-sira"><span className="cip hata">sallayınca</span><span className="cip duraksama">kırlangıç</span><span className="cip duraksama">rüzgâr</span><span className="cip">cıvıl</span></div></div>
        <div className="kart"><h3 className="kart-baslik">Aferin {studentProfile.name}!</h3><p className="kart-alt">Bugünün yıldızını kazandın</p><div className="rozet-sira"><div className="rozet kazanildi">⭐</div><div className="rozet kazanildi">🏅</div><div className="rozet kilitli">🎖️</div></div></div>
      </div>
    </div>
  );
}
