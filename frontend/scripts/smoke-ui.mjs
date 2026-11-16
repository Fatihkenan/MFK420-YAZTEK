import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { pathToFileURL } from 'node:url';

const buildIndex = resolve('dist/index.html');
const appUrl = process.env.YAZTEK_UI_URL ?? pathToFileURL(buildIndex).href;
const referencePath = process.env.YAZTEK_REFERENCE_HTML;
const chromePath = process.env.CHROME_PATH ?? 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const debuggingPort = 10_000 + (process.pid % 40_000);
const profileDirectory = mkdtempSync(join(tmpdir(), 'yaztek-chrome-'));
const screenshots = {
  desktop: join(tmpdir(), 'yaztek-desktop.png'),
  mobile: join(tmpdir(), 'yaztek-mobile.png'),
  readingReact: join(tmpdir(), 'yaztek-reading-react.png'),
  readingReference: join(tmpdir(), 'yaztek-reading-reference.png'),
  taskReact: join(tmpdir(), 'yaztek-task-react.png'),
  taskReference: join(tmpdir(), 'yaztek-task-reference.png'),
  comprehensionReact: join(tmpdir(), 'yaztek-comprehension-react.png'),
  comprehensionReference: join(tmpdir(), 'yaztek-comprehension-reference.png'),
  overflowDesktop: join(tmpdir(), 'yaztek-overflow-desktop.png'),
  overflowMobile: join(tmpdir(), 'yaztek-overflow-mobile.png'),
};

const normalTestText = 'Yapay zekâ günlük yaşamın birçok alanında kullanılmaktadır.';
const longTestWord = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const longTestParagraph = 'Yapay zekâ günlük yaşamın birçok alanında kullanılmaktadır. Öğrenciler okuma sırasında kelimeleri dikkatle takip eder ve öğretmenlerinden aldıkları geri bildirimlerle akıcı okuma becerilerini geliştirir. Bu uzun paragraf, kullanıcı tarafından eklenen içeriğin kart sınırları içinde güvenle satır kırdığını doğrulamak için kullanılır.';

const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
  console.log(`✓ ${message}`);
};

let chrome;
let socket;

try {
  if (appUrl.startsWith('http')) {
    const response = await fetch(appUrl);
    assert(response.ok, `Frontend HTTP ${response.status} ile yanıt veriyor`);
  } else {
    assert(existsSync(buildIndex), 'Üretim index.html çıktısı bulunuyor');
  }

  chrome = spawn(chromePath, [
    '--headless=new',
    '--disable-gpu',
    '--allow-file-access-from-files',
    '--no-first-run',
    '--no-default-browser-check',
    `--remote-debugging-port=${debuggingPort}`,
    `--user-data-dir=${profileDirectory}`,
    'about:blank',
  ], { stdio: 'ignore' });

  let pageTarget;
  for (let attempt = 0; attempt < 40 && !pageTarget; attempt += 1) {
    try {
      const targets = await fetch(`http://127.0.0.1:${debuggingPort}/json/list`).then((result) => result.json());
      pageTarget = targets.find((target) => target.type === 'page');
    } catch {
      await delay(150);
    }
  }
  assert(pageTarget, 'Headless Chrome test hedefi açıldı');

  socket = new WebSocket(pageTarget.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });

  let sequence = 0;
  const pending = new Map();
  socket.addEventListener('message', ({ data }) => {
    const message = JSON.parse(data);
    if (!message.id || !pending.has(message.id)) return;
    const { resolve, reject } = pending.get(message.id);
    pending.delete(message.id);
    if (message.error) reject(new Error(message.error.message));
    else resolve(message.result);
  });

  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async (expression) => {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
  };
  const waitFor = async (expression, attempts = 40) => {
    for (let attempt = 0; attempt < attempts; attempt += 1) {
      if (await evaluate(expression)) return true;
      await delay(100);
    }
    return false;
  };
  const clickButton = async (text) => {
    const clicked = await evaluate(`(() => {
      const button = [...document.querySelectorAll('button')].find((item) => item.textContent.trim() === ${JSON.stringify(text)});
      if (!button) return false;
      button.click();
      return true;
    })()`);
    assert(clicked, `“${text}” düğmesi çalışıyor`);
    await delay(80);
  };
  const clickTask = async (title) => {
    const clicked = await evaluate(`(() => {
      const heading = [...document.querySelectorAll('.gorev h4')].find((item) => item.textContent.trim() === ${JSON.stringify(title)});
      const button = heading?.closest('button');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    assert(clicked, `“${title}” görevi çalışıyor`);
    await delay(80);
  };
  const loginPortal = async (role, name) => {
    await waitFor("document.body?.innerText.includes('Öğrenci girişi') || document.body?.innerText.includes('Öğretmen girişi')");
    if (role === 'mentor') await clickButton('Öğretmen');
    const submitted = await evaluate(`(() => {
      const input = document.querySelector('.giris-karti input');
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, ${JSON.stringify(name)});
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.closest('form').requestSubmit();
      return true;
    })()`);
    assert(submitted, `${role === 'mentor' ? 'Öğretmen' : 'Öğrenci'} girişi gönderiliyor`);
    await waitFor(`document.querySelector('.oturum-bilgisi strong')?.textContent === ${JSON.stringify(name)}`);
  };
  const readingStyleSnapshot = `(() => {
    const readingGrid = document.querySelector('.okuma-sahne').closest('.iki');
    const properties = (target, names, pseudo = null) => {
      const element = typeof target === 'string' ? document.querySelector(target) : target;
      const style = getComputedStyle(element, pseudo);
      return Object.fromEntries(names.map((name) => [name, style[name]]));
    };
    const root = getComputedStyle(document.documentElement);
    const normalizeColor = (value) => {
      const color = value.trim().toLowerCase();
      return /^#[0-9a-f]{3}$/.test(color)
        ? '#' + [...color.slice(1)].map((part) => part + part).join('')
        : color;
    };
    return {
      variables: Object.fromEntries(['--kagit','--yuzey','--cizgi','--murekkep','--indigo','--dogru','--duraksama','--hata','--tekrar'].map((name) => [name, normalizeColor(root.getPropertyValue(name))])),
      grid: properties(readingGrid, ['gridTemplateColumns']),
      sentence: properties('.cumle', ['fontFamily','fontSize','fontWeight','lineHeight','letterSpacing','paddingBottom']),
      passive: properties('.cumle.pasif', ['opacity','fontSize','lineHeight']),
      underline: properties('.kelime', ['left','right','bottom','height','borderRadius','backgroundImage','backgroundSize'], '::after'),
      durationTrack: properties('.sure-serit', ['display','alignItems','gap','height','padding','borderBottomWidth','borderBottomStyle','borderBottomColor']),
      durationBar: properties('.sure-cubuk', ['minWidth','height','backgroundColor','borderRadius','transitionDuration']),
      recording: properties('.kayit-alani', ['display','alignItems','gap','marginTop','flexWrap']),
      microphone: properties('.mikrofon', ['width','height','borderRadius','backgroundColor']),
      wave: properties('.dalga', ['display','alignItems','gap','height','minWidth']),
      metric: properties(readingGrid.querySelector('.metrik'), ['padding','borderRadius','borderWidth','borderColor']),
      metricNumber: properties(readingGrid.querySelector('.metrik dd'), ['fontFamily','fontSize','fontWeight','lineHeight','marginTop']),
      progress: properties(readingGrid.querySelector('.olcek'), ['height','borderRadius','marginTop'])
    };
  })()`;
  const taskStyleSnapshot = `(() => {
    const task = document.querySelector('.gorev');
    const mainCard = task.closest('.kart');
    const properties = (target, names) => {
      const style = getComputedStyle(target);
      return Object.fromEntries(names.map((name) => [name, style[name]]));
    };
    return {
      mainCard: properties(mainCard, ['padding','borderWidth','borderColor','borderRadius','boxShadow']),
      heading: properties(mainCard.querySelector('.kart-baslik'), ['fontFamily','fontSize','fontWeight','marginBottom']),
      subtitle: properties(mainCard.querySelector('.kart-alt'), ['fontSize','color','marginBottom']),
      taskList: properties(mainCard.querySelector('.izgara'), ['display','gap']),
      task: properties(task, ['display','gap','alignItems','padding','borderWidth','borderColor','borderRadius','backgroundColor','transitionDuration']),
      icon: properties(task.querySelector('.gorev-ikon'), ['width','height','borderRadius','display','fontSize','backgroundColor','flex']),
      taskTitle: properties(task.querySelector('h4'), ['fontFamily','fontSize','fontWeight']),
      taskText: properties(task.querySelector('p'), ['fontSize','color']),
      tag: properties(task.querySelector('.etiket'), ['fontSize','fontWeight','padding','borderRadius','backgroundColor','color'])
    };
  })()`;
  const comprehensionStyleSnapshot = `(() => {
    const grid = document.querySelector('.soru').closest('.iki');
    const scoreCard = [...grid.querySelectorAll('.kart')].find((card) => card.querySelector('.kart-baslik')?.textContent === 'Anlama puanın');
    const properties = (target, names) => {
      const style = getComputedStyle(target);
      return Object.fromEntries(names.map((name) => [name, style[name]]));
    };
    return {
      grid: properties(grid, ['gridTemplateColumns','gap']),
      question: properties(grid.querySelector('.soru'), ['borderWidth','borderColor','borderRadius','padding','marginBottom']),
      questionText: properties(grid.querySelector('.soru p'), ['fontFamily','fontSize','fontWeight','marginBottom']),
      options: properties(grid.querySelector('.secenekler'), ['display','gap']),
      option: properties(grid.querySelector('.secenek'), ['fontFamily','fontSize','fontWeight','padding','borderWidth','borderColor','borderRadius','backgroundColor']),
      scoreCard: properties(scoreCard, ['alignSelf','padding','borderRadius']),
      scoreMetric: properties(scoreCard.querySelector('.metrik'), ['borderWidth','padding']),
      metricNumber: properties(scoreCard.querySelector('.metrik dd'), ['fontFamily','fontSize','fontWeight','lineHeight','marginTop']),
      progress: properties(scoreCard.querySelector('.olcek'), ['height','borderRadius','marginTop'])
    };
  })()`;
  const overflowSnapshot = (content) => `(() => {
    const grid = document.querySelector('.okuma-izgara');
    const left = grid.children[0];
    const right = grid.children[1];
    const scene = grid.querySelector('.okuma-sahne');
    const title = grid.querySelector('.metin-ust h2');
    const word = grid.querySelector('.kelime');
    const chip = grid.querySelector('.cip-metin');
    const note = grid.querySelector('.mentor-metni');
    window.__readingOverflowOriginal ??= {
      title: title.textContent,
      word: word.textContent,
      chip: chip.textContent,
      note: note.textContent
    };
    title.textContent = ${JSON.stringify(content)};
    word.textContent = ${JSON.stringify(content)};
    chip.textContent = ${JSON.stringify(content)};
    note.textContent = ${JSON.stringify(content)};
    const leftRect = left.getBoundingClientRect();
    const rightRect = right.getBoundingClientRect();
    const sceneRect = scene.getBoundingClientRect();
    const wordRect = word.getBoundingClientRect();
    return {
      viewportSafe: document.documentElement.scrollWidth <= document.documentElement.clientWidth,
      leftSafe: left.scrollWidth <= left.clientWidth + 1,
      sceneSafe: scene.scrollWidth <= scene.clientWidth + 1,
      wordSafe: word.scrollWidth <= word.clientWidth + 1 && wordRect.right <= sceneRect.right + 1,
      columnsSeparate: innerWidth < 900 || leftRect.right <= rightRect.left,
      rightSafe: rightRect.right <= document.documentElement.clientWidth + 1,
      complete: title.textContent === ${JSON.stringify(content)} && word.textContent === ${JSON.stringify(content)} && chip.textContent === ${JSON.stringify(content)} && note.textContent === ${JSON.stringify(content)},
      dotted: getComputedStyle(word, '::after').backgroundImage.includes('radial-gradient'),
      wordHeight: wordRect.height,
      lineHeight: parseFloat(getComputedStyle(word).lineHeight)
    };
  })()`;
  const restoreOverflowContent = `(() => {
    const original = window.__readingOverflowOriginal;
    if (!original) return false;
    document.querySelector('.metin-ust h2').textContent = original.title;
    document.querySelector('.kelime').textContent = original.word;
    document.querySelector('.cip-metin').textContent = original.chip;
    document.querySelector('.mentor-metni').textContent = original.note;
    delete window.__readingOverflowOriginal;
    return true;
  })()`;

  await send('Page.enable');
  await send('Runtime.enable');
  await send('Page.addScriptToEvaluateOnNewDocument', { source: `
    const originalFetch = window.fetch.bind(window);
    window.fetch = async (input, init) => {
      if (String(input).endsWith('/api/reading-analysis')) {
        const statuses = ['correct','correct','pause','correct','repeat','error','uncertain'];
        const words = Array.from({ length: 24 }, (_, index) => ({
          index,
          reference: 'test',
          recognized: index === 5 ? 'yanlış' : 'test',
          status: statuses[index] || 'correct',
          confidence: index === 6 ? 0.51 : 0.94,
          startSeconds: index * 0.45,
          endSeconds: index * 0.45 + 0.36,
          durationSeconds: 0.36,
          pauseBeforeSeconds: index === 2 ? 1.8 : 0.09,
          effectiveDurationSeconds: index === 2 ? 2.16 : 0.45,
          evidence: 'UI sözleşme testi için sahte API yanıtı'
        }));
        return new Response(JSON.stringify({
          analysisId: '00000000-0000-0000-0000-000000000001',
          transcript: 'test',
          modelId: 'ui-contract-mock',
          words,
          accuracyPercent: 91.7,
          correctWordsPerMinute: 73,
          counts: { pauses: 1, repeats: 1, errors: 1, missing: 0, uncertain: 1, wordLevelReturns: null, sentenceLevelReturns: null },
          warnings: ['Bu yalnızca tarayıcı sözleşme testindeki kontrollü API yanıtıdır.']
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (String(input).endsWith('/api/comprehension/attempts')) {
        const skills = ['main_idea','key_detail','cause_effect','inference','character_event_relationship','context_meaning','general_evaluation'];
        return new Response(JSON.stringify({
          id: '10000000-0000-0000-0000-000000000001', textId: 'kirlangiclar', textTitle: 'Kırlangıçlar', language: 'tr',
          status: 'Ready', modelId: 'ui-contract-mock', totalScore: null, maxScore: 7, error: null,
          questions: Array.from({ length: 7 }, (_, index) => ({
            id: '20000000-0000-0000-0000-' + String(index + 1).padStart(12, '0'), order: index + 1,
            type: index < 4 ? 'multiple_choice' : 'short_answer', skill: skills[index], difficulty: 'medium',
            prompt: 'Metne dayalı dinamik soru ' + (index + 1) + ' nedir?',
            options: index < 4 ? ['Doğru seçenek', 'Çeldirici A', 'Çeldirici B', 'Çeldirici C'] : []
          }))
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (String(input).includes('/api/comprehension/attempts/') && String(input).endsWith('/submit')) {
        return new Response(JSON.stringify({
          attemptId: '10000000-0000-0000-0000-000000000001', totalScore: 7, maxScore: 7, isFinal: true, reviewRequiredCount: 0,
          answers: Array.from({ length: 7 }, (_, index) => ({
            questionId: '20000000-0000-0000-0000-' + String(index + 1).padStart(12, '0'), status: 'Correct',
            score: 1, confidence: .95, feedback: 'Doğru.', explanation: 'Metindeki bilgiyle uyuşuyor.',
            evidence: 'Bahçedeki erik ağacına', evidenceVerified: true,
            correctOptionIndex: index < 4 ? 0 : null, referenceAnswer: 'Doğru yanıt'
          })),
          skills: Array.from({ length: 7 }, (_, index) => ({ skill: ['main_idea','key_detail','cause_effect','inference','character_event_relationship','context_meaning','general_evaluation'][index], score: 1, maximum: 1 }))
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      return originalFetch(input, init);
    };
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: async () => ({ getTracks: () => [{ stop() {} }] }) }
    });
    class MockAnalyser {
      constructor() { this.fftSize = 512; this.smoothingTimeConstant = 0; }
      getByteTimeDomainData(samples) {
        for (let index = 0; index < samples.length; index += 1) samples[index] = index % 2 ? 168 : 88;
      }
    }
    class MockAudioContext {
      constructor() { this.state = 'running'; }
      createMediaStreamSource() { return { connect() {} }; }
      createAnalyser() { return new MockAnalyser(); }
      close() { this.state = 'closed'; return Promise.resolve(); }
    }
    class MockMediaRecorder {
      static isTypeSupported() { return true; }
      constructor() { this.state = 'inactive'; this.mimeType = 'audio/webm;codecs=opus'; }
      start() { this.state = 'recording'; }
      stop() {
        if (this.state !== 'recording') return;
        this.state = 'inactive';
        this.ondataavailable?.({ data: new Blob(['recorded-audio'], { type: this.mimeType }) });
        setTimeout(() => this.onstop?.(), 0);
      }
    }
    class MockSpeechRecognition {
      constructor() {
        this.continuous = false;
        this.interimResults = false;
        this.maxAlternatives = 1;
      }
      start() {
        this.resultTimer = setTimeout(() => this.onresult?.({
          results: [[{ transcript: 'Bahçedeki erik ağacına iki kırlangıç yuva yapmış Yavrular' }]]
        }), 40);
      }
      stop() { clearTimeout(this.resultTimer); this.onend?.(); }
      abort() { clearTimeout(this.resultTimer); }
    }
    window.AudioContext = MockAudioContext;
    window.MediaRecorder = MockMediaRecorder;
    window.SpeechRecognition = MockSpeechRecognition;
    window.webkitSpeechRecognition = MockSpeechRecognition;
    window.confirm = () => true;
  ` });
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: appUrl });
  await loginPortal('ogrenci', 'Elif');
  await waitFor("document.body?.innerText.includes('Merhaba Elif')");
  assert(await evaluate("document.querySelector('.oturum-bilgisi strong').textContent === 'Elif'"), 'Öğrenci adı yerel oturumda gösteriliyor');
  await clickButton('Okuma');
  await waitFor("document.body?.innerText.includes('Kırlangıçlar')");
  await evaluate('document.fonts.ready.then(() => true)');

  assert(await evaluate("document.body.innerText.includes('Kırlangıçlar')"), 'Okuma ekranı varsayılan içerikle açılıyor');
  const readingStructure = await evaluate(`(() => ({
    words: document.querySelectorAll('.kelime').length,
    bars: document.querySelectorAll('.sure-cubuk').length,
    waves: document.querySelectorAll('.dalga i').length,
    spacesOutside: [...document.querySelectorAll('.kelime')].every((word) => word.nextSibling?.nodeType === Node.TEXT_NODE && word.nextSibling.textContent === ' '),
    dottedPerWord: [...document.querySelectorAll('.kelime')].every((word) => getComputedStyle(word, '::after').backgroundImage.includes('radial-gradient')),
    passiveOpacity: getComputedStyle(document.querySelector('.cumle.pasif')).opacity,
    micWidth: getComputedStyle(document.querySelector('.mikrofon')).width,
    micHeight: getComputedStyle(document.querySelector('.mikrofon')).height
  }))()`);
  assert(readingStructure.words === 24 && readingStructure.spacesOutside && readingStructure.dottedPerWord, '24 kelime ayrı öğe ve ayrı noktalı alt çizgiyle oluşturuluyor');
  assert(readingStructure.bars === 24, 'Her kelime için ayrı süre çubuğu bulunuyor');
  assert(readingStructure.waves === 26 && readingStructure.micWidth === '74px' && readingStructure.micHeight === '74px', 'Mikrofon 74×74 px ve waveform 26 çubuk');
  assert(readingStructure.passiveOpacity === '0.28', 'Pasif cümle opaklığı referansla eşleşiyor');
  const reactReadingStyles = await evaluate(readingStyleSnapshot);
  const readingShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.readingReact, Buffer.from(readingShot.data, 'base64'));
  for (const [label, content] of [['normal cümle', normalTestText], ['çok uzun tek kelime', longTestWord], ['uzun normal paragraf', longTestParagraph]]) {
    const overflow = await evaluate(overflowSnapshot(content));
    assert(overflow.viewportSafe && overflow.leftSafe && overflow.sceneSafe && overflow.wordSafe && overflow.columnsSeparate && overflow.rightSafe && overflow.complete, `${label} masaüstünde kart ve viewport sınırları içinde kalıyor`);
    assert(overflow.dotted, `${label} kelime bazlı noktalı alt çizgiyi koruyor`);
    if (label === 'çok uzun tek kelime') assert(overflow.wordHeight > overflow.lineHeight, 'Çok uzun tek kelime kesilmeden birden fazla satıra kırılıyor');
  }
  await evaluate(overflowSnapshot(longTestWord));
  const overflowDesktopShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.overflowDesktop, Buffer.from(overflowDesktopShot.data, 'base64'));
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  const mobileOverflow = await evaluate(overflowSnapshot(longTestWord));
  assert(mobileOverflow.viewportSafe && mobileOverflow.leftSafe && mobileOverflow.sceneSafe && mobileOverflow.wordSafe && mobileOverflow.rightSafe && mobileOverflow.complete, 'Çok uzun tek kelime mobilde yatay taşma oluşturmuyor');
  const overflowMobileShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.overflowMobile, Buffer.from(overflowMobileShot.data, 'base64'));
  await evaluate(restoreOverflowContent);
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
  await clickButton('Görevlerim');
  assert(await evaluate("document.body.innerText.includes('Merhaba Elif')"), 'Görevlerim paneli görüntüleniyor');
  const taskStructure = await evaluate(`(() => {
    const grid = document.querySelector('.gorev').closest('.kart');
    return {
      tasks: grid.querySelectorAll('.gorev').length,
      icons: grid.querySelectorAll('.gorev-ikon').length,
      hasRead: [...grid.querySelectorAll('.gorev h4')].some((item) => item.textContent === 'Kırlangıçlar metnini oku'),
      hasQuestions: [...grid.querySelectorAll('.gorev h4')].some((item) => item.textContent === 'Kırlangıçlar sorularını çöz'),
      hasRandomRepeat: grid.textContent.includes('4 kelimeyi tekrar et'),
      hasRandomGoal: grid.textContent.includes('Bu haftaki hedefin')
    };
  })()`);
  assert(taskStructure.tasks === 2 && taskStructure.icons === 2 && taskStructure.hasRead && taskStructure.hasQuestions, 'Her atanmış metin için okuma ve anlama görevi gösteriliyor');
  assert(!taskStructure.hasRandomRepeat && !taskStructure.hasRandomGoal, 'Sabit ve rastgele görevler Görevlerim ekranından kaldırıldı');
  const reactTaskStyles = await evaluate(taskStyleSnapshot);
  const taskShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.taskReact, Buffer.from(taskShot.data, 'base64'));
  await clickTask('Kırlangıçlar sorularını çöz');
  assert(await evaluate("document.body.innerText.includes('Kırlangıçlar metnine göre 4 çoktan seçmeli ve 3 kısa yanıt sorusu.')"), 'Metne ait anlama görevi doğru ekrana yönlendiriyor');
  await clickButton('Görevlerim');
  await clickTask('Kırlangıçlar metnini oku');
  await evaluate("document.querySelector('.kayit-izin input').click()");
  await evaluate("document.querySelector('.mikrofon').click()");
  await delay(180);
  assert(await evaluate("document.querySelector('.mikrofon').classList.contains('kayitta') && document.body.innerText.includes('Dinliyorum')"), 'Mikrofon kaydı dinleme durumuna geçiyor');
  assert(await evaluate("document.querySelector('#cumleNo').textContent === '2' && document.querySelector('.cumle:not(.pasif)') === document.querySelectorAll('.cumle')[1]"), 'Gerçek konuşma ara sonucu aktif cümleyi ilerletiyor');
  const liveEvaluation = await evaluate(`(() => ({
    assessed: document.querySelectorAll('.kelime.okundu,.kelime.hata,.kelime.duraksama,.kelime.tekrar,.kelime.belirsiz').length,
    coloredBars: document.querySelectorAll('.sure-cubuk.ok,.sure-cubuk.uzun,.sure-cubuk.kotu,.sure-cubuk.tekrar').length,
    movingWaves: [...document.querySelectorAll('.dalga i')].some((bar) => parseFloat(bar.style.height) > 6),
    firstMetric: document.querySelector('.metrik dd').textContent.trim()
  }))()`);
  assert(liveEvaluation.assessed === 0 && liveEvaluation.coloredBars === 0 && liveEvaluation.firstMetric.startsWith('—'), 'Kayıt sırasında kelime analizi ve metrik uydurulmuyor');
  assert(liveEvaluation.movingWaves, 'Waveform kayıt sırasında Web Audio seviyesinden güncelleniyor');
  await evaluate("document.querySelector('.mikrofon').click()");
  assert(await waitFor("document.body.innerText.includes('Analiz tamamlandı')"), 'Kayıt bitince API analizi tamamlanıyor');
  const readingEvaluation = await evaluate(`(() => ({
    correct: document.querySelectorAll('.kelime.okundu').length,
    errors: document.querySelectorAll('.kelime.hata').length,
    pauses: document.querySelectorAll('.kelime.duraksama').length,
    repeats: document.querySelectorAll('.kelime.tekrar').length,
    uncertain: document.querySelectorAll('.kelime.belirsiz').length,
    coloredBars: document.querySelectorAll('.sure-cubuk.ok,.sure-cubuk.uzun,.sure-cubuk.kotu,.sure-cubuk.tekrar').length,
    firstMetric: document.querySelector('.metrik dd').textContent.trim()
  }))()`);
  assert(readingEvaluation.correct > 0 && readingEvaluation.errors === 1 && readingEvaluation.pauses === 1 && readingEvaluation.repeats === 1 && readingEvaluation.uncertain === 1, 'API kanıtı kelime durum sınıflarına aktarılıyor');
  assert(readingEvaluation.coloredBars === 23 && readingEvaluation.firstMetric.startsWith('91.7'), 'API süreleri ve ölçülmüş metrikler kayıt bittikten sonra gösteriliyor; belirsiz kelime kesin renge boyanmıyor');
  assert(await evaluate("document.querySelector('.analiz-ayrinti') && document.querySelectorAll('.analiz-tablo tbody tr').length > 0"), 'Beklenen ve algılanan kelime kanıtları inceleme alanında gösteriliyor');

  await clickButton('Anlama soruları');
  await clickButton('7 soruluk testi hazırla');
  assert(await waitFor("document.querySelectorAll('.soru').length === 7"), 'Anlama ekranı tam 7 dinamik soru gösteriyor');
  assert(await evaluate("document.querySelectorAll('.secenek').length === 16 && document.querySelectorAll('.anlama-yanit').length === 3"), 'Dört adet dört seçenekli ve üç açık uçlu soru gösteriliyor');
  const reactComprehensionStyles = await evaluate(comprehensionStyleSnapshot);
  await evaluate(`(() => {
    document.querySelectorAll('.secenekler').forEach((group) => group.querySelector('.secenek').click());
    document.querySelectorAll('.anlama-yanit').forEach((area) => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(area, 'Metne dayalı kısa yanıt');
      area.dispatchEvent(new Event('input', { bubbles: true }));
    });
    return true;
  })()`);
  assert(await evaluate("document.querySelectorAll('.secenek.dogru,.secenek.yanlis').length === 0 && document.querySelector('#anlamaSkor').textContent.includes('/ 7')"), 'Gönderimden önce cevap anahtarı gösterilmiyor ve sayaç 7 üzerinden');
  await clickButton('Cevapları gönder');
  assert(await waitFor("document.querySelectorAll('.secenek.dogru').length === 4"), 'Gönderimden sonra cevap anahtarı ve geri bildirim gösteriliyor');
  const comprehensionShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.comprehensionReact, Buffer.from(comprehensionShot.data, 'base64'));

  await clickButton('Çıkış');
  await loginPortal('mentor', 'Ayşe Öğretmen');
  await evaluate(`(() => {
    const form = document.querySelectorAll('.yonetim-formu')[0];
    const inputs = form.querySelectorAll('input');
    const setValue = (input, value) => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    };
    setValue(inputs[0], 'Mert A.');
    setValue(inputs[1], '3-B');
    setValue(inputs[2], '9');
    form.requestSubmit();
    return true;
  })()`);
  await delay(80);
  assert(await evaluate("document.querySelectorAll('.ogrenci-satir').length === 1 && document.body.innerText.includes('Mert A.')"), 'Mentör kendi öğrencisini oluşturabiliyor');
  await evaluate("document.querySelector('.anahtar').click()");
  assert(await evaluate("document.querySelector('.anahtar').getAttribute('aria-pressed') === 'false' && document.body.innerText.includes('Rapor paylaşım izni kapalı')"), 'Rapor izin anahtarı çalışıyor');
  await evaluate(`(() => {
    const form = document.querySelectorAll('.yonetim-formu')[1];
    const inputs = form.querySelectorAll('input');
    const areas = form.querySelectorAll('textarea');
    const setInput = (input, value, prototype) => {
      Object.getOwnPropertyDescriptor(prototype, 'value').set.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    };
    setInput(inputs[0], 'Ormandaki Dostlar', HTMLInputElement.prototype);
    setInput(inputs[1], '3. sınıf', HTMLInputElement.prototype);
    setInput(areas[0], 'Sincap ağaca çıktı. Kuşlar neşeyle öttü.', HTMLTextAreaElement.prototype);
    setInput(inputs[2], 'sincap, neşeyle', HTMLInputElement.prototype);
    setInput(areas[1], 'Sakin ve anlaşılır oku.', HTMLTextAreaElement.prototype);
    form.requestSubmit();
    return true;
  })()`);
  await delay(80);
  assert(await evaluate("document.body.innerText.includes('Metin oluşturuldu')"), 'Mentör yeni okuma metni oluşturabiliyor');
  await clickButton('Çıkış');
  await loginPortal('ogrenci', 'Elif');
  await clickButton('Görevlerim');
  assert(await evaluate("document.body.innerText.includes('Ormandaki Dostlar metnini oku')"), 'Yeni metin Görevlerim ekranında görünüyor');
  await evaluate("[...document.querySelectorAll('.gorev-buton')].find((item) => item.textContent.includes('Ormandaki Dostlar')).click()");
  await delay(80);
  assert(await evaluate("document.querySelector('.metin-ust h2').textContent === 'Ormandaki Dostlar' && document.querySelectorAll('.kelime').length === 6"), 'Seçilen görev aynı metinle Okuma ekranını açıyor');

  await clickButton('Çıkış');
  await loginPortal('mentor', 'Ayşe Öğretmen');
  await evaluate("document.querySelector('[aria-label=\"Ormandaki Dostlar metnini sil\"]').click()");
  await delay(80);
  assert(await evaluate("document.querySelectorAll('.metin-yonetim-satiri').length === 1 && document.querySelector('.form-bildirim').textContent.includes('silindi')"), 'Mentör istediği kayıtlı metni silebiliyor');
  await clickButton('Çıkış');
  await loginPortal('ogrenci', 'Elif');
  await clickButton('Görevlerim');
  assert(await evaluate("![...document.querySelectorAll('.gorev-buton')].some((item) => item.textContent.includes('Ormandaki Dostlar'))"), 'Silinen metin Görevlerim ekranından kaldırılıyor');

  await clickButton('Çıkış');
  await loginPortal('mentor', 'Ayşe Öğretmen');
  await clickButton('Teknik mimari');
  assert(await evaluate("document.querySelectorAll('.hat-adim').length === 6"), 'Teknik mimaride altı işlem adımı bulunuyor');
  const desktopShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.desktop, Buffer.from(desktopShot.data, 'base64'));

  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await clickButton('Çıkış');
  await loginPortal('ogrenci', 'Elif');
  await clickButton('Görevlerim');
  const responsive = await evaluate(`(() => ({
    taskInside: document.querySelector('.gorev').getBoundingClientRect().right <= document.documentElement.clientWidth,
    overflow: document.documentElement.scrollWidth <= document.documentElement.clientWidth
  }))()`);
  assert(responsive.taskInside && responsive.overflow, '390 px genişlikte görevler yatay taşma oluşturmuyor');
  const mobileShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
  writeFileSync(screenshots.mobile, Buffer.from(mobileShot.data, 'base64'));

  if (referencePath) {
    assert(existsSync(referencePath), 'Referans HTML dosyası bulundu');
    await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false });
    await send('Page.navigate', { url: pathToFileURL(referencePath).href });
    await waitFor("document.body?.innerText.includes('Kırlangıçlar') && document.querySelectorAll('.kelime').length === 24");
    await evaluate('document.fonts.ready.then(() => true)');
    const referenceReadingStyles = await evaluate(readingStyleSnapshot);
    const stylesMatch = JSON.stringify(reactReadingStyles) === JSON.stringify(referenceReadingStyles);
    if (!stylesMatch) {
      console.log(`REACT_READING_STYLES=${JSON.stringify(reactReadingStyles, null, 2)}`);
      console.log(`REFERENCE_READING_STYLES=${JSON.stringify(referenceReadingStyles, null, 2)}`);
    }
    assert(stylesMatch, 'React okuma ekranının temel hesaplanmış ölçüleri referans HTML ile eşleşiyor');
    const referenceShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    writeFileSync(screenshots.readingReference, Buffer.from(referenceShot.data, 'base64'));
    await evaluate(`(() => {
      const button = [...document.querySelectorAll('.ekran-btn')].find((item) => item.textContent.trim() === 'Görevlerim');
      button.click();
      return true;
    })()`);
    await delay(80);
    const referenceTaskStyles = await evaluate(taskStyleSnapshot);
    const taskStylesMatch = JSON.stringify(reactTaskStyles) === JSON.stringify(referenceTaskStyles);
    if (!taskStylesMatch) {
      console.log(`REACT_TASK_STYLES=${JSON.stringify(reactTaskStyles, null, 2)}`);
      console.log(`REFERENCE_TASK_STYLES=${JSON.stringify(referenceTaskStyles, null, 2)}`);
    }
    assert(taskStylesMatch, 'React Görevlerim ekranının hesaplanmış ölçüleri referans HTML ile eşleşiyor');
    const referenceTaskShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    writeFileSync(screenshots.taskReference, Buffer.from(referenceTaskShot.data, 'base64'));
    await evaluate(`(() => {
      const button = [...document.querySelectorAll('.ekran-btn')].find((item) => item.textContent.trim() === 'Anlama soruları');
      button.click();
      return true;
    })()`);
    await delay(80);
    const referenceComprehensionStyles = await evaluate(comprehensionStyleSnapshot);
    const comprehensionStylesMatch = JSON.stringify(reactComprehensionStyles) === JSON.stringify(referenceComprehensionStyles);
    if (!comprehensionStylesMatch) {
      console.log(`REACT_COMPREHENSION_STYLES=${JSON.stringify(reactComprehensionStyles, null, 2)}`);
      console.log(`REFERENCE_COMPREHENSION_STYLES=${JSON.stringify(referenceComprehensionStyles, null, 2)}`);
    }
    assert(comprehensionStylesMatch, 'React Anlama ekranının hesaplanmış ölçüleri referans HTML ile eşleşiyor');
    const referenceComprehensionShot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    writeFileSync(screenshots.comprehensionReference, Buffer.from(referenceComprehensionShot.data, 'base64'));
    console.log(`READING_REACT_SCREENSHOT=${screenshots.readingReact}`);
    console.log(`READING_REFERENCE_SCREENSHOT=${screenshots.readingReference}`);
    console.log(`TASK_REACT_SCREENSHOT=${screenshots.taskReact}`);
    console.log(`TASK_REFERENCE_SCREENSHOT=${screenshots.taskReference}`);
    console.log(`COMPREHENSION_REACT_SCREENSHOT=${screenshots.comprehensionReact}`);
    console.log(`COMPREHENSION_REFERENCE_SCREENSHOT=${screenshots.comprehensionReference}`);
  }

  console.log(`DESKTOP_SCREENSHOT=${screenshots.desktop}`);
  console.log(`MOBILE_SCREENSHOT=${screenshots.mobile}`);
  console.log(`OVERFLOW_DESKTOP_SCREENSHOT=${screenshots.overflowDesktop}`);
  console.log(`OVERFLOW_MOBILE_SCREENSHOT=${screenshots.overflowMobile}`);
} finally {
  if (socket?.readyState === WebSocket.OPEN) socket.close();
  if (chrome && chrome.exitCode === null) {
    chrome.kill();
    await Promise.race([
      new Promise((resolveExit) => chrome.once('exit', resolveExit)),
      delay(2_000),
    ]);
  }
  try {
    rmSync(profileDirectory, { recursive: true, force: true, maxRetries: 5, retryDelay: 150 });
  } catch (error) {
    console.warn(`Geçici Chrome profili temizlenemedi: ${error.message}`);
  }
}
