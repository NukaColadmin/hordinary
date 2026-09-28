#!/usr/bin/env bash
# Deroule chaque scenario de demonstration dans le moteur, en headless, et resume le verdict de
# chaque prise : « PRISE CONFORME », « PRISE LIBRE » ou l'ecart a l'attente de config/demo_scenarios.json.
#
# C'est la seconde moitie de la verification. Tests/DemoScenarioTests.cs exige la meme chose de chaque
# scenario par le harnais, sans moteur ; ce script la redemande au jeu lui-meme, parce qu'un harnais
# peut avoir raison la ou le moteur se trompe (docs/architecture.md, « Mode demonstration »).
#
#   GODOT=/chemin/vers/Godot_v4.7.1-stable_mono_console.exe tools/demo/prises_headless.sh
#   tools/demo/prises_headless.sh carte_blanche fouiller_puis_rappeler   # quelques prises seulement
#
# Reconstruire avant : `dotnet build Hordinary.sln`. `dotnet test` ne reconstruit pas le jeu, et Godot
# jouerait l'ancien Hordinary.Core avec le config/ neuf.
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
GODOT="${GODOT:-/c/Users/rapha/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe}"
OUT="${OUT:-$ROOT/_work/demo_runs}"
mkdir -p "$OUT"
: > "$OUT/summary.txt"

# Le chemin passe a Godot doit etre natif : sous Git Bash, /c/... n'est pas compris par --path.
PROJECT="$ROOT"
command -v cygpath > /dev/null && PROJECT="$(cygpath -w "$ROOT")"

run() {
  local id="$1"; shift
  local tag="$id$*"
  tag="${tag// /}"
  tag="${tag//[^A-Za-z0-9_=-]/_}"
  # --fixed-fps 60 n'est pas optionnel : sans lui, une frame dure une milliseconde et --quit-after
  # ne dit plus rien du temps de jeu.
  timeout 900 "$GODOT" --headless --path "$PROJECT" --fixed-fps 60 --quit-after 40000 \
    -- --demo="$id" --quitter "$@" > "$OUT/$tag.log" 2>&1
  local verdict partie errors
  verdict=$(grep -E "\[Demo\] PRISE" "$OUT/$tag.log" | head -1)
  partie=$(grep -E "\[Partie\]" "$OUT/$tag.log" | head -1)
  errors=$(grep -cE "ERROR|Unhandled|Exception" "$OUT/$tag.log")
  echo "$tag | ${verdict:-AUCUN BILAN} | $partie | erreurs=$errors" | tee -a "$OUT/summary.txt"
}

if [ "$#" -gt 0 ]; then
  for id in "$@"; do run "$id"; done
  exit 0
fi

TAKES="aucune_effraction ni_vitre_ni_effraction fouiller_avant_de_repartir regroupement menace_avant_la_fuite
effraction combat_arme sans_arme fuite soin partage rappel_du_van
diversion pret_d_arme soin_d_un_camarade radio prevenir_avant_de_rentrer renvoyer_au_van rappeler_tout_le_monde
armer_puis_diversion soigner_et_armer fouiller_puis_rappeler sans_effraction_mais_arme
carte_blanche"

# Les scenarios qui portent une consigne se rejouent sans elle : c'est le contraste qui montre
# que la consigne, et non le terrain, a produit le comportement.
CONTRASTS="aucune_effraction ni_vitre_ni_effraction fouiller_avant_de_repartir regroupement menace_avant_la_fuite
diversion pret_d_arme soin_d_un_camarade prevenir_avant_de_rentrer renvoyer_au_van rappeler_tout_le_monde
armer_puis_diversion soigner_et_armer fouiller_puis_rappeler sans_effraction_mais_arme"

for id in $TAKES; do run "$id"; done
for id in $CONTRASTS; do run "$id" --sans-consignes; done

# La carte blanche sous une consigne du joueur, en grammaire fermee : la prise n'a pas d'attente,
# son bilan dit « PRISE LIBRE ».
run carte_blanche "--consigne=require_before(armed, enter:building)"
run carte_blanche "--consigne=require_before(band_ordered(extraction), ReachExtraction)"

echo FINI >> "$OUT/summary.txt"
