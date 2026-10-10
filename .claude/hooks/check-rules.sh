#!/usr/bin/env bash
# PostToolUse hook (Edit|Write). Reports rule violations back to Claude:
# exit 2 = the file is already written, stderr is shown to Claude so it fixes the code.
# Rules: CLAUDE.md "Naming" (no Teamtailor in *.Domain / *.Application) and "Tech stack" (TimeProvider).
set -u
shopt -s nullglob

root="${CLAUDE_PROJECT_DIR:-.}"
if command -v cygpath >/dev/null 2>&1; then root="$(cygpath -u "$root")"; fi   # Git Bash on Windows
cd "$root" 2>/dev/null || exit 0
[ -d src ] || exit 0

problems=""

# 1. "Teamtailor" never in *.Domain or *.Application (code or project references).
inner=( src/*/TalentSync.*.Domain src/*/TalentSync.*.Application )
if [ ${#inner[@]} -gt 0 ]; then
  hits=$(grep -rIin --include='*.cs' --include='*.csproj' --exclude-dir=bin --exclude-dir=obj \
         'teamtailor' "${inner[@]}" 2>/dev/null)
  if [ -n "$hits" ]; then
    problems+=$'"Teamtailor" in a Domain or Application project (CLAUDE.md, Naming; use the IRecruitmentSource port):\n'"$hits"$'\n\n'
  fi
fi

# 2. No direct clock in src/: use TimeProvider.
hits=$(grep -rIEn --include='*.cs' --exclude-dir=bin --exclude-dir=obj \
       'DateTime(Offset)?\.(UtcNow|Now)\b' src 2>/dev/null)
if [ -n "$hits" ]; then
  problems+=$'Direct clock in src/ (CLAUDE.md, Tech stack; inject TimeProvider instead):\n'"$hits"$'\n'
fi

if [ -n "$problems" ]; then
  printf 'Rule check failed. Fix these before continuing:\n\n%s' "$problems" >&2
  exit 2
fi
exit 0
