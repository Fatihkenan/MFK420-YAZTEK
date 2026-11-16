const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080').replace(/\/$/, '');

const extensionFor = (mimeType) => {
  if (mimeType.includes('ogg')) return 'ogg';
  if (mimeType.includes('mp4')) return 'm4a';
  return 'webm';
};

export const getLocalStudentId = () => {
  const key = 'yaztek-local-student-id';
  const existing = window.localStorage.getItem(key);
  if (existing) return existing;
  const value = `local-${window.crypto?.randomUUID?.() ?? Date.now()}`;
  window.localStorage.setItem(key, value);
  return value;
};

export async function analyzeReading({ blob, referenceText, textId, consent, signal }) {
  const form = new FormData();
  form.append('audio', blob, `reading.${extensionFor(blob.type)}`);
  form.append('referenceText', referenceText);
  form.append('textId', textId);
  form.append('studentId', getLocalStudentId());
  form.append('consent', String(consent));

  const token = window.localStorage.getItem('yaztek-access-token');
  const response = await fetch(`${apiBaseUrl}/api/reading-analysis`, {
    method: 'POST',
    body: form,
    signal,
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.detail || payload?.title || `Analiz servisi HTTP ${response.status} döndürdü.`);
  }
  if (!payload?.words || payload.accuracyPercent === undefined) {
    throw new Error('Analiz servisi beklenen kanıt alanlarını döndürmedi.');
  }
  return payload;
}

