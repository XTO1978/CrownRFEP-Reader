# Deploy en IONOS VPS

## Requisitos del servidor

- Node.js >= 18.x
- npm >= 9.x
- PM2 (gestor de procesos)
- Git

## 1. Configuración inicial del servidor (una sola vez)

### Conectar por SSH
```bash
ssh usuario@tu-servidor-ionos.com
```

### Instalar Node.js 18+ (si no está instalado)
```bash
# Usando nvm (recomendado)
curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.39.0/install.sh | bash
source ~/.bashrc
nvm install 18
nvm use 18
nvm alias default 18

# Verificar
node --version  # Debe ser >= 18.x
```

### Instalar PM2 globalmente
```bash
npm install -g pm2
```

### Clonar el repositorio
```bash
mkdir -p /home/crown
cd /home/crown
git clone https://github.com/XTO1978/CrownRFEP-Reader.git
cd CrownRFEP-Reader/backend
```

### Configurar variables de entorno
```bash
# Crear archivo .env con las credenciales de Wasabi
cp .env.example .env
nano .env
```

Contenido del `.env`:
```env
# Servidor
PORT=3000
NODE_ENV=production

# JWT
JWT_SECRET=tu_clave_secreta_muy_larga_y_segura_aqui

# Wasabi S3
WASABI_REGION=eu-west-2
WASABI_BUCKET=crownanalyzer
WASABI_ACCESS_KEY=tu_access_key_de_wasabi
WASABI_SECRET_KEY=tu_secret_key_de_wasabi
WASABI_ENDPOINT=https://s3.eu-west-2.wasabisys.com
```

### Instalar dependencias
```bash
npm ci --production
```

### Inicializar base de datos (crear usuarios)
```bash
npm run init-db
```

### Crear directorio de logs
```bash
mkdir -p logs
```

## 2. Iniciar el servicio

### Con PM2 (recomendado para producción)
```bash
pm2 start ecosystem.config.cjs --env production
pm2 save
pm2 startup  # Seguir instrucciones para auto-inicio
```

### Verificar que está corriendo
```bash
pm2 status
pm2 logs crownanalyzer-backend
```

## 3. Configurar Firewall (si es necesario)

### Abrir puerto 3000
```bash
sudo ufw allow 3000/tcp
```

### O usar Nginx como proxy (recomendado)
```bash
sudo apt install nginx
```

Configuración Nginx (`/etc/nginx/sites-available/crownanalyzer`):
```nginx
server {
    listen 80;
    server_name api.tudominio.com;

    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_cache_bypass $http_upgrade;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/crownanalyzer /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

### Configurar HTTPS con Let's Encrypt
```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d api.tudominio.com
```

## 4. Deploys posteriores

```bash
# Desde tu Mac, solo tienes que:
ssh usuario@tu-servidor-ionos.com "cd /home/crown/CrownRFEP-Reader/backend && ./deploy.sh"
```

O conectarte por SSH y ejecutar:
```bash
cd /home/crown/CrownRFEP-Reader/backend
./deploy.sh
```

## 5. Comandos útiles de PM2

```bash
# Ver estado
pm2 status

# Ver logs en tiempo real
pm2 logs crownanalyzer-backend

# Reiniciar
pm2 restart crownanalyzer-backend

# Detener
pm2 stop crownanalyzer-backend

# Eliminar
pm2 delete crownanalyzer-backend

# Monitoreo
pm2 monit
```

## 6. Verificar funcionamiento

```bash
# Desde el servidor
curl http://localhost:3000/health

# Desde fuera (si el firewall está abierto)
curl http://tu-servidor-ionos.com:3000/health

# O con dominio y HTTPS
curl https://api.tudominio.com/health
```

Respuesta esperada:
```json
{
  "status": "ok",
  "timestamp": "2026-01-22T...",
  "uptimeSeconds": 123,
  ...
}
```

## 7. Actualizar la app para usar el servidor en producción

Una vez desplegado, actualizar `CloudBackendService.cs` con la URL del servidor:

```csharp
// En el método GetBackendUrl()
#if DEBUG
    return "http://localhost:3000/api";
#else
    return "https://api.tudominio.com/api";
#endif
```
