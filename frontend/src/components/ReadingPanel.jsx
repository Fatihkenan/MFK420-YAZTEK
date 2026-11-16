import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import useAudioRecorder from '../hooks/useAudioRecorder';
import { analyzeReading } from '../services/readingAnalysisApi';
import ReadingMetrics from './ReadingMetrics';

const emptyStats = (mentorNote) => ({
  hasAnalysis: false,
  accuracy: null,
  speed: null,
  pause: null,
  repeat: null,
  error: null,
  restart: null,
  uncertain: null,
  mentorNote,
});

const splitText = (content) => {
  const parts = content.match(/[^.!?]+[.!?]?/g) ?? [content];
  return parts.map((part) => part.trim().split(/\s+/).filter(Boolean)).filter((part) => part.length);
};

const normalizeTrackingWord = (value) => value
  .replaceAll('I', 'ı')
  .replaceAll('İ', 'i')
  .toLocaleLowerCase('tr-TR')
  .replace(/[^a-zçğıöşüâîû]/g, '');

const wordDistance = (left, right) => {
  const previous = Array.from({ length: right.length + 1 }, (_, index) => index);
  for (let leftIndex = 1; leftIndex <= left.length; leftIndex += 1) {
    const current = [leftIndex];
    for (let rightIndex = 1; rightIndex <= right.length; rightIndex += 1) {
      current.push(Math.min(
        current[rightIndex - 1] + 1,
        previous[rightIndex] + 1,
        previous[rightIndex - 1] + (left[leftIndex - 1] === right[rightIndex - 1] ? 0 : 1),
      ));
    }
    previous.splice(0, previous.length, ...current);
  }
  return previous[right.length];
};

const trackingWordsMatch = (reference, spoken) => {
  if (reference === spoken) return true;
  const longest = Math.max(reference.length, spoken.length);
  if (longest < 5) return false;
  const allowedDistance = longest >= 9 ? 2 : 1;
  return wordDistance(reference, spoken) <= allowedDistance;
};

const findLiveProgress = (referenceWords, transcript) => {
  const spokenWords = transcript.split(/\s+/).map(normalizeTrackingWord).filter(Boolean);
  const normalizedReference = referenceWords.map(normalizeTrackingWord);
  let referenceCursor = 0;
  for (const spokenWord of spokenWords) {
    const searchEnd = Math.min(normalizedReference.length, referenceCursor + 6);
    let matchIndex = -1;
    for (let index = referenceCursor; index < searchEnd; index += 1) {
      if (trackingWordsMatch(normalizedReference[index], spokenWord)) {
        matchIndex = index;
        break;
      }
    }
    if (matchIndex >= 0) referenceCursor = matchIndex + 1;
  }
  return referenceCursor;
};

const statusClass = {
  correct: 'okundu',
  error: 'hata',
  missing: 'hata',
  repeat: 'tekrar',
  pause: 'duraksama',
  uncertain: 'belirsiz',
};

const statusLabel = {
  correct: 'Doğru',
  error: 'Yanlış',
  missing: 'Atlandı',
  repeat: 'Tekrar',
  pause: 'Uzun duraksama',
  uncertain: 'Emin değil',
};

function MicrophoneIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
      <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z" />
      <path d="M19 10v2a7 7 0 0 1-14 0v-2M12 19v3" />
    </svg>
  );
}

export default function ReadingPanel({ selectedText }) {
  const sentences = useMemo(() => splitText(selectedText.content), [selectedText]);
  const flatWords = useMemo(() => sentences.flat(), [sentences]);
  const offsets = useMemo(() => sentences.map((_, index) => sentences.slice(0, index).reduce((sum, sentence) => sum + sentence.length, 0)), [sentences]);
  const [phase, setPhase] = useState('idle');
  const [message, setMessage] = useState('');
  const [consent, setConsent] = useState(false);
  const [activeSentence, setActiveSentence] = useState(0);
  const [wordResults, setWordResults] = useState(() => Array(flatWords.length).fill(null));
  const [liveWordIndex, setLiveWordIndex] = useState(0);
  const [stats, setStats] = useState(() => emptyStats(selectedText.mentorNote));
  const [analysisWarnings, setAnalysisWarnings] = useState([]);
  const [analysisDetails, setAnalysisDetails] = useState(null);
  const requestControllerRef = useRef(null);

  const submitRecording = useCallback(async (blob) => {
    if (!blob.size) {
      setPhase('error');
      setMessage('Tarayıcı boş bir kayıt üretti; analiz yapılmadı.');
      return;
    }
    setPhase('analyzing');
    setMessage('');
    const controller = new AbortController();
    requestControllerRef.current = controller;
    try {
      const analysis = await analyzeReading({
        blob,
        referenceText: selectedText.content,
        textId: selectedText.id,
        consent,
        signal: controller.signal,
      });
      const results = Array(flatWords.length).fill(null);
      analysis.words.forEach((word) => {
        if (Number.isInteger(word.index) && word.index >= 0 && word.index < results.length) results[word.index] = word;
      });
      const lastResultIndex = results.reduce((last, result, index) => (result ? index : last), 0);
      setWordResults(results);
      setActiveSentence(Math.max(0, offsets.findLastIndex((offset) => offset <= lastResultIndex)));
      setStats({
        hasAnalysis: true,
        accuracy: analysis.accuracyPercent,
        speed: analysis.correctWordsPerMinute,
        pause: analysis.counts.pauses,
        repeat: analysis.counts.repeats,
        error: analysis.counts.errors + analysis.counts.missing + (analysis.counts.extra ?? 0),
        restart: analysis.counts.wordLevelReturns,
        uncertain: analysis.counts.uncertain,
        mentorNote: selectedText.mentorNote,
      });
      setAnalysisWarnings(analysis.warnings ?? []);
      setAnalysisDetails({
        transcript: analysis.transcript,
        referenceLanguage: analysis.referenceLanguage,
        recordingBoundary: analysis.recordingBoundary,
        extraWords: analysis.extraWords ?? [],
        words: analysis.words,
      });
      setPhase('done');
    } catch (error) {
      if (error.name === 'AbortError') return;
      setPhase('error');
      setMessage(error.message || 'Ses analizi tamamlanamadı.');
      setWordResults(Array(flatWords.length).fill(null));
      setStats(emptyStats(selectedText.mentorNote));
      setAnalysisDetails(null);
    } finally {
      requestControllerRef.current = null;
    }
  }, [consent, flatWords.length, offsets, selectedText]);

  const {
    isRecording,
    waveHeights,
    recorderError,
    liveTranscript,
    liveRecognitionSupported,
    startRecording,
    stopRecording,
  } = useAudioRecorder(submitRecording);

  const liveProgress = useMemo(
    () => findLiveProgress(flatWords, liveTranscript),
    [flatWords, liveTranscript],
  );

  useEffect(() => {
    if (!isRecording) return;
    const nextWordIndex = Math.min(liveProgress, Math.max(0, flatWords.length - 1));
    setLiveWordIndex(nextWordIndex);
    const sentenceIndex = offsets.findLastIndex((offset) => offset <= nextWordIndex);
    setActiveSentence(Math.max(0, sentenceIndex));
  }, [flatWords.length, isRecording, liveProgress, offsets]);

  useEffect(() => {
    requestControllerRef.current?.abort();
    setPhase('idle');
    setMessage('');
    setActiveSentence(0);
    setWordResults(Array(flatWords.length).fill(null));
    setStats(emptyStats(selectedText.mentorNote));
    setAnalysisWarnings([]);
    setAnalysisDetails(null);
  }, [flatWords.length, selectedText.id, selectedText.mentorNote]);

  useEffect(() => {
    if (recorderError) {
      setPhase('error');
      setMessage(recorderError);
    }
  }, [recorderError]);

  useEffect(() => () => requestControllerRef.current?.abort(), []);

  const start = async () => {
    if (!consent) {
      setPhase('error');
      setMessage('Kayda başlamadan önce ses işleme iznini onayla.');
      return;
    }
    setWordResults(Array(flatWords.length).fill(null));
    setStats(emptyStats(selectedText.mentorNote));
    setAnalysisWarnings([]);
    setAnalysisDetails(null);
    setActiveSentence(0);
    setLiveWordIndex(0);
    try {
      await startRecording();
      setPhase('recording');
      setMessage('');
    } catch {
      // Hook exposes the user-facing error without fabricating analysis.
    }
  };

  const stop = () => {
    setPhase('analyzing');
    stopRecording();
  };

  const info = phase === 'error'
    ? ['Analiz tamamlanamadı', message]
    : phase === 'done'
      ? ['Analiz tamamlandı', stats.uncertain ? `${stats.uncertain} kelime düşük güven nedeniyle tekrar dinlenmeli.` : 'Sonuçlar gerçek kayıt ve ASR kanıtından üretildi.']
      : phase === 'analyzing'
        ? ['Kayıt çözümleniyor…', 'Kelime sonuçları model tamamlandıktan sonra gösterilecek.']
        : isRecording
          ? ['Dinliyorum…', liveRecognitionSupported
            ? 'Aktif cümle gerçek konuşma tanıma sonucuyla takip ediliyor. Kesin analiz için bitirdiğinde mikrofona yeniden dokun.'
            : 'Tarayıcı canlı konuşma takibini desteklemiyor. Kesin analiz için bitirdiğinde mikrofona yeniden dokun.']
          : ['Hazır olduğunda başla', 'İzni onayla, mikrofona dokun ve metni yüksek sesle oku. Analiz kayıt bitince yapılır.'];

  return (
    <div className="izgara iki okuma-izgara">
      <div>
        <div className="okuma-sahne">
          <div className="metin-ust">
            <h2>{selectedText.title}</h2>
            <span className={`etiket ${selectedText.level.toLocaleLowerCase('tr-TR')}`}>{selectedText.level} · {selectedText.grade}</span>
            <span className="cumle-sayac">Cümle <b id="cumleNo">{Math.min(activeSentence + 1, sentences.length)}</b>/{sentences.length}</span>
          </div>
          <div className="serit" id="serit" aria-live="polite">
            {sentences.map((sentence, sentenceIndex) => (
              <div className={`cumle ${sentenceIndex === activeSentence ? '' : 'pasif'}`} key={`${selectedText.id}-${sentenceIndex}`}>
                {sentence.map((word, wordIndex) => {
                  const globalIndex = offsets[sentenceIndex] + wordIndex;
                  const result = wordResults[globalIndex];
                  const confidence = result?.confidence == null ? '' : ` Güven: %${Math.round(result.confidence * 100)}.`;
                  const comparison = result
                    ? `Beklenen: ${result.reference}. Algılanan: ${result.recognized ?? 'kanıt yok'}.`
                    : '';
                  return (
                    <Fragment key={`${word}-${wordIndex}`}>
                      <span
                        className={`kelime ${result ? statusClass[result.status] ?? 'belirsiz' : ''} ${isRecording && globalIndex === liveWordIndex ? 'aktif' : ''}`}
                        data-c={sentenceIndex}
                        data-k={wordIndex}
                        data-status={result?.status ?? 'pending'}
                        title={result ? `${comparison} ${result.evidence}${confidence}` : undefined}
                      >
                        {word}
                      </span>{' '}
                    </Fragment>
                  );
                })}
              </div>
            ))}
          </div>
          <div>
            <div className="sure-serit" id="sureSerit" aria-label="Kelime okuma süreleri">
              {flatWords.map((word, index) => {
                const result = wordResults[index];
                const barClass = result?.status === 'error' || result?.status === 'missing'
                  ? 'kotu'
                  : result?.status === 'repeat'
                    ? 'tekrar'
                    : result?.status === 'pause'
                      ? 'uzun'
                      : result?.status === 'correct'
                        ? 'ok'
                        : '';
                const duration = result?.effectiveDurationSeconds ?? result?.durationSeconds;
                return <div className={`sure-cubuk ${barClass}`} style={{ height: duration == null ? '6px' : `${Math.min(42, Math.max(7, Math.round(duration * 15)))}px` }} title={result?.evidence ?? word} key={`${word}-${index}`} />;
              })}
            </div>
            <div className="sure-aciklama">
              <span>Her çubuk gerçek kelime/sessizlik süresidir</span>
              <span><b className="renk-dogru">■</b> doğrulandı</span>
              <span><b className="renk-duraksama">■</b> uzun duraksama</span>
              <span><b className="renk-tekrar">■</b> tekrar</span>
              <span><b className="renk-hata">■</b> hata/eksik</span>
            </div>
          </div>
          <div className="kayit-alani">
            <button
              className={`mikrofon ${isRecording ? 'kayitta' : ''}`}
              id="mikBtn"
              type="button"
              disabled={phase === 'analyzing'}
              aria-label={isRecording ? 'Okumayı bitir ve analiz et' : 'Okumayı kaydet'}
              onClick={isRecording ? stop : start}
            >
              <MicrophoneIcon />
            </button>
            <div className="dalga" id="dalga" aria-hidden="true">
              {waveHeights.map((height, index) => <i style={{ height: `${height}px`, background: isRecording ? 'var(--indigo)' : 'var(--cizgi)' }} key={index} />)}
            </div>
            <div className="kayit-bilgi" id="kayitBilgi" aria-live="polite"><strong>{info[0]}</strong>{info[1]}</div>
          </div>
          <label className="kayit-izin">
            <input type="checkbox" checked={consent} onChange={(event) => setConsent(event.target.checked)} />
            <span>Sesimin yalnızca bu okuma analizi için işlenmesine izin veriyorum. Ham kayıt analizden sonra silinir.</span>
          </label>
          {analysisWarnings.length > 0 && <p className="analiz-uyari">{analysisWarnings.join(' ')}</p>}
          {analysisDetails && (
            <details className="analiz-ayrinti">
              <summary>Beklenen ve algılanan kelimeleri incele</summary>
              <p><strong>Model transkripti:</strong> {analysisDetails.transcript || 'Kelime algılanmadı'}</p>
              <p>
                <strong>Kayıt sınırları:</strong>{' '}
                başlangıç sessizliği {analysisDetails.recordingBoundary?.leadingSilenceSeconds ?? '—'} sn ·
                {' '}bitiş sessizliği {analysisDetails.recordingBoundary?.trailingSilenceSeconds ?? '—'} sn
              </p>
              <div className="analiz-tablo-kabuk">
                <table className="analiz-tablo">
                  <thead><tr><th>Beklenen</th><th>Modelin algıladığı</th><th>Durum</th><th>Güven / zaman</th></tr></thead>
                  <tbody>
                    {analysisDetails.words.map((word) => (
                      <tr key={word.index}>
                        <td>{word.reference}</td>
                        <td>{word.recognized ?? '—'}</td>
                        <td><span className={`analiz-durum ${statusClass[word.status] ?? 'belirsiz'}`}>{statusLabel[word.status] ?? word.status}</span></td>
                        <td>{word.confidence == null ? '—' : `%${Math.round(word.confidence * 100)} · ${word.startSeconds}–${word.endSeconds} sn`}</td>
                      </tr>
                    ))}
                    {analysisDetails.extraWords.map((word, index) => (
                      <tr key={`extra-${index}`}>
                        <td>—</td>
                        <td>{word.recognized}</td>
                        <td><span className="analiz-durum hata">{word.status === 'uncertain' ? 'Emin değil' : 'Fazladan'}</span></td>
                        <td>%{Math.round(word.confidence * 100)} · {word.startSeconds}–{word.endSeconds} sn</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </details>
          )}
        </div>
        <div className="blok-ara" />
        <div className="kart">
          <h3 className="kart-baslik">Bu metinde dikkat edeceğin kelimeler</h3>
          <p className="kart-alt">Öğretmenin ekledi · YAZTEK metne yerleştirdi</p>
          <div className="cip-sira">
            {selectedText.focusWords.map((word, index) => (
              <span className={`cip ${index < 2 ? 'duraksama' : ''}`} key={word}><span className="cip-metin">{word}</span> <button type="button" title="Doğru okunuşunu dinle" aria-label={`${word} kelimesinin doğru okunuşunu dinle`}>▶</button></span>
            ))}
          </div>
        </div>
      </div>
      <ReadingMetrics stats={stats} />
    </div>
  );
}
