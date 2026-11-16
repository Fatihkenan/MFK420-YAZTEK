export default function TaskPanel({ onNavigate, texts, onSelectText, studentProfile }) {
  const openTask = (textId, screen) => {
    onSelectText(textId);
    onNavigate(screen);
  };

  return (
    <div className="izgara">
      <div className="kart">
        <h3 className="kart-baslik">Merhaba {studentProfile.name} 👋</h3>
        <p className="kart-alt">Öğretmenin {texts.length} metin atadı. Her metin için okuma ve anlama görevi bulunuyor.</p>
        <div className="izgara" style={{ gap: '10px' }}>
          {texts.flatMap((text) => [
            <button className="gorev gorev-buton" type="button" onClick={() => openTask(text.id, 'okuma')} key={`${text.id}-okuma`}>
              <span className="gorev-ikon">📖</span>
              <span><h4>{text.title} metnini oku</h4><p>Öğretmenin atadı · <span className={`etiket ${text.level.toLocaleLowerCase('tr-TR')}`}>{text.level}</span></p></span>
            </button>,
            <button className="gorev gorev-buton" type="button" onClick={() => openTask(text.id, 'anlama')} key={`${text.id}-anlama`}>
              <span className="gorev-ikon">❓</span>
              <span><h4>{text.title} sorularını çöz</h4><p>Bu metne özel 7 okuduğunu anlama sorusu</p></span>
            </button>,
          ])}
        </div>
      </div>
    </div>
  );
}
