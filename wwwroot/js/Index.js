/* ===== State ===== */
let allCoins = [];
let filteredCoins = [];
let currentPage = 1;
const pageSize = 10;
const chat = [];
const sparkCache = new Map();

/* ===== Utils ===== */
function fmt(n){ return Number(n).toLocaleString(undefined,{maximumFractionDigits:2}); }
function pctClass(v){ if(v === null || v === undefined) return 'pct dim'; return v>=0 ? 'pct pos' : 'pct neg'; }
function pctText(v){ return v===null || v===undefined ? '—' : (Math.round(v*100)/100) + '%'; }

(function setFavicon() {
  const el = document.querySelector('link[rel="icon"]') || document.createElement('link');
  el.rel = 'icon';
  el.type = 'image/svg+xml';
  el.href = '/favicon-green-dollar.svg';
  if (!el.parentNode) document.head.appendChild(el);
})();

function row(c){
  const pct1y = c.priceChangePercentage1y ?? null;
  return `<tr>
    <td>${c.marketCapRank}</td>
    <td class="coin">
      <div class="coin-cell">
        <img class="avatar" src="${c.image}" alt="${c.symbol}" />
        <div>
          <div class="coin-name">${c.name}</div>
          <div class="coin-sym">${c.symbol.toUpperCase()}</div>
        </div>
      </div>
    </td>
    <td class="num">$${fmt(c.currentPrice)}</td>
    <td class="num ${pctClass(pct1y)}">${pctText(pct1y)}</td>
    <td class="num"><div class="spark" data-id="${c.id}"></div></td>
  </tr>`;
}

/* ===== Data calls ===== */
async function fetchCoins(){
  const res = await fetch('/api/coins');
  if(!res.ok) throw new Error('Failed to load coins');
  return await res.json();
}
async function fetchReco(take=3){
  const res = await fetch('/api/recommend?take='+take);
  if(!res.ok) throw new Error('Failed to generate recommendation');
  return await res.json();
}

/* ===== Filter & render ===== */
function applyFilter(){
  const q = document.getElementById('q').value.trim().toLowerCase();
  filteredCoins = allCoins.filter(c => !q || c.name.toLowerCase().includes(q) || c.symbol.toLowerCase().includes(q));
  currentPage = 1;
  renderTable();
}

function renderTable(){
  const start = (currentPage - 1) * pageSize;
  const page = filteredCoins.slice(start, start + pageSize);
  const tbody = document.querySelector('#grid tbody');

  if(page.length===0){
    tbody.innerHTML = `<tr><td colspan="5" style="padding:1rem;color:var(--muted)">No results.</td></tr>`;
  }else{
    tbody.innerHTML = page.map(row).join('');
  }

  const totalPages = Math.max(1, Math.ceil(filteredCoins.length / pageSize));
  document.getElementById('pageInfo').textContent = `Page ${currentPage} of ${totalPages} (${filteredCoins.length} coins)`;
  document.getElementById('prev').disabled = currentPage <= 1;
  document.getElementById('next').disabled = currentPage >= totalPages;

  loadSparklinesForPage(); // concurrent version
}

async function refresh(){
  const tbody = document.querySelector('#grid tbody');
  tbody.innerHTML = Array.from({length:10}).map(()=>`
    <tr>
      <td colspan="5">
        <div style="height:38px; border-radius:10px; background: linear-gradient(90deg, rgba(255,255,255,.08), rgba(255,255,255,.16), rgba(255,255,255,.08)); background-size: 200% 100%; animation: shimmer 1.2s linear infinite;"></div>
      </td>
    </tr>
  `).join('');
  try {
    allCoins = await fetchCoins();
    filteredCoins = allCoins.slice();
    currentPage = 1;
    renderTable();
  } catch (e) {
    tbody.innerHTML = `<tr><td colspan="5" style="padding:1rem;color:#ef4444;">Failed to load coins. ${e?.message ?? ''}</td></tr>`;
  }
}

/* ===== Chat helpers ===== */
function pushMsg(role, content){
  const div = document.createElement('div');
  div.className = 'msg ' + (role === 'user' ? 'me' : 'bot');
  div.textContent = content;
  const log = document.getElementById('chatLog');
  log.appendChild(div);
  log.scrollTop = log.scrollHeight;
}
function pushUser(text){ pushMsg('user', text); }
function pushBot(text){ pushMsg('assistant', text); }

function formatRecoForChat(r){
  if(!r || !r.top || !r.top.length) return "No picks returned.";
  const lines = r.top.map((p,i)=> `${i+1}) ${p.symbol.toUpperCase()} — ${(p.weight*100).toFixed(0)}% — ${p.why}`);
  const notes = r.notes ? `\nNotes: ${r.notes}` : "";
  return `Here are my latest picks:\n${lines.join("\n")}${notes}\n\nAsk me anything about these choices, risks, or alternatives.`;
}

async function generateReco(){
  const btn = document.getElementById('genReco');
  try {
    btn.disabled = true;
    pushBot('Generating a fresh shortlist…');
    const r = await fetchReco(3);
    document.getElementById('chatLog').lastChild.textContent = formatRecoForChat(r);

    if (window.innerWidth <= 640) {
      const chatCard = document.querySelector('.chat-card');
      if (chatCard) chatCard.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  } catch (e) {
    document.getElementById('chatLog').lastChild.textContent = 'Sorry, I couldn’t generate picks just now. Try again.';
  } finally {
    btn.disabled = false;
  }
}

/* ===== Chat API ===== */
async function sendChat(){
  const input = document.getElementById('chatInput');
  const send = document.getElementById('sendChat');
  const text = input.value.trim();
  if(!text) return;

  pushUser(text);
  input.value = '';
  send.disabled = true;

  try {
    const res = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messages: chat })
    });
    const data = await res.json();
    const reply = (data?.messages?.[0]?.content) ?? '(no reply)';
    pushBot(reply);
  } catch (e) {
    pushBot('Sorry, something went wrong: ' + (e?.message ?? ''));
  } finally {
    send.disabled = false;
  }
}

/* ===== Sparklines (CONCURRENT) ===== */
async function loadSparklinesForPage() {
  const holders = Array.from(document.querySelectorAll('.spark'));
  const tasks = [];

  for (const el of holders) {
    const id = el.getAttribute('data-id');
    if (!id) continue;

    // If cached, draw immediately and skip fetch.
    if (sparkCache.has(id)) {
      drawSparkline(el, sparkCache.get(id));
      continue;
    }

    // Fetch concurrently (no artificial delay)
    const p = fetch(`/api/sparkline?id=${encodeURIComponent(id)}&days=365`)
      .then(res => { if (!res.ok) throw new Error('sparkline fetch failed'); return res.json(); })
      .then(arr => { sparkCache.set(id, arr); drawSparkline(el, arr); })
      .catch(() => { el.innerHTML = '<span style="color:var(--neutral);font-size:.85rem">n/a</span>'; });

    tasks.push(p);
  }

  // Wait for all in-flight fetches to settle (doesn't block rendering)
  if (tasks.length) await Promise.allSettled(tasks);
}

function drawSparkline(holder, values) {
  const w = holder.clientWidth || 150;
  const h = holder.clientHeight || 42;

  if (!Array.isArray(values) || values.length < 2) {
    holder.innerHTML = '<span style="color:var(--neutral);font-size:.85rem">n/a</span>';
    return;
  }

  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = (max - min) || 1;
  const pts = values.map((v, i) => {
    const x = (i / (values.length - 1)) * (w - 2) + 1;
    const y = h - 1 - ((v - min) / span) * (h - 2);
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(' ');

  const rising = values[values.length - 1] >= values[0];
  const stroke = rising ? '#16a34a' : '#ef4444';

  holder.innerHTML = `
    <svg width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="1y sparkline">
      <polyline points="${pts}" fill="none" stroke="${stroke}" stroke-width="2" vector-effect="non-scaling-stroke" />
    </svg>`;
}

/* ===== Events ===== */
document.getElementById('q').addEventListener('input', applyFilter);
document.getElementById('refresh').addEventListener('click', refresh);
document.getElementById('prev').addEventListener('click', () => { if (currentPage>1){ currentPage--; renderTable(); } });
document.getElementById('next').addEventListener('click', () => {
  const totalPages = Math.max(1, Math.ceil(filteredCoins.length / pageSize));
  if (currentPage < totalPages){ currentPage++; renderTable(); }
});
document.getElementById('genReco').addEventListener('click', generateReco);
document.getElementById('sendChat').addEventListener('click', sendChat);
document.getElementById('chatInput').addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendChat(); }
});

/* ===== Theme toggle (persisted) ===== */
(function () {
  const KEY = 'theme';
  const btn = document.getElementById('themeToggle');
  const icon = document.getElementById('themeIcon');

  function setTheme(t) {
    document.documentElement.dataset.theme = t;
    localStorage.setItem(KEY, t);
    if (icon) icon.className = (t === 'light') ? 'bi bi-moon-stars' : 'bi bi-brightness-high';
  }

  const saved = localStorage.getItem(KEY);
  if (saved === 'light' || saved === 'dark') {
    setTheme(saved);
  } else {
    const prefersLight = window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches;
    setTheme(prefersLight ? 'light' : 'dark');
  }

  if (btn) {
    btn.addEventListener('click', () => {
      const next = (document.documentElement.dataset.theme === 'light') ? 'dark' : 'light';
      setTheme(next);
    });
  }
})();

/* ===== Initial load ===== */
refresh();
