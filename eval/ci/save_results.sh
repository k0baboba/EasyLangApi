#!/usr/bin/env bash
# save_results.sh SRC DEST [MSG] — кладёт содержимое SRC в папку DEST ветки results и пушит.
# Каждая попытка начинается со свежего состояния ветки (аналог rebase), до 6 попыток.
set -uo pipefail
SRC=$1; DEST=$2; MSG=${3:-"results: $DEST"}
[ -d "$SRC" ] || { echo "нет $SRC, сохранять нечего"; exit 0; }
git config --global user.name "github-actions[bot]"
git config --global user.email "41898282+github-actions[bot]@users.noreply.github.com"
WT=$(mktemp -d)
for attempt in 1 2 3 4 5 6; do
  rm -rf "$WT"; mkdir -p "$WT"
  if git fetch -q origin results 2>/dev/null; then
    git worktree add -q --force -B results-save "$WT" FETCH_HEAD
  else
    git worktree add -q --force --detach "$WT"
    (cd "$WT" && git checkout -q --orphan results-save && git rm -rqf . >/dev/null && git clean -fdxq)
  fi
  mkdir -p "$WT/$DEST"
  cp -r "$SRC"/. "$WT/$DEST"/
  (cd "$WT" && git add -A "$DEST" && { git diff --cached --quiet && echo "без изменений" || git commit -q -m "$MSG"; })
  if (cd "$WT" && git push -q origin HEAD:refs/heads/results); then
    echo "сохранено в results:$DEST"; git worktree remove --force "$WT"; exit 0
  fi
  git worktree remove --force "$WT" || true
  sleep $((attempt * 5))
done
echo "не удалось запушить results:$DEST"; exit 1
