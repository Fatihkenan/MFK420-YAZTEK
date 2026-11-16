import { useCallback, useEffect, useRef, useState } from 'react';

const idleWave = () => Array(26).fill(6);

const chooseMimeType = () => [
  'audio/webm;codecs=opus',
  'audio/ogg;codecs=opus',
  'audio/mp4',
].find((type) => window.MediaRecorder?.isTypeSupported?.(type)) ?? '';

export default function useAudioRecorder(onRecordingReady, language = 'tr-TR', maxDurationMs = 180_000) {
  const [isRecording, setIsRecording] = useState(false);
  const [waveHeights, setWaveHeights] = useState(idleWave);
  const [recorderError, setRecorderError] = useState('');
  const [liveTranscript, setLiveTranscript] = useState('');
  const recorderRef = useRef(null);
  const streamRef = useRef(null);
  const audioContextRef = useRef(null);
  const recognitionRef = useRef(null);
  const recognitionRestartRef = useRef(null);
  const recognitionShouldRunRef = useRef(false);
  const committedTranscriptRef = useRef('');
  const recognitionSessionRef = useRef('');
  const animationRef = useRef(null);
  const timeoutRef = useRef(null);
  const callbackRef = useRef(onRecordingReady);
  callbackRef.current = onRecordingReady;

  const cleanup = useCallback(() => {
    if (animationRef.current) window.cancelAnimationFrame(animationRef.current);
    window.clearTimeout(timeoutRef.current);
    animationRef.current = null;
    timeoutRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') audioContextRef.current.close();
    audioContextRef.current = null;
    recognitionShouldRunRef.current = false;
    window.clearTimeout(recognitionRestartRef.current);
    recognitionRestartRef.current = null;
    const recognition = recognitionRef.current;
    recognitionRef.current = null;
    if (recognition) {
      recognition.onend = null;
      try {
        recognition.abort();
      } catch {
        // Tanıma süreci zaten kapanmış olabilir.
      }
    }
    setWaveHeights(idleWave());
  }, []);

  const stopRecording = useCallback(() => {
    recognitionShouldRunRef.current = false;
    window.clearTimeout(recognitionRestartRef.current);
    try {
      recognitionRef.current?.stop();
    } catch {
      // Tanıma henüz başlamadan kayıt durdurulmuş olabilir.
    }
    const recorder = recorderRef.current;
    if (recorder?.state === 'recording') recorder.stop();
  }, []);

  const startLiveRecognition = useCallback(() => {
    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SpeechRecognition) return;

    committedTranscriptRef.current = '';
    recognitionSessionRef.current = '';
    setLiveTranscript('');
    recognitionShouldRunRef.current = true;
    const recognition = new SpeechRecognition();
    recognition.lang = language;
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;
    recognitionRef.current = recognition;

    recognition.onresult = (event) => {
      const sessionTranscript = Array.from(event.results)
        .map((result) => result[0]?.transcript ?? '')
        .join(' ')
        .trim();
      recognitionSessionRef.current = sessionTranscript;
      setLiveTranscript(`${committedTranscriptRef.current} ${sessionTranscript}`.trim());
    };
    recognition.onerror = (event) => {
      if (!['no-speech', 'aborted'].includes(event.error)) recognitionShouldRunRef.current = false;
    };
    recognition.onend = () => {
      if (!recognitionShouldRunRef.current) return;
      committedTranscriptRef.current = `${committedTranscriptRef.current} ${recognitionSessionRef.current}`.trim();
      recognitionSessionRef.current = '';
      recognitionRestartRef.current = window.setTimeout(() => {
        if (!recognitionShouldRunRef.current) return;
        try {
          recognition.start();
        } catch {
          recognitionShouldRunRef.current = false;
        }
      }, 150);
    };

    try {
      recognition.start();
    } catch {
      recognitionShouldRunRef.current = false;
      recognitionRef.current = null;
    }
  }, [language]);

  const startRecording = useCallback(async () => {
    setRecorderError('');
    if (!navigator.mediaDevices?.getUserMedia || !window.MediaRecorder) {
      const message = 'Bu tarayıcı MediaRecorder ve mikrofon erişimini desteklemiyor.';
      setRecorderError(message);
      throw new Error(message);
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
      });
      streamRef.current = stream;
      const AudioContext = window.AudioContext || window.webkitAudioContext;
      if (!AudioContext) throw new Error('Web Audio API desteklenmiyor.');
      const audioContext = new AudioContext();
      audioContextRef.current = audioContext;
      const source = audioContext.createMediaStreamSource(stream);
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 512;
      analyser.smoothingTimeConstant = 0.72;
      source.connect(analyser);
      const samples = new Uint8Array(analyser.fftSize);
      let lastPaint = 0;

      const drawWave = (timestamp) => {
        analyser.getByteTimeDomainData(samples);
        if (timestamp - lastPaint >= 70) {
          const step = Math.max(1, Math.floor(samples.length / 26));
          const heights = Array.from({ length: 26 }, (_, index) => {
            const amplitude = Math.abs(samples[index * step] - 128) / 128;
            return 6 + Math.min(32, amplitude * 95);
          });
          setWaveHeights(heights);
          lastPaint = timestamp;
        }
        animationRef.current = window.requestAnimationFrame(drawWave);
      };
      animationRef.current = window.requestAnimationFrame(drawWave);

      const mimeType = chooseMimeType();
      const recorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
      const chunks = [];
      recorderRef.current = recorder;
      recorder.ondataavailable = (event) => {
        if (event.data?.size) chunks.push(event.data);
      };
      recorder.onerror = () => {
        setRecorderError('Tarayıcı ses kaydını tamamlayamadı.');
        setIsRecording(false);
        cleanup();
      };
      recorder.onstop = () => {
        const blob = new Blob(chunks, { type: recorder.mimeType || mimeType || 'audio/webm' });
        recorderRef.current = null;
        setIsRecording(false);
        cleanup();
        callbackRef.current(blob);
      };
      recorder.start(250);
      setIsRecording(true);
      startLiveRecognition();
      timeoutRef.current = window.setTimeout(stopRecording, maxDurationMs);
    } catch (error) {
      cleanup();
      const permissionDenied = error?.name === 'NotAllowedError' || error?.name === 'SecurityError';
      const message = permissionDenied
        ? 'Mikrofon izni verilmedi. Tarayıcı ayarlarından erişimi aç.'
        : error?.message || 'Mikrofon başlatılamadı.';
      setRecorderError(message);
      throw new Error(message);
    }
  }, [cleanup, maxDurationMs, startLiveRecognition, stopRecording]);

  useEffect(() => () => {
    const recorder = recorderRef.current;
    recorderRef.current = null;
    if (recorder?.state === 'recording') recorder.stop();
    cleanup();
  }, [cleanup]);

  return {
    isRecording,
    waveHeights,
    recorderError,
    liveTranscript,
    liveRecognitionSupported: Boolean(window.SpeechRecognition || window.webkitSpeechRecognition),
    startRecording,
    stopRecording,
  };
}

