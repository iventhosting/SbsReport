/* =========================================================
   SbsReport — Web Report & Form Designer Engine
   SbsReport Web Designer IDE
   Sobis Dark & Light Themes | TR & EN Localization
   ========================================================= */
(() => {
  const MM_TO_PX = 96 / 25.4; // ~3.7795 px per mm
  const t = (k, ...args) => (window.SbsI18n ? window.SbsI18n.t(k, ...args) : k);

  const state = {
    report: null,
    masterReport: null, // Loaded Master Form document if report.masterReportName is set
    availableReports: [],
    activePageIndex: 0,
    demoSources: [],
    selectedBandId: null,
    selectedCompId: null, // null means Form/Page is selected!
    inspectorTarget: '__form__', // '__form__' | '__page__' | component id
    multiIds: new Set(),
    activeTool: 'pointer', // 'pointer' | component type name
    mode: 'design', // 'design' | 'preview' | 'pdfpreview'
    zoom: 1,
    dirty: false,
    undoStack: [],
    redoStack: [],
    clipboard: null,
    formatPainterStyle: null,
    inspectorView: 'categorized', // 'categorized' | 'alphabetical'
    inspectorFilter: '',
    inspectorTab: 'properties', // 'properties' | 'tree' | 'dictionary'
    rendered: null,
    previewPageIndex: 0,
    selectedTemplateKey: 'BasvuruFormu'
  };

  const basePath = window.location.pathname.replace(/\/+$/, '');
  const apiUrl = (path) => `${basePath}/api${path}`;

  const $ = (sel) => document.querySelector(sel);
  const $$ = (sel) => Array.from(document.querySelectorAll(sel));

  // ---------- Helpers ----------
  function uid() { return Math.random().toString(36).slice(2, 10); }
  function mmToPx(mm) { return mm * MM_TO_PX; }
  function pxToMm(px) { return px / MM_TO_PX; }
  function snapMm(mm) {
    if (!state.report?.page?.snapToGrid) return +mm.toFixed(2);
    const g = state.report?.page?.gridSizeMm || 2;
    return +(Math.round(mm / g) * g).toFixed(2);
  }
  function setStatus(msg) {
    const el = $('#statusText');
    if (el) el.textContent = msg;
  }
  function setStatusGeometry(x, y, w, h) {
    const el = $('#statusGeometry');
    if (!el) return;
    if (x == null) {
      el.textContent = 'X: 0 mm, Y: 0 mm | W: 0 mm, H: 0 mm';
    } else {
      el.textContent = `X: ${x.toFixed(1)} mm, Y: ${y.toFixed(1)} mm | W: ${w.toFixed(1)} mm, H: ${h.toFixed(1)} mm`;
    }
  }
  function markDirty(val = true) {
    state.dirty = val;
    const dot = $('#dirtyDot');
    if (dot) dot.hidden = !val;
  }
  function pushUndo() {
    if (!state.report) return;
    state.undoStack.push(JSON.stringify(state.report));
    if (state.undoStack.length > 50) state.undoStack.shift();
    state.redoStack = [];
    markDirty(true);
  }

  function findBand(bandId) {
    return state.report?.bands.find(b => b.id === bandId) || null;
  }
  function findSelected() {
    const band = findBand(state.selectedBandId);
    if (!band) return { band: null, comp: null };
    const comp = band.components.find(c => c.id === state.selectedCompId) || null;
    return { band, comp };
  }
  function findCompAnywhere(compId) {
    if (!state.report) return { band: null, comp: null };
    for (const b of state.report.bands) {
      const c = b.components.find(x => x.id === compId);
      if (c) return { band: b, comp: c };
    }
    return { band: null, comp: null };
  }

  // ---------- Init ----------
  async function init() {
    if (window.SbsI18n) {
      window.SbsI18n.apply();
      const langSel = $('#langSelect');
      if (langSel) {
        langSel.value = window.SbsI18n.lang;
        langSel.onchange = (e) => window.SbsI18n.setLang(e.target.value);
      }
    }
    const themeBtn = $('#btnThemeToggle');
    if (themeBtn) {
      themeBtn.onclick = () => {
        const cur = document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
        const next = cur === 'light' ? 'dark' : 'light';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('sbs-report-theme', next);
      };
    }
    window.addEventListener('sbs:langchange', () => {
      if (window.SbsI18n) window.SbsI18n.apply();
      renderAll();
    });

    bindUIEvents();
    await loadDemoSources();

    // Check URL param or list
    const q = new URLSearchParams(location.search);
    const reqReport = q.get('report');
    if (reqReport) {
      await loadReport(reqReport);
    } else {
      const listRes = await fetch(apiUrl('/reports'));
      const reports = await listRes.json();
      if (reports.length > 0) {
        const preferred = reports.find(r => r.name === 'BasvuruFormu') || reports[0];
        await loadReport(preferred.name);
      } else {
        await loadReport('BasvuruFormu');
      }
    }
  }

  async function loadDemoSources() {
    try {
      const res = await fetch(apiUrl('/datasources/demo'));
      state.demoSources = await res.json();
    } catch {
      state.demoSources = [];
    }
  }

  async function loadReport(name) {
    try {
      const res = await fetch(apiUrl(`/reports/${encodeURIComponent(name)}`));
      if (!res.ok) {
        alert(t('loadFailed'));
        return;
      }
      state.report = await res.json();
      state.activePageIndex = 0;
      ensureReportPages(state.report);
      ensurePageDefaults(state.report);
      await loadMasterReport(state.report.masterReportName);
      await refreshReportsList();
      state.selectedBandId = state.report.bands[0]?.id || null;
      state.selectedCompId = null; // Default to Form selected
      state.inspectorTarget = '__form__';
      state.multiIds = new Set();
      state.undoStack = [];
      state.redoStack = [];
      markDirty(false);
      syncPageControls();
      renderAll();
      setStatus(t('loaded', `${state.report.name}.sbs`));
      const badge = state.report.isMasterTemplate ? ' 👑 MASTER' : (state.report.masterReportName ? ` ↳ 👑 ${state.report.masterReportName}` : '');
      $('#reportNameLabel').textContent = `${state.report.name}.sbs${badge}`;
    } catch (e) {
      console.error(e);
      setStatus(t('loadFailed'));
    }
  }

  async function loadMasterReport(masterName) {
    if (!masterName || !state.report || masterName.toLowerCase() === (state.report.name || '').toLowerCase()) {
      state.masterReport = null;
      return;
    }
    try {
      const res = await fetch(apiUrl(`/reports/${encodeURIComponent(masterName)}`));
      if (res.ok) {
        state.masterReport = await res.json();
      } else {
        state.masterReport = null;
      }
    } catch {
      state.masterReport = null;
    }
  }

  async function refreshReportsList() {
    try {
      const res = await fetch(apiUrl('/reports'));
      if (res.ok) {
        state.availableReports = await res.json();
        populateMasterSelect();
      }
    } catch {
      state.availableReports = [];
    }
  }

  function populateMasterSelect() {
    const selects = [$('#pgMasterReport'), $('#topMasterSelect')].filter(Boolean);
    if (selects.length === 0) return;
    const curName = (state.report?.name || '').toLowerCase();
    const curMaster = state.report?.masterReportName || '';
    const isMst = !!state.report?.isMasterTemplate;
    const sorted = [...state.availableReports]
      .filter(r => r.name.toLowerCase() !== curName)
      .sort((a, b) => (b.isMaster ? 1 : 0) - (a.isMaster ? 1 : 0) || a.name.localeCompare(b.name));

    selects.forEach(sel => {
      sel.innerHTML = `<option value="">${sel.id === 'topMasterSelect' ? 'Master: (Yok)' : t('masterNone')}</option>`;
      sorted.forEach(r => {
        const opt = document.createElement('option');
        opt.value = r.name;
        opt.textContent = (r.isMaster ? '👑 ' : '') + r.name;
        sel.appendChild(opt);
      });
      if (sel.id === 'topMasterSelect') {
        const newOpt = document.createElement('option');
        newOpt.value = '__new_master__';
        newOpt.textContent = '＋ Yeni Master Form Oluştur...';
        sel.appendChild(newOpt);
      }
      sel.value = curMaster;
    });

    const topBox = $('#topbarMasterBox');
    if (topBox) topBox.hidden = isMst;
    const btnOpen = $('#btnOpenMaster');
    if (btnOpen) btnOpen.hidden = !curMaster;
    const btnTopEdit = $('#btnTopEditMaster');
    if (btnTopEdit) btnTopEdit.hidden = !curMaster;
  }

  async function applyMasterSelection(val) {
    if (val === '__new_master__') {
      state.returnFromMasterReport = state.report?.name || null;
      createNewMasterForm('YeniMasterForm');
      return;
    }
    pushUndo();
    const masterName = val || null;
    state.report.masterReportName = masterName;
    if (state.report.inheritMasterHeader === undefined) state.report.inheritMasterHeader = true;
    if (state.report.inheritMasterFooter === undefined) state.report.inheritMasterFooter = true;
    await loadMasterReport(masterName);
    populateMasterSelect();
    renderAll();
    setStatus(masterName ? `Master Form bağlandı: ${masterName} (Header & Footer sabit)` : 'Master Form bağlantısı kaldırıldı.');
  }

  async function extractCurrentReportToMaster() {
    if (!state.report) return;
    const headerBands = state.report.bands.filter(b => b.type === 'PageHeader' || b.type === 'ReportTitle');
    const footerBands = state.report.bands.filter(b => b.type === 'PageFooter');
    if (headerBands.length === 0 && footerBands.length === 0) {
      alert('Bu raporda Master olarak çıkarılacak PageHeader veya PageFooter bandı bulunamadı.');
      return;
    }
    const masterName = prompt('Yeni Master Form adı:', `${state.report.name}_Master`);
    if (!masterName || !masterName.trim()) return;
    const cleanName = masterName.trim().replace(/\.sbs$/i, '');

    const clonedBands = [
      ...headerBands.map(b => ({ ...JSON.parse(JSON.stringify(b)), type: 'PageHeader' })),
      ...footerBands.map(b => ({ ...JSON.parse(JSON.stringify(b)), type: 'PageFooter' }))
    ];
    const masterDoc = {
      version: '1.0',
      name: cleanName,
      description: `${state.report.name} raporundan çıkarılan Master Form`,
      isMasterTemplate: true,
      page: JSON.parse(JSON.stringify(state.report.page)),
      bands: clonedBands
    };

    const res = await fetch(apiUrl(`/reports/${encodeURIComponent(cleanName)}`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(masterDoc)
    });
    if (!res.ok) { alert('Master Form kaydedilemedi!'); return; }

    pushUndo();
    // Remove extracted PageHeader/PageFooter from child and attach Master!
    state.report.bands = state.report.bands.filter(b => b.type !== 'PageHeader' && b.type !== 'PageFooter');
    state.report.masterReportName = cleanName;
    state.report.inheritMasterHeader = true;
    state.report.inheritMasterFooter = true;
    await refreshReportsList();
    await loadMasterReport(cleanName);
    populateMasterSelect();
    renderAll();
    setStatus(`⚡ "${cleanName}.sbs" Master Form oluşturuldu ve bu rapora bağlandı!`);
  }

  function ensurePageDefaults(rep) {
    if (!rep.page) rep.page = {};
    const p = rep.page;
    p.gridColumns = p.gridColumns || 96;
    p.gridVerticalStep = p.gridVerticalStep || 5;
    p.formName = p.formName || 'Form';
    p.defaultLabelWidthMm = p.defaultLabelWidthMm || 30;
    p.defaultLabelFontFamily = p.defaultLabelFontFamily || 'Segoe UI, sans-serif';
    p.defaultLabelFontSizePt = p.defaultLabelFontSizePt || 10;
    p.defaultLabelColor = p.defaultLabelColor || '#000000';
    p.defaultInnerTextFontFamily = p.defaultInnerTextFontFamily || 'Segoe UI, sans-serif';
    p.defaultInnerTextFontSizePt = p.defaultInnerTextFontSizePt || 9;
    p.defaultInnerTextColor = p.defaultInnerTextColor || '#000000';
    p.defaultBackColor = p.defaultBackColor || '#ffffff';
    p.defaultBorderColor = p.defaultBorderColor || '#cbd5e1';
    p.defaultCheckColor = p.defaultCheckColor || '#000000';
    p.defaultBorderWidth = p.defaultBorderWidth || 'Thin';
    p.defaultDescriptionFontFamily = p.defaultDescriptionFontFamily || 'Segoe UI, sans-serif';
    p.defaultDescriptionFontSizePt = p.defaultDescriptionFontSizePt || 9;
    p.defaultPaddingLeftMm = p.defaultPaddingLeftMm ?? 0;
    p.defaultPaddingTopMm = p.defaultPaddingTopMm ?? 0;
    p.defaultPaddingRightMm = p.defaultPaddingRightMm ?? 0;
    p.defaultPaddingBottomMm = p.defaultPaddingBottomMm ?? 2;
    if (!p.watermark) {
      p.watermark = { enabled: true, text: 'Trial', angle: -45, opacity: 0.12, fontSizePt: 76, color: '#64748b' };
    }
  }

  // ---------- Multi-Page Helpers ----------
  function ensureReportPages(rep) {
    if (!rep) return;
    if (!rep.pages || rep.pages.length === 0) {
      rep.pages = [{
        id: uid(),
        name: 'Page 1',
        page: rep.page || {},
        bands: rep.bands || []
      }];
    }
    state.activePageIndex = Math.min(state.activePageIndex || 0, rep.pages.length - 1);
    const active = rep.pages[state.activePageIndex];
    rep.page = active.page;
    rep.bands = active.bands;
  }

  function syncActivePageToPagesArray() {
    if (!state.report) return;
    ensureReportPages(state.report);
    const active = state.report.pages[state.activePageIndex];
    if (active) {
      active.page = state.report.page;
      active.bands = state.report.bands;
    }
  }

  function renderPageTabs() {
    const container = $('#pageTabsContainer');
    if (!container || !state.report) return;
    ensureReportPages(state.report);
    container.innerHTML = '';
    state.report.pages.forEach((pgObj, idx) => {
      const btn = document.createElement('button');
      const isAct = state.mode === 'design' && idx === state.activePageIndex;
      btn.className = 'cdoc-tab' + (isAct ? ' cdoc-active active' : '');
      btn.dataset.doctab = 'design';
      btn.dataset.pageIdx = String(idx);
      const delHtml = state.report.pages.length > 1
        ? `<span class="cdoc-close" title="Sayfayı Sil" style="margin-left:5px;opacity:.65;font-size:10px;">✕</span>`
        : '';
      btn.innerHTML = `<span class="cdoc-ico">📄</span> <span class="cdoc-lbl">${escapeHtml(pgObj.name || `Page ${idx + 1}`)}</span>${delHtml}`;
      btn.onclick = (e) => {
        if (e.target.classList.contains('cdoc-close')) {
          e.stopPropagation();
          deleteReportPage(idx);
          return;
        }
        selectReportPage(idx);
      };
      container.appendChild(btn);
    });
  }

  function selectReportPage(idx) {
    if (!state.report) return;
    syncActivePageToPagesArray();
    state.activePageIndex = idx;
    const target = state.report.pages[idx];
    if (target) {
      state.report.page = target.page;
      state.report.bands = target.bands;
      ensurePageDefaults(state.report);
      state.selectedBandId = state.report.bands[0]?.id || null;
      state.selectedCompId = null;
      state.inspectorTarget = '__form__';
      state.multiIds = new Set();
    }
    if (state.mode !== 'design') {
      switchMode('design');
    } else {
      syncPageControls();
      renderAll();
    }
  }

  function addReportPage() {
    if (!state.report) return;
    pushUndo();
    syncActivePageToPagesArray();
    const nextNum = state.report.pages.length + 1;
    const clonedPageSettings = JSON.parse(JSON.stringify(state.report.page));
    const newPage = {
      id: uid(),
      name: `Page ${nextNum}`,
      page: clonedPageSettings,
      bands: [
        { id: uid(), type: 'Data', name: `DataBand_P${nextNum}`, heightMm: 60, backgroundColor: 'transparent', components: [] }
      ]
    };
    state.report.pages.push(newPage);
    selectReportPage(state.report.pages.length - 1);
    markDirty(true);
    setStatus(`Yeni sayfa eklendi: Page ${nextNum}`);
  }

  function deleteReportPage(idx) {
    if (!state.report || !state.report.pages || state.report.pages.length <= 1) return;
    const pgName = state.report.pages[idx]?.name || `Page ${idx + 1}`;
    if (!confirm(`"${pgName}" silinsin mi?`)) return;
    pushUndo();
    syncActivePageToPagesArray();
    state.report.pages.splice(idx, 1);
    const nextIdx = Math.max(0, Math.min(state.activePageIndex, state.report.pages.length - 1));
    state.activePageIndex = nextIdx;
    const target = state.report.pages[nextIdx];
    state.report.page = target.page;
    state.report.bands = target.bands;
    state.selectedBandId = state.report.bands[0]?.id || null;
    state.selectedCompId = null;
    markDirty(true);
    renderAll();
  }

  function updateMasterModeUI() {
    if (!state.report) return;
    const isMst = !!state.report.isMasterTemplate;
    const mstName = state.report.masterReportName;
    const lbl = $('#reportNameLabel');
    if (lbl) {
      lbl.classList.toggle('is-master', isMst);
      if (isMst) {
        lbl.textContent = `👑 MASTER: ${state.report.name}.sbs`;
      } else if (mstName) {
        lbl.textContent = `${state.report.name}.sbs ↳ 👑 ${mstName}`;
      } else {
        lbl.textContent = `${state.report.name}.sbs`;
      }
    }
    const banner = $('#masterModeBanner');
    if (banner) banner.hidden = !isMst;
    const retBtn = $('#btnReturnFromMaster');
    if (retBtn) {
      retBtn.hidden = !(isMst && state.returnFromMasterReport);
      if (state.returnFromMasterReport) {
        retBtn.textContent = `← ${state.returnFromMasterReport} Raporuna Dön`;
      }
    }
  }

  // ---------- Render All ----------
  function renderAll() {
    updateMasterModeUI();
    renderPageTabs();
    renderCanvas();
    renderTree();
    renderDictionary();
    renderInspectorTargetSelect();
    renderProperties();
    syncToolbarFromSelection();
  }

  // ---------- Canvas Rendering ----------
  function renderCanvas() {
    const sheet = $('#pageSheet');
    if (!sheet || !state.report) return;
    const pg = state.report.page;

    const wPx = mmToPx(pg.widthMm);
    const hPx = mmToPx(pg.heightMm);
    sheet.style.width = `${wPx}px`;
    sheet.style.height = `${hPx}px`;

    // Zoom transform on stage
    const stage = $('#designerStage');
    if (stage) {
      stage.style.transform = `scale(${state.zoom})`;
      stage.style.transformOrigin = 'top center';
    }

    // Grid toggle: add/remove class + set background-size
    if (pg.showGrid) {
      const gPx = mmToPx(pg.gridSizeMm || 2);
      sheet.classList.add('sheet-grid-active');
      sheet.style.setProperty('--grid-size', `${gPx}px`);
    } else {
      sheet.classList.remove('sheet-grid-active');
    }

    // Canvas document tab sync
    const docTabs = document.querySelectorAll('.cdoc-tab');
    docTabs.forEach(t => {
      t.classList.toggle('cdoc-active', t.dataset.doctab === state.mode);
      t.classList.toggle('active', t.dataset.doctab === state.mode);
    });

    renderRulers(pg.widthMm, pg.heightMm);
    sheet.innerHTML = '';

    // Watermark layer
    if (pg.watermark?.enabled && pg.watermark.text) {
      const wm = document.createElement('div');
      wm.className = 'page-watermark-overlay';
      const wmt = document.createElement('div');
      wmt.className = 'watermark-text';
      wmt.textContent = pg.watermark.text;
      wmt.style.fontFamily = pg.watermark.fontFamily || 'Inter, Arial, sans-serif';
      wmt.style.fontSize = `${pg.watermark.fontSizePt || 72}pt`;
      wmt.style.color = pg.watermark.color || '#cbd5e1';
      wmt.style.opacity = pg.watermark.opacity || 0.12;
      wmt.style.transform = `rotate(${pg.watermark.angle || -45}deg)`;
      wm.appendChild(wmt);
      sheet.appendChild(wm);
    }

    // Inner Margins Guide
    const inner = document.createElement('div');
    inner.className = 'page-margins-guide';
    inner.style.top = `${mmToPx(pg.marginTopMm || 0)}px`;
    inner.style.left = `${mmToPx(pg.marginLeftMm || 0)}px`;
    inner.style.right = `${mmToPx(pg.marginRightMm || 0)}px`;
    inner.style.bottom = `${mmToPx(pg.marginBottomMm || 0)}px`;

    // Grid background on bands (applied via CSS class on pageSheet)
    if (pg.showGrid) {
      const gPx = mmToPx(pg.gridSizeMm || 2);
      sheet.classList.add('sheet-grid-active');
      sheet.style.setProperty('--grid-size', `${gPx}px`);
      // Also set size on each band-body via CSS variable
      inner.style.setProperty('--grid-size', `${gPx}px`);
    } else {
      sheet.classList.remove('sheet-grid-active');
    }

    // 4 Corner Crop Marks (registration marks)
    const c1 = document.createElement('div'); c1.className = 'corner-crop cc-tl'; inner.appendChild(c1);
    const c2 = document.createElement('div'); c2.className = 'corner-crop cc-tr'; inner.appendChild(c2);
    const c3 = document.createElement('div'); c3.className = 'corner-crop cc-bl'; inner.appendChild(c3);
    const c4 = document.createElement('div'); c4.className = 'corner-crop cc-br'; inner.appendChild(c4);

    // Helper: render locked Master Form inherited band (Header or Footer)
    const renderInheritedMasterBand = (mBand, masterName) => {
      const mBox = document.createElement('div');
      mBox.className = 'band-box master-inherited';

      const mCap = document.createElement('div');
      mCap.className = 'band-caption band-caption-master';
      mCap.innerHTML =
        `<div class="band-caption-left">` +
          `<span class="master-badge">🔒 MASTER: ${escapeHtml(masterName)}</span>` +
          `<span>${escapeHtml(mBand.name)} <small style="opacity:.8">(${t('masterLockedHint')})</small></span>` +
        `</div>`;

      const mActs = document.createElement('div');
      mActs.className = 'band-caption-actions';
      const btnEditMaster = document.createElement('button');
      btnEditMaster.className = 'band-act-btn';
      btnEditMaster.style.width = 'auto';
      btnEditMaster.style.padding = '0 6px';
      btnEditMaster.textContent = '✏';
      btnEditMaster.title = t('editMaster');
      btnEditMaster.onclick = (e) => {
        e.stopPropagation();
        state.returnFromMasterReport = state.report?.name || null;
        loadReport(masterName);
      };
      const btnDetachMaster = document.createElement('button');
      btnDetachMaster.className = 'band-act-btn';
      btnDetachMaster.style.color = '#fca5a5';
      btnDetachMaster.textContent = '✕';
      btnDetachMaster.title = 'Master Form bağlantısını kaldır';
      btnDetachMaster.onclick = (e) => {
        e.stopPropagation();
        applyMasterSelection('');
      };
      mActs.appendChild(btnEditMaster);
      mActs.appendChild(btnDetachMaster);
      mCap.appendChild(mActs);
      mBox.appendChild(mCap);

      const mBody = document.createElement('div');
      mBody.className = 'band-body';
      mBody.style.height = `${mmToPx(mBand.heightMm)}px`;
      if (mBand.backgroundColor && mBand.backgroundColor !== 'transparent') {
        mBody.style.backgroundColor = mBand.backgroundColor;
      }
      (mBand.components || []).forEach(comp => {
        const compEl = createComponentElement(mBand, comp);
        compEl.onmousedown = null;
        compEl.oncontextmenu = null;
        compEl.ondblclick = null;
        compEl.style.cursor = 'default';
        mBody.appendChild(compEl);
      });
      mBox.appendChild(mBody);
      inner.appendChild(mBox);
    };

    // 1) Fixed TOP bands inherited from Master Form (PageHeader)
    const inheritH = state.report.inheritMasterHeader !== false;
    const inheritF = state.report.inheritMasterFooter !== false;
    const masterHeaders = (inheritH && state.masterReport?.bands) ? state.masterReport.bands.filter(b => b.type === 'PageHeader') : [];
    const masterFooters = (inheritF && state.masterReport?.bands) ? state.masterReport.bands.filter(b => b.type === 'PageFooter') : [];
    if (masterHeaders.length > 0) {
      masterHeaders.forEach(mb => renderInheritedMasterBand(mb, state.masterReport.name));
    }

    // 2) Child report's own editable middle bands (preserve user order; semantic placement happens in addBandToReport)
    state.report.bands.forEach((band, bandIdx) => {
      const bandBox = document.createElement('div');
      const isBandSel = band.id === state.selectedBandId && !state.selectedCompId;
      bandBox.className = 'band-box' + (isBandSel ? ' selected' : '');
      bandBox.dataset.bandId = band.id;

      // === Colored Caption with Move Up/Down/Delete ===
      const caption = document.createElement('div');
      caption.className = `band-caption band-caption-${band.type}`;

      const captionLeft = document.createElement('div');
      captionLeft.className = 'band-caption-left';
      const groupInfo = band.type === 'GroupHeader'
        ? `<small style="opacity:.9;">; ${band.groupBy ? 'Grup: ' + escapeHtml(band.groupBy) : 'Grup atanmamış'}</small>`
        : '';
      const dsInfo = (band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData')
        ? (band.dataSource ? `<span class="band-ds-tag">🗄 ${escapeHtml(band.dataSource)}</span>` : `<small style="opacity:.85;">; Veri Kaynağı: Atanmamış</small>`)
        : (band.dataSource ? `<span class="band-ds-tag">🗄 ${escapeHtml(band.dataSource)}</span>` : '');
      captionLeft.innerHTML =
        `<span class="b-dot b-dot-${band.type}"></span>` +
        `<span>${escapeHtml(band.name)}${groupInfo}</span>` +
        dsInfo;

      const captionActions = document.createElement('div');
      captionActions.className = 'band-caption-actions';

      const btnUp = document.createElement('button');
      btnUp.className = 'band-act-btn'; btnUp.title = 'Yukarı Taşı'; btnUp.textContent = '▲';
      btnUp.onclick = (e) => { e.stopPropagation(); moveBand(band.id, -1); };

      const btnDown = document.createElement('button');
      btnDown.className = 'band-act-btn'; btnDown.title = 'Aşağı Taşı'; btnDown.textContent = '▼';
      btnDown.onclick = (e) => { e.stopPropagation(); moveBand(band.id, +1); };

      const btnDel = document.createElement('button');
      btnDel.className = 'band-act-btn'; btnDel.title = 'Bandı Sil'; btnDel.textContent = '✕';
      btnDel.style.color = '#fca5a5';
      btnDel.onclick = (e) => {
        e.stopPropagation();
        if (!confirm(t('confirmDeleteBand', band.name))) return;
        pushUndo();
        state.report.bands = state.report.bands.filter(b => b.id !== band.id);
        if (state.selectedBandId === band.id) state.selectedBandId = state.report.bands[0]?.id || null;
        renderAll();
      };

      captionActions.appendChild(btnUp);
      captionActions.appendChild(btnDown);
      captionActions.appendChild(btnDel);
      caption.appendChild(captionLeft);
      caption.appendChild(captionActions);

      caption.onclick = (e) => {
        e.stopPropagation();
        state.selectedBandId = band.id;
        state.selectedCompId = null;
        state.inspectorTarget = '__band__';
        state.multiIds = new Set();
        renderAll();
      };
      caption.ondblclick = (e) => {
        e.stopPropagation();
        if (band.type === 'GroupHeader') {
          openGroupBandModal(band);
        } else if (band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData') {
          openDataBandModal(band);
        }
      };
      bandBox.appendChild(caption);

      const body = document.createElement('div');
      body.className = 'band-body';
      body.style.height = `${mmToPx(band.heightMm)}px`;
      if (band.backgroundColor && band.backgroundColor !== 'transparent') {
        body.style.backgroundColor = band.backgroundColor;
      }
      // Grid size CSS variable for band-body background-size
      if (pg.showGrid) {
        const gPx = mmToPx(pg.gridSizeMm || 2);
        body.style.setProperty('--grid-size', `${gPx}px`);
      }

      body.onclick = (e) => {
        if (e.target === body) {
          if (state.activeTool !== 'pointer') {
            const rect = body.getBoundingClientRect();
            const xMm = snapMm(pxToMm((e.clientX - rect.left) / state.zoom));
            const yMm = snapMm(pxToMm((e.clientY - rect.top) / state.zoom));
            addComponentToBand(band, state.activeTool, xMm, yMm);
            setActiveTool('pointer');
            return;
          }
          // Clicking body selects the Form
          state.selectedBandId = band.id;
          state.selectedCompId = null;
          state.inspectorTarget = '__form__';
          state.multiIds = new Set();
          setStatusGeometry(null);
          renderAll();
        }
      };

      body.ondblclick = (e) => {
        if (e.target === body) {
          if (band.type === 'GroupHeader') openGroupBandModal(band);
          else if (band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData') openDataBandModal(band);
        }
      };

      body.ondragover = (e) => e.preventDefault();
      body.ondrop = (e) => {
        e.preventDefault();
        const rect = body.getBoundingClientRect();
        const xMm = snapMm(pxToMm((e.clientX - rect.left) / state.zoom));
        const yMm = snapMm(pxToMm((e.clientY - rect.top) / state.zoom));

        const expr = e.dataTransfer.getData('application/sbs-expr');
        const dsName = e.dataTransfer.getData('application/sbs-ds');
        const dsWizard = e.dataTransfer.getData('application/sbs-ds-wizard');
        const toolType = e.dataTransfer.getData('application/sbs-tool');

        if (dsWizard) {
          openDataPlacementWizard(dsWizard);
        } else if (expr) {
          if (dsName && (band.type === 'Data' || band.type === 'Table') && !band.dataSource) {
            band.dataSource = dsName;
          }
          addComponentToBand(band, 'Text', xMm, yMm, expr);
        } else if (toolType) {
          addComponentToBand(band, toolType, xMm, yMm);
        }
      };

      band.components.forEach(comp => {
        const compEl = createComponentElement(band, comp);
        body.appendChild(compEl);
      });

      bandBox.appendChild(body);

      const resizer = document.createElement('div');
      resizer.className = 'band-resizer';
      resizer.onmousedown = (e) => startBandResize(e, band);
      bandBox.appendChild(resizer);

      inner.appendChild(bandBox);

      // When designing a Master Form, show clear visual child-content slot right after PageHeader!
      if (state.report.isMasterTemplate && band.type === 'PageHeader') {
        const slot = document.createElement('div');
        slot.className = 'master-child-slot';
        slot.innerHTML = '⬇ ÇOCUK RAPOR İÇERİK ALANI ⬇<br/><small style="font-weight:500;opacity:.85;">Bu Master Formu kullanan diğer raporların gövde bantları (Data, Tablo, Grup, Form) otomatik olarak buraya yerleşir.</small>';
        inner.appendChild(slot);
      }
    });

    // 3) Fixed BOTTOM bands inherited from Master Form (PageFooter)
    if (masterFooters.length > 0) {
      masterFooters.forEach(mb => renderInheritedMasterBand(mb, state.masterReport.name));
    }

    sheet.appendChild(inner);
  }

  // ---------- Create Component HTML Element ----------
  function createComponentElement(band, comp) {
    const el = document.createElement('div');
    const isSel = state.selectedBandId === band.id && state.selectedCompId === comp.id;
    el.className = 'comp-box' + (isSel ? ' selected' : '');
    el.dataset.compId = comp.id;
    if (state.multiIds.has(comp.id) && !isSel) el.classList.add('multi');

    el.style.left = `${mmToPx(comp.xMm)}px`;
    el.style.top = `${mmToPx(comp.yMm)}px`;
    el.style.width = `${mmToPx(comp.widthMm)}px`;
    el.style.height = `${mmToPx(comp.heightMm)}px`;
    el.style.backgroundColor = comp.backgroundColor || 'transparent';
    el.style.color = comp.color || '#111827';
    el.style.fontFamily = comp.fontFamily || 'Inter, Arial, sans-serif';
    el.style.fontSize = `${comp.fontSizePt || 10}pt`;
    el.style.fontWeight = comp.bold ? '700' : '400';
    el.style.fontStyle = comp.italic ? 'italic' : 'normal';
    el.style.textDecoration = [comp.underline ? 'underline' : '', comp.strike ? 'line-through' : ''].filter(Boolean).join(' ') || 'none';

    if (comp.cornerRadiusPx > 0) {
      el.style.borderRadius = `${comp.cornerRadiusPx}px`;
    }
    if (comp.angle) {
      el.style.transform = `rotate(${comp.angle}deg)`;
    }

    const bStyle = (comp.borderStyle || 'Solid').toLowerCase();
    if (comp.borderWidthPx > 0 && comp.borderSides !== 'None') {
      const bProp = `${comp.borderWidthPx}px ${bStyle} ${comp.borderColor || '#cbd5e1'}`;
      if (comp.borderSides === 'All' || comp.borderSides === 'Box') {
        el.style.border = bProp;
      } else {
        el.style.border = 'none';
        if (comp.borderSides.includes('Top')) el.style.borderTop = bProp;
        if (comp.borderSides.includes('Bottom')) el.style.borderBottom = bProp;
        if (comp.borderSides.includes('Left')) el.style.borderLeft = bProp;
        if (comp.borderSides.includes('Right')) el.style.borderRight = bProp;
      }
    } else {
      el.style.border = '1px dotted rgba(148,163,184,0.35)';
    }

    // Component Content by Type (16 component types)
    if (comp.type === 'Input') {
      const inBox = document.createElement('div');
      inBox.className = 'comp-input-box';
      inBox.textContent = comp.text || '';
      if (!comp.text && comp.placeholder) {
        const ph = document.createElement('span');
        ph.className = 'comp-input-placeholder';
        ph.textContent = comp.placeholder;
        inBox.appendChild(ph);
      }
      el.appendChild(inBox);
    } else if (comp.type === 'CheckBox') {
      const cBox = document.createElement('div');
      cBox.className = 'comp-check-box';
      const glyph = document.createElement('span');
      glyph.className = 'comp-check-glyph' + (comp.checked ? ' checked' : '');
      glyph.textContent = comp.checked ? '✓' : '';
      cBox.appendChild(glyph);
      const lbl = document.createElement('span');
      lbl.textContent = comp.text || comp.label || 'CheckBox';
      cBox.appendChild(lbl);
      el.appendChild(cBox);
      // Toggle checkbox on click (design or preview)
      el.addEventListener('click', (e) => {
        e.stopPropagation();
        comp.checked = !comp.checked;
        glyph.textContent = comp.checked ? '✓' : '';
        glyph.classList.toggle('checked', comp.checked);
      });
    } else if (comp.type === 'Radio') {
      const rBox = document.createElement('div');
      rBox.className = 'comp-check-box';
      const glyph = document.createElement('span');
      glyph.className = 'comp-radio-glyph' + (comp.checked ? ' checked' : '');
      rBox.appendChild(glyph);
      const lbl = document.createElement('span');
      lbl.textContent = comp.text || comp.label || 'Radio';
      rBox.appendChild(lbl);
      el.appendChild(rBox);
      // Toggle radio on click
      el.addEventListener('click', (e) => {
        e.stopPropagation();
        comp.checked = !comp.checked;
        glyph.classList.toggle('checked', comp.checked);
      });
    } else if (comp.type === 'ComboBox') {
      const cbBox = document.createElement('div');
      cbBox.className = 'comp-combo-box';
      const txt = document.createElement('span');
      txt.textContent = comp.text || (comp.items ? comp.items.split(',')[0] : 'Dropdown');
      cbBox.appendChild(txt);
      const arrow = document.createElement('span');
      arrow.className = 'comp-combo-arrow';
      arrow.textContent = '▼';
      cbBox.appendChild(arrow);
      el.appendChild(cbBox);
    } else if (comp.type === 'DatePicker') {
      const dBox = document.createElement('div');
      dBox.className = 'comp-date-box';
      const dt = document.createElement('span');
      dt.textContent = comp.text || '11/20/2026';
      dBox.appendChild(dt);
      const ico = document.createElement('span');
      ico.className = 'comp-date-ico';
      ico.textContent = '📅';
      dBox.appendChild(ico);
      el.appendChild(dBox);
    } else if (comp.type === 'Signature') {
      const sBox = document.createElement('div');
      sBox.className = 'comp-signature-box';
      sBox.innerHTML = `<span class="comp-signature-ico">✍</span><span>${escapeHtml(comp.text || 'Signature')}</span>`;
      el.appendChild(sBox);
    } else if (comp.type === 'Button') {
      const bBox = document.createElement('div');
      bBox.className = 'comp-btn-box';
      bBox.textContent = comp.text || 'Button';
      el.appendChild(bBox);
    } else if (comp.type === 'Table') {
      // Real interactive Table cell matrix (StiTable / StiTableCell replica)
      let tblData;
      try { tblData = comp.tableColumnsJson ? JSON.parse(comp.tableColumnsJson) : null; } catch { tblData = null; }
      if (!tblData || !Array.isArray(tblData.rows) || tblData.rows.length === 0) {
        tblData = {
          rows: [
            [{ text: 'Başlık 1', bold: true, bg: '#eef2f9' }, { text: 'Başlık 2', bold: true, bg: '#eef2f9' }, { text: 'Başlık 3', bold: true, bg: '#eef2f9' }],
            [{ text: 'Hücre 1', bg: '#ffffff' }, { text: 'Hücre 2', bg: '#ffffff' }, { text: 'Hücre 3', bg: '#ffffff' }],
            [{ text: 'Hücre 4', bg: '#ffffff' }, { text: 'Hücre 5', bg: '#ffffff' }, { text: 'Hücre 6', bg: '#ffffff' }]
          ]
        };
        comp.tableColumnsJson = JSON.stringify(tblData);
      }
      const grid = document.createElement('div');
      grid.className = 'comp-table-grid';
      tblData.rows.forEach((rowArr, ri) => {
        const rEl = document.createElement('div');
        rEl.className = 'comp-table-row';
        rowArr.forEach((cellObj, ci) => {
          const cEl = document.createElement('div');
          cEl.className = 'comp-tcell';
          cEl.textContent = cellObj.text ?? '';
          if (cellObj.bold) cEl.style.fontWeight = '700';
          if (cellObj.bg && cellObj.bg !== 'transparent') cEl.style.backgroundColor = cellObj.bg;
          if (cellObj.hAlign) cEl.style.justifyContent = cellObj.hAlign === 'right' ? 'flex-end' : cellObj.hAlign === 'center' ? 'center' : 'flex-start';
          cEl.title = 'Hücreyi düzenlemek için çift tıklayın';
          cEl.ondblclick = (ev) => {
            ev.stopPropagation();
            const val = prompt(`Tablo Hücresi [Satır ${ri + 1}, Sütun ${ci + 1}] metin veya ifade:`, cellObj.text ?? '');
            if (val === null) return;
            pushUndo();
            tblData.rows[ri][ci].text = val;
            comp.tableColumnsJson = JSON.stringify(tblData);
            renderAll();
          };
          rEl.appendChild(cEl);
        });
        grid.appendChild(rEl);
      });
      el.style.overflow = 'visible';
      el.appendChild(grid);

      if (isSel) {
        const bar = document.createElement('div');
        bar.className = 'table-mini-bar';
        bar.onmousedown = (ev) => ev.stopPropagation();
        const mkBtn = (label, fn) => {
          const b = document.createElement('button');
          b.type = 'button'; b.className = 'table-mini-btn'; b.textContent = label;
          b.onclick = (ev) => { ev.stopPropagation(); pushUndo(); fn(); comp.tableColumnsJson = JSON.stringify(tblData); renderAll(); };
          return b;
        };
        bar.appendChild(mkBtn('+ Satır', () => {
          const cols = tblData.rows[0]?.length || 3;
          tblData.rows.push(Array.from({ length: cols }, () => ({ text: '', bg: '#ffffff' })));
        }));
        bar.appendChild(mkBtn('− Satır', () => { if (tblData.rows.length > 1) tblData.rows.pop(); }));
        bar.appendChild(mkBtn('+ Sütun', () => {
          tblData.rows.forEach((r, idx) => r.push({ text: idx === 0 ? `Başlık ${r.length + 1}` : '', bold: idx === 0, bg: idx === 0 ? '#eef2f9' : '#ffffff' }));
        }));
        bar.appendChild(mkBtn('− Sütun', () => {
          if ((tblData.rows[0]?.length || 0) > 1) tblData.rows.forEach(r => r.pop());
        }));
        el.appendChild(bar);
      }
    } else if (comp.type === 'Panel') {
      const pBox = document.createElement('div');
      pBox.className = 'comp-inner';
      pBox.innerHTML = `<small style="opacity:.5;">⧉ Panel</small>`;
      el.appendChild(pBox);
    } else if (comp.type === 'Line') {
      el.style.border = 'none';
      if (comp.lineOrientation === 'Vertical') {
        el.style.borderLeft = `${Math.max(1, comp.borderWidthPx || 1)}px ${bStyle} ${comp.color || '#111827'}`;
      } else {
        el.style.borderBottom = `${Math.max(1, comp.borderWidthPx || 1)}px ${bStyle} ${comp.color || '#111827'}`;
      }
    } else if (comp.type === 'Barcode') {
      const inner = document.createElement('div');
      inner.className = 'comp-inner';
      inner.innerHTML = buildBarcodeSvg(comp.text, comp.barcodeFormat);
      el.appendChild(inner);
    } else if (comp.type === 'Chart') {
      const inner = document.createElement('div');
      inner.className = 'comp-inner';
      inner.innerHTML = buildChartSvg(comp.chartType || 'Bar');
      el.appendChild(inner);
    } else if (comp.type === 'Image') {
      const inner = document.createElement('div');
      inner.className = 'comp-inner';
      inner.style.justifyContent = 'center';
      if (comp.imageUrl) {
        inner.innerHTML = `<img src="${escapeHtml(comp.imageUrl)}" style="max-width:100%;max-height:100%;object-fit:contain;" />`;
      } else {
        inner.innerHTML = `<span style="opacity:.6;">🖼 [Image]</span>`;
      }
      el.appendChild(inner);
    } else if (comp.type === 'Shape' && (comp.shapeType === 'Ellipse' || comp.shapeType === 'Triangle' || comp.shapeType === 'RoundedRectangle')) {
      el.style.backgroundColor = 'transparent';
      el.style.border = 'none';
      const inner = document.createElement('div');
      inner.className = 'comp-inner';
      inner.innerHTML = buildShapeSvg(comp);
      el.appendChild(inner);
    } else {
      // Text / Label / Rectangle Shape
      const inner = document.createElement('div');
      inner.className = 'comp-inner';
      inner.style.justifyContent = comp.hAlign === 'center' ? 'center' : comp.hAlign === 'right' ? 'flex-end' : comp.hAlign === 'justify' ? 'space-between' : 'flex-start';
      inner.style.alignItems = comp.vAlign === 'top' ? 'flex-start' : comp.vAlign === 'bottom' ? 'flex-end' : 'center';
      inner.textContent = comp.type === 'Shape' ? '' : (comp.text ?? '');
      el.appendChild(inner);
    }

    // Click / Selection
    el.onmousedown = (e) => {
      if (e.button !== 0) return;
      if (e.target.classList.contains('resize-handle') || e.target.tagName === 'TEXTAREA') return;
      e.stopPropagation();
      hideContextMenu();

      if (state.formatPainterStyle) {
        pushUndo();
        Object.assign(comp, JSON.parse(JSON.stringify(state.formatPainterStyle)));
        state.formatPainterStyle = null;
        $('#btnFormatPainter')?.classList.remove('active');
        renderAll();
        return;
      }

      if (e.ctrlKey || e.shiftKey) {
        if (state.selectedBandId !== band.id) state.multiIds.clear();
        state.selectedBandId = band.id;
        if (state.multiIds.has(comp.id) && state.multiIds.size > 1) {
          state.multiIds.delete(comp.id);
          if (state.selectedCompId === comp.id) state.selectedCompId = [...state.multiIds][0] || null;
        } else {
          state.multiIds.add(comp.id);
          state.selectedCompId = comp.id;
        }
        state.inspectorTarget = state.selectedCompId || '__form__';
        renderAll();
        return;
      }

      if (!(state.multiIds.has(comp.id) && state.multiIds.size > 1 && state.selectedBandId === band.id)) {
        state.multiIds = new Set([comp.id]);
      }
      state.selectedBandId = band.id;
      state.selectedCompId = comp.id;
      state.inspectorTarget = comp.id;
      setStatusGeometry(comp.xMm, comp.yMm, comp.widthMm, comp.heightMm);
      renderTree();
      renderInspectorTargetSelect();
      renderProperties();
      syncToolbarFromSelection();
      $$('.comp-box').forEach(x => x.classList.remove('selected'));
      el.classList.add('selected');
      startCompDrag(e, band, comp, el);
    };

    el.oncontextmenu = (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (!state.multiIds.has(comp.id)) state.multiIds = new Set([comp.id]);
      state.selectedBandId = band.id;
      state.selectedCompId = comp.id;
      state.inspectorTarget = comp.id;
      renderAll();
      showContextMenu(e.clientX, e.clientY);
    };

    el.ondblclick = (e) => {
      e.stopPropagation();
      if (comp.type === 'Image') openImageEditorModal(comp);
      else if (comp.type === 'Barcode') openBarcodeEditorModal(comp);
      else if (comp.type === 'Chart') openChartEditorModal(comp);
      else if (comp.type !== 'Table') openExpressionEditor(comp);
    };

    // 8 Resize Handles on selected component
    if (isSel) {
      ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'].forEach(dir => {
        const h = document.createElement('div');
        h.className = `resize-handle rh-${dir}`;
        h.onmousedown = (e) => startCompResize(e, comp, el, dir);
        el.appendChild(h);
      });
    }

    return el;
  }

  // ---------- Drag & Resize with Snap Guides ----------
  function startCompDrag(e, band, comp, el) {
    const startX = e.clientX;
    const startY = e.clientY;
    const group = band.components.filter(c => state.multiIds.has(c.id));
    if (!group.includes(comp)) group.push(comp);
    const origins = group.map(c => ({ c, x: c.xMm, y: c.yMm, el: document.querySelector(`.comp-box[data-comp-id="${c.id}"]`) }));
    const others = band.components.filter(c => !group.includes(c));
    const bodyEl = el.parentElement;
    let moved = false;
    const SNAP_MM = 1.2;

    const clearGuides = () => bodyEl.querySelectorAll('.snap-guide').forEach(g => g.remove());
    const addGuide = (kind, mm) => {
      const g = document.createElement('div');
      g.className = `snap-guide ${kind}`;
      if (kind === 'vert') {
        g.style.left = `${mmToPx(mm)}px`;
        g.style.top = '0';
        g.style.bottom = '0';
      } else {
        g.style.top = `${mmToPx(mm)}px`;
        g.style.left = '0';
        g.style.right = '0';
      }
      bodyEl.appendChild(g);
    };

    const onMove = (ev) => {
      const dxPx = (ev.clientX - startX) / state.zoom;
      const dyPx = (ev.clientY - startY) / state.zoom;
      if (Math.hypot(dxPx, dyPx) > 3) moved = true;
      if (!moved) return;

      clearGuides();
      let dxMm = pxToMm(dxPx);
      let dyMm = pxToMm(dyPx);

      if (!ev.altKey) {
        const myOrigin = origins.find(o => o.c === comp);
        const candX = myOrigin.x + dxMm;
        const candY = myOrigin.y + dyMm;
        const candR = candX + comp.widthMm;
        const candB = candY + comp.heightMm;

        for (const o of others) {
          const ox = o.xMm, oy = o.yMm, or = ox + o.widthMm, ob = oy + o.heightMm;
          if (Math.abs(candX - ox) < SNAP_MM) { dxMm = ox - myOrigin.x; addGuide('vert', ox); }
          else if (Math.abs(candR - or) < SNAP_MM) { dxMm = or - comp.widthMm - myOrigin.x; addGuide('vert', or); }
          else if (Math.abs(candX - or) < SNAP_MM) { dxMm = or - myOrigin.x; addGuide('vert', or); }
          else if (Math.abs(candR - ox) < SNAP_MM) { dxMm = ox - comp.widthMm - myOrigin.x; addGuide('vert', ox); }

          if (Math.abs(candY - oy) < SNAP_MM) { dyMm = oy - myOrigin.y; addGuide('horiz', oy); }
          else if (Math.abs(candB - ob) < SNAP_MM) { dyMm = ob - comp.heightMm - myOrigin.y; addGuide('horiz', ob); }
          else if (Math.abs(candY - ob) < SNAP_MM) { dyMm = ob - myOrigin.y; addGuide('horiz', ob); }
          else if (Math.abs(candB - oy) < SNAP_MM) { dyMm = oy - comp.heightMm - myOrigin.y; addGuide('horiz', oy); }
        }
      }

      origins.forEach(item => {
        let nx = snapMm(item.x + dxMm);
        let ny = snapMm(item.y + dyMm);
        nx = Math.max(0, Math.min(state.report.page.widthMm - item.c.widthMm, nx));
        ny = Math.max(0, Math.min(band.heightMm - item.c.heightMm, ny));
        item.c.xMm = nx;
        item.c.yMm = ny;
        if (item.el) {
          item.el.style.left = `${mmToPx(nx)}px`;
          item.el.style.top = `${mmToPx(ny)}px`;
        }
      });
      setStatusGeometry(comp.xMm, comp.yMm, comp.widthMm, comp.heightMm);
    };

    const onUp = () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      clearGuides();
      if (moved) {
        pushUndo();
        renderProperties();
      }
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  function startCompResize(e, comp, el, dir) {
    e.stopPropagation();
    const startX = e.clientX;
    const startY = e.clientY;
    const ox = comp.xMm, oy = comp.yMm, ow = comp.widthMm, oh = comp.heightMm;

    const onMove = (ev) => {
      const dxMm = pxToMm((ev.clientX - startX) / state.zoom);
      const dyMm = pxToMm((ev.clientY - startY) / state.zoom);
      let nx = ox, ny = oy, nw = ow, nh = oh;

      if (dir.includes('e')) nw = Math.max(4, snapMm(ow + dxMm));
      if (dir.includes('s')) nh = Math.max(2, snapMm(oh + dyMm));
      if (dir.includes('w')) {
        const targetW = ow - dxMm;
        if (targetW > 4) { nx = snapMm(ox + dxMm); nw = snapMm(targetW); }
      }
      if (dir.includes('n')) {
        const targetH = oh - dyMm;
        if (targetH > 2) { ny = snapMm(oy + dyMm); nh = snapMm(targetH); }
      }

      comp.xMm = nx; comp.yMm = ny; comp.widthMm = nw; comp.heightMm = nh;
      el.style.left = `${mmToPx(nx)}px`;
      el.style.top = `${mmToPx(ny)}px`;
      el.style.width = `${mmToPx(nw)}px`;
      el.style.height = `${mmToPx(nh)}px`;
      setStatusGeometry(nx, ny, nw, nh);
    };

    const onUp = () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      pushUndo();
      renderProperties();
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  function startBandResize(e, band) {
    e.stopPropagation();
    const startY = e.clientY;
    const oHeight = band.heightMm;

    const onMove = (ev) => {
      const dyMm = pxToMm((ev.clientY - startY) / state.zoom);
      const nh = Math.max(4, snapMm(oHeight + dyMm));
      band.heightMm = nh;
      const bEl = document.querySelector(`.band-box[data-band-id="${band.id}"] .band-body`);
      if (bEl) bEl.style.height = `${mmToPx(nh)}px`;
      setStatus(`${band.name} ${t('height')}: ${nh.toFixed(1)} mm`);
    };

    const onUp = () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', onUp);
      pushUndo();
      renderAll();
    };

    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
  }

  // ---------- Inspector Target Selector Dropdown ----------
  function renderInspectorTargetSelect() {
    const sel = $('#inspectorTargetSelect');
    if (!sel || !state.report) return;

    let html = `<option value="__form__" ${state.inspectorTarget === '__form__' ? 'selected' : ''}>${state.report.page?.formName || 'Form'} : Form</option>`;
    html += `<option value="__page__" ${state.inspectorTarget === '__page__' ? 'selected' : ''}>Page 1 : Page</option>`;

    state.report.bands.forEach(b => {
      html += `<optgroup label="Band: ${escapeHtml(b.name)} [${b.type}]">`;
      html += `<option value="band:${b.id}" ${state.inspectorTarget === 'band:' + b.id ? 'selected' : ''}>${escapeHtml(b.name)} : Band</option>`;
      b.components.forEach(c => {
        const isSel = state.selectedCompId === c.id;
        html += `<option value="${c.id}" ${isSel ? 'selected' : ''}>${escapeHtml(c.name)} : ${c.type}</option>`;
      });
      html += `</optgroup>`;
    });

    sel.innerHTML = html;
    sel.onchange = (e) => {
      const val = e.target.value;
      state.inspectorTarget = val;
      if (val === '__form__' || val === '__page__') {
        state.selectedCompId = null;
        state.multiIds = new Set();
      } else if (val.startsWith('band:')) {
        state.selectedBandId = val.slice(5);
        state.selectedCompId = null;
        state.multiIds = new Set();
      } else {
        const found = findCompAnywhere(val);
        if (found.comp) {
          state.selectedBandId = found.band.id;
          state.selectedCompId = found.comp.id;
          state.multiIds = new Set([found.comp.id]);
        }
      }
      renderCanvas();
      renderTree();
      renderProperties();
    };
  }

  // ---------- Property Inspector ----------
  let propCallbacks = [];
  function renderProperties() {
    const panel = $('#propertiesPanel');
    if (!panel || !state.report) return;
    propCallbacks = [];

    // If target is Form : Form (EXACT REPLICA OF USER'S SCREENSHOT)
    if (state.inspectorTarget === '__form__' || (!state.selectedCompId && !state.selectedBandId)) {
      renderFormProperties(panel);
      return;
    }

    if (state.inspectorTarget === '__page__') {
      renderPageProperties(panel);
      return;
    }

    const { band, comp } = findSelected();
    if (comp) {
      renderComponentProperties(panel, comp);
    } else if (band) {
      renderBandProperties(panel, band);
    } else {
      renderFormProperties(panel);
    }
  }

  // Form Properties
  function renderFormProperties(panel) {
    const pg = state.report.page;
    panel.innerHTML = `
      <div class="prop-group">
        <div class="prop-group-title">Form</div>
        <div class="prop-row">
          <label>${t('pPicture')}</label>
          <div style="display:flex;gap:4px;width:100%;">
            <input type="text" value="${escapeHtml(pg.picture || '')}" placeholder="URL / dosya" data-form-prop="picture" style="flex:1;" />
            <button class="rbtn rbtn-xs" onclick="alert('Görsel URL giriniz')">📁</button>
          </div>
        </div>
        ${propInput(t('pFormName'), 'text', pg.formName || 'Form', v => { pg.formName = v; $('#reportNameLabel').textContent = v + '.sbs'; })}
        <div class="prop-row">
          <label>${t('pGridColumns')}</label>
          <div style="display:flex;gap:4px;width:100%;">
            <input type="number" value="${pg.gridColumns || 96}" min="10" max="200" data-form-prop="gridColumns" style="flex:1;" />
            <button class="rbtn rbtn-xs" id="btnApplyGridCols">${t('apply')}</button>
          </div>
        </div>
        <div class="prop-row">
          <label>${t('pGridVerticalStep')}</label>
          <div style="display:flex;gap:4px;width:100%;">
            <input type="number" value="${pg.gridVerticalStep || 5}" min="1" max="50" data-form-prop="gridVerticalStep" style="flex:1;" />
            <button class="rbtn rbtn-xs" id="btnApplyGridStep">${t('apply')}</button>
          </div>
        </div>
      </div>

      <!-- Default Settings (Exact match to screenshot!) -->
      <div class="prop-group">
        <div class="prop-group-title">${t('pDefaultSettings')}</div>

        <!-- Label Defaults -->
        <div class="prop-sub-label">${t('pDefaultLabel')}</div>
        <div class="prop-row">
          <select data-form-prop="defaultLabelFontFamily" class="rselect" style="width:130px;">
            <option value="Segoe UI, sans-serif" selected>Segoe UI</option>
            <option value="Inter, Arial, sans-serif">Inter / Arial</option>
            <option value="Times New Roman, serif">Times New Roman</option>
          </select>
          <input type="number" data-form-prop="defaultLabelFontSizePt" class="rinput-sm" value="${pg.defaultLabelFontSizePt || 10}" style="width:50px;" />
        </div>
        <div class="prop-btn-row">
          <button class="rbtn ${pg.defaultLabelBold ? 'active' : ''}" data-toggle-def="defaultLabelBold"><b>B</b></button>
          <button class="rbtn ${pg.defaultLabelItalic ? 'active' : ''}" data-toggle-def="defaultLabelItalic"><i>I</i></button>
          <button class="rbtn ${pg.defaultLabelUnderline ? 'active' : ''}" data-toggle-def="defaultLabelUnderline"><u>U</u></button>
        </div>
        <div class="prop-row">
          <label>${t('textColor')}</label>
          <input type="color" data-form-prop="defaultLabelColor" value="${pg.defaultLabelColor || '#000000'}" />
        </div>
        <div class="prop-btn-row">
          <button class="rbtn ${pg.defaultLabelHAlign === 'left' ? 'active' : ''}" data-def-align="left">⫤</button>
          <button class="rbtn ${pg.defaultLabelHAlign === 'center' ? 'active' : ''}" data-def-align="center">⫶</button>
          <button class="rbtn ${pg.defaultLabelHAlign === 'right' ? 'active' : ''}" data-def-align="right">⫥</button>
          <button class="rbtn ${pg.defaultLabelHAlign === 'justify' ? 'active' : ''}" data-def-align="justify">≡</button>
        </div>

        ${propInput(t('pWidth'), 'number', pg.defaultLabelWidthMm || 114, v => { pg.defaultLabelWidthMm = parseFloat(v) || 114; })}

        <!-- Inner Text Defaults -->
        <div class="prop-sub-label">${t('pDefaultInnerText')}</div>
        <div class="prop-row">
          <select data-form-prop="defaultInnerTextFontFamily" class="rselect" style="width:130px;">
            <option value="Segoe UI, sans-serif" selected>Segoe UI</option>
            <option value="Inter, Arial, sans-serif">Inter / Arial</option>
            <option value="Times New Roman, serif">Times New Roman</option>
          </select>
          <input type="number" data-form-prop="defaultInnerTextFontSizePt" class="rinput-sm" value="${pg.defaultInnerTextFontSizePt || 9}" style="width:50px;" />
        </div>
        <div class="prop-btn-row">
          <button class="rbtn ${pg.defaultInnerTextBold ? 'active' : ''}" data-toggle-def="defaultInnerTextBold"><b>B</b></button>
          <button class="rbtn ${pg.defaultInnerTextItalic ? 'active' : ''}" data-toggle-def="defaultInnerTextItalic"><i>I</i></button>
          <button class="rbtn ${pg.defaultInnerTextUnderline ? 'active' : ''}" data-toggle-def="defaultInnerTextUnderline"><u>U</u></button>
        </div>
        <div class="prop-row">
          <label>${t('textColor')}</label>
          <input type="color" data-form-prop="defaultInnerTextColor" value="${pg.defaultInnerTextColor || '#000000'}" />
        </div>

        ${propInput(t('pBackColor'), 'color', pg.defaultBackColor || '#ffffff', v => { pg.defaultBackColor = v; })}
        ${propInput(t('pBorderColor'), 'color', pg.defaultBorderColor || '#cbd5e1', v => { pg.defaultBorderColor = v; })}
        ${propInput(t('pCheckColor'), 'color', pg.defaultCheckColor || '#000000', v => { pg.defaultCheckColor = v; })}
        ${propSelect(t('pBorderWidth'), ['None', 'Thin', 'Medium', 'Thick'], pg.defaultBorderWidth || 'Thin', v => { pg.defaultBorderWidth = v; })}

        <!-- Description Defaults -->
        <div class="prop-sub-label">${t('pDefaultDescription')}</div>
        <div class="prop-row">
          <select data-form-prop="defaultDescriptionFontFamily" class="rselect" style="width:130px;">
            <option value="Segoe UI, sans-serif" selected>Segoe UI</option>
            <option value="Inter, Arial, sans-serif">Inter / Arial</option>
          </select>
          <input type="number" data-form-prop="defaultDescriptionFontSizePt" class="rinput-sm" value="${pg.defaultDescriptionFontSizePt || 9}" style="width:50px;" />
        </div>
        <div class="prop-btn-row">
          <button class="rbtn ${pg.defaultDescriptionBold ? 'active' : ''}" data-toggle-def="defaultDescriptionBold"><b>B</b></button>
          <button class="rbtn ${pg.defaultDescriptionItalic ? 'active' : ''}" data-toggle-def="defaultDescriptionItalic"><i>I</i></button>
          <button class="rbtn ${pg.defaultDescriptionUnderline ? 'active' : ''}" data-toggle-def="defaultDescriptionUnderline"><u>U</u></button>
        </div>

        <!-- 4-Box Padding (+0 +0 +0 +8 from screenshot!) -->
        <div class="prop-sub-label">${t('pPadding')}</div>
        <div class="padding-4box">
          <div class="p4-item"><span>${t('pPadLeft')}</span><input type="number" step="1" value="${pg.defaultPaddingLeftMm ?? 0}" data-def-pad="left" /></div>
          <div class="p4-item"><span>${t('pPadTop')}</span><input type="number" step="1" value="${pg.defaultPaddingTopMm ?? 0}" data-def-pad="top" /></div>
          <div class="p4-item"><span>${t('pPadRight')}</span><input type="number" step="1" value="${pg.defaultPaddingRightMm ?? 0}" data-def-pad="right" /></div>
          <div class="p4-item"><span>${t('pPadBottom')}</span><input type="number" step="1" value="${pg.defaultPaddingBottomMm ?? 8}" data-def-pad="bottom" /></div>
        </div>
      </div>

      <!-- Watermark Settings -->
      <div class="prop-group">
        <div class="prop-group-title">${t('watermark')}</div>
        <label class="rfield" style="margin-bottom:6px;"><input type="checkbox" id="chkDefWatermark" ${pg.watermark?.enabled ? 'checked' : ''} /> <span>${t('watermark')} Aktif</span></label>
        ${propInput(t('watermarkText'), 'text', pg.watermark?.text || 'Trial', v => { if (!pg.watermark) pg.watermark = {}; pg.watermark.text = v; renderCanvas(); })}
        ${propInput(t('pAngle'), 'number', pg.watermark?.angle || -45, v => { if (!pg.watermark) pg.watermark = {}; pg.watermark.angle = parseFloat(v) || -45; renderCanvas(); })}
        ${propInput('Opaklık (0.05-1)', 'number', pg.watermark?.opacity || 0.12, v => { if (!pg.watermark) pg.watermark = {}; pg.watermark.opacity = parseFloat(v) || 0.12; renderCanvas(); })}
        ${propInput(t('pFontSize'), 'number', pg.watermark?.fontSizePt || 76, v => { if (!pg.watermark) pg.watermark = {}; pg.watermark.fontSizePt = parseFloat(v) || 76; renderCanvas(); })}
        ${propInput(t('textColor'), 'color', pg.watermark?.color || '#64748b', v => { if (!pg.watermark) pg.watermark = {}; pg.watermark.color = v; renderCanvas(); })}
      </div>
    `;

    bindPropInputs(panel);

    // Apply buttons for Grid Columns & Step
    const btnCols = $('#btnApplyGridCols');
    if (btnCols) {
      btnCols.onclick = () => {
        pushUndo();
        const inp = panel.querySelector('[data-form-prop="gridColumns"]');
        if (inp) pg.gridColumns = parseInt(inp.value, 10) || 96;
        setStatus(`Grid Columns: ${pg.gridColumns}`);
      };
    }
    const btnStep = $('#btnApplyGridStep');
    if (btnStep) {
      btnStep.onclick = () => {
        pushUndo();
        const inp = panel.querySelector('[data-form-prop="gridVerticalStep"]');
        if (inp) pg.gridVerticalStep = parseInt(inp.value, 10) || 5;
        setStatus(`Grid Vertical Step: ${pg.gridVerticalStep}`);
      };
    }

    // Default padding inputs
    panel.querySelectorAll('[data-def-pad]').forEach(inp => {
      inp.onchange = () => {
        pushUndo();
        const side = inp.dataset.defPad;
        const v = parseFloat(inp.value) || 0;
        if (side === 'left') pg.defaultPaddingLeftMm = v;
        if (side === 'top') pg.defaultPaddingTopMm = v;
        if (side === 'right') pg.defaultPaddingRightMm = v;
        if (side === 'bottom') pg.defaultPaddingBottomMm = v;
      };
    });

    // Checkbox watermark
    const chkWm = $('#chkDefWatermark');
    if (chkWm) {
      chkWm.onchange = () => {
        pushUndo();
        if (!pg.watermark) pg.watermark = {};
        pg.watermark.enabled = chkWm.checked;
        renderCanvas();
      };
    }
  }

  function renderPageProperties(panel) {
    const pg = state.report.page;
    panel.innerHTML = `
      <div class="prop-group">
        <div class="prop-group-title">${t('pPage')}</div>
        ${propSelect(t('paper'), ['A4', 'A5', 'Letter', 'Custom'], pg.paperSize, v => { pg.paperSize = v; })}
        ${propSelect(t('orientation'), ['Portrait', 'Landscape'], pg.orientation, v => { pg.orientation = v; })}
        ${propInput(t('pWidth'), 'number', pg.widthMm, v => { pg.widthMm = parseFloat(v) || 210; })}
        ${propInput(t('pHeight'), 'number', pg.heightMm, v => { pg.heightMm = parseFloat(v) || 297; })}
        ${propInput(t('gridMm'), 'number', pg.gridSizeMm || 2, v => { pg.gridSizeMm = parseFloat(v) || 2; })}
      </div>
    `;
    bindPropInputs(panel);
  }

  function renderComponentProperties(panel, comp) {
    panel.innerHTML = `
      <!-- Identification & Text -->
      <div class="prop-group">
        <div class="prop-group-title">${t('pComponent')} (${comp.type})</div>
        ${propInput(t('pName'), 'text', comp.name, v => { comp.name = v; })}
        ${comp.type === 'Input' || comp.type === 'CheckBox' || comp.type === 'Radio' || comp.type === 'ComboBox' ? propInput(t('pLabel'), 'text', comp.label || '', v => { comp.label = v; }) : ''}
        ${comp.type === 'Input' ? propInput(t('pPlaceholder'), 'text', comp.placeholder || '', v => { comp.placeholder = v; }) : ''}
        ${comp.type === 'ComboBox' ? propInput(t('pDropdownItems'), 'text', comp.items || '', v => { comp.items = v; }) : ''}
        ${comp.type !== 'Shape' && comp.type !== 'Line' && comp.type !== 'Chart' && comp.type !== 'Table' ? propTextarea(t('pText'), comp.text, v => { comp.text = v; }) : ''}
        ${propInput(t('pFormat'), 'text', comp.format || '', v => { comp.format = v || null; }, 'N2, C2, dd.MM.yyyy')}
      </div>

      <!-- Position & Size -->
      <div class="prop-group">
        <div class="prop-group-title">${t('pPosSize')}</div>
        ${propInput('X (mm)', 'number', comp.xMm, v => { comp.xMm = parseFloat(v) || 0; })}
        ${propInput('Y (mm)', 'number', comp.yMm, v => { comp.yMm = parseFloat(v) || 0; })}
        ${propInput(t('pWidth'), 'number', comp.widthMm, v => { comp.widthMm = Math.max(2, parseFloat(v) || 10); })}
        ${propInput(t('pHeight'), 'number', comp.heightMm, v => { comp.heightMm = Math.max(1, parseFloat(v) || 6); })}
        ${propInput(t('pAngle'), 'number', comp.angle || 0, v => { comp.angle = parseFloat(v) || 0; })}
      </div>

      <!-- 4-Box Padding -->
      <div class="prop-group">
        <div class="prop-group-title">${t('pPadding')}</div>
        <div class="padding-4box">
          <div class="p4-item"><span>${t('pPadLeft')}</span><input type="number" step="0.5" value="${comp.paddingLeftMm || 0}" data-pad="left" /></div>
          <div class="p4-item"><span>${t('pPadTop')}</span><input type="number" step="0.5" value="${comp.paddingTopMm || 0}" data-pad="top" /></div>
          <div class="p4-item"><span>${t('pPadRight')}</span><input type="number" step="0.5" value="${comp.paddingRightMm || 0}" data-pad="right" /></div>
          <div class="p4-item"><span>${t('pPadBottom')}</span><input type="number" step="0.5" value="${comp.paddingBottomMm || 0}" data-pad="bottom" /></div>
        </div>
      </div>

      <!-- Appearance & Borders -->
      <div class="prop-group">
        <div class="prop-group-title">${t('pAppearance')}</div>
        ${propInput(t('pFontSize'), 'number', comp.fontSizePt, v => { comp.fontSizePt = parseFloat(v) || 10; })}
        ${propInput(t('pTextColor'), 'color', comp.color || '#111827', v => { comp.color = v; })}
        ${propInput(t('pBackColor'), 'text', comp.backgroundColor || 'transparent', v => { comp.backgroundColor = v; })}
        ${propInput(t('pBorderWidth'), 'number', comp.borderWidthPx || 0, v => { comp.borderWidthPx = parseFloat(v) || 0; })}
        ${propInput(t('pBorderColor'), 'color', comp.borderColor || '#cbd5e1', v => { comp.borderColor = v; })}
        ${propSelect(t('pBorderStyle'), ['Solid', 'Dashed', 'Dotted', 'Double'], comp.borderStyle || 'Solid', v => { comp.borderStyle = v; })}
        ${propInput(t('pCornerRadius'), 'number', comp.cornerRadiusPx || 0, v => { comp.cornerRadiusPx = parseFloat(v) || 0; })}
      </div>

      <!-- Form Controls Settings -->
      ${comp.type === 'CheckBox' || comp.type === 'Radio' ? `
      <div class="prop-group">
        <div class="prop-group-title">Form Değeri</div>
        <label class="rfield"><input type="checkbox" id="chkCompChecked" ${comp.checked ? 'checked' : ''} /> <span>${t('pChecked')}</span></label>
        ${comp.type === 'Radio' ? propInput('Grup Adı (GroupName)', 'text', comp.groupName || 'Group1', v => { comp.groupName = v; }) : ''}
      </div>` : ''}

      <!-- Specifics -->
      ${comp.type === 'Barcode' ? `
      <div class="prop-group">
        <div class="prop-group-title">Barkod</div>
        ${propSelect(t('pBarcodeFormat'), ['Code128', 'QR', 'EAN13'], comp.barcodeFormat || 'Code128', v => { comp.barcodeFormat = v; })}
      </div>` : ''}

      ${comp.type === 'Chart' ? `
      <div class="prop-group">
        <div class="prop-group-title">Grafik</div>
        ${propSelect(t('pChartType'), ['Bar', 'Line', 'Pie', 'Doughnut'], comp.chartType || 'Bar', v => { comp.chartType = v; })}
        ${propInput(t('pCategoryField'), 'text', comp.categoryField || '', v => { comp.categoryField = v; })}
        ${propInput(t('pValueField'), 'text', comp.valueField || '', v => { comp.valueField = v; })}
      </div>` : ''}

      <!-- Conditional Formatting -->
      <div class="prop-group">
        <div class="prop-group-title">
          <span>${t('pConditions')}</span>
          <button class="rbtn rbtn-xs" id="btnAddCond">${t('pAddCondition')}</button>
        </div>
        <div id="condList"></div>
      </div>
    `;
    renderConditions(comp);
    bindPropInputs(panel);

    // Padding inputs
    panel.querySelectorAll('.padding-4box input').forEach(inp => {
      inp.onchange = () => {
        pushUndo();
        const side = inp.dataset.pad;
        const val = parseFloat(inp.value) || 0;
        if (side === 'left') comp.paddingLeftMm = val;
        if (side === 'top') comp.paddingTopMm = val;
        if (side === 'right') comp.paddingRightMm = val;
        if (side === 'bottom') comp.paddingBottomMm = val;
        renderCanvas();
      };
    });

    const chk = $('#chkCompChecked');
    if (chk) {
      chk.onchange = () => {
        pushUndo();
        comp.checked = chk.checked;
        renderCanvas();
      };
    }
  }

  function renderBandProperties(panel, band) {
    const mergedDs = new Set([
      ...state.demoSources.map(d => d.name),
      ...(state.report.dataSources || []).map(d => d.name)
    ]);
    const dsOptions = ['', ...mergedDs];
    panel.innerHTML = `
      <div class="prop-group">
        <div class="prop-group-title">${t('pBand')} (${band.type})</div>
        ${propInput(t('pName'), 'text', band.name, v => { band.name = v; })}
        ${propInput(t('pBandHeight'), 'number', band.heightMm, v => { band.heightMm = Math.max(4, parseFloat(v) || 10); })}
        ${propInput(t('pBackColor'), 'text', band.backgroundColor || 'transparent', v => { band.backgroundColor = v; })}
        ${band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData' ? propSelect(t('pDataSource'), dsOptions, band.dataSource || '', v => { band.dataSource = v || null; }) : ''}
        ${band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData' ? propInput(t('pSortBy'), 'text', band.sortBy || '', v => { band.sortBy = v || null; }) : ''}
        ${band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData' ? propSelect(t('pSortDir'), ['asc', 'desc'], band.sortDirection || 'asc', v => { band.sortDirection = v; }) : ''}
        ${band.type === 'Data' || band.type === 'Table' || band.type === 'HierarchicalData' ? propInput(t('pFilter'), 'text', band.filterExpression || '', v => { band.filterExpression = v || null; }, t('pFilterPh')) : ''}
        ${band.type === 'Data' || band.type === 'Table' ? propInput('Veri Sayısı (CountData)', 'number', band.countData || 0, v => { band.countData = parseInt(v, 10) || 0; }) : ''}
        ${band.type === 'Data' || band.type === 'Table' ? propInput(t('pZebra'), 'text', band.evenBackgroundColor || '', v => { band.evenBackgroundColor = v || null; }, '#f8fafc') : ''}
        ${band.type === 'GroupHeader' ? propInput(t('pGroupBy'), 'text', band.groupBy || '', v => { band.groupBy = v || null; }, 'CategoryName') : ''}
        ${band.type === 'HierarchicalData' ? propInput('Anahtar Sütun (KeyColumn)', 'text', band.keyDataColumn || '', v => { band.keyDataColumn = v || null; }, 'Id') : ''}
        ${band.type === 'HierarchicalData' ? propInput('Üst Sütun (MasterKey)', 'text', band.masterKeyDataColumn || '', v => { band.masterKeyDataColumn = v || null; }, 'ParentId') : ''}
        ${band.type === 'HierarchicalData' ? propInput('Girinti (Indent mm)', 'number', band.indentMm ?? 8, v => { band.indentMm = parseFloat(v) || 0; }) : ''}
      </div>
      <div class="prop-group">
        <div class="prop-group-title">Davranış (Behavior)</div>
        ${propCheck('Otomatik Büyü (CanGrow)', !!band.canGrow, v => { band.canGrow = v; })}
        ${propCheck('Her Sayfada Yazdır (PrintOnAllPages)', band.printOnAllPages !== false, v => { band.printOnAllPages = v; })}
        ${propCheck('Önce Yeni Sayfa (NewPageBefore)', !!band.newPageBefore, v => { band.newPageBefore = v; })}
        ${propCheck('Sonra Yeni Sayfa (NewPageAfter)', !!band.newPageAfter, v => { band.newPageAfter = v; })}
      </div>
    `;
    bindPropInputs(panel);
  }

  function renderConditions(comp) {
    const list = $('#condList');
    if (!list) return;
    list.innerHTML = '';
    if (!comp.conditions) comp.conditions = [];
    comp.conditions.forEach((c, idx) => {
      const row = document.createElement('div');
      row.className = 'cond-row';
      row.innerHTML = `
        <div class="cond-line">
          <input type="text" class="c-expr" value="${escapeHtml(c.expression)}" placeholder="${t('pCondPlaceholder')}" />
          <button class="rbtn rbtn-icon c-del" title="${t('delete')}">✕</button>
        </div>
        <div class="cond-line">
          <label style="font-size:10.5px;">Metin: <input type="color" class="c-color" value="${c.textColor || '#dc2626'}" /></label>
          <label style="font-size:10.5px;">Arka: <input type="color" class="c-bg" value="${c.backgroundColor || '#fee2e2'}" /></label>
          <label style="font-size:10.5px;"><input type="checkbox" class="c-bold" ${c.bold ? 'checked' : ''} /> ${t('bold')}</label>
          <label style="font-size:10.5px;"><input type="checkbox" class="c-hide" ${c.visible === false ? 'checked' : ''} /> ${t('pHide')}</label>
        </div>
      `;
      const commit = () => {
        pushUndo();
        c.expression = row.querySelector('.c-expr').value;
        c.textColor = row.querySelector('.c-color').value;
        c.backgroundColor = row.querySelector('.c-bg').value;
        c.bold = row.querySelector('.c-bold').checked;
        c.visible = row.querySelector('.c-hide').checked ? false : null;
      };
      row.querySelectorAll('input').forEach(i => i.onchange = commit);
      row.querySelector('.c-del').onclick = () => {
        pushUndo();
        comp.conditions.splice(idx, 1);
        renderConditions(comp);
      };
      list.appendChild(row);
    });
    const addBtn = $('#btnAddCond');
    if (addBtn) addBtn.onclick = () => {
      pushUndo();
      comp.conditions.push({ expression: '', textColor: '#dc2626', backgroundColor: null, bold: true, visible: null });
      renderConditions(comp);
    };
  }

  function bindPropInputs(panel) {
    panel.querySelectorAll('[data-prop-idx]').forEach(input => {
      const idx = parseInt(input.dataset.propIdx, 10);
      const cb = propCallbacks[idx];
      input.onchange = () => {
        pushUndo();
        cb(input.value);
        renderAll();
      };
    });
  }

  function propInput(label, type, val, onChange, placeholder = '') {
    const idx = propCallbacks.length;
    propCallbacks.push(onChange);
    return `<div class="prop-row"><label>${label}</label><input type="${type}" value="${escapeHtml(val)}" placeholder="${placeholder}" data-prop-idx="${idx}" /></div>`;
  }
  function propTextarea(label, val, onChange) {
    const idx = propCallbacks.length;
    propCallbacks.push(onChange);
    return `<div class="prop-row"><label>${label}</label><div style="display:flex;gap:3px;"><textarea rows="2" data-prop-idx="${idx}">${escapeHtml(val)}</textarea><button class="rbtn rbtn-icon" style="height:auto;" title="İfade Düzenleyici" onclick="document.dispatchEvent(new CustomEvent('sbs:openexpr'))">…</button></div></div>`;
  }
  function propSelect(label, options, val, onChange) {
    const idx = propCallbacks.length;
    propCallbacks.push(onChange);
    const opts = options.map(o => `<option value="${escapeHtml(o)}" ${o === val ? 'selected' : ''}>${o || t('pSelect')}</option>`).join('');
    return `<div class="prop-row"><label>${label}</label><select data-prop-idx="${idx}">${opts}</select></div>`;
  }
  function escapeHtml(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  document.addEventListener('sbs:openexpr', () => {
    const { comp } = findSelected();
    if (comp) openExpressionEditor(comp);
  });

  // ---------- Tree & Dictionary Panels ----------
  function renderTree() {
    const trees = [$('#reportTree'), $('#inspectorTreeContent')];
    trees.forEach(tree => {
      if (!tree || !state.report) return;
      tree.innerHTML = '';
      state.report.bands.forEach(b => {
        const bNode = document.createElement('div');
        bNode.className = 'tree-node band-node' + (b.id === state.selectedBandId && !state.selectedCompId ? ' selected' : '');
        bNode.innerHTML = `<span>📂 <b>${escapeHtml(b.name)}</b> <small style="opacity:.6;">[${b.type}]</small></span>`;
        bNode.onclick = (e) => {
          e.stopPropagation();
          state.selectedBandId = b.id;
          state.selectedCompId = null;
          state.inspectorTarget = 'band:' + b.id;
          state.multiIds = new Set();
          renderAll();
        };

        const children = document.createElement('div');
        children.className = 'tree-children';
        b.components.forEach(c => {
          const cNode = document.createElement('div');
          cNode.className = 'tree-node comp-node' + (c.id === state.selectedCompId ? ' selected' : '');
          cNode.innerHTML = `<span>◽ ${escapeHtml(c.name)} <small style="opacity:.6;">[${c.type}]</small></span>`;
          cNode.onclick = (e) => {
            e.stopPropagation();
            state.selectedBandId = b.id;
            state.selectedCompId = c.id;
            state.inspectorTarget = c.id;
            state.multiIds = new Set([c.id]);
            renderAll();
          };
          children.appendChild(cNode);
        });

        bNode.appendChild(children);
        tree.appendChild(bNode);
      });
    });
  }

  function renderDictionary() {
    const dicts = [$('#dictionaryTree'), $('#inspectorDictContent')];
    dicts.forEach(tree => {
      if (!tree || !state.report) return;
      tree.innerHTML = '';
      const allSources = [
        ...state.demoSources,
        ...(state.report.dataSources || []).map(ds => ({
          name: ds.name,
          alias: ds.alias || ds.name,
          columns: (ds.columns || []).map(c => ({ name: c.name, type: c.dataType || 'string' }))
        }))
      ];

      allSources.forEach(ds => {
        const dsNode = document.createElement('div');
        dsNode.className = 'dict-ds';
        const head = document.createElement('div');
        head.className = 'dict-ds-head';
        head.draggable = true;
        head.style.cursor = 'grab';
        head.title = 'Sayfaya sürükleyin veya ⚡ tıklayarak otomatik tablo/bant oluşturun';
        head.ondragstart = (e) => {
          e.dataTransfer.setData('application/sbs-ds-wizard', ds.name);
        };
        head.innerHTML = `<span>🗄 <b>${escapeHtml(ds.alias || ds.name)}</b></span>`;

        const acts = document.createElement('div');
        acts.className = 'dict-ds-acts';

        const btnWiz = document.createElement('button');
        btnWiz.className = 'rbtn rbtn-xs';
        btnWiz.textContent = '⚡';
        btnWiz.title = 'Veri Bandı Yerleşim Sihirbazı (Otomatik Header + Data oluştur)';
        btnWiz.onclick = (e) => { e.stopPropagation(); openDataPlacementWizard(ds.name); };
        acts.appendChild(btnWiz);

        const isCustom = (state.report.dataSources || []).some(x => x.name.toLowerCase() === ds.name.toLowerCase());
        if (isCustom) {
          const btnAddCol = document.createElement('button');
          btnAddCol.className = 'rbtn rbtn-xs';
          btnAddCol.textContent = '+';
          btnAddCol.title = 'Bu veri kaynağına yeni kolon ekle';
          btnAddCol.onclick = (e) => {
            e.stopPropagation();
            const colName = prompt(`"${ds.name}" için yeni kolon adı (örn: TCKIMLIKNO, AD, UNVAN):`);
            if (!colName || !colName.trim()) return;
            pushUndo();
            const targetDs = state.report.dataSources.find(x => x.name.toLowerCase() === ds.name.toLowerCase());
            if (targetDs) {
              if (!targetDs.columns) targetDs.columns = [];
              targetDs.columns.push({ name: colName.trim(), dataType: 'string' });
              renderDictionary();
              setStatus(`Kolon eklendi: ${ds.name}.${colName.trim()}`);
            }
          };
          acts.appendChild(btnAddCol);
        }

        const btnView = document.createElement('button');
        btnView.className = 'rbtn rbtn-xs';
        btnView.textContent = '👁';
        btnView.title = 'Veriyi Gör (View Data)';
        btnView.onclick = (e) => { e.stopPropagation(); openDataPreviewModal(ds.name); };

        const btnBind = document.createElement('button');
        btnBind.className = 'rbtn rbtn-xs';
        btnBind.textContent = '🔗';
        btnBind.title = 'Seçili Data/Tablo bandına bağla';
        btnBind.onclick = (e) => {
          e.stopPropagation();
          const b = findBand(state.selectedBandId) || state.report.bands.find(x => x.type === 'Data' || x.type === 'Table');
          if (!b) { alert('Önce bir Data veya Tablo bandı ekleyin/seçin.'); return; }
          pushUndo();
          b.dataSource = ds.name;
          renderAll();
          setStatus(`"${ds.name}" veri kaynağı "${b.name}" bandına bağlandı.`);
        };

        acts.appendChild(btnView);
        acts.appendChild(btnBind);
        head.appendChild(acts);
        dsNode.appendChild(head);

        const colList = document.createElement('div');
        colList.className = 'dict-cols';

        ds.columns.forEach(col => {
          const colEl = document.createElement('div');
          colEl.className = 'dict-col';
          colEl.draggable = true;
          colEl.innerHTML = `<span class="col-type">${(col.type || 'S')[0].toUpperCase()}</span><span>${escapeHtml(col.name)}</span>`;
          colEl.ondragstart = (e) => {
            e.dataTransfer.setData('application/sbs-expr', `{${ds.name}.${col.name}}`);
            e.dataTransfer.setData('application/sbs-ds', ds.name);
          };
          colEl.ondblclick = () => {
            const b = findBand(state.selectedBandId) || state.report.bands.find(x => x.type === 'Data') || state.report.bands[0];
            if (!b) return;
            if ((b.type === 'Data' || b.type === 'Table') && !b.dataSource) b.dataSource = ds.name;
            addComponentToBand(b, 'Text', 10, 2, `{${ds.name}.${col.name}}`);
          };
          colList.appendChild(colEl);
        });

        dsNode.appendChild(colList);
        tree.appendChild(dsNode);
      });
    });

    // Render report Variables / Parameters in right Dictionary panel
    const varsHost = $('#inspectorVarsContent');
    if (varsHost && state.report) {
      varsHost.innerHTML = '';
      const vars = state.report.variables || {};
      const keys = Object.keys(vars);
      if (keys.length === 0) {
        varsHost.innerHTML = '<div style="font-size:10.5px;color:var(--text-muted);padding:2px 4px;">Henüz parametre yok (+ Değişken ile ekleyin)</div>';
      } else {
        keys.forEach(k => {
          const vEl = document.createElement('div');
          vEl.className = 'dict-col';
          vEl.draggable = true;
          vEl.style.justifyContent = 'space-between';
          vEl.innerHTML = `<span><span class="col-type">P</span> {${escapeHtml(k)}}</span><span class="var-del" title="Sil" style="opacity:.6;cursor:pointer;">✕</span>`;
          vEl.ondragstart = (e) => {
            e.dataTransfer.setData('application/sbs-expr', `{${k}}`);
          };
          vEl.ondblclick = () => {
            const b = findBand(state.selectedBandId) || state.report.bands[0];
            if (b) addComponentToBand(b, 'Text', 10, 2, `{${k}}`);
          };
          vEl.querySelector('.var-del').onclick = (e) => {
            e.stopPropagation();
            pushUndo();
            delete state.report.variables[k];
            renderDictionary();
          };
          varsHost.appendChild(vEl);
        });
      }
    }
  }

  // ---------- Add Component / Band ----------
  function addComponentToBand(band, type, xMm = 10, yMm = 5, initialText = '') {
    pushUndo();
    const defaults = {
      Text: { widthMm: 40, heightMm: 8, text: initialText || 'Metin' },
      Input: { widthMm: 48, heightMm: 7.5, text: '', placeholder: 'Bilgi girin...' },
      CheckBox: { widthMm: 35, heightMm: 6, text: initialText || 'Onay', checked: false },
      Radio: { widthMm: 30, heightMm: 6, text: initialText || 'Seçenek', checked: false, groupName: 'Group1' },
      ComboBox: { widthMm: 48, heightMm: 7.5, text: 'Seçenek 1', items: 'Seçenek 1,Seçenek 2,Seçenek 3' },
      DatePicker: { widthMm: 45, heightMm: 7.5, text: '11/20/2026' },
      Signature: { widthMm: 45, heightMm: 16, text: 'İmza / Kaşe', borderStyle: 'Dashed', borderWidthPx: 1 },
      Button: { widthMm: 35, heightMm: 8, text: 'Tamam' },
      Image: { widthMm: 32, heightMm: 24, text: '' },
      Shape: { widthMm: 30, heightMm: 15, text: '', shapeType: 'Rectangle', backgroundColor: '#e2e8f0' },
      Line: { widthMm: 50, heightMm: 2, text: '', borderWidthPx: 1 },
      Barcode: { widthMm: 48, heightMm: 14, text: '1234567890', barcodeFormat: 'Code128' },
      Chart: { widthMm: 80, heightMm: 45, text: '', chartType: 'Bar', categoryField: 'CategoryName', valueField: 'UnitsInStock' },
      Table: { widthMm: 100, heightMm: 25, text: '' },
      CrossTab: { widthMm: 100, heightMm: 30, text: '' },
      RichText: { widthMm: 60, heightMm: 15, text: initialText || 'Zengin Metin' },
      SystemText: { widthMm: 40, heightMm: 8, text: initialText || '{PageNumber} / {TotalPageCount}' },
      Panel: { widthMm: 60, heightMm: 35, text: '', borderWidthPx: 1, borderStyle: 'Solid' }
    };

    const d = defaults[type] || defaults.Text;
    const name = `${type}${band.components.length + 1}`;
    // Apply extra tool properties from flyout selection (shapeType, lineOrientation, barcodeFormat, chartType)
    const extra = state._toolExtra || {};
    const comp = {
      id: uid(),
      type,
      name,
      xMm,
      yMm,
      widthMm: d.widthMm,
      heightMm: d.heightMm,
      text: d.text,
      fontFamily: 'Segoe UI, Inter, sans-serif',
      fontSizePt: 10,
      color: '#111827',
      backgroundColor: d.backgroundColor || (type === 'Input' || type === 'DatePicker' ? '#ffffff' : 'transparent'),
      borderColor: '#cbd5e1',
      borderWidthPx: d.borderWidthPx ?? (type === 'Input' || type === 'DatePicker' || type === 'ComboBox' ? 1 : 0),
      borderStyle: d.borderStyle || 'Solid',
      borderSides: 'All',
      cornerRadiusPx: (type === 'Input' || type === 'DatePicker' || type === 'ComboBox') ? 2 : 0,
      placeholder: d.placeholder,
      items: d.items,
      checked: d.checked,
      groupName: d.groupName,
      shapeType: extra.shapeType || d.shapeType,
      lineOrientation: extra.lineOrientation || d.lineOrientation,
      barcodeFormat: extra.barcodeFormat || d.barcodeFormat,
      chartType: extra.chartType || d.chartType,
      categoryField: d.categoryField,
      valueField: d.valueField,
      conditions: []
    };

    band.components.push(comp);
    state.selectedBandId = band.id;
    state.selectedCompId = comp.id;
    state.inspectorTarget = comp.id;
    state.multiIds = new Set([comp.id]);
    renderAll();
    setStatus(`Eklendi: ${comp.name} (${comp.type})`);
  }

  const BAND_PRIORITY = {
    PageHeader: 10, ReportTitle: 20, Header: 30, ColumnHeader: 35,
    GroupHeader: 40, Data: 50, Table: 51, HierarchicalData: 52,
    ChildBand: 53, EmptyBand: 54, GroupFooter: 60, ColumnFooter: 65,
    Footer: 70, ReportSummary: 80, Overlay: 90, PageFooter: 100
  };

  function addBandToReport(type) {
    pushUndo();
    const bandHeights = {
      PageHeader: 14, ReportTitle: 22, Header: 10, ColumnHeader: 10,
      GroupHeader: 10, Data: 12, Table: 24, HierarchicalData: 12,
      ChildBand: 10, EmptyBand: 12, GroupFooter: 10, ColumnFooter: 10,
      Footer: 10, ReportSummary: 18, Overlay: 16, PageFooter: 12
    };
    const sameCount = state.report.bands.filter(x => x.type === type).length;
    const b = {
      id: uid(),
      type,
      name: `${type}Band${sameCount + 1}`,
      heightMm: bandHeights[type] || 12,
      backgroundColor: 'transparent',
      components: []
    };

    // Semantic auto-placement: insert right before the first band with higher priority
    const newPrio = BAND_PRIORITY[type] ?? 50;
    const insertIdx = state.report.bands.findIndex(existing => (BAND_PRIORITY[existing.type] ?? 50) > newPrio);
    if (insertIdx === -1) {
      state.report.bands.push(b);
    } else {
      state.report.bands.splice(insertIdx, 0, b);
    }

    state.selectedBandId = b.id;
    state.selectedCompId = null;
    state.inspectorTarget = '__band__';
    state.multiIds = new Set();
    renderAll();
    setStatus(`Band eklendi: ${b.name}`);
  }

  // ---------- Toolbar & UI Event Binding ----------
  function bindUIEvents() {
    // Mode Switch Tabs (Ribbon Style)
    $$('.ribbon-tab').forEach(btn => {
      btn.onclick = () => {
        const tab = btn.dataset.tab;
        if (tab === 'preview' || tab === 'pdfpreview') {
          switchMode(tab);
          return;
        }
        if (state.mode !== 'design') {
          state.lastDesignTab = tab;
          switchMode('design');
          return;
        }
        state.lastDesignTab = tab;
        $$('.ribbon-tab').forEach(b => b.classList.remove('active'));
        $$('.ribbon-panel').forEach(p => p.classList.remove('active'));
        btn.classList.add('active');
        $(`.ribbon-panel[data-panel="${tab}"]`)?.classList.add('active');
      };
    });

    $('#btnReturnToDesign')?.addEventListener('click', () => switchMode('design'));

    // Inspector Bottom Tabs: Properties / Form Tree / Dictionary
    $$('.ib-tab').forEach(btn => {
      btn.onclick = () => {
        const target = btn.dataset.ib;
        state.inspectorTab = target;
        $$('.ib-tab').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');

        $('#propertiesPanel').hidden = target !== 'properties';
        $('#inspectorTreePanel').hidden = target !== 'tree';
        $('#inspectorDictPanel').hidden = target !== 'dictionary';

        if (target === 'tree') renderTree();
        if (target === 'dictionary') renderDictionary();
        if (target === 'properties') renderProperties();
      };
    });

    // Inspector View Mode: Categorized / Alphabetical
    $('#btnViewCategorized')?.addEventListener('click', () => {
      state.inspectorView = 'categorized';
      $('#btnViewCategorized').classList.add('active');
      $('#btnViewAlphabetical').classList.remove('active');
      renderProperties();
    });
    $('#btnViewAlphabetical')?.addEventListener('click', () => {
      state.inspectorView = 'alphabetical';
      $('#btnViewAlphabetical').classList.add('active');
      $('#btnViewCategorized').classList.remove('active');
      renderProperties();
    });

    // Left Slim Tools (16 Tools)
    $$('.tool-btn[data-tool]').forEach(btn => {
      btn.onclick = () => setActiveTool(btn.dataset.tool);
    });

    // === Flyout: Close all when clicking outside ===
    document.addEventListener('click', (e) => {
      if (!e.target.closest('.tool-group')) {
        $$('.tool-group').forEach(g => g.classList.remove('open'));
      }
    });

    // === Flyout: Toggle open on trigger click ===
    $$('.tg-trigger').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const grp = btn.closest('.tool-group');
        const wasOpen = grp.classList.contains('open');
        $$('.tool-group').forEach(g => g.classList.remove('open'));
        if (!wasOpen) grp.classList.add('open');
      });
    });

    // === Flyout: Add Band items ===
    document.addEventListener('click', (e) => {
      const fi = e.target.closest('.flyout-item[data-add-band]');
      if (fi) {
        e.stopPropagation();
        addBandToReport(fi.dataset.addBand);
        fi.closest('.tool-group')?.classList.remove('open');
      }
    });

    // === Flyout: Set Tool items ===
    document.addEventListener('click', (e) => {
      const fi = e.target.closest('.flyout-item[data-tool-act]');
      if (fi) {
        e.stopPropagation();
        const tool = fi.dataset.toolAct;
        const extra = {};
        if (fi.dataset.shapeType) extra.shapeType = fi.dataset.shapeType;
        if (fi.dataset.lineOrient) extra.lineOrientation = fi.dataset.lineOrient;
        if (fi.dataset.barcodeFmt) extra.barcodeFormat = fi.dataset.barcodeFmt;
        if (fi.dataset.chartType) extra.chartType = fi.dataset.chartType;
        setActiveTool(tool, extra);
        fi.closest('.tool-group')?.classList.remove('open');
      }
    });

    // === Canvas Document Tabs (Page1 / Önizleme / PDF Önizleme) ===
    $$('.cdoc-tab[data-doctab]').forEach(tab => {
      tab.addEventListener('click', () => {
        switchMode(tab.dataset.doctab);
      });
    });
    $('#btnAddReportPage')?.addEventListener('click', addReportPage);

    $('#btnQuickWatermark')?.addEventListener('click', () => {
      const pg = state.report.page;
      if (!pg.watermark) pg.watermark = { enabled: false, text: 'Trial', angle: -45, opacity: 0.12, fontSizePt: 76 };
      pg.watermark.enabled = !pg.watermark.enabled;
      renderCanvas();
      renderProperties();
      setStatus(`Filigran: ${pg.watermark.enabled ? 'Açık (' + pg.watermark.text + ')' : 'Kapalı'}`);
    });

    $('#btnToggleSideDock')?.addEventListener('click', () => {
      const dock = $('#auxDock');
      if (dock) dock.hidden = !dock.hidden;
    });
    $('#btnCloseDock')?.addEventListener('click', () => {
      const dock = $('#auxDock');
      if (dock) dock.hidden = true;
    });

    // Insert Ribbon buttons (scoped to .ribbon-panel so left flyout items aren't double-bound!)
    $$('.ribbon-panel [data-add-comp]').forEach(btn => {
      btn.onclick = () => {
        const b = findBand(state.selectedBandId) || state.report.bands[0];
        if (b) addComponentToBand(b, btn.dataset.addComp, 15, 8);
      };
    });
    $$('.ribbon-panel [data-add-band]').forEach(btn => {
      btn.onclick = () => addBandToReport(btn.dataset.addBand);
    });

    // Home Ribbon Format Controls
    $('#btnUndo')?.addEventListener('click', undo);
    $('#btnRedo')?.addEventListener('click', redo);
    $('#btnCut')?.addEventListener('click', cutSelected);
    $('#btnCopy')?.addEventListener('click', copySelected);
    $('#btnPaste')?.addEventListener('click', pasteClipboard);
    $('#btnFormatPainter')?.addEventListener('click', toggleFormatPainter);
    $('#tbDeleteComp')?.addEventListener('click', deleteSelected);

    $('#tbFontFamily')?.addEventListener('change', (e) => applyCompStyle('fontFamily', e.target.value));
    $('#tbFontSize')?.addEventListener('change', (e) => applyCompStyle('fontSizePt', parseFloat(e.target.value)));
    $('#tbBold')?.addEventListener('click', () => toggleCompProp('bold'));
    $('#tbItalic')?.addEventListener('click', () => toggleCompProp('italic'));
    $('#tbUnderline')?.addEventListener('click', () => toggleCompProp('underline'));
    $('#tbStrike')?.addEventListener('click', () => toggleCompProp('strike'));
    $('#tbWordWrap')?.addEventListener('click', () => toggleCompProp('wordWrap'));
    $('#tbCanGrow')?.addEventListener('click', () => toggleCompProp('canGrow'));
    $('#btnOpenTextFormat')?.addEventListener('click', () => {
      const { comp } = findSelected();
      if (!comp) { alert('Önce bir metin bileşeni seçin.'); return; }
      openTextFormatModal(comp);
    });
    $('#btnOpenConditions')?.addEventListener('click', () => {
      const { comp } = findSelected();
      if (!comp) { alert('Önce bir bileşen seçin.'); return; }
      openConditionsModal(comp);
    });
    $('#tbColor')?.addEventListener('change', (e) => applyCompStyle('color', e.target.value));
    $('#tbBgColor')?.addEventListener('change', (e) => applyCompStyle('backgroundColor', e.target.value));
    $('#tbBorders')?.addEventListener('change', (e) => applyCompStyle('borderSides', e.target.value));

    $('#tbAlignLeft')?.addEventListener('click', () => applyCompStyle('hAlign', 'left'));
    $('#tbAlignCenter')?.addEventListener('click', () => applyCompStyle('hAlign', 'center'));
    $('#tbAlignRight')?.addEventListener('click', () => applyCompStyle('hAlign', 'right'));
    $('#tbAlignJustify')?.addEventListener('click', () => applyCompStyle('hAlign', 'justify'));
    $('#tbAlignTop')?.addEventListener('click', () => applyCompStyle('vAlign', 'top'));
    $('#tbAlignMiddle')?.addEventListener('click', () => applyCompStyle('vAlign', 'middle'));
    $('#tbAlignBottom')?.addEventListener('click', () => applyCompStyle('vAlign', 'bottom'));

    // Layout Alignment buttons
    $$('[data-align]').forEach(btn => {
      btn.onclick = () => alignSelectedGroup(btn.dataset.align);
    });

    // Page Settings Controls
    $('#pgPaperSize')?.addEventListener('change', (e) => {
      pushUndo();
      state.report.page.paperSize = e.target.value;
      const dims = { A4: [210, 297], A5: [148, 210], Letter: [216, 279] }[e.target.value];
      if (dims) {
        state.report.page.widthMm = state.report.page.orientation === 'Landscape' ? dims[1] : dims[0];
        state.report.page.heightMm = state.report.page.orientation === 'Landscape' ? dims[0] : dims[1];
      }
      renderAll();
    });
    $('#pgOrientation')?.addEventListener('change', (e) => {
      pushUndo();
      const ori = e.target.value;
      if (state.report.page.orientation !== ori) {
        state.report.page.orientation = ori;
        const w = state.report.page.widthMm;
        state.report.page.widthMm = state.report.page.heightMm;
        state.report.page.heightMm = w;
      }
      renderAll();
    });
    $('#pgGridSize')?.addEventListener('change', (e) => {
      state.report.page.gridSizeMm = parseFloat(e.target.value) || 2;
      renderCanvas();
    });
    $('#pgShowGrid')?.addEventListener('change', (e) => {
      state.report.page.showGrid = e.target.checked;
      renderCanvas();
    });
    $('#pgSnapToGrid')?.addEventListener('change', (e) => {
      state.report.page.snapToGrid = e.target.checked;
    });
    $('#pgWatermarkEnable')?.addEventListener('change', (e) => {
      pushUndo();
      if (!state.report.page.watermark) state.report.page.watermark = {};
      state.report.page.watermark.enabled = e.target.checked;
      renderCanvas();
    });
    $('#pgWatermarkText')?.addEventListener('change', (e) => {
      pushUndo();
      if (!state.report.page.watermark) state.report.page.watermark = {};
      state.report.page.watermark.text = e.target.value;
      renderCanvas();
    });

    // Master Form selection & flag (Topbar + Page Ribbon synced!)
    $('#pgMasterReport')?.addEventListener('change', (e) => applyMasterSelection(e.target.value));
    $('#topMasterSelect')?.addEventListener('change', (e) => applyMasterSelection(e.target.value));
    $('#btnExtractMaster')?.addEventListener('click', extractCurrentReportToMaster);
    $('#btnReturnFromMaster')?.addEventListener('click', async () => {
      const target = state.returnFromMasterReport;
      state.returnFromMasterReport = null;
      if (target) await loadReport(target);
    });
    const openLinkedMaster = () => {
      if (state.report?.masterReportName) {
        state.returnFromMasterReport = state.report.name;
        loadReport(state.report.masterReportName);
      }
    };
    $('#btnOpenMaster')?.addEventListener('click', openLinkedMaster);
    $('#btnTopEditMaster')?.addEventListener('click', openLinkedMaster);

    $('#pgIsMaster')?.addEventListener('change', (e) => {
      pushUndo();
      state.report.isMasterTemplate = e.target.checked;
      updateMasterModeUI();
      populateMasterSelect();
      markDirty(true);
    });

    // Data Source Modal (Schema-Only + Optional JSON / API)
    let dsModalMode = 'schema'; // 'schema' | 'json'
    const addSchemaColRow = (name = '', dataType = 'string') => {
      const list = $('#dsSchemaColsList');
      if (!list) return;
      const row = document.createElement('div');
      row.className = 'schema-col-row';
      row.style.cssText = 'display:flex;gap:4px;align-items:center;';
      row.innerHTML = `
        <input class="rinput sc-name" style="flex:1;" placeholder="Kolon Adı (örn: TCKIMLIKNO, UNVAN)" value="${escapeHtml(name)}" />
        <select class="rselect sc-type" style="width:95px;">
          <option value="string" ${dataType === 'string' ? 'selected' : ''}>Metin</option>
          <option value="number" ${dataType === 'number' ? 'selected' : ''}>Sayı</option>
          <option value="date" ${dataType === 'date' ? 'selected' : ''}>Tarih</option>
          <option value="boolean" ${dataType === 'boolean' ? 'selected' : ''}>Mantıksal</option>
        </select>
        <button type="button" class="rbtn rbtn-xs sc-del" style="color:var(--danger);">✕</button>
      `;
      row.querySelector('.sc-del').onclick = () => row.remove();
      list.appendChild(row);
    };

    const setDsModalTab = (mode) => {
      dsModalMode = mode;
      $('#dsTabSchema')?.classList.toggle('cdoc-active', mode === 'schema');
      $('#dsTabJson')?.classList.toggle('cdoc-active', mode === 'json');
      if ($('#dsPaneSchema')) $('#dsPaneSchema').hidden = mode !== 'schema';
      if ($('#dsPaneJson')) $('#dsPaneJson').hidden = mode !== 'json';
    };

    const openDsModal = () => {
      const m = $('#dsModal');
      if (!m) return;
      setDsModalTab('schema');
      const list = $('#dsSchemaColsList');
      if (list && list.children.length === 0) {
        addSchemaColRow('TCKIMLIKNO', 'string');
        addSchemaColRow('AD', 'string');
        addSchemaColRow('SOYAD', 'string');
        addSchemaColRow('UNVAN', 'string');
      }
      m.hidden = false;
    };

    $('#btnAddDataSource')?.addEventListener('click', openDsModal);
    $('#btnAddDataSourceRight')?.addEventListener('click', openDsModal);
    $('#btnCloseDsModal')?.addEventListener('click', () => { $('#dsModal').hidden = true; });
    $('#btnCloseDataPreview')?.addEventListener('click', () => { $('#dataPreviewModal').hidden = true; });
    $('#dsTabSchema')?.addEventListener('click', () => setDsModalTab('schema'));
    $('#dsTabJson')?.addEventListener('click', () => setDsModalTab('json'));
    $('#btnAddSchemaCol')?.addEventListener('click', () => addSchemaColRow('', 'string'));
    $('#btnBulkAddCols')?.addEventListener('click', () => {
      const raw = ($('#dsBulkColsInput')?.value || '').trim();
      if (!raw) return;
      const cols = raw.split(/[,;\n\r]+/).map(s => s.trim()).filter(Boolean);
      cols.forEach(c => addSchemaColRow(c, 'string'));
      $('#dsBulkColsInput').value = '';
    });

    // Report Variables / Parameters (+ Değişken)
    $('#btnAddVariableRight')?.addEventListener('click', () => {
      const varName = prompt('Yeni Rapor Değişkeni / Parametre Adı (örn: KurumAd, BaslangicTarihi):');
      if (!varName || !varName.trim()) return;
      const defVal = prompt(`"${varName.trim()}" varsayılan tasarım değeri (opsiyonel):`, '') ?? '';
      pushUndo();
      if (!state.report.variables) state.report.variables = {};
      state.report.variables[varName.trim()] = defVal;
      renderDictionary();
      setStatus(`Parametre eklendi: {${varName.trim()}}`);
    });

    const DS_PRESETS = {
      faturalar: {
        name: 'Faturalar',
        rows: [
          { FaturaNo: 'F-1001', Sehir: 'İstanbul', Musteri: 'Sobis A.Ş.', Tutar: 14500, Adet: 3 },
          { FaturaNo: 'F-1002', Sehir: 'İstanbul', Musteri: 'Anadolu Bilişim', Tutar: 8200, Adet: 2 },
          { FaturaNo: 'F-1003', Sehir: 'Ankara', Musteri: 'Ivent Ltd.', Tutar: 9800, Adet: 4 },
          { FaturaNo: 'F-1004', Sehir: 'Ankara', Musteri: 'Başkent Sağlık', Tutar: 19400, Adet: 5 },
          { FaturaNo: 'F-1005', Sehir: 'İzmir', Musteri: 'Ege Lojistik', Tutar: 6750, Adet: 1 }
        ]
      },
      hastalar: {
        name: 'Hastalar',
        rows: [
          { Protokol: 'P-2026-01', Poliklinik: 'Kardiyoloji', HastaAdi: 'Ayşe Yılmaz', Yas: 54, Ucret: 1250 },
          { Protokol: 'P-2026-02', Poliklinik: 'Kardiyoloji', HastaAdi: 'Mehmet Demir', Yas: 62, Ucret: 1850 },
          { Protokol: 'P-2026-03', Poliklinik: 'Dahiliye', HastaAdi: 'Zeynep Kaya', Yas: 38, Ucret: 950 },
          { Protokol: 'P-2026-04', Poliklinik: 'Dahiliye', HastaAdi: 'Ali Çelik', Yas: 45, Ucret: 1100 },
          { Protokol: 'P-2026-05', Poliklinik: 'Ortopedi', HastaAdi: 'Fatma Şahin', Yas: 29, Ucret: 1400 }
        ]
      },
      personel: {
        name: 'Personel',
        rows: [
          { Sicil: 'S-101', Departman: 'Yazılım', AdSoyad: 'Can Öztürk', Unvan: 'Kıdemli Geliştirici', Maas: 85000 },
          { Sicil: 'S-102', Departman: 'Yazılım', AdSoyad: 'Selin Arslan', Unvan: 'Frontend Uzmanı', Maas: 72000 },
          { Sicil: 'S-103', Departman: 'Satış', AdSoyad: 'Burak Koç', Unvan: 'Bölge Müdürü', Maas: 78000 },
          { Sicil: 'S-104', Departman: 'Satış', AdSoyad: 'Deniz Aksoy', Unvan: 'Satış Temsilcisi', Maas: 52000 }
        ]
      }
    };
    $$('[data-ds-preset]').forEach(btn => {
      btn.addEventListener('click', () => {
        const p = DS_PRESETS[btn.dataset.dsPreset];
        if (!p) return;
        $('#dsNameInput').value = p.name;
        $('#dsJsonInput').value = JSON.stringify(p.rows, null, 2);
      });
    });

    $('#btnFetchDsUrl')?.addEventListener('click', async () => {
      const url = ($('#dsApiUrlInput')?.value || '').trim();
      if (!url) { alert('Geçerli bir API URL girin.'); return; }
      try {
        const res = await fetch(url);
        const data = await res.json();
        const arr = Array.isArray(data) ? data : (data.items || data.data || [data]);
        $('#dsJsonInput').value = JSON.stringify(arr.slice(0, 100), null, 2);
        if (!$('#dsNameInput').value.trim()) $('#dsNameInput').value = 'ApiVerisi';
      } catch (err) {
        alert('URL okunamadı (CORS veya ağ hatası): ' + err.message);
      }
    });

    $('#btnSaveDs')?.addEventListener('click', () => {
      const dsName = ($('#dsNameInput')?.value || '').trim();
      const dsAlias = ($('#dsAliasInput')?.value || '').trim() || dsName;
      if (!dsName) { alert(t('enterDsName')); return; }

      pushUndo();
      if (!state.report.dataSources) state.report.dataSources = [];
      const existingIdx = state.report.dataSources.findIndex(d => d.name.toLowerCase() === dsName.toLowerCase());

      if (dsModalMode === 'schema') {
        const cols = Array.from(document.querySelectorAll('#dsSchemaColsList .schema-col-row'))
          .map(r => ({
            name: (r.querySelector('.sc-name')?.value || '').trim(),
            dataType: r.querySelector('.sc-type')?.value || 'string'
          }))
          .filter(c => c.name.length > 0);
        if (cols.length === 0) { alert('En az 1 kolon ekleyin.'); return; }
        const dsObj = { name: dsName, alias: dsAlias, type: 'schema', columns: cols, inlineData: null };
        if (existingIdx >= 0) state.report.dataSources[existingIdx] = dsObj;
        else state.report.dataSources.push(dsObj);

        $('#dsModal').hidden = true;
        renderAll();
        setStatus(`Şema veri kaynağı eklendi: ${dsName} (${cols.length} kolon — C# DataSet uyumlu)`);
        return;
      }

      let rows;
      try {
        rows = JSON.parse($('#dsJsonInput').value);
        if (!Array.isArray(rows) || rows.length === 0) throw new Error('En az 1 nesne içeren JSON dizisi olmalı.');
      } catch (err) {
        alert(t('invalidJson') + err.message);
        return;
      }
      const cols = Object.keys(rows[0]).map(k => ({
        name: k,
        dataType: typeof rows[0][k] === 'number' ? 'number' : typeof rows[0][k] === 'boolean' ? 'boolean' : 'string'
      }));
      const dsObj = { name: dsName, alias: dsAlias, type: 'json', columns: cols, inlineData: rows };
      if (existingIdx >= 0) state.report.dataSources[existingIdx] = dsObj;
      else state.report.dataSources.push(dsObj);

      $('#dsModal').hidden = true;
      renderAll();
      setStatus(t('dsAdded', `${dsName} (${rows.length} satır)`));
    });

    // Preview toolbar export dropdown (PDF, Excel, Word, HTML, CSV, XML, JSON, SVG, Snapshot)
    $('#selExportFormat')?.addEventListener('change', async (e) => {
      const fmt = e.target.value;
      if (!fmt) return;
      e.target.value = '';
      await exportServerFormat(fmt);
    });

    // Save & Open
    $('#btnQuickSave')?.addEventListener('click', () => saveReport(false));
    $('#btnOpenModal')?.addEventListener('click', openReportsModal);
    $('#btnOpenModalRibbon')?.addEventListener('click', openReportsModal);
    $('#btnSaveAs')?.addEventListener('click', () => saveReport(true));
    $('#btnDownloadSbs')?.addEventListener('click', downloadSbsFile);
    $('#btnUploadSbs')?.addEventListener('click', () => $('#sbsFileInput')?.click());
    $('#sbsFileInput')?.addEventListener('change', handleUploadSbsFile);

    // MRT Import

    // Template Gallery Modal
    $('#btnNewReport')?.addEventListener('click', openTemplateModal);
    $('#btnCloseTemplateModal')?.addEventListener('click', () => $('#templateModal').hidden = true);
    $('#btnCancelTemplateModal')?.addEventListener('click', () => $('#templateModal').hidden = true);
    $('#btnConfirmTemplateModal')?.addEventListener('click', confirmTemplateSelection);

    $$('.tpl-card').forEach(card => {
      card.onclick = () => {
        $$('.tpl-card').forEach(c => c.classList.remove('active'));
        card.classList.add('active');
        state.selectedTemplateKey = card.dataset.tpl;
      };
    });

    // Zoom Controls
    const zoomSlider = $('#zoomSlider');
    const updateZoom = (z) => {
      state.zoom = Math.max(0.2, Math.min(2.5, z));
      if (zoomSlider) zoomSlider.value = Math.round(state.zoom * 100);
      $('#zoomLabel').textContent = `${Math.round(state.zoom * 100)}%`;
      renderCanvas();
    };
    zoomSlider?.addEventListener('input', (e) => updateZoom(parseInt(e.target.value, 10) / 100));
    $('#btnZoomIn')?.addEventListener('click', () => updateZoom(state.zoom + 0.1));
    $('#btnZoomOut')?.addEventListener('click', () => updateZoom(state.zoom - 0.1));
    $('#btnZoomFit')?.addEventListener('click', () => updateZoom(1.0));

    // Keyboard Shortcuts
    window.addEventListener('keydown', handleGlobalKeydown);

    // Context Menu Actions
    $$('[data-ctx]').forEach(item => {
      item.onclick = () => {
        hideContextMenu();
        const action = item.dataset.ctx;
        if (action === 'edit') {
          const { comp } = findSelected();
          if (comp) openExpressionEditor(comp);
        } else if (action === 'copy') copySelected();
        else if (action === 'paste') pasteClipboard();
        else if (action === 'duplicate') duplicateSelected();
        else if (action === 'front') alignSelectedGroup('front');
        else if (action === 'back') alignSelectedGroup('back');
        else if (action === 'delete') deleteSelected();
      };
    });
    document.addEventListener('click', (e) => {
      if (!e.target.closest('#ctxMenu')) hideContextMenu();
    });

    // Preview Bar Actions
    $('#btnPrintPdf')?.addEventListener('click', () => window.print());
    $('#btnExportExcel')?.addEventListener('click', exportExcelHtml);
    $('#btnExportHtml')?.addEventListener('click', exportStandaloneHtml);
    $('#btnRefreshPreview')?.addEventListener('click', () => renderPreview());
    $('#btnPrevPage')?.addEventListener('click', () => {
      if (state.previewPageIndex > 0) {
        state.previewPageIndex--;
        scrollToPreviewPage(state.previewPageIndex);
      }
    });
    $('#btnNextPage')?.addEventListener('click', () => {
      if (state.rendered && state.previewPageIndex < state.rendered.pages.length - 1) {
        state.previewPageIndex++;
        scrollToPreviewPage(state.previewPageIndex);
      }
    });
  }

  function setActiveTool(tool, extra = {}) {
    state.activeTool = tool;
    state._toolExtra = extra; // store extra props for next placement
    $$('.tool-btn').forEach(b => b.classList.toggle('active', b.dataset.tool === tool));
    document.body.style.cursor = tool === 'pointer' ? '' : 'crosshair';
    setStatus(`Aktif Araç: ${tool}`);
  }

  // ---------- Move Band Up / Down ----------
  function moveBand(bandId, dir) {
    const bands = state.report.bands;
    const idx = bands.findIndex(b => b.id === bandId);
    if (idx < 0) return;
    const newIdx = idx + dir;
    if (newIdx < 0 || newIdx >= bands.length) return;
    pushUndo();
    const tmp = bands[idx];
    bands[idx] = bands[newIdx];
    bands[newIdx] = tmp;
    state.selectedBandId = bandId;
    renderAll();
  }

  // ---------- Keyboard Shortcuts ----------
  function handleGlobalKeydown(e) {
    if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.isContentEditable) return;

    if (e.ctrlKey || e.metaKey) {
      if (e.key === 'z') { e.preventDefault(); undo(); return; }
      if (e.key === 'y') { e.preventDefault(); redo(); return; }
      if (e.key === 's') { e.preventDefault(); saveReport(false); return; }
      if (e.key === 'c') { e.preventDefault(); copySelected(); return; }
      if (e.key === 'v') { e.preventDefault(); pasteClipboard(); return; }
      if (e.key === 'd') { e.preventDefault(); duplicateSelected(); return; }
      if (e.key === 'a') {
        e.preventDefault();
        const band = findBand(state.selectedBandId);
        if (band) {
          state.multiIds = new Set(band.components.map(c => c.id));
          renderAll();
        }
        return;
      }
    }

    if (e.key === 'Delete' || e.key === 'Backspace') {
      e.preventDefault();
      deleteSelected();
      return;
    }

    // Arrow keys nudge
    if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) {
      e.preventDefault();
      const step = e.shiftKey ? 5 : 1;
      const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0;
      const dy = e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0;
      nudgeSelected(dx, dy);
    }
  }

  function nudgeSelected(dxMm, dyMm) {
    const band = findBand(state.selectedBandId);
    if (!band) return;
    const comps = band.components.filter(c => state.multiIds.has(c.id));
    if (comps.length === 0) return;
    pushUndo();
    comps.forEach(c => {
      c.xMm = Math.max(0, snapMm(c.xMm + dxMm));
      c.yMm = Math.max(0, snapMm(c.yMm + dyMm));
    });
    renderCanvas();
    renderProperties();
  }

  // ---------- Undo / Redo ----------
  function undo() {
    if (state.undoStack.length === 0) return;
    state.redoStack.push(JSON.stringify(state.report));
    const prev = state.undoStack.pop();
    state.report = JSON.parse(prev);
    renderAll();
    setStatus('Geri Alındı');
  }
  function redo() {
    if (state.redoStack.length === 0) return;
    state.undoStack.push(JSON.stringify(state.report));
    const next = state.redoStack.pop();
    state.report = JSON.parse(next);
    renderAll();
    setStatus('Yinelendi');
  }

  // ---------- Cut / Copy / Paste / Duplicate / Delete ----------
  function copySelected() {
    const { band } = findSelected();
    if (!band) return;
    const comps = band.components.filter(c => state.multiIds.has(c.id));
    if (comps.length === 0) return;
    state.clipboard = JSON.parse(JSON.stringify(comps));
    setStatus(t('copied', comps.length));
  }
  function cutSelected() {
    copySelected();
    deleteSelected();
  }
  function pasteClipboard() {
    if (!state.clipboard || state.clipboard.length === 0) return;
    const band = findBand(state.selectedBandId) || state.report.bands[0];
    if (!band) return;
    pushUndo();
    state.multiIds.clear();
    state.clipboard.forEach(template => {
      const clone = JSON.parse(JSON.stringify(template));
      clone.id = uid();
      clone.name = `${clone.type}_Copy`;
      clone.xMm = snapMm(clone.xMm + 4);
      clone.yMm = snapMm(clone.yMm + 4);
      band.components.push(clone);
      state.multiIds.add(clone.id);
      state.selectedCompId = clone.id;
    });
    renderAll();
  }
  function duplicateSelected() {
    copySelected();
    pasteClipboard();
  }
  function deleteSelected() {
    const band = findBand(state.selectedBandId);
    if (!band) return;
    const count = state.multiIds.size;
    if (count === 0 && !state.selectedCompId) return;
    pushUndo();
    band.components = band.components.filter(c => !state.multiIds.has(c.id) && c.id !== state.selectedCompId);
    state.multiIds.clear();
    state.selectedCompId = null;
    state.inspectorTarget = '__form__';
    renderAll();
    setStatus(`${count} öğe silindi`);
  }

  function toggleFormatPainter() {
    const { comp } = findSelected();
    if (!comp) return;
    state.formatPainterStyle = {
      fontFamily: comp.fontFamily,
      fontSizePt: comp.fontSizePt,
      bold: comp.bold,
      italic: comp.italic,
      underline: comp.underline,
      strike: comp.strike,
      color: comp.color,
      backgroundColor: comp.backgroundColor,
      borderColor: comp.borderColor,
      borderWidthPx: comp.borderWidthPx,
      borderStyle: comp.borderStyle,
      borderSides: comp.borderSides,
      cornerRadiusPx: comp.cornerRadiusPx
    };
    $('#btnFormatPainter')?.classList.add('active');
    setStatus('Biçim kopyalandı — Hedef öğeye tıklayın');
  }

  function applyCompStyle(prop, val) {
    const { band } = findSelected();
    if (!band) return;
    pushUndo();
    band.components.filter(c => state.multiIds.has(c.id)).forEach(c => {
      c[prop] = val;
    });
    renderCanvas();
    renderProperties();
  }
  function toggleCompProp(prop) {
    const { comp } = findSelected();
    if (!comp) return;
    applyCompStyle(prop, !comp[prop]);
  }

  function syncToolbarFromSelection() {
    const { comp } = findSelected();
    if (!comp) return;
    if ($('#tbFontFamily')) $('#tbFontFamily').value = comp.fontFamily || 'Segoe UI, Inter, sans-serif';
    if ($('#tbFontSize')) $('#tbFontSize').value = comp.fontSizePt || 10;
    if ($('#tbBold')) $('#tbBold').classList.toggle('active', !!comp.bold);
    if ($('#tbItalic')) $('#tbItalic').classList.toggle('active', !!comp.italic);
    if ($('#tbUnderline')) $('#tbUnderline').classList.toggle('active', !!comp.underline);
    if ($('#tbStrike')) $('#tbStrike').classList.toggle('active', !!comp.strike);
    if ($('#tbAlignLeft')) $('#tbAlignLeft').classList.toggle('active', comp.hAlign === 'left');
    if ($('#tbAlignCenter')) $('#tbAlignCenter').classList.toggle('active', comp.hAlign === 'center');
    if ($('#tbAlignRight')) $('#tbAlignRight').classList.toggle('active', comp.hAlign === 'right');
    if ($('#tbAlignJustify')) $('#tbAlignJustify').classList.toggle('active', comp.hAlign === 'justify');
    if ($('#tbWordWrap')) $('#tbWordWrap').classList.toggle('active', !!comp.wordWrap);
  }

  function syncPageControls() {
    if (!state.report) return;
    const pg = state.report.page;
    if ($('#pgPaperSize')) $('#pgPaperSize').value = pg.paperSize || 'A4';
    if ($('#pgOrientation')) $('#pgOrientation').value = pg.orientation || 'Portrait';
    if ($('#pgGridSize')) $('#pgGridSize').value = pg.gridSizeMm || 2;
    if ($('#pgShowGrid')) $('#pgShowGrid').checked = pg.showGrid !== false;
    if ($('#pgSnapToGrid')) $('#pgSnapToGrid').checked = pg.snapToGrid !== false;
    if ($('#pgWatermarkEnable')) $('#pgWatermarkEnable').checked = !!pg.watermark?.enabled;
    if ($('#pgWatermarkText')) $('#pgWatermarkText').value = pg.watermark?.text || 'Trial';
    if ($('#pgIsMaster')) $('#pgIsMaster').checked = !!state.report.isMasterTemplate;
    populateMasterSelect();
  }

  // ---------- Layout Alignment ----------
  function alignSelectedGroup(mode) {
    const band = findBand(state.selectedBandId);
    if (!band) return;
    const comps = band.components.filter(c => state.multiIds.has(c.id));
    if (comps.length < 1) return;
    pushUndo();

    if (mode === 'left') {
      const minX = Math.min(...comps.map(c => c.xMm));
      comps.forEach(c => c.xMm = minX);
    } else if (mode === 'right') {
      const maxR = Math.max(...comps.map(c => c.xMm + c.widthMm));
      comps.forEach(c => c.xMm = maxR - c.widthMm);
    } else if (mode === 'hcenter') {
      const minX = Math.min(...comps.map(c => c.xMm));
      const maxR = Math.max(...comps.map(c => c.xMm + c.widthMm));
      const center = (minX + maxR) / 2;
      comps.forEach(c => c.xMm = snapMm(center - c.widthMm / 2));
    } else if (mode === 'top') {
      const minY = Math.min(...comps.map(c => c.yMm));
      comps.forEach(c => c.yMm = minY);
    } else if (mode === 'bottom') {
      const maxB = Math.max(...comps.map(c => c.yMm + c.heightMm));
      comps.forEach(c => c.yMm = maxB - c.heightMm);
    } else if (mode === 'vcenter') {
      const minY = Math.min(...comps.map(c => c.yMm));
      const maxB = Math.max(...comps.map(c => c.yMm + c.heightMm));
      const center = (minY + maxB) / 2;
      comps.forEach(c => c.yMm = snapMm(center - c.heightMm / 2));
    } else if (mode === 'samewidth') {
      const targetW = comps[0].widthMm;
      comps.forEach(c => c.widthMm = targetW);
    } else if (mode === 'sameheight') {
      const targetH = comps[0].heightMm;
      comps.forEach(c => c.heightMm = targetH);
    } else if (mode === 'samesize') {
      const targetW = comps[0].widthMm;
      const targetH = comps[0].heightMm;
      comps.forEach(c => { c.widthMm = targetW; c.heightMm = targetH; });
    } else if (mode === 'hdist' && comps.length > 2) {
      comps.sort((a, b) => a.xMm - b.xMm);
      const minX = comps[0].xMm;
      const maxR = comps[comps.length - 1].xMm + comps[comps.length - 1].widthMm;
      const totalCompW = comps.reduce((acc, c) => acc + c.widthMm, 0);
      const gap = (maxR - minX - totalCompW) / (comps.length - 1);
      let curX = minX;
      comps.forEach(c => {
        c.xMm = snapMm(curX);
        curX += c.widthMm + gap;
      });
    } else if (mode === 'vdist' && comps.length > 2) {
      comps.sort((a, b) => a.yMm - b.yMm);
      const minY = comps[0].yMm;
      const maxB = comps[comps.length - 1].yMm + comps[comps.length - 1].heightMm;
      const totalCompH = comps.reduce((acc, c) => acc + c.heightMm, 0);
      const gap = (maxB - minY - totalCompH) / (comps.length - 1);
      let curY = minY;
      comps.forEach(c => {
        c.yMm = snapMm(curY);
        curY += c.heightMm + gap;
      });
    } else if (mode === 'front') {
      comps.forEach(c => {
        const idx = band.components.indexOf(c);
        if (idx !== -1) {
          band.components.splice(idx, 1);
          band.components.push(c);
        }
      });
    } else if (mode === 'back') {
      comps.forEach(c => {
        const idx = band.components.indexOf(c);
        if (idx !== -1) {
          band.components.splice(idx, 1);
          band.components.unshift(c);
        }
      });
    }

    renderCanvas();
    renderProperties();
  }

  // ---------- Interactive Preview & PDF Preview ----------
  async function switchMode(mode) {
    state.mode = mode;
    const isPreview = mode === 'preview' || mode === 'pdfpreview';

    // Sync ribbon tabs & panels cleanly when returning to design
    if (isPreview) {
      $$('.ribbon-tab').forEach(b => {
        b.classList.toggle('active', b.dataset.tab === mode);
      });
      const rPanels = $('#designerRibbon .ribbon-panels');
      if (rPanels) rPanels.hidden = true;
    } else {
      const rPanels = $('#designerRibbon .ribbon-panels');
      if (rPanels) rPanels.hidden = false;
      const activeDesignTab = state.lastDesignTab || 'home';
      $$('.ribbon-tab').forEach(b => {
        b.classList.toggle('active', b.dataset.tab === activeDesignTab);
      });
      $$('.ribbon-panel').forEach(p => {
        p.classList.toggle('active', p.dataset.panel === activeDesignTab);
      });
    }

    // Sync canvas document tabs
    $$('.cdoc-tab[data-doctab]').forEach(t => {
      t.classList.toggle('cdoc-active', t.dataset.doctab === mode);
      t.classList.toggle('active', t.dataset.doctab === mode);
    });

    // Show/hide panes
    $('#previewBar').hidden = !isPreview;
    $('#designerStage').hidden = isPreview;
    $('#previewStage').hidden = !isPreview;
    $('#leftToolRibbon').hidden = isPreview;
    $('#rightSidebar').hidden = isPreview;

    if (isPreview) {
      renderPageTabs();
      await renderPreview();
    } else {
      renderPageTabs();
      renderCanvas();
    }
  }

  async function renderPreview() {
    syncActivePageToPagesArray();
    setStatus(t('rendering'));
    try {
      const res = await fetch(apiUrl('/reports/render'), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ report: state.report })
      });
      state.rendered = await res.json();
      state.previewPageIndex = 0;
      $('#renderPerfBadge').textContent = `⚡ ${state.rendered.renderTimeMs} ms`;
      drawPreviewPages();
      setStatus(t('previewReady', state.rendered.pages.length, state.rendered.renderTimeMs));
    } catch (e) {
      console.error(e);
      setStatus('Önizleme oluşturulamadı.');
    }
  }

  function drawPreviewPages() {
    const stage = $('#previewStage');
    stage.innerHTML = '';
    const r = state.rendered;
    if (!r) return;

    $('#pageIndicator').textContent = t('pageOf', state.previewPageIndex + 1, r.pages.length);

    r.pages.forEach((p, idx) => {
      const pageEl = document.createElement('div');
      pageEl.className = 'preview-page';
      pageEl.style.width = `${mmToPx(r.pageWidthMm)}px`;
      pageEl.style.height = `${mmToPx(r.pageHeightMm)}px`;
      pageEl.dataset.pageIndex = idx;

      // Watermark in preview
      if (r.watermark?.enabled && r.watermark.text) {
        const wm = document.createElement('div');
        wm.className = 'page-watermark-overlay';
        const wmt = document.createElement('div');
        wmt.className = 'watermark-text';
        wmt.textContent = r.watermark.text;
        wmt.style.fontFamily = r.watermark.fontFamily || 'Inter, Arial, sans-serif';
        wmt.style.fontSize = `${r.watermark.fontSizePt || 72}pt`;
        wmt.style.color = r.watermark.color || '#64748b';
        wmt.style.opacity = r.watermark.opacity || 0.12;
        wmt.style.transform = `rotate(${r.watermark.angle || -45}deg)`;
        wm.appendChild(wmt);
        pageEl.appendChild(wm);
      }

      p.elements.forEach(el => {
        const div = document.createElement('div');
        div.className = 'pv-el';
        div.style.left = `${mmToPx(el.xMm)}px`;
        div.style.top = `${mmToPx(el.yMm)}px`;
        div.style.width = `${mmToPx(el.widthMm)}px`;
        div.style.height = `${mmToPx(el.heightMm)}px`;
        div.style.backgroundColor = el.backgroundColor || 'transparent';
        div.style.color = el.color || '#111827';
        div.style.fontFamily = el.fontFamily || 'Inter, Arial, sans-serif';
        div.style.fontSize = `${el.fontSizePt || 10}pt`;
        div.style.fontWeight = el.bold ? '700' : '400';
        div.style.fontStyle = el.italic ? 'italic' : 'normal';
        div.style.textDecoration = [el.underline ? 'underline' : '', el.strike ? 'line-through' : ''].filter(Boolean).join(' ') || 'none';
        div.style.justifyContent = el.hAlign === 'center' ? 'center' : el.hAlign === 'right' ? 'flex-end' : 'flex-start';
        div.style.alignItems = el.vAlign === 'top' ? 'flex-start' : el.vAlign === 'bottom' ? 'flex-end' : 'center';

        if (el.cornerRadiusPx > 0) div.style.borderRadius = `${el.cornerRadiusPx}px`;
        const bStyle = (el.borderStyle || 'Solid').toLowerCase();
        if (el.borderWidthPx > 0 && el.borderSides !== 'None') {
          const bProp = `${el.borderWidthPx}px ${bStyle} ${el.borderColor || '#cbd5e1'}`;
          if (el.borderSides === 'All' || el.borderSides === 'Box') {
            div.style.border = bProp;
          } else {
            if (el.borderSides.includes('Top')) div.style.borderTop = bProp;
            if (el.borderSides.includes('Bottom')) div.style.borderBottom = bProp;
            if (el.borderSides.includes('Left')) div.style.borderLeft = bProp;
            if (el.borderSides.includes('Right')) div.style.borderRight = bProp;
          }
        }

        // Live Interactive Preview Controls
        if (el.type === 'Input') {
          div.className += ' comp-input-box';
          if (el.wordWrap && el.heightMm > 12) {
            div.innerHTML = `<textarea class="interactive-input" placeholder="${escapeHtml(el.placeholder || '')}">${escapeHtml(el.text || '')}</textarea>`;
          } else {
            div.innerHTML = `<input type="text" class="interactive-input" value="${escapeHtml(el.text || '')}" placeholder="${escapeHtml(el.placeholder || '')}" />`;
          }
        } else if (el.type === 'CheckBox') {
          div.className += ' comp-check-box';
          div.innerHTML = `<label style="display:inline-flex;align-items:center;gap:6px;cursor:pointer;"><input type="checkbox" ${el.checked ? 'checked' : ''} style="accent-color:#008b6b;cursor:pointer;" /> <span>${escapeHtml(el.text || el.label || '')}</span></label>`;
        } else if (el.type === 'Radio') {
          div.className += ' comp-check-box';
          div.innerHTML = `<label style="display:inline-flex;align-items:center;gap:6px;cursor:pointer;"><input type="radio" name="${escapeHtml(el.groupName || 'radG')}" ${el.checked ? 'checked' : ''} style="accent-color:#008b6b;cursor:pointer;" /> <span>${escapeHtml(el.text || el.label || '')}</span></label>`;
        } else if (el.type === 'ComboBox') {
          const items = (el.items || 'Option 1,Option 2,Option 3').split(',');
          div.innerHTML = `<select class="interactive-input">${items.map(it => `<option ${it.trim() === el.text ? 'selected' : ''}>${escapeHtml(it.trim())}</option>`).join('')}</select>`;
        } else if (el.type === 'DatePicker') {
          div.className += ' comp-date-box';
          div.innerHTML = `<input type="text" class="interactive-input" value="${escapeHtml(el.text || '11/20/2026')}" style="padding-right:24px;" /><span class="comp-date-ico" style="right:6px;top:50%;transform:translateY(-50%);">📅</span>`;
        } else if (el.type === 'Signature') {
          div.innerHTML = `<canvas class="interactive-sig" width="${Math.round(mmToPx(el.widthMm))}" height="${Math.round(mmToPx(el.heightMm))}"></canvas>`;
          initSignatureCanvas(div.querySelector('canvas'));
        } else if (el.type === 'Button') {
          div.innerHTML = `<button class="comp-btn-box" onclick="window.print()">${escapeHtml(el.text || 'Submit')}</button>`;
        } else if (el.type === 'Line') {
          div.style.border = 'none';
          if (el.lineOrientation === 'Vertical') {
            div.style.borderLeft = `${Math.max(1, el.borderWidthPx || 1)}px ${bStyle} ${el.color || '#111827'}`;
          } else {
            div.style.borderBottom = `${Math.max(1, el.borderWidthPx || 1)}px ${bStyle} ${el.color || '#111827'}`;
          }
        } else if (el.type === 'Image') {
          if (el.imageUrl) {
            div.innerHTML = `<img src="${escapeHtml(el.imageUrl)}" style="max-width:100%;max-height:100%;object-fit:contain;" />`;
          }
        } else if (el.type === 'Shape') {
          if (el.shapeType === 'Ellipse' || el.shapeType === 'Triangle' || el.shapeType === 'RoundedRectangle') {
            div.style.backgroundColor = 'transparent';
            div.style.border = 'none';
            div.innerHTML = buildShapeSvg(el);
          }
        } else if (el.type === 'Barcode') {
          div.innerHTML = buildBarcodeSvg(el.text, el.barcodeFormat);
        } else if (el.type === 'Chart') {
          div.innerHTML = buildChartSvg(el.chartType || 'Bar', el.chartData);
        } else {
          div.textContent = el.text || '';
        }
        pageEl.appendChild(div);
      });

      stage.appendChild(pageEl);
    });
  }

  function buildShapeSvg(comp) {
    const fill = (comp.backgroundColor && comp.backgroundColor !== 'transparent') ? comp.backgroundColor : 'none';
    const stroke = comp.borderColor || comp.color || '#334155';
    const sw = Math.max(1, comp.borderWidthPx || 1.5);
    if (comp.shapeType === 'Ellipse') {
      return `<svg viewBox="0 0 100 100" preserveAspectRatio="none" style="width:100%;height:100%;display:block;"><ellipse cx="50" cy="50" rx="48" ry="48" fill="${escapeHtml(fill)}" stroke="${escapeHtml(stroke)}" stroke-width="${sw * 1.5}" vector-effect="non-scaling-stroke"/></svg>`;
    }
    if (comp.shapeType === 'Triangle') {
      return `<svg viewBox="0 0 100 100" preserveAspectRatio="none" style="width:100%;height:100%;display:block;"><polygon points="50,3 97,97 3,97" fill="${escapeHtml(fill)}" stroke="${escapeHtml(stroke)}" stroke-width="${sw * 1.5}" vector-effect="non-scaling-stroke"/></svg>`;
    }
    if (comp.shapeType === 'RoundedRectangle') {
      return `<svg viewBox="0 0 100 100" preserveAspectRatio="none" style="width:100%;height:100%;display:block;"><rect x="2" y="2" width="96" height="96" rx="14" ry="14" fill="${escapeHtml(fill)}" stroke="${escapeHtml(stroke)}" stroke-width="${sw * 1.5}" vector-effect="non-scaling-stroke"/></svg>`;
    }
    return `<svg viewBox="0 0 100 100" preserveAspectRatio="none" style="width:100%;height:100%;display:block;"><rect x="2" y="2" width="96" height="96" fill="${escapeHtml(fill)}" stroke="${escapeHtml(stroke)}" stroke-width="${sw * 1.5}" vector-effect="non-scaling-stroke"/></svg>`;
  }

  function initSignatureCanvas(canvas) {
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    let drawing = false;
    ctx.lineWidth = 2;
    ctx.lineCap = 'round';
    ctx.strokeStyle = '#0f172a';

    const getPos = (e) => {
      const rect = canvas.getBoundingClientRect();
      return { x: e.clientX - rect.left, y: e.clientY - rect.top };
    };
    canvas.onmousedown = (e) => {
      drawing = true;
      const pos = getPos(e);
      ctx.beginPath();
      ctx.moveTo(pos.x, pos.y);
    };
    window.addEventListener('mousemove', (e) => {
      if (!drawing) return;
      const pos = getPos(e);
      ctx.lineTo(pos.x, pos.y);
      ctx.stroke();
    });
    window.addEventListener('mouseup', () => { drawing = false; });
  }

  function scrollToPreviewPage(idx) {
    const pageEl = document.querySelector(`.preview-page[data-page-index="${idx}"]`);
    if (pageEl) pageEl.scrollIntoView({ behavior: 'smooth' });
    $('#pageIndicator').textContent = t('pageOf', idx + 1, state.rendered?.pages.length || 1);
  }

  // ---------- Template Gallery Modal ----------
  function openTemplateModal() {
    $('#templateModal').hidden = false;
  }
  async function confirmTemplateSelection() {
    $('#templateModal').hidden = true;
    const key = state.selectedTemplateKey || 'BasvuruFormu';
    if (key === 'BosForm') {
      createNewBlankReport('BosForm', false);
    } else if (key === 'BosRapor') {
      createNewBlankReport('BosRapor', true);
    } else if (key === 'MasterForm') {
      createNewMasterForm('YeniMasterForm');
    } else {
      await loadReport(key);
    }
  }

  function createNewMasterForm(name) {
    pushUndo();
    state.masterReport = null;
    state.report = {
      version: '1.0',
      name,
      description: 'Sabit Header & Footer Master Şablonu',
      isMasterTemplate: true,
      masterReportName: null,
      page: {
        paperSize: 'A4', orientation: 'Portrait',
        widthMm: 210, heightMm: 297,
        marginTopMm: 10, marginRightMm: 10, marginBottomMm: 10, marginLeftMm: 10,
        gridSizeMm: 2, showGrid: true, snapToGrid: true,
        watermark: { enabled: false, text: 'SOBIS', angle: -45, opacity: 0.12, fontSizePt: 76, color: '#64748b' }
      },
      bands: [
        { id: uid(), type: 'PageHeader', name: 'MasterPageHeader', heightMm: 22, backgroundColor: '#0f2744', components: [
          { id: uid(), type: 'Text', name: 'MasterTitle', xMm: 4, yMm: 4, widthMm: 120, heightMm: 9, text: '✦ KURUMSAL MASTER ANTET', fontSizePt: 13, bold: true, color: '#ffffff' },
          { id: uid(), type: 'Text', name: 'MasterDate', xMm: 140, yMm: 5, widthMm: 46, heightMm: 7, text: 'Tarih: {Today}', fontSizePt: 8.5, bold: true, hAlign: 'right', color: '#f8a045' }
        ]},
        { id: uid(), type: 'PageFooter', name: 'MasterPageFooter', heightMm: 11, backgroundColor: '#f8fafc', components: [
          { id: uid(), type: 'Line', name: 'MasterFooterLine', xMm: 0, yMm: 0, widthMm: 190, heightMm: 1, color: '#0d6b62', borderWidthPx: 1.5 },
          { id: uid(), type: 'Text', name: 'MasterFooterInfo', xMm: 2, yMm: 2, widthMm: 125, heightMm: 7, text: 'SbsReport Kurumsal Master Altbilgi | {Now}', fontSizePt: 7.5, color: '#64748b' },
          { id: uid(), type: 'Text', name: 'MasterFooterPage', xMm: 135, yMm: 2, widthMm: 53, heightMm: 7, text: 'Sayfa {PageNumber} / {TotalPageCount}', fontSizePt: 8.5, bold: true, hAlign: 'right', color: '#0f2744' }
        ]}
      ]
    };
    ensurePageDefaults(state.report);
    state.selectedBandId = state.report.bands[0]?.id || null;
    state.selectedCompId = null;
    state.inspectorTarget = '__form__';
    state.multiIds = new Set();
    markDirty(true);
    syncPageControls();
    renderAll();
    setStatus(t('created', `${name}.sbs (Master)`));
    $('#reportNameLabel').textContent = `${name}.sbs 👑 MASTER`;
  }

  function createNewBlankReport(name, withBands = true) {
    pushUndo();
    state.report = {
      version: '1.0',
      name,
      description: 'SbsReport',
      page: {
        paperSize: 'A4', orientation: 'Portrait',
        widthMm: 210, heightMm: 297,
        marginTopMm: 10, marginRightMm: 10, marginBottomMm: 10, marginLeftMm: 10,
        gridSizeMm: 2, showGrid: true, snapToGrid: true,
        watermark: { enabled: false, text: 'Trial', angle: -45, opacity: 0.12, fontSizePt: 76, color: '#64748b' }
      },
      bands: withBands ? [
        { id: uid(), type: 'ReportTitle', name: 'RaporBasligi', heightMm: 20, backgroundColor: 'transparent', components: [
          { id: uid(), type: 'Text', name: 'Title1', xMm: 0, yMm: 4, widthMm: 100, heightMm: 10, text: name, fontSizePt: 16, bold: true, color: '#0f172a' }
        ]},
        { id: uid(), type: 'Data', name: 'VeriAlani', heightMm: 40, backgroundColor: 'transparent', components: [] },
        { id: uid(), type: 'PageFooter', name: 'SayfaAltbilgisi', heightMm: 10, backgroundColor: 'transparent', components: [
          { id: uid(), type: 'Text', name: 'PageLbl', xMm: 130, yMm: 2, widthMm: 60, heightMm: 6, text: 'Sayfa {PageNumber} / {TotalPageCount}', fontSizePt: 8, hAlign: 'right', color: '#64748b' }
        ]}
      ] : [
        { id: uid(), type: 'Data', name: 'FormCanvas', heightMm: 270, backgroundColor: 'transparent', components: [] }
      ]
    };
    ensurePageDefaults(state.report);
    state.selectedBandId = state.report.bands[0]?.id || null;
    state.selectedCompId = null;
    state.inspectorTarget = '__form__';
    state.multiIds = new Set();
    markDirty(true);
    renderAll();
    setStatus(t('created', `${name}.sbs`));
    $('#reportNameLabel').textContent = `${name}.sbs`;
  }

  // ---------- Reports Modal ----------
  async function openReportsModal() {
    const modal = $('#openModal');
    const list = $('#reportsList');
    list.innerHTML = `<div style="padding:10px;color:var(--text-muted);">${t('rendering')}</div>`;
    modal.hidden = false;

    $('#btnCloseOpenModal').onclick = () => { modal.hidden = true; };

    try {
      const res = await fetch(apiUrl('/reports'));
      const reports = await res.json();
      list.innerHTML = '';
      if (reports.length === 0) {
        list.innerHTML = '<div style="padding:10px;">Henüz kayıtlı rapor yok.</div>';
        return;
      }

      const masters = reports.filter(r => r.isMaster);
      const normals = reports.filter(r => !r.isMaster);

      const renderGroup = (titleHtml, items, emptyMsg) => {
        const hdr = document.createElement('div');
        hdr.className = 'modal-section-title';
        hdr.innerHTML = titleHtml;
        list.appendChild(hdr);
        if (items.length === 0) {
          const em = document.createElement('div');
          em.style.cssText = 'padding:8px 6px;color:var(--text-muted);font-size:11px;';
          em.textContent = emptyMsg;
          list.appendChild(em);
          return;
        }
        items.forEach(r => {
          const item = document.createElement('div');
          item.className = 'modal-item';
          if (r.isMaster) item.style.borderLeft = '3px solid #7c3aed';
          const masterTag = r.isMaster
            ? `<span class="master-pill-badge">👑 MASTER</span>`
            : (r.masterReportName ? `<small style="color:#a78bfa;margin-left:6px;">↳ 👑 ${escapeHtml(r.masterReportName)}</small>` : '');
          item.innerHTML = `
            <div>
              <div><b>${escapeHtml(r.name)}.sbs</b>${masterTag}</div>
              <small>${(r.sizeBytes / 1024).toFixed(1)} KB · ${new Date(r.updatedAt).toLocaleString('tr-TR')}</small>
            </div>
            <div style="display:flex;gap:4px;">
              <button class="btn btn-primary rbtn-xs btn-load">${t('open')}</button>
              <button class="btn btn-secondary rbtn-xs btn-del" style="color:var(--danger);" title="${t('delete')}">🗑</button>
            </div>
          `;
          item.querySelector('.btn-load').onclick = async () => {
            modal.hidden = true;
            await loadReport(r.name);
          };
          item.querySelector('.btn-del').onclick = async () => {
            if (!confirm(t('confirmDelete', `${r.name}.sbs`))) return;
            await fetch(apiUrl(`/reports/${encodeURIComponent(r.name)}`), { method: 'DELETE' });
            openReportsModal();
          };
          list.appendChild(item);
        });
      };

      renderGroup(
        `<span>👑 Master Formlar (Sabit Header &amp; Footer)</span><button class="rbtn rbtn-xs" id="btnNewMasterFromModal">+ Yeni Master</button>`,
        masters,
        'Henüz Master Form oluşturulmamış.'
      );
      renderGroup(`<span>📄 Raporlar &amp; Formlar</span>`, normals, 'Henüz normal rapor yok.');

      $('#btnNewMasterFromModal')?.addEventListener('click', () => {
        modal.hidden = true;
        createNewMasterForm('YeniMasterForm');
      });
    } catch {
      list.innerHTML = '<div style="padding:10px;color:var(--danger);">Rapor listesi alınamadı.</div>';
    }
  }

  // ---------- View Data Modal ----------
  async function openDataPreviewModal(dsName) {
    const modal = $('#dataPreviewModal');
    const title = $('#dataPreviewTitle');
    const body = $('#dataPreviewBody');
    if (!modal || !body) return;

    title.textContent = `👁 Veri Önizleme — ${dsName}`;
    body.innerHTML = '<div style="padding:12px;color:var(--text-muted);">Yükleniyor...</div>';
    modal.hidden = false;

    let rows = [];
    const customDs = (state.report?.dataSources || []).find(d => d.name.toLowerCase() === dsName.toLowerCase());
    if (customDs?.inlineData?.length) {
      rows = customDs.inlineData;
    } else {
      try {
        const res = await fetch(apiUrl(`/datasources/demo/${encodeURIComponent(dsName)}/rows`));
        if (res.ok) rows = await res.json();
      } catch { /* ignore */ }
    }

    if (!rows || rows.length === 0) {
      body.innerHTML = '<div style="padding:12px;">Bu veri kaynağında satır bulunamadı.</div>';
      return;
    }

    const cols = Object.keys(rows[0]);
    const headerHtml = cols.map(c => `<th>${escapeHtml(c)}</th>`).join('');
    const rowsHtml = rows.map(r => `<tr>${cols.map(c => `<td>${escapeHtml(r[c] ?? '')}</td>`).join('')}</tr>`).join('');
    body.innerHTML = `
      <div style="margin-bottom:6px;font-size:11px;color:var(--text-muted);">Toplam <b>${rows.length}</b> satır · <b>${cols.length}</b> sütun</div>
      <table class="data-preview-table">
        <thead><tr>${headerHtml}</tr></thead>
        <tbody>${rowsHtml}</tbody>
      </table>
    `;
  }

  // ---------- Helper: All Available DataSources ----------
  function getAllDataSources() {
    const map = new Map();
    (state.demoSources || []).forEach(ds => {
      map.set(ds.name.toLowerCase(), {
        name: ds.name,
        alias: ds.alias || ds.name,
        columns: (ds.columns || []).map(c => ({ name: c.name, type: c.dataType || c.type || 'string' }))
      });
    });
    (state.report?.dataSources || []).forEach(ds => {
      map.set(ds.name.toLowerCase(), {
        name: ds.name,
        alias: ds.alias || ds.name,
        columns: (ds.columns || []).map(c => ({ name: c.name, type: c.dataType || c.type || 'string' }))
      });
    });
    return Array.from(map.values());
  }

  // ---------- Group Condition Modal (Grup Durumu) ----------
  function openGroupBandModal(band) {
    const modal = $('#groupBandModal');
    if (!modal || !band) return;

    $('#groupBandModalTitle').textContent = `✏ Grup Durumu — ${band.name}`;
    let selectedExpr = band.groupBy || '';
    const exprInput = $('#grpExprInput');
    const sortSel = $('#grpSortSelect');
    const sumDirSel = $('#grpSummaryDir');
    if (exprInput) exprInput.value = selectedExpr;
    if (sortSel) sortSel.value = band.sortDirection || 'asc';
    if (sumDirSel) sumDirSel.value = band.sortDirection || 'asc';

    // Tab switching
    const setTab = (tab) => {
      $$('#groupBandModal .sti-stab').forEach(b => b.classList.toggle('active', b.dataset.grptab === tab));
      $('#grpPaneColumn').hidden = tab !== 'column';
      $('#grpPaneExpr').hidden = tab !== 'expr';
      $('#grpPaneSummary').hidden = tab !== 'summary';
    };
    setTab('column');
    $$('#groupBandModal .sti-stab').forEach(b => { b.onclick = () => setTab(b.dataset.grptab); });

    // Populate Data Columns tree
    const tree = $('#grpColumnsTree');
    const renderColsTree = () => {
      tree.innerHTML = '';
      const noneEl = document.createElement('div');
      noneEl.className = 'sti-tree-item' + (!selectedExpr ? ' selected' : '');
      noneEl.innerHTML = '<span>✕</span> <span>Hayır (Gruplama Yok)</span>';
      noneEl.onclick = () => {
        selectedExpr = '';
        if (exprInput) exprInput.value = '';
        renderColsTree();
      };
      tree.appendChild(noneEl);

      const sources = getAllDataSources();
      sources.forEach(ds => {
        const hdr = document.createElement('div');
        hdr.className = 'sti-tree-group';
        hdr.textContent = `🗄 ${ds.alias || ds.name} (${ds.name})`;
        tree.appendChild(hdr);

        ds.columns.forEach(col => {
          const full = `${ds.name}.${col.name}`;
          const isSel = selectedExpr.toLowerCase() === full.toLowerCase() || selectedExpr.toLowerCase() === col.name.toLowerCase();
          const item = document.createElement('div');
          item.className = 'sti-tree-item sti-tree-child' + (isSel ? ' selected' : '');
          item.innerHTML = `<span class="col-type">${(col.type || 'S')[0].toUpperCase()}</span> <span>${escapeHtml(col.name)}</span>`;
          item.onclick = () => {
            selectedExpr = full;
            if (exprInput) exprInput.value = full;
            renderColsTree();
          };
          item.ondblclick = () => {
            selectedExpr = full;
            if (exprInput) exprInput.value = full;
            applyGroupModal();
          };
          tree.appendChild(item);
        });
      });
    };
    renderColsTree();

    const applyGroupModal = () => {
      pushUndo();
      const raw = (exprInput?.value || selectedExpr || '').trim().replace(/^\{|\}$/g, '');
      band.groupBy = raw || null;
      band.sortDirection = sortSel?.value || 'asc';

      // Auto-sync DataSource on Data/Table band if user picked "DataSource.Column"
      if (raw && raw.includes('.')) {
        const dsName = raw.split('.')[0];
        const dataBand = state.report.bands.find(b => b.type === 'Data' || b.type === 'Table' || b.type === 'HierarchicalData');
        if (dataBand && !dataBand.dataSource) {
          dataBand.dataSource = dsName;
        }
      }

      // If GroupHeader has no text component yet, automatically add a crisp group label so Preview shows group headings immediately!
      if (raw && (!band.components || band.components.length === 0)) {
        band.components = [{
          id: uid(), type: 'Text', name: `GrpTitle_${band.name}`,
          xMm: 2, yMm: 1, widthMm: 120, heightMm: Math.max(6, band.heightMm - 2),
          text: `{${raw}}`, fontFamily: 'Segoe UI, Inter, sans-serif', fontSizePt: 10.5,
          bold: true, color: '#0f172a', backgroundColor: '#fef3c7',
          borderColor: '#f59e0b', borderWidthPx: 1, borderStyle: 'Solid', borderSides: 'Bottom',
          hAlign: 'left', vAlign: 'middle', wordWrap: true
        }];
      }

      modal.hidden = true;
      renderAll();
      setStatus(raw ? `${band.name} gruplama alanı ayarlandı: ${raw}` : `${band.name} gruplaması kaldırıldı.`);
    };

    $('#btnGroupOk').onclick = applyGroupModal;
    $('#btnGroupCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseGroupModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Data Band Setup Modal (Veri) ----------
  function openDataBandModal(band) {
    const modal = $('#dataBandModal');
    if (!modal || !band) return;

    $('#dataBandModalTitle').textContent = `🗄 Veri — ${band.name}`;
    let selectedDs = band.dataSource || '';

    const sortBySel = $('#dbSortBySelect');
    const sortDirSel = $('#dbSortDirSelect');
    const filterInput = $('#dbFilterInput');
    const countInput = $('#dbCountDataInput');
    const masterSel = $('#dbMasterCompSelect');

    if (sortDirSel) sortDirSel.value = band.sortDirection || 'asc';
    if (filterInput) filterInput.value = band.filterExpression || '';
    if (countInput) countInput.value = band.countData || 0;

    // Master Component bands dropdown
    if (masterSel) {
      masterSel.innerHTML = '<option value="">[Atanmamış]</option>';
      state.report.bands
        .filter(b => b.id !== band.id && (b.type === 'Data' || b.type === 'Table' || b.type === 'GroupHeader'))
        .forEach(b => {
          const opt = document.createElement('option');
          opt.value = b.name;
          opt.textContent = `${b.name} (${b.type})`;
          masterSel.appendChild(opt);
        });
      masterSel.value = band.masterComponent || '';
    }

    const populateSortCols = (dsName) => {
      if (!sortBySel) return;
      sortBySel.innerHTML = '<option value="">[Sıralama Yok]</option>';
      const ds = getAllDataSources().find(d => d.name.toLowerCase() === (dsName || '').toLowerCase());
      if (ds) {
        ds.columns.forEach(c => {
          const opt = document.createElement('option');
          opt.value = c.name;
          opt.textContent = `${ds.name}.${c.name}`;
          sortBySel.appendChild(opt);
        });
      }
      sortBySel.value = band.sortBy || '';
    };

    const setTab = (tab) => {
      $$('#dataBandModal .sti-stab').forEach(b => b.classList.toggle('active', b.dataset.dbtab === tab));
      $('#dbPaneSource').hidden = tab !== 'source';
      $('#dbPaneMaster').hidden = tab !== 'master';
      $('#dbPaneSort').hidden = tab !== 'sort';
      $('#dbPaneFilter').hidden = tab !== 'filter';
    };
    setTab('source');
    $$('#dataBandModal .sti-stab').forEach(b => { b.onclick = () => setTab(b.dataset.dbtab); });

    const tree = $('#dbSourcesTree');
    const renderDsTree = () => {
      tree.innerHTML = '';
      const noneEl = document.createElement('div');
      noneEl.className = 'sti-tree-item' + (!selectedDs ? ' selected' : '');
      noneEl.innerHTML = '<span>✕</span> <span>[Atanmamış]</span>';
      noneEl.onclick = () => {
        selectedDs = '';
        populateSortCols('');
        renderDsTree();
      };
      tree.appendChild(noneEl);

      getAllDataSources().forEach(ds => {
        const isSel = selectedDs.toLowerCase() === ds.name.toLowerCase();
        const item = document.createElement('div');
        item.className = 'sti-tree-item' + (isSel ? ' selected' : '');
        item.innerHTML = `<span>🗄</span> <span><b>${escapeHtml(ds.alias || ds.name)}</b> <small style="opacity:.75;">(${escapeHtml(ds.name)} · ${ds.columns.length} kolon)</small></span>`;
        item.onclick = () => {
          selectedDs = ds.name;
          populateSortCols(ds.name);
          renderDsTree();
        };
        item.ondblclick = () => {
          selectedDs = ds.name;
          applyDataBandModal();
        };
        tree.appendChild(item);
      });
    };
    renderDsTree();
    populateSortCols(selectedDs);

    const applyDataBandModal = () => {
      pushUndo();
      band.dataSource = selectedDs || null;
      band.sortBy = sortBySel?.value || null;
      band.sortDirection = sortDirSel?.value || 'asc';
      band.filterExpression = (filterInput?.value || '').trim() || null;
      band.countData = parseInt(countInput?.value || '0', 10) || 0;
      band.masterComponent = masterSel?.value || null;
      modal.hidden = true;
      renderAll();
      setStatus(selectedDs ? `${band.name} veri kaynağı atandı: ${selectedDs}` : `${band.name} veri kaynağı kaldırıldı.`);
    };

    $('#btnDataModalNewDs').onclick = () => {
      modal.hidden = true;
      $('#btnAddDataSourceRight')?.click();
    };
    $('#btnDataBandOk').onclick = applyDataBandModal;
    $('#btnDataBandCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseDataBandModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Data Band Placement Wizard ----------
  function openDataPlacementWizard(dsName) {
    const ds = getAllDataSources().find(d => d.name.toLowerCase() === (dsName || '').toLowerCase());
    const modal = $('#dsWizardModal');
    const colsHost = $('#dsWizardCols');
    if (!ds || !modal || !colsHost) return;

    $('#dsWizardTitle').textContent = `⚡ Veri Bandı Yerleşim Sihirbazı — ${ds.alias || ds.name}`;
    colsHost.innerHTML = '';
    ds.columns.forEach((col, i) => {
      const lbl = document.createElement('label');
      lbl.className = 'sti-tree-item';
      lbl.innerHTML = `<input type="checkbox" class="wiz-col-chk" value="${escapeHtml(col.name)}" data-col-type="${escapeHtml(col.type || 'string')}" ${i < 6 ? 'checked' : ''} /> <span class="col-type">${(col.type || 'S')[0].toUpperCase()}</span> <span>${escapeHtml(col.name)}</span>`;
      colsHost.appendChild(lbl);
    });

    $('#btnWizSelectAll').onclick = () => $$('#dsWizardCols .wiz-col-chk').forEach(c => { c.checked = true; });
    $('#btnWizSelectNone').onclick = () => $$('#dsWizardCols .wiz-col-chk').forEach(c => { c.checked = false; });

    $('#btnDsWizardOk').onclick = () => {
      const picked = $$('#dsWizardCols .wiz-col-chk:checked').map(c => ({
        name: c.value,
        type: c.dataset.colType || 'string'
      }));
      if (picked.length === 0) { alert('En az 1 sütun seçin.'); return; }

      pushUndo();
      const pg = state.report.page;
      const printableW = (pg.widthMm || 210) - (pg.marginLeftMm || 10) - (pg.marginRightMm || 10);
      const colW = +(printableW / picked.length).toFixed(2);

      const insertSemanticBand = (band) => {
        const prio = BAND_PRIORITY[band.type] ?? 50;
        const idx = state.report.bands.findIndex(x => (BAND_PRIORITY[x.type] ?? 50) > prio);
        if (idx === -1) state.report.bands.push(band);
        else state.report.bands.splice(idx, 0, band);
      };

      if ($('#wizCreateHeader')?.checked) {
        const hBand = {
          id: uid(), type: 'Header', name: `Header_${ds.name}`, heightMm: 9, backgroundColor: '#1e293b',
          components: picked.map((col, idx) => ({
            id: uid(), type: 'Text', name: `Hdr_${col.name}`,
            xMm: +(idx * colW).toFixed(2), yMm: 0, widthMm: colW, heightMm: 9,
            text: col.name, fontFamily: 'Segoe UI, Inter, sans-serif', fontSizePt: 9,
            bold: true, color: '#ffffff', backgroundColor: 'transparent',
            borderColor: '#475569', borderWidthPx: 1, borderStyle: 'Solid', borderSides: 'All',
            hAlign: col.type === 'number' ? 'right' : 'left', vAlign: 'middle', wordWrap: true
          }))
        };
        insertSemanticBand(hBand);
      }

      if ($('#wizCreateData')?.checked) {
        const dBand = {
          id: uid(), type: 'Data', name: `Data_${ds.name}`, heightMm: 8, dataSource: ds.name,
          backgroundColor: 'transparent', evenBackgroundColor: '#f8fafc',
          components: picked.map((col, idx) => ({
            id: uid(), type: 'Text', name: `Dat_${col.name}`,
            xMm: +(idx * colW).toFixed(2), yMm: 0, widthMm: colW, heightMm: 8,
            text: `{${ds.name}.${col.name}}`, fontFamily: 'Segoe UI, Inter, sans-serif', fontSizePt: 9,
            bold: false, color: '#0f172a', backgroundColor: 'transparent',
            borderColor: '#e2e8f0', borderWidthPx: 1, borderStyle: 'Solid', borderSides: 'All',
            hAlign: col.type === 'number' ? 'right' : 'left', vAlign: 'middle', wordWrap: true
          }))
        };
        insertSemanticBand(dBand);
        state.selectedBandId = dBand.id;
      }

      if ($('#wizCreateFooter')?.checked) {
        const fBand = {
          id: uid(), type: 'Footer', name: `Footer_${ds.name}`, heightMm: 9, backgroundColor: '#f1f5f9',
          components: picked.map((col, idx) => ({
            id: uid(), type: 'Text', name: `Ftr_${col.name}`,
            xMm: +(idx * colW).toFixed(2), yMm: 0, widthMm: colW, heightMm: 9,
            text: idx === 0 ? `Toplam: {Count()}` : (col.type === 'number' ? `{Sum(${col.name})}` : ''),
            fontFamily: 'Segoe UI, Inter, sans-serif', fontSizePt: 9,
            bold: true, color: '#0f172a', backgroundColor: 'transparent',
            borderColor: '#cbd5e1', borderWidthPx: 1, borderStyle: 'Solid', borderSides: 'All',
            hAlign: col.type === 'number' ? 'right' : 'left', vAlign: 'middle', wordWrap: true
          }))
        };
        insertSemanticBand(fBand);
      }

      modal.hidden = true;
      renderAll();
      setStatus(`⚡ "${ds.name}" için ${picked.length} sütunlu bantlar otomatik oluşturuldu!`);
    };

    $('#btnDsWizardCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseDsWizard').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Save / Download / Upload SBS ----------
  async function saveReport(asNew = false) {
    syncActivePageToPagesArray();
    let name = state.report.name;
    if (asNew) {
      const input = prompt(t('promptName'), name);
      if (!input) return;
      name = input.replace(/\.sbs$/i, '');
      state.report.name = name;
    }
    const res = await fetch(apiUrl(`/reports/${encodeURIComponent(name)}`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(state.report)
    });
    if (res.ok) {
      markDirty(false);
      await refreshReportsList();
      setStatus(t('saved', `${name}.sbs`));
      const badge = state.report.isMasterTemplate ? ' 👑 MASTER' : (state.report.masterReportName ? ` ↳ 👑 ${state.report.masterReportName}` : '');
      $('#reportNameLabel').textContent = `${name}.sbs${badge}`;
    } else {
      alert('Kaydetme başarısız!');
    }
  }

  async function exportServerFormat(fmt) {
    if (!state.report) return;
    syncActivePageToPagesArray();
    setStatus(`Dışa aktarılıyor (${fmt.toUpperCase()})...`);
    try {
      const res = await fetch(apiUrl(`/reports/export/${encodeURIComponent(fmt)}`), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ report: state.report })
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const blob = await res.blob();
      const extMap = { pdf: '.pdf', xlsx: '.xls', xls: '.xls', excel: '.xls', docx: '.doc', word: '.doc', html: '.html', csv: '.csv', xml: '.xml', json: '.json', svg: '.svg', rtf: '.rtf', txt: '.txt', sbsdoc: '.sbsdoc' };
      const ext = extMap[fmt] || `.${fmt}`;
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${state.report.name || 'Rapor'}${ext}`;
      a.click();
      URL.revokeObjectURL(url);
      setStatus(`Dışa aktarıldı: ${a.download}`);
    } catch (err) {
      console.error(err);
      alert(`Dışa aktarma hatası (${fmt}): ` + err.message);
    }
  }

  function downloadSbsFile() {
    syncActivePageToPagesArray();
    const json = JSON.stringify(state.report, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${state.report.name || 'Rapor'}.sbs`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function handleUploadSbsFile(e) {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (ev) => {
      try {
        state.report = JSON.parse(ev.target.result);
        ensurePageDefaults(state.report);
        state.selectedBandId = state.report.bands[0]?.id || null;
        state.selectedCompId = null;
        state.inspectorTarget = '__form__';
        state.multiIds = new Set();
        markDirty(true);
        renderAll();
        setStatus(t('loaded', file.name));
        $('#reportNameLabel').textContent = file.name;
      } catch (err) {
        alert('Geçersiz .sbs dosyası: ' + err.message);
      }
    };
    reader.readAsText(file);
    e.target.value = '';
  }

  function exportExcelHtml() {
    const r = state.rendered;
    if (!r) return;
    let html = `<html><head><meta charset="utf-8"></head><body><table border="1">`;
    r.pages.forEach(p => {
      p.elements.filter(e => e.text).forEach(e => {
        html += `<tr><td>${escapeHtml(e.text)}</td></tr>`;
      });
    });
    html += `</table></body></html>`;
    const blob = new Blob([html], { type: 'application/vnd.ms-excel' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${state.report.name || 'Rapor'}.xls`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function exportStandaloneHtml() {
    const stage = $('#previewStage');
    if (!stage) return;
    const css = $('link[rel="stylesheet"]').href;
    const html = `<!DOCTYPE html><html><head><meta charset="utf-8"><title>${escapeHtml(state.report.name)}</title><link rel="stylesheet" href="${css}"><style>body{background:#475569;display:flex;flex-direction:column;align-items:center;padding:20px;gap:20px;}</style></head><body>${stage.innerHTML}</body></html>`;
    const blob = new Blob([html], { type: 'text/html; charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${state.report.name || 'Rapor'}.html`;
    a.click();
    URL.revokeObjectURL(url);
  }

  // ---------- Context Menu ----------
  function showContextMenu(x, y) {
    const menu = $('#ctxMenu');
    if (!menu) return;
    menu.style.left = `${x}px`;
    menu.style.top = `${y}px`;
    menu.hidden = false;
  }
  function hideContextMenu() {
    const menu = $('#ctxMenu');
    if (menu) menu.hidden = true;
  }

  // ---------- 4-Tab Text / Expression Editor ----------
  function openExpressionEditor(comp) {
    const modal = $('#exprModal');
    const ta = $('#exprText');
    const fieldsDiv = $('#exprFields');
    if (!modal || !ta) return;

    ta.value = comp.text || '';
    modal.hidden = false;

    const setExprTab = (tab) => {
      $$('#exprModal [data-exprtab]').forEach(b => b.classList.toggle('cdoc-active', b.dataset.exprtab === tab));
      $('#exprPaneExpression').hidden = tab !== 'expression';
      $('#exprPaneColumn').hidden = tab !== 'column';
      $('#exprPaneSysvar').hidden = tab !== 'sysvar';
      $('#exprPaneSummary').hidden = tab !== 'summary';
    };
    setExprTab('expression');
    $$('#exprModal [data-exprtab]').forEach(b => { b.onclick = () => setExprTab(b.dataset.exprtab); });

    const allSources = getAllDataSources();

    // Tab 1: Expression fields
    fieldsDiv.innerHTML = '';
    allSources.forEach(ds => {
      ds.columns.forEach(col => {
        const item = document.createElement('div');
        item.className = 'expr-field';
        item.textContent = `{${ds.name}.${col.name}}`;
        item.onclick = () => insertAtCursor(ta, `{${ds.name}.${col.name}}`);
        fieldsDiv.appendChild(item);
      });
    });
    $$('.expr-fn').forEach(fn => {
      fn.onclick = () => insertAtCursor(ta, fn.dataset.ins);
    });

    // Tab 2: Data Column tree
    const colTree = $('#exprColTree');
    if (colTree) {
      colTree.innerHTML = '';
      allSources.forEach(ds => {
        const hdr = document.createElement('div');
        hdr.className = 'sti-tree-group';
        hdr.textContent = `🗄 ${ds.alias || ds.name} (${ds.name})`;
        colTree.appendChild(hdr);
        ds.columns.forEach(col => {
          const el = document.createElement('div');
          el.className = 'sti-tree-item sti-tree-child';
          el.innerHTML = `<span class="col-type">${(col.type || 'S')[0].toUpperCase()}</span> <span>${escapeHtml(col.name)}</span>`;
          el.onclick = () => {
            ta.value = `{${ds.name}.${col.name}}`;
            setExprTab('expression');
          };
          el.ondblclick = () => {
            pushUndo();
            comp.text = `{${ds.name}.${col.name}}`;
            modal.hidden = true;
            renderAll();
          };
          colTree.appendChild(el);
        });
      });
    }

    // Tab 3: System & Report Variables
    const sysList = $('#exprSysVarList');
    if (sysList) {
      sysList.innerHTML = '';
      const sysVars = [
        { k: '{PageNumber}', d: 'Sayfa Numarası' },
        { k: '{TotalPageCount}', d: 'Toplam Sayfa Sayısı' },
        { k: '{Today}', d: 'Bugünün Tarihi' },
        { k: '{Now}', d: 'Tarih & Saat' },
        { k: '{Line}', d: 'Satır Numarası' },
        { k: '{ReportName}', d: 'Rapor Adı' },
        ...Object.keys(state.report?.variables || {}).map(v => ({ k: `{${v}}`, d: 'Rapor Parametresi' }))
      ];
      sysVars.forEach(sv => {
        const el = document.createElement('div');
        el.className = 'sti-tree-item';
        el.style.border = '1px solid var(--border)';
        el.innerHTML = `<b>${escapeHtml(sv.k)}</b> <small style="color:var(--text-muted);margin-left:auto;">${escapeHtml(sv.d)}</small>`;
        el.onclick = () => {
          insertAtCursor(ta, sv.k);
          setExprTab('expression');
        };
        sysList.appendChild(el);
      });
    }

    // Tab 4: Summary builder
    const sumColSel = $('#exprSumCol');
    if (sumColSel) {
      sumColSel.innerHTML = '<option value="">(Satır Sayısı / Yok)</option>';
      allSources.forEach(ds => {
        ds.columns.forEach(col => {
          const opt = document.createElement('option');
          opt.value = col.name;
          opt.textContent = `${ds.name}.${col.name}`;
          sumColSel.appendChild(opt);
        });
      });
    }
    $('#btnApplySummaryToExpr').onclick = () => {
      const fn = $('#exprSumFunc')?.value || 'Sum';
      const col = $('#exprSumCol')?.value || '';
      ta.value = fn === 'Count' && !col ? '{Count()}' : `{${fn}(${col})}`;
      setExprTab('expression');
    };

    $('#btnExprOk').onclick = () => {
      pushUndo();
      comp.text = ta.value;
      modal.hidden = true;
      renderAll();
    };
    $('#btnExprCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseExpr').onclick = () => { modal.hidden = true; };
  }

  // ---------- Text Format Editor ----------
  function openTextFormatModal(comp) {
    const modal = $('#textFormatModal');
    const codeInput = $('#fmtCodeInput');
    const sampleDiv = $('#fmtSamplePreview');
    const presetsDiv = $('#fmtPresets');
    if (!modal || !codeInput) return;

    const CATS = {
      general:  { sample: 'Metin / 1234.56', presets: [{ lbl: 'Genel (Yok)', code: '' }] },
      number:   { sample: '1.234,56', presets: [{ lbl: '2 Ondalık (N2)', code: 'N2' }, { lbl: 'Tam Sayı (N0)', code: 'N0' }, { lbl: '3 Ondalık (N3)', code: 'N3' }] },
      currency: { sample: '1.234,56 ₺', presets: [{ lbl: 'TL (C2)', code: 'C2' }, { lbl: 'TL Tam (C0)', code: 'C0' }, { lbl: '#,##0.00 ₺', code: '#,##0.00 ₺' }] },
      date:     { sample: '21.11.2026', presets: [{ lbl: 'dd.MM.yyyy', code: 'dd.MM.yyyy' }, { lbl: 'dd.MM.yyyy HH:mm', code: 'dd.MM.yyyy HH:mm' }, { lbl: 'MMMM yyyy', code: 'MMMM yyyy' }] },
      percent:  { sample: '%45,00', presets: [{ lbl: 'Yüzde (P0)', code: 'P0' }, { lbl: 'Yüzde 2 Ondalık (P2)', code: 'P2' }] },
      custom:   { sample: 'Özel .NET Formatı', presets: [{ lbl: '00000', code: '00000' }, { lbl: '#,##0.00', code: '#,##0.00' }] }
    };

    codeInput.value = comp.format || '';
    const updateSample = (cat) => {
      sampleDiv.textContent = `${CATS[cat]?.sample || ''}  [${codeInput.value || 'Genel'}]`;
    };
    const setCat = (cat) => {
      $$('#textFormatModal [data-fmtcat]').forEach(b => b.classList.toggle('active', b.dataset.fmtcat === cat));
      presetsDiv.innerHTML = '';
      (CATS[cat]?.presets || []).forEach(p => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'rbtn rbtn-xs'; b.textContent = p.lbl;
        b.onclick = () => { codeInput.value = p.code; updateSample(cat); };
        presetsDiv.appendChild(b);
      });
      updateSample(cat);
    };
    $$('#textFormatModal [data-fmtcat]').forEach(b => { b.onclick = () => setCat(b.dataset.fmtcat); });
    setCat(comp.format ? 'custom' : 'general');

    $('#btnFmtOk').onclick = () => {
      pushUndo();
      comp.format = codeInput.value.trim() || null;
      modal.hidden = true;
      renderAll();
      setStatus(`Biçim uygulandı: ${comp.format || 'Genel'}`);
    };
    $('#btnFmtCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseFmtModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Conditions Editor ----------
  function openConditionsModal(comp) {
    const modal = $('#conditionsModal');
    const list = $('#condModalList');
    if (!modal || !list) return;

    let draft = JSON.parse(JSON.stringify(comp.conditions || []));
    const renderDraft = () => {
      list.innerHTML = '';
      if (draft.length === 0) {
        list.innerHTML = '<div style="padding:12px;color:var(--text-muted);font-size:11.5px;">Henüz koşul yok. "+ Koşul Ekle" ile ekleyin.</div>';
        return;
      }
      draft.forEach((c, idx) => {
        const row = document.createElement('div');
        row.style.cssText = 'border:1px solid var(--border);border-radius:5px;padding:8px;display:flex;flex-direction:column;gap:6px;background:var(--card2);';
        row.innerHTML = `
          <div style="display:flex;gap:6px;align-items:center;">
            <span style="font-size:11px;font-weight:700;">#${idx + 1}</span>
            <input class="rinput mono c-expr" style="flex:1;" placeholder="Örn: UnitsInStock < 20" value="${escapeHtml(c.expression || '')}" />
            <button type="button" class="rbtn rbtn-xs c-del" style="color:var(--danger);">✕</button>
          </div>
          <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;font-size:11px;">
            <label>Metin: <input type="color" class="c-fg" value="${c.textColor || '#dc2626'}" /></label>
            <label>Arka Plan: <input type="color" class="c-bg" value="${c.backgroundColor || '#fee2e2'}" /></label>
            <label><input type="checkbox" class="c-bold" ${c.bold ? 'checked' : ''} /> Kalın</label>
            <label><input type="checkbox" class="c-hide" ${c.visible === false ? 'checked' : ''} /> Gizle</label>
          </div>
        `;
        row.querySelector('.c-expr').oninput = (e) => { c.expression = e.target.value; };
        row.querySelector('.c-fg').oninput = (e) => { c.textColor = e.target.value; };
        row.querySelector('.c-bg').oninput = (e) => { c.backgroundColor = e.target.value; };
        row.querySelector('.c-bold').onchange = (e) => { c.bold = e.target.checked; };
        row.querySelector('.c-hide').onchange = (e) => { c.visible = e.target.checked ? false : true; };
        row.querySelector('.c-del').onclick = () => { draft.splice(idx, 1); renderDraft(); };
        list.appendChild(row);
      });
    };
    renderDraft();

    $('#btnModalAddCond').onclick = () => {
      draft.push({ expression: '', textColor: '#dc2626', backgroundColor: '#fee2e2', bold: true, visible: true });
      renderDraft();
    };
    $('#btnCondOk').onclick = () => {
      pushUndo();
      comp.conditions = draft.filter(x => (x.expression || '').trim().length > 0);
      modal.hidden = true;
      renderAll();
      setStatus(`${comp.name} için ${comp.conditions.length} koşul kaydedildi.`);
    };
    $('#btnCondCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseCondModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Image Editor ----------
  function openImageEditorModal(comp) {
    const modal = $('#imageEditorModal');
    const urlInput = $('#imgUrlInput');
    const fileInput = $('#imgFileUpload');
    const preview = $('#imgPreviewBox');
    if (!modal || !urlInput) return;

    const updatePrev = (src) => {
      preview.innerHTML = src
        ? `<img src="${escapeHtml(src)}" style="max-width:100%;max-height:100%;object-fit:contain;" />`
        : '<span style="color:#94a3b8;">Önizleme yok</span>';
    };
    urlInput.value = comp.imageUrl || '';
    if (fileInput) fileInput.value = '';
    updatePrev(urlInput.value);

    urlInput.oninput = () => updatePrev(urlInput.value.trim());
    if (fileInput) {
      fileInput.onchange = (e) => {
        const file = e.target.files?.[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = () => {
          urlInput.value = reader.result;
          updatePrev(urlInput.value);
        };
        reader.readAsDataURL(file);
      };
    }
    $('#btnImgClear').onclick = () => { urlInput.value = ''; updatePrev(''); };
    $('#btnImgOk').onclick = () => {
      pushUndo();
      comp.imageUrl = urlInput.value.trim() || null;
      modal.hidden = true;
      renderAll();
    };
    $('#btnImgCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseImgModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Barcode Editor ----------
  function openBarcodeEditorModal(comp) {
    const modal = $('#barcodeEditorModal');
    const fmtSel = $('#bcFormatSelect');
    const angSel = $('#bcAngleSelect');
    const valInput = $('#bcValueInput');
    const prev = $('#bcLivePreview');
    if (!modal) return;

    fmtSel.value = comp.barcodeFormat || 'Code128';
    angSel.value = String(comp.angle || 0);
    valInput.value = comp.text || '1234567890';
    const refreshPrev = () => { prev.innerHTML = buildBarcodeSvg(valInput.value, fmtSel.value); };
    refreshPrev();
    fmtSel.onchange = refreshPrev;
    valInput.oninput = refreshPrev;

    $('#btnBcOk').onclick = () => {
      pushUndo();
      comp.barcodeFormat = fmtSel.value;
      comp.angle = parseFloat(angSel.value) || 0;
      comp.text = valInput.value;
      modal.hidden = true;
      renderAll();
    };
    $('#btnBcCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseBcModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  // ---------- Chart Editor ----------
  function openChartEditorModal(comp) {
    const modal = $('#chartEditorModal');
    const typeSel = $('#chTypeSelect');
    const catSel = $('#chCatSelect');
    const valSel = $('#chValSelect');
    const prev = $('#chLivePreview');
    if (!modal) return;

    typeSel.value = comp.chartType || 'Bar';
    const allCols = [];
    getAllDataSources().forEach(ds => ds.columns.forEach(c => allCols.push({ label: `${ds.name}.${c.name}`, val: c.name })));

    [catSel, valSel].forEach(sel => {
      sel.innerHTML = '';
      allCols.forEach(c => {
        const opt = document.createElement('option');
        opt.value = c.val; opt.textContent = c.label;
        sel.appendChild(opt);
      });
    });
    catSel.value = comp.categoryField || allCols[0]?.val || '';
    valSel.value = comp.valueField || allCols[1]?.val || '';

    const refreshPrev = () => { prev.innerHTML = buildChartSvg(typeSel.value); };
    refreshPrev();
    typeSel.onchange = refreshPrev;

    $('#btnChartOk').onclick = () => {
      pushUndo();
      comp.chartType = typeSel.value;
      comp.categoryField = catSel.value;
      comp.valueField = valSel.value;
      modal.hidden = true;
      renderAll();
    };
    $('#btnChartCancel').onclick = () => { modal.hidden = true; };
    $('#btnCloseChartModal').onclick = () => { modal.hidden = true; };
    modal.hidden = false;
  }

  function insertAtCursor(myField, myValue) {
    if (document.selection) {
      myField.focus();
      const sel = document.selection.createRange();
      sel.text = myValue;
    } else if (myField.selectionStart || myField.selectionStart === 0) {
      const startPos = myField.selectionStart;
      const endPos = myField.selectionEnd;
      myField.value = myField.value.substring(0, startPos) + myValue + myField.value.substring(endPos, myField.value.length);
      myField.selectionStart = startPos + myValue.length;
      myField.selectionEnd = startPos + myValue.length;
    } else {
      myField.value += myValue;
    }
    myField.focus();
  }

  // ---------- SVG Barcode & Chart Generators ----------
  function buildBarcodeSvg(text = '1234567890', format = 'Code128') {
    if (format === 'QR') {
      return `
        <svg viewBox="0 0 100 100" style="width:100%;height:100%;display:block;">
          <rect width="100" height="100" fill="#ffffff" />
          <rect x="10" y="10" width="26" height="26" fill="#000000" />
          <rect x="14" y="14" width="18" height="18" fill="#ffffff" />
          <rect x="18" y="18" width="10" height="10" fill="#000000" />
          <rect x="64" y="10" width="26" height="26" fill="#000000" />
          <rect x="68" y="14" width="18" height="18" fill="#ffffff" />
          <rect x="72" y="18" width="10" height="10" fill="#000000" />
          <rect x="10" y="64" width="26" height="26" fill="#000000" />
          <rect x="14" y="68" width="18" height="18" fill="#ffffff" />
          <rect x="18" y="72" width="10" height="10" fill="#000000" />
          <rect x="42" y="42" width="16" height="16" fill="#000000" />
          <rect x="46" y="20" width="8" height="8" fill="#000000" />
          <rect x="46" y="70" width="8" height="8" fill="#000000" />
          <rect x="70" y="46" width="8" height="8" fill="#000000" />
          <rect x="70" y="70" width="12" height="12" fill="#000000" />
        </svg>
      `;
    }
    // Code 128 / EAN13 Line Barcode
    const bars = [];
    let seed = 0;
    for (let i = 0; i < text.length; i++) seed += text.charCodeAt(i);
    let x = 6;
    for (let i = 0; i < 34; i++) {
      const w = ((seed * (i + 1) * 7) % 3) + 1;
      const gap = ((seed * (i + 2) * 5) % 3) + 1;
      bars.push(`<rect x="${x}" y="4" width="${w}" height="36" fill="#000000" />`);
      x += w + gap;
      if (x > 188) break;
    }
    return `
      <svg viewBox="0 0 200 50" preserveAspectRatio="none" style="width:100%;height:100%;display:block;background:#fff;">
        ${bars.join('')}
        <text x="100" y="46" font-size="8" font-family="Consolas, monospace" text-anchor="middle" fill="#000000">${escapeHtml(text)}</text>
      </svg>
    `;
  }

  function buildChartSvg(type = 'Bar', points = null) {
    const data = points && points.length > 0 ? points : [
      { Label: 'Ocak', Value: 42 },
      { Label: 'Şubat', Value: 68 },
      { Label: 'Mart', Value: 55 },
      { Label: 'Nisan', Value: 90 },
      { Label: 'Mayıs', Value: 75 }
    ];

    if (type === 'Pie' || type === 'Doughnut') {
      const colors = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6'];
      const total = data.reduce((a, b) => a + Number(b.Value || 0), 0) || 1;
      let angleAcc = 0;
      const paths = data.map((d, i) => {
        const angle = (Number(d.Value || 0) / total) * 360;
        const startRad = (angleAcc - 90) * Math.PI / 180;
        angleAcc += angle;
        const endRad = (angleAcc - 90) * Math.PI / 180;
        const x1 = 100 + 70 * Math.cos(startRad);
        const y1 = 100 + 70 * Math.sin(startRad);
        const x2 = 100 + 70 * Math.cos(endRad);
        const y2 = 100 + 70 * Math.sin(endRad);
        const largeArc = angle > 180 ? 1 : 0;
        return `<path d="M 100 100 L ${x1.toFixed(1)} ${y1.toFixed(1)} A 70 70 0 ${largeArc} 1 ${x2.toFixed(1)} ${y2.toFixed(1)} Z" fill="${colors[i % colors.length]}" />`;
      });
      const hole = type === 'Doughnut' ? `<circle cx="100" cy="100" r="38" fill="#ffffff" />` : '';
      return `<svg viewBox="0 0 200 200" style="width:100%;height:100%;display:block;background:#fff;">${paths.join('')}${hole}</svg>`;
    }

    // Bar / Line Chart
    const maxVal = Math.max(...data.map(d => Number(d.Value || 0)), 1);
    const count = data.length;
    const colW = Math.max(8, Math.min(30, 200 / (count * 1.6)));
    const gap = (240 - count * colW) / (count + 1);

    const bars = data.map((d, i) => {
      const h = ((Number(d.Value || 0) / maxVal) * 110) || 4;
      const x = gap + i * (colW + gap);
      const y = 140 - h;
      return `
        <rect x="${x.toFixed(1)}" y="${y.toFixed(1)}" width="${colW.toFixed(1)}" height="${h.toFixed(1)}" rx="2" fill="#3b82f6" />
        <text x="${(x + colW / 2).toFixed(1)}" y="152" font-size="7.5" font-family="Inter, sans-serif" text-anchor="middle" fill="#475569">${escapeHtml(d.Label)}</text>
      `;
    });

    return `
      <svg viewBox="0 0 260 160" preserveAspectRatio="none" style="width:100%;height:100%;display:block;background:#fff;">
        <line x1="20" y1="20" x2="20" y2="140" stroke="#cbd5e1" stroke-width="1" />
        <line x1="20" y1="140" x2="250" y2="140" stroke="#cbd5e1" stroke-width="1" />
        ${bars.join('')}
      </svg>
    `;
  }

  // ---------- Rulers ----------
  function renderRulers(widthMm, heightMm) {
    const top = $('#rulerTop');
    const left = $('#rulerLeft');
    if (!top || !left) return;

    top.innerHTML = '';
    left.innerHTML = '';

    for (let mm = 0; mm <= widthMm; mm += 10) {
      const tick = document.createElement('div');
      tick.className = 'ruler-tick';
      tick.style.left = `${mmToPx(mm)}px`;
      if (mm % 20 === 0) {
        const lbl = document.createElement('span');
        lbl.className = 'ruler-lbl';
        lbl.textContent = mm;
        tick.appendChild(lbl);
      }
      top.appendChild(tick);
    }

    for (let mm = 0; mm <= heightMm; mm += 10) {
      const tick = document.createElement('div');
      tick.className = 'ruler-tick-v';
      tick.style.top = `${mmToPx(mm)}px`;
      if (mm % 20 === 0) {
        const lbl = document.createElement('span');
        lbl.className = 'ruler-lbl-v';
        lbl.textContent = mm;
        tick.appendChild(lbl);
      }
      left.appendChild(tick);
    }
  }

  // Launch on DOM ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
