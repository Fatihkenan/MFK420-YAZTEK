import { useState } from 'react';

const roleCopy = {
  ogrenci: {
    icon: '📖',
    title: 'Öğrenci girişi',
    description: 'Öğretmenin atadığı okuma ve anlama görevlerini aç.',
    placeholder: 'Örn. Deniz Yılmaz',
    button: 'Öğrenci paneline gir',
  },
  mentor: {
    icon: '🧭',
    title: 'Öğretmen girişi',
    description: 'Metinleri, öğrencileri ve mentör değerlendirmelerini yönet.',
    placeholder: 'Örn. Ayşe Öğretmen',
    button: 'Öğretmen paneline gir',
  },
};

export default function PortalLogin({ onLogin }) {
  const [role, setRole] = useState('ogrenci');
  const [name, setName] = useState('');
  const copy = roleCopy[role];

  const submit = (event) => {
    event.preventDefault();
    const normalizedName = name.trim().replace(/\s+/g, ' ');
    if (normalizedName.length >= 2) onLogin(role, normalizedName);
  };

  return (
    <section className="giris-kabuk" aria-label="YAZTEK girişi">
      <form className="kart giris-karti" onSubmit={submit}>
        <div className="giris-rol-secim" role="tablist" aria-label="Panel seçimi">
          <button type="button" role="tab" aria-selected={role === 'ogrenci'} onClick={() => setRole('ogrenci')}>Öğrenci</button>
          <button type="button" role="tab" aria-selected={role === 'mentor'} onClick={() => setRole('mentor')}>Öğretmen</button>
        </div>
        <span className="giris-ikon" aria-hidden="true">{copy.icon}</span>
        <h2>{copy.title}</h2>
        <p>{copy.description}</p>
        <label>
          Ad soyad
          <input
            autoFocus
            required
            minLength="2"
            maxLength="80"
            autoComplete="name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder={copy.placeholder}
          />
        </label>
        <button className="buton birincil" type="submit">{copy.button}</button>
        <small>Bu yerel prototipte oturum bilgisi yalnızca bu cihazda saklanır.</small>
      </form>
    </section>
  );
}
