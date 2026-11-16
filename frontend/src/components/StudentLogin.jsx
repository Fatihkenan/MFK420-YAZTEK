import { useState } from 'react';

export default function StudentLogin({ onLogin }) {
  const [name, setName] = useState('');

  const submit = (event) => {
    event.preventDefault();
    const normalizedName = name.trim().replace(/\s+/g, ' ');
    if (normalizedName.length >= 2) onLogin(normalizedName);
  };

  return (
    <section className="giris-kabuk" aria-label="Öğrenci girişi">
      <form className="kart giris-karti" onSubmit={submit}>
        <span className="giris-ikon" aria-hidden="true">👋</span>
        <h2>Öğrenci girişi</h2>
        <p>Görevlerini kendi adınla açmak için adını yaz.</p>
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
            placeholder="Örn. Deniz Yılmaz"
          />
        </label>
        <button className="buton birincil" type="submit">Giriş yap</button>
        <small>Bu yerel prototipte profil bilgisi yalnızca bu cihazda saklanır.</small>
      </form>
    </section>
  );
}
