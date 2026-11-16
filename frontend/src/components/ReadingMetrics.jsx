function Metric({ title, value, suffix, valueId, children }) {
  return (
    <dl className="metrik">
      <dt>{title}</dt>
      <dd id={valueId}>{value}<small>{suffix}</small></dd>
      {children}
    </dl>
  );
}

export default function ReadingMetrics({ stats }) {
  const accuracy = stats.hasAnalysis ? stats.accuracy : null;
  const speed = stats.hasAnalysis ? stats.speed : null;
  const metricValue = (value) => (stats.hasAnalysis && value != null ? value : '—');

  return (
    <aside className="izgara metrik-kolon" aria-label="Okuma metrikleri">
      <Metric title="Doğru okuma" value={accuracy ?? '—'} suffix=" %" valueId="mDogruluk">
        <div className="olcek"><i className="yesil" id="oDogruluk" style={{ width: `${accuracy ?? 0}%` }} /></div>
        <p className="norm">%96+ bağımsız · %90–95 öğretimsel</p>
      </Metric>
      <Metric title="Okuma hızı" value={speed ?? '—'} suffix=" kel/dk" valueId="mHiz">
        <div className="olcek"><i className="sari" id="oHiz" style={{ width: `${Math.min(100, ((speed ?? 0) / 140) * 100)}%` }} /></div>
        <p className="norm">3. sınıf ilkbahar normu: 80–140</p>
      </Metric>
      <div className="izgara mini-metrikler">
        <Metric title="Duraksama" value={metricValue(stats.pause)} suffix="" valueId="mDuraksama" />
        <Metric title="Tekrar" value={metricValue(stats.repeat)} suffix="" valueId="mTekrar" />
        <Metric title="Başa dönme" value={metricValue(stats.restart)} suffix="" valueId="mBasa" />
        <Metric title="Hatalı kelime" value={metricValue(stats.error)} suffix="" valueId="mHata" />
      </div>
      <div className="kart mentor-notu">
        <p className="mini-baslik">Mentör notu</p>
        <p className="mentor-metni">“{stats.mentorNote}” — Ayşe Öğretmen</p>
      </div>
    </aside>
  );
}
