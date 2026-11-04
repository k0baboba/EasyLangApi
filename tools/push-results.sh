#!/usr/bin/env bash
# Кладёт папку с результатами в ветку results: каждая задача — в свою папку.
# Если кто-то запушил в results раньше, забирает свежую ветку и повторяет попытку.
# Использование (в GitHub Actions, нужен GH_TOKEN с contents: write):
#   bash tools/push-results.sh <папка в results> <откуда копировать>
set -euo pipefail
dest="$1"
src="$2"
# RESULTS_REMOTE — чтобы проверить скрипт локально на другом репозитории.
repo="${RESULTS_REMOTE:-https://x-access-token:${GH_TOKEN}@github.com/${GITHUB_REPOSITORY}.git}"
work="$(mktemp -d)"
git -C "$work" init -q -b results
git -C "$work" remote add origin "$repo"
git -C "$work" config user.name "github-actions[bot]"
git -C "$work" config user.email "41898282+github-actions[bot]@users.noreply.github.com"

for attempt in 1 2 3 4 5; do
  # Ветки ещё нет — тогда наш коммит станет её первым коммитом.
  if git -C "$work" fetch -q --depth 1 origin results 2>/dev/null; then
    git -C "$work" reset -q --hard FETCH_HEAD
  fi
  rm -rf "${work:?}/$dest"
  mkdir -p "$work/$dest"
  cp -r "$src"/. "$work/$dest"/
  git -C "$work" add -A "$dest"
  if git -C "$work" diff --cached --quiet; then
    echo "нечего коммитить"
    exit 0
  fi
  git -C "$work" commit -q -m "results: $dest (${GITHUB_WORKFLOW}, ${GITHUB_SHA::7})"
  if git -C "$work" push -q origin HEAD:results; then
    echo "запушено в results/$dest"
    exit 0
  fi
  echo "push отклонён (попытка $attempt), забираю свежую ветку results"
  sleep $((attempt * 5))
done
exit 1
