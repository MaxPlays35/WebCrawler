# k3s deployment

This deploys a DaemonSet with one application pod per eligible Linux node,
including control-plane nodes. Each pod runs both sitemap crawlers and the queue
consumer, with one concurrent message per pod. It uses the existing MassTransit
PostgreSQL `tasks` queue; no additional message broker is needed.
Environment variables override appsettings.json. The migration Job uses the
`--migrate` command-line option and exits before starting the application.

Included infrastructure:

- PostgreSQL 18: application tables and durable queue, 10 GiB PVC.
- MongoDB 8.0: authenticated single-member `rs0` replica set, 20 GiB PVC.
  A replica set is required by the consumer's MongoDB transactions.
- mongo-express: MongoDB viewer with generated HTTP basic-auth credentials.
- EF migration Job, run before workers on every deployment.

## GitHub Actions image build

`.github/workflows/docker.yml` builds `linux/amd64` images:

- Pull requests build the image without publishing or registry login.
- Pushes to `master`/`main`, `v*` tags, and manual workflow runs publish to GHCR.
- Images receive `sha-<full-commit-sha>` and branch/tag tags. `latest` is updated
  only from the default branch.

For this repository, the image is `ghcr.io/maxplays35/webcrawler`. Publishing
uses the built-in `GITHUB_TOKEN` with `packages: write`; no extra registry secret
is required. Repository/organization settings must permit Actions package writes.
If an existing package is not linked to this repository, grant the repository
Actions access in the package settings.

After a successful workflow run, deploy its commit-specific tag:

```sh
./deploy/k3s/deploy.sh ghcr.io/maxplays35/webcrawler:sha-<full-commit-sha>
```

Replace the placeholder with the commit SHA shown in the successful run. Make
the GHCR package public for anonymous pulls, or configure cluster pull credentials
for a private package as described below. The workflow builds and publishes only;
it does not deploy to the cluster.

## Build and deploy

Requires Docker, kubectl targeting your k3s cluster, OpenSSL, and the k3s
`local-path` StorageClass. Build an image for your cluster's CPU architecture
(use `linux/arm64` instead if appropriate) and push to a registry accessible by
all cluster nodes:

```sh
docker buildx build --platform linux/amd64 -t YOUR_REGISTRY/webcrawler:v1 --push .
./deploy/k3s/deploy.sh YOUR_REGISTRY/webcrawler:v1
```

Use a new image tag for each release. For private registries, configure k3s
registry credentials or the namespace's default ServiceAccount imagePullSecrets
before running the deployment. Set `NAMESPACE=my-namespace` to override the
dedicated `webcrawler` namespace created from namespace.yaml. All application,
database, viewer, Job, Secret, and PVC resources are scoped to that namespace. Run only one deployment script at a time.

The script creates and reuses Kubernetes Secrets, starts the databases,
initializes the replica set, waits for the migration Job, then starts the
application. Do not apply workers.yaml/migrate.yaml directly: the script replaces
`CRAWLER_IMAGE`. No live cluster changes occur until you run the script.

## Supply your own secrets

`secrets.example.yaml` contains all five Secrets using Kubernetes `stringData`
(plain-text values). For a new deployment, copy it to the ignored local file:

```sh
cp deploy/k3s/secrets.example.yaml deploy/k3s/secrets.local.yaml
# Edit secrets.local.yaml and replace the example passwords and replica-set key.
kubectl apply -f deploy/k3s/namespace.yaml
kubectl -n webcrawler apply -f deploy/k3s/secrets.local.yaml
./deploy/k3s/deploy.sh ghcr.io/maxplays35/webcrawler:sha-<full-commit-sha>
```

The Secrets omit a namespace so `kubectl -n` selects it. For a custom namespace,
create that namespace first, use it in `kubectl -n`, and set `NAMESPACE` when
running deploy.sh. The script reuses all pre-created Secrets.

Keep database passwords consistent with the application and viewer connection
strings. URL-encode special characters in MongoDB URI credentials. Do not apply
example credentials over an existing installation: updating Secrets does not
change database users stored on persistent volumes.

## Access

Services are cluster-internal. Open two terminals as needed:

```sh
kubectl -n webcrawler port-forward svc/mongo-express 8081:8081
kubectl -n webcrawler port-forward svc/crawler 8080:8080
```

Viewer: http://localhost:8081 (username `admin`). Retrieve its password:

```sh
kubectl -n webcrawler get secret mongo-express-auth -o jsonpath='{.data.ME_CONFIG_BASICAUTH_PASSWORD}' | base64 -d
```

API reference: http://localhost:8080/scalar/v1.

## Operations

```sh
kubectl -n webcrawler get pods,pvc,jobs
kubectl -n webcrawler get daemonset crawler
kubectl -n webcrawler get pods -l app=crawler -o wide
kubectl -n webcrawler logs -l app=crawler --tail=100
kubectl -n webcrawler logs job/crawler-migrate
kubectl -n webcrawler logs job/mongodb-init
```

The DaemonSet automatically adds a pod when a Linux node joins the cluster.
Control-plane/master NoSchedule taints are tolerated; custom node taints may
require additional tolerations in workers.yaml. Nodes need sufficient resources
and access to the image registry. CI publishes AMD64-only images; these require
AMD64 nodes.

Every pod discovers URLs and consumes from the shared queue. URL discovery is
not coordinated across nodes, so concurrent existence checks can result in
duplicate page records and queued tasks. Increase `WorkerCount` in workers.yaml for more
consumer concurrency per pod. The script removes previous application Deployments
when upgrading. Databases and the viewer remain shared single instances.

Each crawler pod requests 0.5 CPU core and 1 GiB RAM, with limits of 2 CPU cores
and 4 GiB RAM. These are starting allocations for a heavier workload, not measured
requirements. Because the app is a DaemonSet, budget these resources on every
eligible node, in addition to system services and any databases hosted there.
Check actual usage with `kubectl -n webcrawler top pods --containers` and adjust
the values in workers.yaml based on sustained usage and memory peaks.

PostgreSQL requests 1 GiB RAM with a 4 GiB memory limit; MongoDB requests 2 GiB
with an 8 GiB memory limit. Both request 0.5 CPU core; PostgreSQL has a 2-core CPU
limit and MongoDB has a 2-core CPU limit. These allocations also need validation against workload metrics and node
capacity, especially when both databases share a node with a crawler pod.

The database volumes use node-local storage and single replicas: this setup
has persistence across pod restarts, but is not highly available and does not
protect against node/disk loss. Back up both databases. Deleting the namespace
also deletes its PVCs and may delete the underlying data. Do not delete/recreate
authentication Secrets independently of their databases; changing a Secret does
not rotate the users stored on existing volumes. Connection and viewer Secrets
must also be updated explicitly when rotating credentials.

The default setup uses database administrator accounts for bootstrap and the
application (MassTransit creates its own transport schema). Use separate,
restricted runtime accounts and a dedicated migration account when hardening
this deployment. The API has no authentication; keep access private. Database
traffic is internal and is not configured for TLS.

Failed Mongo initialization can be retried after fixing its cause by deleting
`job/mongodb-init` and rerunning the script. Application migrations run before
new workers; on later deployments, old workers may still be running, so schema
changes must be backward compatible or workers must be stopped first.
