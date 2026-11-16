const screens = [
  ['gorevler', 'Görevlerim'],
  ['okuma', 'Okuma'],
  ['anlama', 'Anlama soruları'],
  ['karne', 'Karnem'],
];

export default function StudentNavigation({ activeScreen, onScreenChange }) {
  return (
    <nav className="ekran-nav" role="tablist" aria-label="Öğrenci ekranları">
      {screens.map(([value, label]) => (
        <button
          className="ekran-btn"
          type="button"
          role="tab"
          aria-selected={activeScreen === value}
          key={value}
          onClick={() => onScreenChange(value)}
        >
          {label}
        </button>
      ))}
    </nav>
  );
}
