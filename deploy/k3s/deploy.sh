#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
image=${1:?Usage: ./deploy.sh registry.example.com/webcrawler:version}
namespace=${NAMESPACE:-webcrawler}
# Restrict substitution to valid image-reference characters.
[[ "$image" =~ ^[a-zA-Z0-9][a-zA-Z0-9./:@_-]*$ ]] || { echo 'Invalid image reference' >&2; exit 1; }
[[ ${#namespace} -le 63 && "$namespace" =~ ^[a-z0-9]([-a-z0-9]*[a-z0-9])?$ ]] || { echo 'Invalid namespace' >&2; exit 1; }
sed "s/name: webcrawler/name: $namespace/" namespace.yaml | kubectl apply -f -
k() { kubectl -n "$namespace" "$@"; }
# Reuse credentials on every deployment, including partial first deployments.
ensure_secret() {
  local name=$1
  shift
  if ! k get secret "$name" >/dev/null 2>&1; then
    k create secret generic "$name" "$@"
  fi
}
ensure_secret postgres-auth --from-literal=POSTGRES_USER=postgres --from-literal=POSTGRES_DB=crawler --from-literal=POSTGRES_PASSWORD="$(openssl rand -hex 24)"
ensure_secret mongodb-auth --from-literal=MONGO_INITDB_ROOT_USERNAME=crawler --from-literal=MONGO_INITDB_ROOT_PASSWORD="$(openssl rand -hex 24)"
ensure_secret mongodb-keyfile --from-literal=keyfile="$(openssl rand -base64 512 | tr -d '\n')"
pg_password=$(k get secret postgres-auth -o jsonpath='{.data.POSTGRES_PASSWORD}' | base64 -d)
mongo_password=$(k get secret mongodb-auth -o jsonpath='{.data.MONGO_INITDB_ROOT_PASSWORD}' | base64 -d)
mongo_url="mongodb://crawler:${mongo_password}@mongodb-0.mongodb:27017/crawler?authSource=admin&replicaSet=rs0"
ensure_secret crawler-connections \
  --from-literal=ConnectionStrings__postgres="Host=postgres;Port=5432;Database=crawler;Username=postgres;Password=${pg_password}" \
  --from-literal=ConnectionStrings__mongo="$mongo_url"
ensure_secret mongo-express-auth --from-literal=ME_CONFIG_MONGODB_URL="$mongo_url" \
  --from-literal=ME_CONFIG_BASICAUTH_USERNAME=admin \
  --from-literal=ME_CONFIG_BASICAUTH_PASSWORD="$(openssl rand -hex 24)"
k apply -f infrastructure.yaml
k rollout status statefulset/postgres --timeout=300s
k wait --for=condition=complete job/mongodb-init --timeout=600s
k rollout status statefulset/mongodb --timeout=300s
k delete job crawler-migrate --ignore-not-found --wait=true
sed "s|CRAWLER_IMAGE|$image|g" migrate.yaml | k apply -f -
k wait --for=condition=complete job/crawler-migrate --timeout=600s
# Remove previous application Deployments before starting one pod per node.
k delete deployment crawler crawler-producer crawler-consumer --ignore-not-found --wait=true
sed "s|CRAWLER_IMAGE|$image|g" workers.yaml | k apply -f -
k rollout status daemonset/crawler --timeout=300s
k rollout status deployment/mongo-express --timeout=300s
