#!/bin/bash
# Script de inicio para CrownAnalyzer Backend
# Usar: ./start.sh

export NVM_DIR="$HOME/.nvm"
[ -s "$NVM_DIR/nvm.sh" ] && . "$NVM_DIR/nvm.sh"

cd ~/crownanalyzer-backend || exit 1
mkdir -p logs

# Matar proceso existente si hay
pkill -f "node src/index.js" 2>/dev/null || true
sleep 1

# Iniciar nuevo proceso
nohup node src/index.js >> logs/server.log 2>&1 &
echo $! > logs/server.pid
sleep 2

if ps -p $(cat logs/server.pid) > /dev/null 2>&1; then
    echo "✅ Servidor iniciado con PID: $(cat logs/server.pid)"
    echo "📋 Últimas líneas del log:"
    tail -5 logs/server.log 2>/dev/null || echo "(log vacío)"
else
    echo "❌ Error iniciando servidor"
    cat logs/server.log
    exit 1
fi
