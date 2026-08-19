#!/usr/bin/env bash
# Compila o pacote e roda os testes EditMode em batchmode, imprimindo um resumo legível.
#
# Uso:  bash tools~/unity-test.sh [--compile-only]
#
# O projeto Unity usado para hospedar o pacote é criado sob demanda FORA do repositório e
# reusado nas execuções seguintes. Nada de projeto Unity versionado: o que o repositório
# precisa entregar é o pacote, e um projeto commitado só serviria para inflar o clone e
# desatualizar.
#
# Variáveis:
#   UNITY_VERSION       versão do editor (default 6000.3.20f1)
#   UNITY_TEST_PROJECT  onde criar o projeto (default ../ui-exporter-test-project)
set -uo pipefail

UNITY_VERSION="${UNITY_VERSION:-6000.3.20f1}"
UNITY="/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe"

PACKAGE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="${UNITY_TEST_PROJECT:-$(cd "${PACKAGE_DIR}/.." && pwd)/ui-exporter-test-project}"

if [[ ! -x "${UNITY}" ]]; then
  echo "Unity ${UNITY_VERSION} não encontrado em:" >&2
  echo "  ${UNITY}" >&2
  echo "Defina UNITY_VERSION para uma versão instalada." >&2
  exit 1
fi

# Caminho no formato que o Unity.exe entende (C:/... em vez de /c/...).
to_windows_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s\n' "$1"
  fi
}

PROJECT_WIN="$(to_windows_path "${PROJECT_DIR}")"
PACKAGE_WIN="$(to_windows_path "${PACKAGE_DIR}")"
RESULTS="${PROJECT_DIR}/test-results.xml"
LOG="${PROJECT_DIR}/last-run.log"

if [[ ! -d "${PROJECT_DIR}/Assets" ]]; then
  echo "== criando projeto de teste em ${PROJECT_DIR} =="
  "${UNITY}" -batchmode -nographics -quit \
    -createProject "${PROJECT_WIN}" -logFile - >/dev/null 2>&1 || true

  if [[ ! -d "${PROJECT_DIR}/Assets" ]]; then
    echo "não consegui criar o projeto de teste" >&2
    exit 1
  fi
fi

# Reescrito a cada execução: garante que o pacote aponta para ESTE checkout, mesmo que o
# repositório tenha sido movido, e que "testables" está lá — sem ele o test runner não
# descobre os testes que vivem dentro do pacote.
mkdir -p "${PROJECT_DIR}/Packages"
cat > "${PROJECT_DIR}/Packages/manifest.json" <<MANIFEST
{
  "dependencies": {
    "com.arvore.uiexporter": "file:${PACKAGE_WIN}",
    "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.unity.test-framework": "1.4.6",
    "com.unity.ugui": "2.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.unitywebrequest": "1.0.0"
  },
  "testables": [
    "com.arvore.uiexporter"
  ]
}
MANIFEST

compile_only=0
[[ "${1:-}" == "--compile-only" ]] && compile_only=1

rm -f "${RESULTS}"

if (( compile_only )); then
  echo "== compilando =="
  "${UNITY}" -batchmode -nographics -quit \
    -projectPath "${PROJECT_WIN}" -logFile - >"${LOG}" 2>&1
else
  echo "== compilando e rodando testes EditMode =="
  "${UNITY}" -batchmode -nographics -runTests \
    -projectPath "${PROJECT_WIN}" \
    -testPlatform EditMode \
    -testResults "${PROJECT_WIN}/test-results.xml" \
    -logFile - >"${LOG}" 2>&1
fi

# Erro de compilação aparece no log como CSxxxx; sem esta checagem um build quebrado passaria
# como "0 testes" e daria impressão de sucesso.
if grep -qE "\bCS[0-9]{4}\b" "${LOG}"; then
  echo "== ERROS DE COMPILAÇÃO =="
  grep -oE "[^ ]+\.cs\([0-9]+,[0-9]+\): error CS[0-9]{4}: .*" "${LOG}" | sort -u | head -40
  exit 1
fi

if (( compile_only )); then
  echo "compilou sem erros"
  exit 0
fi

if [[ ! -f "${RESULTS}" ]]; then
  echo "== nenhum resultado de teste gerado; fim do log: =="
  tail -30 "${LOG}"
  exit 1
fi

node "${PACKAGE_DIR}/tools~/parse-test-results.mjs" "${RESULTS}"
