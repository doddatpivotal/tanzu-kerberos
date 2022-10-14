# Tanzu Kerberos

The follow repo demonstrates a solution for running .NET Core application on Kubernetes powered by Tanzu Build Service and Tanzu Application Platform.

## Key Solution Characteristics

- All container images are built using Tanzu Build Service
- The TBS base stack was customized using CustomStack to add the kerberos linux module
- .NET Core test application using a solution-project structure
- Kerberos sidecar container is responsible for retrieving and refreshing kerberos token
- Customized TAP Supply Chain to generate a deployment with the kerberos sidecar container

## Guide

The following [guide](guide.md) should be followed for step-by-step instructions to deploy the solution.

## Referneces

This project is heavily inspired through work done by the following...

- [https://github.com/macsux/kerberos-buildpack](https://github.com/macsux/kerberos-buildpack) - Tanzu Application Service solution for the same problem space.
- [Kerberos Sidecar Container](https://cloud.redhat.com/blog/kerberos-sidecar-container) - Blog for implementing kerberos auth on OpenShift.  And associated [github repo](https://github.com/edseymour/kinit-sidecar/blob/master/openshift/example-client-deploy.yaml).
- [Kerberos Test Server](https://github.com/gcavalcante8808/docker-krb5-server) - Referenced in the Kerberos Sidecar Container blog.
