// ===== State =====
let allCoins = [];
let filteredCoins = [];
let currentPage = 1;
const pageSize = 10;
const chat = []; // {role, content}
const sparkCache = new Map();

// ===== Utils =====
function fmt(n){ return Number(n).toLocaleString(undefined,{maximumFractionDigits:2}); }
function pctClass(v){ if(v === null || v === undefined) return 'pct dim'; return v>=0 ? 'pct pos' : 'pct neg'; }

function row(c){
  const pct = c.priceChangePercentage1y ?? null;
  return `<tr>
    <td>${c.marketCapRank}</td>
    <td style="display:flex; gap:.65rem; align-items:center;">
      <img class="avatar" src="${c.image}" alt="${c.symbol}" />
      <div>
        <div style="font-weight:600">${c.name}</div>
        <div style="color:var(--muted); font-size:.86rem">${c.symbol.toUpperCase()}</div>
      </div>
    </td>
    <td class="num">$${fmt(c.currentPrice)}</td>
    <td class="num ${pctClass(pct)}">${pct===null?'—':fmt(pct)+'%'}</td>
    <td class="num"><div class="spark" data-id="${c.id}"></div></td>
  </tr>`;
}

// ===== Data calls =====
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

// ===== Render =====
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

  loadSparklinesForPage();
}

function applyFilter(){
  const q = document.getElementById('q').value.trim().toLowerCase();
  filteredCoins = allCoins.filter(c => !q || c.name.toLowerCase().includes(q) || c.symbol.toLowerCase().includes(q));
  currentPage = 1;
  renderTable();
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

// ===== Chat helpers =====
function pushMsg(role, content){
  chat.push({ role, content });
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

// Generate picks -> ONLY post into chat
async function generateReco(){
  const btn = document.getElementById('genReco');
  try {
    btn.disabled = true;
    pushBot('Generating a fresh shortlist…');
    const r = await fetchReco(3);
    document.getElementById('chatLog').lastChild.textContent = formatRecoForChat(r);
  } catch (e) {
    document.getElementById('chatLog').lastChild.textContent = 'Sorry, I couldn’t generate picks just now. Try again.';
  } finally {
    btn.disabled = false;
  }
}

// ===== Chat API =====
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

// ===== Sparklines =====
async function loadSparklinesForPage() {
  const holders = Array.from(document.querySelectorAll('.spark'));
  for (let i = 0; i < holders.length; i++) {
    const el = holders[i];
    const id = el.getAttribute('data-id');
    if (!id) continue;

    if (sparkCache.has(id)) {
      drawSparkline(el, sparkCache.get(id));
      continue;
    }

    try {
      await new Promise(r => setTimeout(r, i * 80)); // gentle stagger
      const res = await fetch(`/api/sparkline?id=${encodeURIComponent(id)}&days=365`);
      if (!res.ok) throw new Error('sparkline fetch failed');
      const arr = await res.json(); // number[]
      sparkCache.set(id, arr);
      drawSparkline(el, arr);
    } catch (e) {
      el.innerHTML = '<span style="color:var(--neutral);font-size:.85rem">n/a</span>';
    }
  }
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

// ===== Events =====
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

// ===== Initial load =====
refresh();
