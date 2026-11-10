#!/usr/bin/env bash
# Deploys the hosts to the local kind cluster (ADR 011): the Secret, the three host images, `kind load`, `helm upgrade`.
# Needs the cluster (kind/cluster.yaml) and docker compose (deploy/infra) running.
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

images=(talentsync-ingress-api talentsync-sync-worker talentsync-matching-worker)
projects=(TalentSync.Ingress.Api TalentSync.Sync.Worker TalentSync.Matching.Worker)
for i in "${!images[@]}"; do
  echo "Building ${images[$i]}..."
  docker build --quiet -f deploy/images/host.Dockerfile --build-arg HOST="${projects[$i]}" -t "${images[$i]}:$tag" .
  kind load docker-image --name "$cluster" "${images[$i]}:$tag"
done

# --wait: return only when the new pods are ready, so a host that fails on start fails this script.
helm upgrade --install talentsync deploy/kubernetes/helm/talentsync \
  --kube-context "$context" \
  -f deploy/kubernetes/helm/talentsync/values-local.yaml \
  --set image.tag="$tag" \
  --wait --timeout 3m
