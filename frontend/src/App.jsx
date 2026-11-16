import { useEffect, useState } from 'react';
import Header from './components/Header';
import StudentNavigation from './components/StudentNavigation';
import TaskPanel from './components/TaskPanel';
import ReadingPanel from './components/ReadingPanel';
import ComprehensionPanel from './components/ComprehensionPanel';
import ReportPanel from './components/ReportPanel';
import MentorPanel from './components/MentorPanel';
import TechnicalPanel from './components/TechnicalPanel';
import PortalLogin from './components/PortalLogin';
import { defaultReadingTexts } from './data/prototypeData';

const studentPanels = {
  gorevler: TaskPanel,
  okuma: ReadingPanel,
  anlama: ComprehensionPanel,
  karne: ReportPanel,
};

const readStored = (key, fallback) => {
  try {
    const value = window.localStorage.getItem(key);
    return value ? JSON.parse(value) : fallback;
  } catch {
    return fallback;
  }
};

const createId = (prefix) => `${prefix}-${window.crypto?.randomUUID?.() ?? Date.now()}`;
const normalizeStoredTexts = (items) => items.map((text) => (
  text.id === 'kirlangiclar' && text.mentorNote?.startsWith('Elif,')
    ? { ...text, mentorNote: text.mentorNote.replace(/^Elif,\s*/, '') }
    : text
));

export default function App() {
  const [role, setRole] = useState(() => readStored('yaztek-active-role', null));
  const [teacherScreen, setTeacherScreen] = useState('mentor');
  const [studentScreen, setStudentScreen] = useState('okuma');
  const [texts, setTexts] = useState(() => normalizeStoredTexts(readStored('yaztek-reading-texts', defaultReadingTexts)));
  const [students, setStudents] = useState(() => readStored('yaztek-students', []));
  const [studentProfile, setStudentProfile] = useState(() => readStored('yaztek-student-profile', null));
  const [mentorProfile, setMentorProfile] = useState(() => readStored('yaztek-mentor-profile', null));
  const [selectedTextId, setSelectedTextId] = useState(() => readStored('yaztek-selected-text', defaultReadingTexts[0].id));
  const StudentPanel = studentPanels[studentScreen];
  const selectedText = texts.find((text) => text.id === selectedTextId) ?? texts[0] ?? defaultReadingTexts[0];

  useEffect(() => {
    window.localStorage.setItem('yaztek-reading-texts', JSON.stringify(texts));
  }, [texts]);

  useEffect(() => {
    window.localStorage.setItem('yaztek-students', JSON.stringify(students));
  }, [students]);

  useEffect(() => {
    window.localStorage.setItem('yaztek-selected-text', JSON.stringify(selectedTextId));
  }, [selectedTextId]);

  useEffect(() => {
    if (studentProfile) window.localStorage.setItem('yaztek-student-profile', JSON.stringify(studentProfile));
    else window.localStorage.removeItem('yaztek-student-profile');
  }, [studentProfile]);

  useEffect(() => {
    if (mentorProfile) window.localStorage.setItem('yaztek-mentor-profile', JSON.stringify(mentorProfile));
    else window.localStorage.removeItem('yaztek-mentor-profile');
  }, [mentorProfile]);

  useEffect(() => {
    if (role) window.localStorage.setItem('yaztek-active-role', JSON.stringify(role));
    else window.localStorage.removeItem('yaztek-active-role');
  }, [role]);

  const navigateStudent = (screen) => {
    setStudentScreen(screen);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const addText = (text) => {
    const next = { ...text, id: createId('metin') };
    setTexts((current) => [...current, next]);
    setSelectedTextId(next.id);
    return next.id;
  };

  const deleteText = (textId) => {
    if (texts.length <= 1 || !texts.some((text) => text.id === textId)) return false;
    const remaining = texts.filter((text) => text.id !== textId);
    setTexts(remaining);
    if (selectedTextId === textId) setSelectedTextId(remaining[0].id);
    return true;
  };

  const addStudent = (student) => {
    const next = {
      ...student,
      id: createId('ogrenci'),
      last: 'henüz okuma yok',
      accuracy: 0,
      speed: 0,
      comprehension: 0,
      level: 'Yeni',
      levelClass: 'yeni',
      accuracyColor: 'var(--cizgi)',
      note: 'Henüz değerlendirme yapılmadı.',
    };
    setStudents((current) => [...current, next]);
    return next.id;
  };

  const login = (nextRole, name) => {
    if (nextRole === 'ogrenci') {
      const knownStudent = students.find((student) => student.name.toLocaleLowerCase('tr-TR') === name.toLocaleLowerCase('tr-TR'));
      setStudentProfile({ id: knownStudent?.id ?? createId('profil'), name });
      setStudentScreen('gorevler');
    } else {
      setMentorProfile({ id: createId('mentor'), name });
      setTeacherScreen('mentor');
    }
    setRole(nextRole);
  };

  const logout = () => {
    setRole(null);
  };

  return (
    <div className="kabuk">
      <Header
        role={role}
        profile={role === 'mentor' ? mentorProfile : studentProfile}
        teacherScreen={teacherScreen}
        onTeacherScreenChange={setTeacherScreen}
        onLogout={logout}
      />
      {!role && <PortalLogin onLogin={login} />}
      {role === 'ogrenci' && studentProfile && (
        <section aria-label="Öğrenci paneli">
          <StudentNavigation activeScreen={studentScreen} onScreenChange={setStudentScreen} />
          <StudentPanel
            onNavigate={navigateStudent}
            texts={texts}
            selectedText={selectedText}
            onSelectText={setSelectedTextId}
            studentProfile={studentProfile}
          />
        </section>
      )}
      {role === 'mentor' && mentorProfile && teacherScreen === 'mentor' && <MentorPanel texts={texts} students={students} onAddText={addText} onDeleteText={deleteText} onAddStudent={addStudent} />}
      {role === 'mentor' && mentorProfile && teacherScreen === 'teknik' && <TechnicalPanel />}
    </div>
  );
}
