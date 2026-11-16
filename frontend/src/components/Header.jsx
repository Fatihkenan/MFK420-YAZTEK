export default function Header({
  role,
  profile,
  teacherScreen,
  onTeacherScreenChange,
  onLogout,
}) {
  return (
    <header className="ustbar">
      <div className="marka">YAZ<span>TEK</span><small>Okuma Akıcılığı Platformu</small></div>
      {role === 'mentor' && (
        <div className="rol-secim" role="tablist" aria-label="Öğretmen paneli">
          <button className="rol-btn" type="button" role="tab" aria-selected={teacherScreen === 'mentor'} onClick={() => onTeacherScreenChange('mentor')}>Mentör</button>
          <button className="rol-btn" type="button" role="tab" aria-selected={teacherScreen === 'teknik'} onClick={() => onTeacherScreenChange('teknik')}>Teknik mimari</button>
        </div>
      )}
      {role && profile && (
        <div className="oturum-bilgisi">
          <span><small>{role === 'mentor' ? 'Öğretmen' : 'Öğrenci'}</small><strong>{profile.name}</strong></span>
          <button type="button" onClick={onLogout}>Çıkış</button>
        </div>
      )}
    </header>
  );
}
