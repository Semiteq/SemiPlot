ARG BASE_IMAGE=postgres:17-alpine

ARG PROVISIONER_IMAGE=ghcr.io/semiteq/semibase:latest

FROM ${PROVISIONER_IMAGE} AS provisioner

FROM ${BASE_IMAGE}

COPY --from=provisioner /semibase /semibase

COPY provision.sh /docker-entrypoint-initdb.d/10-semibase.sh

RUN chmod 0755 /docker-entrypoint-initdb.d/10-semibase.sh
