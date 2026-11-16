const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080').replace(/\/$/, '');

const parseResponse = async (response) => {
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.message ?? body.title ?? 'Soru servisi isteği tamamlanamadı.');
  return body;
};

export const createComprehensionAttempt = async ({ selectedText, studentId, idempotencyKey, signal }) => {
  const response = await fetch(`${API_BASE_URL}/api/comprehension/attempts`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, signal,
    body: JSON.stringify({ studentId, textId: selectedText.id, textTitle: selectedText.title, sourceText: selectedText.content, idempotencyKey }),
  });
  return parseResponse(response);
};

export const submitComprehensionAttempt = async ({ attemptId, studentId, answers, signal }) => {
  const response = await fetch(`${API_BASE_URL}/api/comprehension/attempts/${attemptId}/submit`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, signal,
    body: JSON.stringify({ studentId, answers }),
  });
  return parseResponse(response);
};
