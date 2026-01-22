const statusEl = document.getElementById('status');
const loginCard = document.getElementById('login-card');
const loginForm = document.getElementById('login-form');
const emailInput = document.getElementById('email');
const passwordInput = document.getElementById('password');

const usersTableBody = document.querySelector('#users-table tbody');
const teamsTableBody = document.querySelector('#teams-table tbody');
const totalUsers = document.getElementById('total-users');
const totalTeams = document.getElementById('total-teams');
const totalObjects = document.getElementById('total-objects');
const totalBytes = document.getElementById('total-bytes');

const tokenKey = 'admin_token';

function setStatus(text, type) {
  statusEl.textContent = text;
  statusEl.classList.remove('ok', 'err');
  if (type) statusEl.classList.add(type);
}

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
}

async function api(path) {
  const token = localStorage.getItem(tokenKey);
  const res = await fetch(path, {
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

async function loadAll() {
  try {
    setStatus('Cargando...', '');
    const [usersRes, teamsRes, statsRes, metricsRes] = await Promise.all([
      api('/api/admin/users'),
      api('/api/admin/teams'),
      api('/api/admin/wasabi/stats'),
      api('/api/admin/metrics')
    ]);

    renderUsers(usersRes.users || []);
    renderTeams(statsRes.teams || []);

    totalUsers.textContent = metricsRes.totals?.users ?? '-';
    totalTeams.textContent = metricsRes.totals?.teams ?? '-';
    totalObjects.textContent = statsRes.totals?.totalObjects ?? '-';
    totalBytes.textContent = formatBytes(statsRes.totals?.totalBytes ?? 0);

    setStatus('Conectado', 'ok');
  } catch (err) {
    console.error(err);
    setStatus('Error', 'err');
  }
}

function renderUsers(users) {
  usersTableBody.innerHTML = '';
  users.forEach(u => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${u.email || ''}</td>
      <td>${u.name || ''}</td>
      <td>${u.role || ''}</td>
      <td>${u.team_name || u.team_id || ''}</td>
      <td>${u.last_login || ''}</td>
    `;
    usersTableBody.appendChild(tr);
  });
}

function renderTeams(teams) {
  teamsTableBody.innerHTML = '';
  teams.forEach(t => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${t.teamName || ''}</td>
      <td>${t.wasabiFolder || ''}</td>
      <td>${t.objectCount ?? 0}</td>
      <td>${formatBytes(t.totalBytes ?? 0)}</td>
      <td>${t.lastModified ? new Date(t.lastModified).toLocaleString() : ''}</td>
    `;
    teamsTableBody.appendChild(tr);
  });
}

loginForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const email = emailInput.value.trim();
  const password = passwordInput.value.trim();
  if (!email || !password) return;

  try {
    setStatus('Autenticando...', '');
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    const data = await res.json();
    if (!res.ok || !data.accessToken) {
      setStatus('Credenciales inválidas', 'err');
      return;
    }

    localStorage.setItem(tokenKey, data.accessToken);
    loginCard.style.display = 'none';
    await loadAll();
  } catch (err) {
    console.error(err);
    setStatus('Error login', 'err');
  }
});

if (localStorage.getItem(tokenKey)) {
  loginCard.style.display = 'none';
  loadAll();
} else {
  setStatus('Desconectado', '');
}
