# The bench database is provisioned by the image rather than by the machine running
# the suite: the provisioner rides in as a layer and runs from the entrypoint's own
# init hook, so no semibase binary has to exist on the host.
#
# The base image stays an argument so SEMIPLOT_PG_IMAGE keeps selecting the
# PostgreSQL version, as its consumers already assume.
ARG BASE_IMAGE=postgres:17-alpine

# The fixture fetches the provisioner itself before it builds and passes in the digest it
# resolved, so the image built is the image pulled; `docs/architecture/bench.md` states
# why. The default is the moving tag a hand-run `docker build` resolves for itself.
ARG PROVISIONER_IMAGE=ghcr.io/semiteq/semibase:latest

# FROM scratch, carrying only /semibase, so copying the binary out is the only use.
FROM ${PROVISIONER_IMAGE} AS provisioner

FROM ${BASE_IMAGE}

COPY --from=provisioner /semibase /semibase

# The entrypoint executes a script it finds here, so the mode bit has to be set, and a
# Windows checkout carries none. It is a separate RUN rather than COPY --chmod because the
# fixture builds this image through the Docker Engine API, which is the classic builder:
# --chmod needs BuildKit.
COPY provision.sh /docker-entrypoint-initdb.d/10-semibase.sh

RUN chmod 0755 /docker-entrypoint-initdb.d/10-semibase.sh
