#!/usr/bin/env bash
# environment.sh FILE — записывает, на чём шёл прогон (без секретов).
{
  echo "runner: ${RUNNER_OS:-?} ${ImageOS:-} ${ImageVersion:-}"
  echo "cpu: $(nproc) x $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs)"
  echo "memory: $(free -g | awk '/Mem:/ {print $2}') GiB"
  echo "python: $(python3 --version 2>&1)"
  echo "ollama: $(ollama --version 2>&1 | tail -1)"
  echo "models:"; ollama list 2>&1 | sed 's/^/  /'
  echo "git: ${GITHUB_SHA:-?} (${GITHUB_REF_NAME:-?}), run ${GITHUB_RUN_ID:-?} attempt ${GITHUB_RUN_ATTEMPT:-?}"
  echo "date: $(date -u '+%Y-%m-%d %H:%M UTC')"
} > "$1"
