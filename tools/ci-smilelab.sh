#!/usr/bin/env bash
# 在专属隔离 Docker daemon 验证完整镜像；不接触运行数据库或患者数据。
set -euo pipefail
umask 077
mkdir -p "$TMPDIR" backend/test-results
CI_TEMP="$(mktemp -d "$TMPDIR/smilelab-tests.XXXXXX")"
DSO_PID=''
cleanup() {
    touch "$CI_TEMP/ui.done"
    if [ -n "$DSO_PID" ]; then wait "$DSO_PID" || true; fi
    rm -rf -- "$CI_TEMP"
}
trap cleanup EXIT
npm ci --ignore-scripts
npm run test --workspace @smilelab/client
npm run build --workspace backend
python3 tools/generate-openapi.py
git diff --exit-code -- platform/openapi.json platform/DeveloperGuide.md
python3 -m openapi_spec_validator platform/openapi.json
IMAGE="chipmunk-platform:smilelab-${GITHUB_SHA:?Expected CI commit}"
docker build --memory=3g --cpuset-cpus=0,1 \
    --label "org.opencontainers.image.revision=$GITHUB_SHA" \
    --label 'org.opencontainers.image.source=https://git.dcad.ai/dcad/smilelab' \
    -f platform/Dockerfile.full -t "$IMAGE" .
python3 platform/tests/dso_integration.py --image "$IMAGE" --result backend/test-results/platform-summary.json \
    --ui-handoff "$CI_TEMP/ui.json" --hold-seconds 600 > "$CI_TEMP/platform.log" 2>&1 &
DSO_PID=$!
for ((attempt=1; attempt<=150; attempt++)); do
    if [ -f "$CI_TEMP/ui.json" ]; then break; fi
    if ! kill -0 "$DSO_PID" 2>/dev/null; then cat "$CI_TEMP/platform.log"; exit 1; fi
    sleep 2
done
test -f "$CI_TEMP/ui.json"
python3 - "$CI_TEMP/ui.json" <<'PY'
import json, sys
data = json.load(open(sys.argv[1]))
for value in [data['devKey'], data['aliceToken'], *data['staffKeys'].values(), *data['staffTokens'].values()]:
    print('::add-mask::' + value)
PY
export DSO_TEST_CREDENTIALS="$CI_TEMP/ui.json"
SMILELAB_TEST_URL="$(python3 -c 'import json,os; print(json.load(open(os.environ["DSO_TEST_CREDENTIALS"]))["apiBase"])')"
export SMILELAB_TEST_URL
npm run test:e2e --workspace backend
touch "$CI_TEMP/ui.done"
wait "$DSO_PID"
DSO_PID=''
cat backend/test-results/platform-summary.json
python3 platform/tests/odoo_integration.py --result backend/test-results/odoo-summary.json
cat backend/test-results/odoo-summary.json
docker image inspect --format '{{.Id}}' "$IMAGE" > backend/test-results/validated-image.txt
echo 'PASS: complete candidate validated; main may now publish this exact image ID'
