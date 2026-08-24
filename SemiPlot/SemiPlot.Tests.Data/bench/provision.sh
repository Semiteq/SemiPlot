#!/bin/sh
# Executed from /docker-entrypoint-initdb.d while initdb is still in progress: the
# server listens on the unix socket only, superuser auth is local trust, and the
# published port has not opened yet. The entrypoint runs this file as a child rather
# than sourcing it, because the Dockerfile sets its mode bit, so set -e exits the
# script and the entrypoint -- itself under set -e -- aborts with it: a failed
# provisioning exits the container instead of publishing an unprovisioned database.
#
# The database name and the two role passwords come from the container environment,
# which the entrypoint passes through unchanged. SEMIPLOT_PROVISIONED_DATABASE is
# written in C# as SemibaseProvisioner.ProvisionedDatabase; semibase fails on its own
# when either password is missing on a fresh cluster.
#
# No --expected-major: the base image is an argument, and SemiBase enforces its own
# floor anyway.
set -e

: "${SEMIPLOT_PROVISIONED_DATABASE:?the fixture passes the database name into the container}"

/semibase bench --host /var/run/postgresql --database "$SEMIPLOT_PROVISIONED_DATABASE"
