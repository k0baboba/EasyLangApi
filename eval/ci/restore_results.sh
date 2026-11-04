#!/usr/bin/env bash
# restore_results.sh DEST LOCAL — если в ветке results есть папка DEST, копирует её в LOCAL (продолжение прогона).
set -uo pipefail
DEST=$1; LOCAL=$2
mkdir -p "$LOCAL"
if git fetch -q origin results 2>/dev/null && git cat-file -e "FETCH_HEAD:$DEST" 2>/dev/null; then
  git archive FETCH_HEAD "$DEST" | tar -x -C /tmp
  cp -r "/tmp/$DEST"/. "$LOCAL"/
  echo "продолжаю с сохранённого: $(cat "$LOCAL"/answers.jsonl 2>/dev/null | wc -l) ответов"
else
  echo "сохранённого прогона нет, начинаю с нуля"
fi
