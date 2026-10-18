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
kubectl patch deployment metrics-server -n kube-system --type=json --patch-file k8s/metrics-server-patch.json
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
kubectl run load --rm -it --image=busybox:1.36 --restart=Never -- \
  /bin/sh -c "while true; do wget -q -O- http://flagship-api:8080/work > /dev/null; done"
```

Replicas should climb as CPU usage passes the 50% target. Stop the load generator with `Ctrl+C`, and the deployment scales back down after about a minute of low load.
