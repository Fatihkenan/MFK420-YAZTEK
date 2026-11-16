import { useEffect, useState } from 'react';

const emptyStudent = { name: '', className: '3-B', age: '9' };
const emptyText = { title: '', level: 'Orta', grade: '3. sınıf', content: '', focusWords: '', mentorNote: '' };

export default function MentorPanel({ texts, students, onAddText, onDeleteText, onAddStudent }) {
  const [selectedId, setSelectedId] = useState(students[0]?.id ?? '');
  const [permission, setPermission] = useState(true);
  const [studentForm, setStudentForm] = useState(emptyStudent);
  const [textForm, setTextForm] = useState(emptyText);
  const [notice, setNotice] = useState('');
  const selected = students.find((student) => student.id === selectedId);

  useEffect(() => {
    if (!selected && students[0]) setSelectedId(students[0].id);
  }, [selected, students]);

  const addStudent = (event) => {
    event.preventDefault();
    const id = onAddStudent({ ...studentForm, age: Number(studentForm.age) });
    setSelectedId(id);
    setStudentForm(emptyStudent);
    setNotice('Öğrenci oluşturuldu.');
  };

  const addText = (event) => {
    event.preventDefault();
    onAddText({
      ...textForm,
      focusWords: textForm.focusWords.split(',').map((word) => word.trim()).filter(Boolean),
      mentorNote: textForm.mentorNote || 'Acele etme, nokta gördüğünde bir nefes al.',
    });
    setTextForm(emptyText);
    setNotice('Metin oluşturuldu ve öğrencinin Görevlerim ekranına eklendi.');
  };

  const deleteText = (text) => {
    if (texts.length <= 1) {
      setNotice('Okuma ekranının çalışması için en az bir metin kalmalıdır.');
      return;
    }
    if (!window.confirm(`“${text.title}” metnini silmek istediğine emin misin?`)) return;
    if (onDeleteText(text.id)) setNotice(`“${text.title}” metni silindi.`);
  };

  return (
    <section className="izgara iki" aria-label="Mentör paneli">
      <div className="izgara yan-kolon">
        <div className="kart">
          <h3 className="kart-baslik">Öğrenciler · {students.length} kayıt</h3>
          <p className="kart-alt">Bir öğrenci seç, sağdaki panel güncellensin.</p>
          {students.length > 0 && <div className="ogrenci-baslik gizle-mobil"><span className="mini-etiket">Öğrenci</span><span className="mini-etiket">Doğruluk</span><span className="mini-etiket">Hız</span><span className="mini-etiket">Düzey</span></div>}
          {students.map((student) => (
            <button className="ogrenci-satir" type="button" aria-current={selectedId === student.id} onClick={() => setSelectedId(student.id)} key={student.id}>
              <span className="ogrenci-ad">{student.name}<small>{student.className} · Son okuma: {student.last}</small></span>
              <span className="gizle-mobil"><span className="mini-olcek"><i style={{ width: `${student.accuracy}%`, background: student.accuracyColor }} /></span></span>
              <span className="gizle-mobil"><span className="mini-olcek"><i style={{ width: `${Math.min(100, (student.speed / 140) * 100)}%`, background: 'var(--indigo)' }} /></span></span>
              <span className={`durum ${student.levelClass}`}>{student.level}</span>
            </button>
          ))}
          {students.length === 0 && <p className="bos-durum">Henüz öğrenci yok. Aşağıdaki formdan ilk öğrencini oluştur.</p>}
          <p className="aciklama">Düzey eşikleri Akyol (2011) ölçütlerine göre hesaplanır: %96–100 bağımsız, %90–95 öğretimsel, %90 altı destek gerektirir.</p>
        </div>

        <form className="kart yonetim-formu" onSubmit={addStudent}>
          <h3 className="kart-baslik">Öğrenci oluştur</h3>
          <p className="kart-alt">Listeye yalnızca senin eklediğin öğrenciler gelir.</p>
          <label>Ad soyad<input required value={studentForm.name} onChange={(event) => setStudentForm({ ...studentForm, name: event.target.value })} /></label>
          <div className="form-satir">
            <label>Sınıf<input required value={studentForm.className} onChange={(event) => setStudentForm({ ...studentForm, className: event.target.value })} /></label>
            <label>Yaş<input required min="5" max="18" type="number" value={studentForm.age} onChange={(event) => setStudentForm({ ...studentForm, age: event.target.value })} /></label>
          </div>
          <button className="buton birincil" type="submit">Öğrenciyi ekle</button>
        </form>
      </div>

      <div className="izgara yan-kolon">
        {selected ? (
          <>
            <div className="kart">
              <h3 className="kart-baslik">{selected.name}</h3>
              <p className="kart-alt">{selected.className} · {selected.age} yaş · 0 oturum tamamlandı</p>
              <div className="izgara uc mini-aralik">
                <dl className="metrik"><dt>Doğruluk</dt><dd>{selected.accuracy}<small>%</small></dd></dl>
                <dl className="metrik"><dt>Hız</dt><dd>{selected.speed}</dd></dl>
                <dl className="metrik"><dt>Anlama</dt><dd>{selected.comprehension}<small>/7</small></dd></dl>
              </div>
              <p className="ogrenci-notu">{selected.note}</p>
            </div>
            <div className="kart">
              <div className="izin-kutu">
                <div><strong>Rapor paylaşım izni {permission ? 'açık' : 'kapalı'}</strong>Öğrenci izni kapattığında ayrıntılı raporlar size görünmez; yalnızca oturum sayısı kalır.</div>
                <button className="anahtar" type="button" aria-pressed={permission} aria-label="Rapor paylaşım izni" onClick={() => setPermission((value) => !value)} />
              </div>
            </div>
            <div className="kart">
              <h3 className="kart-baslik">Geri bildirim yaz</h3>
              <p className="kart-alt">Öğrencinin okuma ekranında görünür</p>
              <textarea className="not-alani" placeholder={`${selected.name.split(' ')[0]}’e bir not bırak…`} aria-label="Mentör geri bildirimi" />
              <div className="buton-sira"><button className="buton birincil" type="button">Notu gönder</button><button className="buton ikincil" type="button">Metin ata</button><button className="buton ikincil" type="button">Tekrar kelimesi ekle</button></div>
            </div>
          </>
        ) : <div className="kart bos-durum">Öğrenci ayrıntılarını görmek için önce bir öğrenci oluştur.</div>}

        <form className="kart yonetim-formu" onSubmit={addText}>
          <h3 className="kart-baslik">Okuma metni oluştur</h3>
          <p className="kart-alt">Kaydedilen metin, Görevlerim ekranından seçilip Okuma ekranında açılır. Toplam {texts.length} metin var.</p>
          <label>Başlık<input required value={textForm.title} onChange={(event) => setTextForm({ ...textForm, title: event.target.value })} /></label>
          <div className="form-satir">
            <label>Düzey<select value={textForm.level} onChange={(event) => setTextForm({ ...textForm, level: event.target.value })}><option>Kolay</option><option>Orta</option><option>Zor</option></select></label>
            <label>Sınıf<input required value={textForm.grade} onChange={(event) => setTextForm({ ...textForm, grade: event.target.value })} /></label>
          </div>
          <label>Metin<textarea required className="not-alani" value={textForm.content} onChange={(event) => setTextForm({ ...textForm, content: event.target.value })} placeholder="Cümleleri nokta ile ayırarak yaz…" /></label>
          <label>Dikkat edilecek kelimeler<input value={textForm.focusWords} onChange={(event) => setTextForm({ ...textForm, focusWords: event.target.value })} placeholder="virgülle, ayır" /></label>
          <label>Mentör notu<textarea className="not-alani kisa" value={textForm.mentorNote} onChange={(event) => setTextForm({ ...textForm, mentorNote: event.target.value })} /></label>
          <button className="buton birincil" type="submit">Metni kaydet</button>
          {notice && <p className="form-bildirim" role="status">{notice}</p>}
          <div className="metin-yonetim-listesi" aria-label="Kayıtlı okuma metinleri">
            <h4>Kayıtlı metinler</h4>
            {texts.map((text) => (
              <div className="metin-yonetim-satiri" key={text.id}>
                <span><strong>{text.title}</strong><small>{text.level} · {text.grade}</small></span>
                <button
                  className="metin-sil"
                  type="button"
                  disabled={texts.length <= 1}
                  aria-label={`${text.title} metnini sil`}
                  title={texts.length <= 1 ? 'En az bir okuma metni kalmalıdır' : 'Metni sil'}
                  onClick={() => deleteText(text)}
                >Sil</button>
              </div>
            ))}
          </div>
        </form>
      </div>
    </section>
  );
}
