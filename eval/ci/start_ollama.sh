#!/usr/bin/env bash
# Ставит Ollama на раннер GitHub Actions, запускает сервер и скачивает модели.
# Модели: $EVAL_MODEL и $EVAL_JUDGE_MODEL (если отличается).
set -euo pipefail

# Освобождаем место: две модели занимают ~10 ГБ, а SSD раннера 14 ГБ.
sudo rm -rf /usr/local/lib/android /opt/ghc /usr/share/dotnet /opt/hostedtoolcache/CodeQL || true
df -h / | tail -1

curl -fsSL https://ollama.com/install.sh | sh
if ! curl -sf http://127.0.0.1:11434/api/tags >/dev/null; then
  nohup ollama serve > /tmp/ollama.log 2>&1 &
fi
for i in $(seq 1 60); do curl -sf http://127.0.0.1:11434/api/tags >/dev/null && break; sleep 1; done
curl -sf http://127.0.0.1:11434/api/tags >/dev/null || { echo "Ollama не поднялась"; cat /tmp/ollama.log || true; exit 1; }

for m in "$EVAL_MODEL" "${EVAL_JUDGE_MODEL:-$EVAL_MODEL}"; do
  start=$(date +%s)
  ollama pull "$m"
  echo "pull $m: $(( $(date +%s) - start )) с"
done
ollama --version
ollama list
