#!/bin/bash

# ============================================
# Script de Deploy para CrownAnalyzer Backend
# Servidor: IONOS VPS
# ============================================

set -e  # Salir si hay errores

echo "🚀 Iniciando deploy de CrownAnalyzer Backend..."

# Colores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Directorios
GIT_DIR="/home/crown/CrownRFEP-Reader"
PROD_DIR="/opt/crownanalyzer/backend"
GIT_BRANCH="${GIT_BRANCH:-refactor/code-behind}"

# 1. Actualizar repo git
echo -e "${YELLOW}📥 Obteniendo últimos cambios de Git...${NC}"
cd "$GIT_DIR"
git fetch origin
git checkout "$GIT_BRANCH"
git pull origin "$GIT_BRANCH"

# 2. Sincronizar src a producción (sin tocar data, .env, logs, node_modules)
echo -e "${YELLOW}📂 Sincronizando código a $PROD_DIR...${NC}"
rsync -av --delete \
  --exclude node_modules \
  --exclude data \
  --exclude .env \
  --exclude logs \
  "$GIT_DIR/backend/src/" "$PROD_DIR/src/"

# 3. Sincronizar package.json si cambió
cp "$GIT_DIR/backend/package.json" "$PROD_DIR/package.json"
cp "$GIT_DIR/backend/package-lock.json" "$PROD_DIR/package-lock.json" 2>/dev/null
cp "$GIT_DIR/backend/ecosystem.config.cjs" "$PROD_DIR/ecosystem.config.cjs"

# 4. Instalar dependencias
echo -e "${YELLOW}📦 Instalando dependencias...${NC}"
cd "$PROD_DIR"
npm ci --production 2>&1 | tail -5

# 5. Crear directorio de logs si no existe
mkdir -p logs

# 6. Reiniciar servicio
echo -e "${YELLOW}🔄 Reiniciando servicio...${NC}"
PID=$(ss -tlnp "sport = :3000" 2>/dev/null | grep -oP 'pid=\K[0-9]+')
if [ -n "$PID" ]; then
  kill "$PID" 2>/dev/null
  sleep 2
fi
NODE_ENV=production nohup node src/index.js >> logs/out.log 2>> logs/error.log &
sleep 3

echo -e "${GREEN}✅ Deploy completado exitosamente!${NC}"
echo ""
echo "📊 Health check:"
curl -s http://localhost:3000/health
