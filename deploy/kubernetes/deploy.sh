#!/usr/bin/env bash
# Deploys the hosts to the local kind cluster (ADR 011): the Secret, the migrations and host images, `kind load`,
# `helm upgrade`.
# Needs the cluster (kind/cluster.yaml) and docker compose (deploy/infra) running.
# Arguments go to `helm upgrade`, e.g. `--set llm.provider=Anthropic` (needs ANTHROPIC_API_KEY in deploy/infra/.env).
# Fake stays the default, so a plain deploy never calls the billed model.
# Each run gets a new image tag, so `helm upgrade` sees a changed manifest and rolls the pods; under one fixed tag the
# pods would keep running the old image.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
cluster=talentsync
context="kind-$cluster"

# The commit, plus a timestamp when the working tree has uncommitted changes: two runs on the same dirty tree must
# still differ.
tag="$(git rev-parse --short HEAD)"
if [ -n "$(git status --porcelain)" ]; then
  tag="$tag-dirty-$(date +%Y%m%d%H%M%S)"
fi
echo "Image tag: $tag"

"$root/deploy/kubernetes/create-secret.sh"

build_and_load() {
  local image=$1
  shift
  echo "Building $image..."
  docker build --quiet "$@" -t "$image:$tag" .
  kind load docker-image --name "$cluster" "$image:$tag"
}

build_and_load talentsync-migrations -f deploy/images/migrations.Dockerfile
images=(talentsync-ingress-api talentsync-sync-worker talentsync-matching-worker)
projects=(TalentSync.Ingress.Api TalentSync.Sync.Worker TalentSync.Matching.Worker)
for i in "${!images[@]}"; do
  build_and_load "${images[$i]}" -f deploy/images/host.Dockerfile --build-arg HOST="${projects[$i]}"
done

# The migrations Job runs as a pre-install / pre-upgrade hook and must succeed before any host rolls out.
# --wait: return only when the new pods are ready, so a host that fails on start fails this script.
# The timeout covers the hook as well.
helm upgrade --install talentsync deploy/kubernetes/helm/talentsync \
  --kube-context "$context" \
  -f deploy/kubernetes/helm/talentsync/values-local.yaml \
  --set image.tag="$tag" \
  --wait --timeout 3m \
  "$@"
