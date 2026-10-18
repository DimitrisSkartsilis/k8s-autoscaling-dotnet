# Flagship

A small ASP.NET Core (.NET 10) minimal API, packaged as a container and deployed to Kubernetes with CPU-based horizontal autoscaling.

## Repository layout

```
src/Api/                 Minimal API (Program.cs)
tests/Api.Tests/         xUnit integration tests using WebApplicationFactory
k8s/                     Kubernetes manifests (Deployment, Service, HPA) and a metrics-server patch
Dockerfile               Multi-stage build: SDK image to publish, ASP.NET runtime image to run
Flagship.slnx            Solution file
```

## API

| Method | Route              | Description |
|--------|--------------------|-------------|
| `POST` | `/documents`       | Create a document. Body: `{ "title": "...", "content": "..." }`. Returns `201 Created`, or `400` if either field is empty. |
| `GET`  | `/documents/{id}`  | Fetch a document by its GUID. Returns `404` if it doesn't exist. |
| `GET`  | `/work?iterations=N` | CPU-heavy endpoint that repeatedly SHA-256 hashes a value. `N` defaults to 100,000 and is clamped to 1–1,000,000. Used to generate load for the autoscaling demo. |

Documents are kept in an in-memory store, so they are lost on restart and are not shared between pods.

In the Development environment, the OpenAPI document is served at `/openapi/v1.json`.

## Running locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project src/Api
```

The API listens on `http://localhost:5282`.

```sh
curl -X POST http://localhost:5282/documents \
  -H "Content-Type: application/json" \
  -d '{"title":"First","content":"Hello"}'
```

## Tests

```sh
dotnet test
```

## Container

```sh
docker build -t flagship-api:0.2 .
docker run --rm -p 8080:8080 flagship-api:0.2
```

The image runs as a non-root user and listens on port 8080.

## Kubernetes

The manifests target a local cluster (for example Docker Desktop's Kubernetes). The Deployment uses the locally built `flagship-api:0.2` image with `imagePullPolicy: IfNotPresent`, so build the image first (see above).

### 1. Install metrics-server

The HorizontalPodAutoscaler needs CPU metrics. Install metrics-server, then apply the patch that lets it talk to kubelets using self-signed certificates on a local cluster:

```sh
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml
kubectl patch deployment metrics-server -n kube-system --type=json --patch-file k8s/local/metrics-server-patch.json
```

Check that it works with `kubectl top pods` (it can take a minute to start reporting).

### 2. Deploy

```sh
kubectl apply -f k8s/
```

This creates:

- **Deployment** `flagship-api`: requests 100m CPU / 128Mi memory, limited to 500m CPU / 256Mi.
- **Service** `flagship-api`: a `LoadBalancer` on port 8080.
- **HorizontalPodAutoscaler** `flagship-api`: scales between 1 and 6 replicas, targeting 50% average CPU utilization (of the request), with a 60-second scale-down stabilization window.

### 3. Watch it autoscale

In one terminal, watch the autoscaler:

```sh
kubectl get hpa flagship-api --watch
```

In another, generate load against the `/work` endpoint from inside the cluster:

```sh
kubectl run load-generator --image=busybox:1.36 --restart=Never -- /bin/sh -c "for i in 1 2 3 4; do (while true; do wget -q -O /dev/null http://flagship-api:8080/work; done) & done; wait"
```

This starts four concurrent request loops in one pod. Replicas should climb as CPU usage passes the 50% target. Stop the load generator with `kubectl delete pod load-generator`, and the deployment scales back down after about a minute of low load.

## Results

Condensed from one run of `kubectl get hpa -w` (raw output: [docs/hpa-run.txt](docs/hpa-run.txt); pod events: [docs/pods-run.txt](docs/pods-run.txt)):

| TARGETS | REPLICAS | What happened |
|---------|----------|---------------|
| `1%–3%/50%` | 1 | Idle baseline before load. |
| `501%/50%` | 1 | Load generator started; the single pod is pinned at its 500m CPU limit. |
| `306%/50%` | 5 | First scale-up, 1→5 (capped at +4 pods per step); new pods are still warming up. |
| `232%/50%` | 6 | Reached `maxReplicas`; still well above target, but can't scale further. |
| `1%/50%` | 6 | Load generator deleted; CPU drops, but replicas are held during the scale-down stabilization window. |
| `1%/50%` | 1 | Scaled back down to `minReplicas`. |

## Observations

- **Utilization is relative to the CPU request, not the limit.** The request is 100m, so the 50% target means 50m per pod. Because the limit is 500m, a pod can report up to ~500% utilization.
- **The single pod was throttled at its limit.** 501% is 501m, i.e. the 500m limit. The pod couldn't use more CPU, so the reading shows the cap, not the real demand.
- **The first scale-up went 1→5, not straight to 6.** By default the HPA adds at most 4 pods (or doubles the count, whichever is larger) per 15-second step. The pod events show four new pods created together, then one more.
- **`maxReplicas` acts as a cost ceiling.** At 6 replicas utilization was still 232%, well above target, so the HPA wanted more pods but stopped at the maximum.
- **Total CPU in use grew from ~500m to ~1.4 cores.** One throttled pod at 501% is ~0.5 cores; six pods averaging 232% of 100m is ~1.4 cores.
- **Scale-down waited for the stabilization window.** After the load stopped, utilization dropped to 1% while the deployment stayed at 6 replicas, then it went to 1 once the 60-second window had passed.
- **The document store isn't safe to scale; `/work` is.** Documents live in memory in each pod, so a document created through one pod returns `404` when a later request is routed to another pod (seen in a found/404 check). `/work` keeps no state, so any pod can serve any request.

## Cleanup

```sh
kubectl delete -f k8s/
kubectl delete pod load-generator --ignore-not-found
```
