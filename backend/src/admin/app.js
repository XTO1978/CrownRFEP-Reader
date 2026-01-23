const statusEl = document.getElementById('status');
const loginCard = document.getElementById('login-card');
const loginForm = document.getElementById('login-form');
const emailInput = document.getElementById('email');
const passwordInput = document.getElementById('password');

const usersTableBody = document.querySelector('#users-table tbody');
const teamsTableBody = document.querySelector('#teams-table tbody');
const rolesTableBody = document.querySelector('#roles-table tbody');
const orgsTableBody = document.querySelector('#orgs-table tbody');
const totalUsers = document.getElementById('total-users');
const totalTeams = document.getElementById('total-teams');
const totalObjects = document.getElementById('total-objects');
const totalBytes = document.getElementById('total-bytes');
const userForm = document.getElementById('user-form');
const roleForm = document.getElementById('role-form');
const orgForm = document.getElementById('org-form');
const userRoleSelect = document.getElementById('user-role');
const userTeamSelect = document.getElementById('user-team');

const userEmail = document.getElementById('user-email');
const userName = document.getElementById('user-name');
const userPassword = document.getElementById('user-password');

const roleId = document.getElementById('role-id');
const roleName = document.getElementById('role-name');
const roleDescription = document.getElementById('role-description');
const rolePerms = document.getElementById('role-perms');

const orgId = document.getElementById('org-id');
const orgName = document.getElementById('org-name');
const orgWasabi = document.getElementById('org-wasabi');

const ROOT_USER_EMAIL = 'xtaberna@outlook.com';

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

function formatDate(value) {
  if (!value) return '';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return String(value);
  }
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
    const [usersRes, teamsRes, statsRes, metricsRes, rolesRes] = await Promise.all([
      api('/api/admin/users'),
      api('/api/admin/teams'),
      api('/api/admin/wasabi/stats'),
      api('/api/admin/metrics'),
      api('/api/admin/roles')
    ]);

    renderOrgs(teamsRes.teams || []);
    renderTeamSelect(teamsRes.teams || []);
    renderRoles(rolesRes.roles || []);
    renderRoleSelect(rolesRes.roles || []);
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
    const isRootUser = u.email === ROOT_USER_EMAIL;
    const roleOptions = Array.from(userRoleSelect.options)
      .map(opt => `<option value="${opt.value}" ${opt.value === u.role ? 'selected' : ''}>${opt.value}</option>`)
      .join('');
    const teamOptions = Array.from(userTeamSelect.options)
      .map(opt => `<option value="${opt.value}" ${opt.value === u.team_id ? 'selected' : ''}>${opt.textContent}</option>`)
      .join('');
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${u.email || ''}</td>
      <td>${u.name || ''}</td>
      <td>
        <select data-user-role="${u.id}" ${isRootUser ? 'disabled' : ''}>${roleOptions}</select>
      </td>
      <td>
        ${isRootUser 
          ? '<span style="color: #888; font-style: italic;">Root (sin org)</span>' 
          : `<select data-user-team="${u.id}">${teamOptions}</select>`}
      </td>
      <td>
        <button class="btn-secondary" data-user-devices="${u.id}">Ver</button>
        <div data-user-devices-container="${u.id}" style="margin-top:6px;font-size:12px;color:#bbb;"></div>
      </td>
      <td>${u.last_login || ''}</td>
      <td>
        ${isRootUser 
          ? '<span style="color: #888;">Protegido</span>' 
          : `<button class="btn-secondary" data-user-save="${u.id}">Guardar</button>
             <button class="btn-danger" data-user-delete="${u.id}">Eliminar</button>`}
      </td>
    `;
    usersTableBody.appendChild(tr);
  });

  usersTableBody.querySelectorAll('[data-user-save]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-user-save');
      const role = usersTableBody.querySelector(`[data-user-role="${id}"]`).value;
      const teamSelect = usersTableBody.querySelector(`[data-user-team="${id}"]`);
      const teamId = teamSelect ? teamSelect.value : null;
      await fetch(`/api/admin/users/${id}`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${localStorage.getItem(tokenKey)}`
        },
        body: JSON.stringify({ role, teamId })
      });
      await loadAll();
    });
  });

  usersTableBody.querySelectorAll('[data-user-delete]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-user-delete');
      if (!confirm('¿Eliminar usuario?')) return;
      await fetch(`/api/admin/users/${id}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${localStorage.getItem(tokenKey)}` }
      });
      await loadAll();
    });
  });

  usersTableBody.querySelectorAll('[data-user-devices]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-user-devices');
      const container = usersTableBody.querySelector(`[data-user-devices-container="${id}"]`);
      if (!container) return;

      container.textContent = 'Cargando...';
      try {
        const res = await fetch(`/api/admin/users/${id}/devices`, {
          headers: { Authorization: `Bearer ${localStorage.getItem(tokenKey)}` }
        });
        if (!res.ok) {
          container.textContent = 'Error cargando dispositivos';
          return;
        }
        const data = await res.json();
        const devices = data.devices || [];
        if (devices.length === 0) {
          container.textContent = 'Sin dispositivos';
          return;
        }

        container.innerHTML = devices.map(d => {
          const revoked = d.revoked_at ? ' (revocado)' : '';
          const revokeBtn = d.revoked_at
            ? ''
            : `<button class="btn-danger" data-device-revoke="${id}" data-device-id="${d.device_id}">Revocar</button>`;
          return `
            <div style="margin-bottom:6px;">
              <div><strong>${d.platform || 'unknown'}</strong> - ${d.device_name || d.device_id}${revoked}</div>
              <div>Último uso: ${formatDate(d.last_seen)}</div>
              ${revokeBtn}
            </div>
          `;
        }).join('');

        container.querySelectorAll('[data-device-revoke]').forEach(rbtn => {
          rbtn.addEventListener('click', async () => {
            const deviceId = rbtn.getAttribute('data-device-id');
            if (!confirm('¿Revocar este dispositivo?')) return;
            await fetch(`/api/admin/users/${id}/devices/${encodeURIComponent(deviceId)}`, {
              method: 'DELETE',
              headers: { Authorization: `Bearer ${localStorage.getItem(tokenKey)}` }
            });
            btn.click();
          });
        });
      } catch (err) {
        console.error(err);
        container.textContent = 'Error cargando dispositivos';
      }
    });
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

function renderRoles(roles) {
  rolesTableBody.innerHTML = '';
  roles.forEach(r => {
    const perms = (() => {
      try { return JSON.parse(r.permissions_json || '[]').join(', '); } catch { return ''; }
    })();
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${r.id}</td>
      <td><input data-role-name="${r.id}" value="${r.name || ''}" /></td>
      <td><input data-role-desc="${r.id}" value="${r.description || ''}" /></td>
      <td><input data-role-perms="${r.id}" value="${perms}" /></td>
      <td>
        <button class="btn-secondary" data-role-save="${r.id}">Guardar</button>
        <button class="btn-danger" data-role-delete="${r.id}">Eliminar</button>
      </td>
    `;
    rolesTableBody.appendChild(tr);
  });

  rolesTableBody.querySelectorAll('[data-role-save]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-role-save');
      const name = rolesTableBody.querySelector(`[data-role-name="${id}"]`).value;
      const description = rolesTableBody.querySelector(`[data-role-desc="${id}"]`).value;
      const permissions = rolesTableBody.querySelector(`[data-role-perms="${id}"]`).value
        .split(',').map(p => p.trim()).filter(Boolean);
      await fetch(`/api/admin/roles/${id}`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${localStorage.getItem(tokenKey)}`
        },
        body: JSON.stringify({ name, description, permissions })
      });
      await loadAll();
    });
  });

  rolesTableBody.querySelectorAll('[data-role-delete]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-role-delete');
      if (!confirm('¿Eliminar rol?')) return;
      await fetch(`/api/admin/roles/${id}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${localStorage.getItem(tokenKey)}` }
      });
      await loadAll();
    });
  });
}

function renderRoleSelect(roles) {
  userRoleSelect.innerHTML = '';
  roles.forEach(r => {
    const opt = document.createElement('option');
    opt.value = r.id;
    opt.textContent = r.id;
    userRoleSelect.appendChild(opt);
  });
}

function renderTeamSelect(teams) {
  userTeamSelect.innerHTML = '';
  teams.forEach(t => {
    const opt = document.createElement('option');
    opt.value = t.id;
    opt.textContent = t.name;
    userTeamSelect.appendChild(opt);
  });
}

function renderOrgs(teams) {
  orgsTableBody.innerHTML = '';
  teams.forEach(t => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${t.id}</td>
      <td><input data-org-name="${t.id}" value="${t.name || ''}" /></td>
      <td><input data-org-wasabi="${t.id}" value="${t.wasabi_folder || ''}" /></td>
      <td>${t.created_at || ''}</td>
      <td>
        <button class="btn-secondary" data-org-save="${t.id}">Guardar</button>
        <button class="btn-danger" data-org-delete="${t.id}">Eliminar</button>
      </td>
    `;
    orgsTableBody.appendChild(tr);
  });

  orgsTableBody.querySelectorAll('[data-org-save]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-org-save');
      const name = orgsTableBody.querySelector(`[data-org-name="${id}"]`).value;
      const wasabiFolder = orgsTableBody.querySelector(`[data-org-wasabi="${id}"]`).value;
      await fetch(`/api/admin/teams/${id}`, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${localStorage.getItem(tokenKey)}`
        },
        body: JSON.stringify({ name, wasabiFolder })
      });
      await loadAll();
    });
  });

  orgsTableBody.querySelectorAll('[data-org-delete]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const id = btn.getAttribute('data-org-delete');
      if (!confirm('¿Eliminar organización? Asegúrate de que no hay usuarios asignados.')) return;
      const res = await fetch(`/api/admin/teams/${id}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${localStorage.getItem(tokenKey)}` }
      });
      if (!res.ok) {
        const data = await res.json();
        alert(data.error || 'Error eliminando organización');
      }
      await loadAll();
    });
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

userForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const payload = {
    email: userEmail.value.trim(),
    password: userPassword.value.trim(),
    name: userName.value.trim(),
    teamId: userTeamSelect.value,
    role: userRoleSelect.value
  };
  if (!payload.email || !payload.password || !payload.name) return;

  await fetch('/api/admin/users', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${localStorage.getItem(tokenKey)}`
    },
    body: JSON.stringify(payload)
  });

  userEmail.value = '';
  userName.value = '';
  userPassword.value = '';
  await loadAll();
});

orgForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const payload = {
    id: orgId.value.trim(),
    name: orgName.value.trim(),
    wasabiFolder: orgWasabi.value.trim()
  };
  if (!payload.id || !payload.name || !payload.wasabiFolder) return;

  const res = await fetch('/api/admin/teams', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${localStorage.getItem(tokenKey)}`
    },
    body: JSON.stringify(payload)
  });

  if (!res.ok) {
    const data = await res.json();
    alert(data.error || 'Error creando organización');
  }

  orgId.value = '';
  orgName.value = '';
  orgWasabi.value = '';
  await loadAll();
});

roleForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const payload = {
    id: roleId.value.trim(),
    name: roleName.value.trim(),
    description: roleDescription.value.trim(),
    permissions: rolePerms.value.split(',').map(p => p.trim()).filter(Boolean)
  };
  if (!payload.id || !payload.name) return;

  await fetch('/api/admin/roles', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${localStorage.getItem(tokenKey)}`
    },
    body: JSON.stringify(payload)
  });

  roleId.value = '';
  roleName.value = '';
  roleDescription.value = '';
  rolePerms.value = '';
  await loadAll();
});

if (localStorage.getItem(tokenKey)) {
  loginCard.style.display = 'none';
  loadAll();
} else {
  setStatus('Desconectado', '');
}
