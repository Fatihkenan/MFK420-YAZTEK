import { useEffect, useMemo, useRef, useState } from 'react';
import { createComprehensionAttempt, submitComprehensionAttempt } from '../services/comprehensionApi';

const skills = { main_idea: 'Ana fikir', key_detail: 'Önemli bilgi', cause_effect: 'Neden-sonuç', inference: 'Çıkarım', character_event_relationship: 'Karakter / olay ilişkisi', context_meaning: 'Bağlamdan anlam', general_evaluation: 'Genel değerlendirme' };
const statuses = { Correct: 'Doğru', Partial: 'Kısmen doğru', Incorrect: 'Yanlış', ReviewRequired: 'Değerlendirilemedi' };
const newKey = () => window.crypto?.randomUUID?.() ?? `00000000-0000-4000-8000-${String(Date.now()).padStart(12, '0').slice(-12)}`;

export default function ComprehensionPanel({ selectedText, studentProfile }) {
  const studentId = studentProfile.id;
  const [attempt, setAttempt] = useState(null);
  const [answers, setAnswers] = useState({});
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const inFlightKey = useRef(null);

  useEffect(() => {
    setAttempt(null); setAnswers({}); setResult(null); setError(''); inFlightKey.current = null;
  }, [selectedText.id, studentId]);

  const answeredCount = useMemo(() => attempt?.questions.filter((question) => {
    const answer = answers[question.id];
    return question.type === 'multiple_choice' ? Number.isInteger(answer?.selectedOptionIndex) : Boolean(answer?.textAnswer?.trim());
  }).length ?? 0, [answers, attempt]);
  const feedback = useMemo(() => Object.fromEntries((result?.answers ?? []).map((item) => [item.questionId, item])), [result]);

  const startAttempt = async () => {
    if (loading) return;
    const idempotencyKey = newKey();
    inFlightKey.current = idempotencyKey;
    setLoading(true); setError(''); setAttempt(null); setResult(null); setAnswers({});
    try {
      const generated = await createComprehensionAttempt({ selectedText, studentId, idempotencyKey });
      if (generated.status !== 'Ready' || generated.questions?.length !== 7) {
        throw new Error(generated.error ?? 'Soru seti henüz hazır değil. Biraz sonra yeniden deneyin.');
      }
      if (inFlightKey.current === idempotencyKey) setAttempt(generated);
    } catch (requestError) {
      if (inFlightKey.current === idempotencyKey) setError(requestError.message);
    } finally {
      if (inFlightKey.current === idempotencyKey) { inFlightKey.current = null; setLoading(false); }
    }
  };

  const choose = (questionId, selectedOptionIndex) => {
    if (!result) setAnswers((current) => ({ ...current, [questionId]: { questionId, selectedOptionIndex, textAnswer: null } }));
  };
  const write = (questionId, textAnswer) => {
    if (!result) setAnswers((current) => ({ ...current, [questionId]: { questionId, selectedOptionIndex: null, textAnswer } }));
  };
  const submit = async () => {
    if (!attempt || answeredCount !== 7 || submitting) return;
    setSubmitting(true); setError('');
    try {
      setResult(await submitComprehensionAttempt({ attemptId: attempt.id, studentId, answers: attempt.questions.map((question) => answers[question.id]) }));
    } catch (requestError) { setError(requestError.message); }
    finally { setSubmitting(false); }
  };

  const score = result ? result.totalScore : answeredCount;
  const displayedScore = result && score == null ? '—' : Number(score).toLocaleString('tr-TR');
  const scoreWidth = score == null ? 0 : (Number(score) / 7) * 100;
  return (
    <div className="izgara iki anlama-izgara">
      <div className="kart">
        <h3 className="kart-baslik">Okuduğunu anlama</h3>
        <p className="kart-alt">{selectedText.title} metnine göre 4 çoktan seçmeli ve 3 kısa yanıt sorusu.</p>
        {!attempt && !loading && <div className="anlama-baslangic"><p>Sorular bu metne özel hazırlanır. Önceki denemelerin birebir kopyası gösterilmez.</p><button className="buton birincil" type="button" onClick={startAttempt}>7 soruluk testi hazırla</button></div>}
        {loading && <p className="anlama-durum" role="status">Metne dayalı sorular hazırlanıyor…</p>}
        {error && <p className="anlama-hata" role="alert">{error}</p>}

        {attempt?.questions.map((question) => {
          const answer = answers[question.id];
          const itemFeedback = feedback[question.id];
          return <div className="soru" key={question.id}>
            <div className="soru-ust"><span className="etiket">{question.order} / 7</span><span className="soru-beceri">{skills[question.skill] ?? question.skill}</span></div>
            <p>{question.prompt}</p>
            {question.type === 'multiple_choice' ? <div className="secenekler">
              {question.options.map((option, optionIndex) => {
                const chosen = answer?.selectedOptionIndex === optionIndex;
                const isCorrect = result && optionIndex === itemFeedback?.correctOptionIndex;
                const isWrong = result && chosen && !isCorrect;
                return <button className={`secenek ${chosen ? 'secili' : ''} ${isCorrect ? 'dogru' : ''} ${isWrong ? 'yanlis' : ''}`} type="button" disabled={Boolean(result)} aria-pressed={chosen} onClick={() => choose(question.id, optionIndex)} key={`${question.id}-${optionIndex}`}>{option}</button>;
              })}
            </div> : <textarea className="not-alani anlama-yanit" value={answer?.textAnswer ?? ''} disabled={Boolean(result)} maxLength={2000} aria-label={`${question.order}. sorunun kısa yanıtı`} placeholder="Kısa yanıtını metne dayanarak yaz…" onChange={(event) => write(question.id, event.target.value)} />}
            {itemFeedback && <div className={`soru-geribildirim ${itemFeedback.status}`}><strong>{statuses[itemFeedback.status] ?? itemFeedback.status} · {itemFeedback.score} puan</strong><span>{itemFeedback.feedback}</span><span>{itemFeedback.explanation}</span>{itemFeedback.evidenceVerified && itemFeedback.evidence ? <small>Metinden kanıt: “{itemFeedback.evidence}”</small> : <small>Metinden kanıt güvenilir biçimde doğrulanamadı.</small>}</div>}
          </div>;
        })}

        {attempt?.questions.length === 7 && !result && <div className="buton-sira"><button className="buton birincil" type="button" disabled={answeredCount !== 7 || submitting} onClick={submit}>{submitting ? 'Yanıtlar değerlendiriliyor…' : 'Cevapları gönder'}</button></div>}
        {result && <div className="buton-sira"><button className="buton ikincil" type="button" onClick={startAttempt}>Tekrar dene · yeni sorular</button></div>}
      </div>

      <div className="kart kart-yukari">
        <h3 className="kart-baslik">Anlama puanın</h3>
        <p className="kart-alt">{result ? 'Yedi soru üzerinden sonuç' : 'Sorular yanıtlandıkça güncellenir'}</p>
        <dl className="metrik metrik-ciplak"><dt>{result ? (result.isFinal ? 'Toplam puan' : 'Puan beklemede') : 'Yanıtlanan soru'}</dt><dd id="anlamaSkor">{displayedScore}<small> / 7</small></dd><div className="olcek"><i id="anlamaOlcek" style={{ width: `${scoreWidth}%` }} /></div></dl>
        {result?.reviewRequiredCount > 0 && <p className="anlama-inceleme">{result.reviewRequiredCount} yanıt güvenilir biçimde değerlendirilemedi; toplam puan mentör incelemesine kadar kesinleşmedi.</p>}
        {result?.skills?.length > 0 && <div className="beceri-ozeti"><h4 className="mini-baslik">Beceri analizi</h4>{result.skills.map((skill) => <div className="beceri-satir" key={skill.skill}><span>{skills[skill.skill] ?? skill.skill}</span><b>{skill.score} / {skill.maximum}</b></div>)}</div>}
        <p className="aciklama">Anlama puanı, okuma hızı ve doğruluğuyla birlikte değerlendirilir. Üçü birden bir sonraki metnin zorluk seviyesini belirler.</p>
      </div>
    </div>
  );
}
