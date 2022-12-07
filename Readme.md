# Tanzu Kerberos

The following repo demonstrates a solution for running .NET Core application on Kubernetes powered by Tanzu Build Service, Tanzu Cloud Native Runtimes and Tanzu Application Platform.

![Desired Result](docs/desired-result.png)

## Context

Why is this novel and warrant a specific solution?  It is quite common for .NET applications leverage kerberos authentication when accessing dependent resources, namely SqlServer.  And this is easly done when the application is running on Windows.  However, to run that application on Kubernetes with linux workers, additional care needs to be taken.  The container base images require [MIT Kerberos](https://web.mit.edu/kerberos/krb5-latest/doc/index.html) modules to be enabled.  And a sidecar needs to be setup to retrieve and refresh Kerberos tickets based upon the desired identity.  Ed Seymour has written a great [blog](https://cloud.redhat.com/blog/kerberos-sidecar-container) describing the problem space and a Sidecar solution to the problem on this topic back in 2018.  A very similar [approach](https://github.com/macsux/kerberos-buildpack) to the same problem was designed by maxsux for Cloud Foundry.

## Key Solution Characteristics

- All container images are built using Tanzu Build Service
- The TBS base stack was customized using CustomStack to add the kerberos linux module and corporate CA
- The .NET Core test application uses a solution-project structure
- A Kerberos sidecar container is responsible for retrieving and refreshing kerberos ticket
- A Custom TAP Convention Server adds the sidecar container and associated volumes/mounts to the PodSpec
- Knative Serving is configured to support EmptyDir volumes and allow downward-api
- TAP Services Toolkit's [Direct Secret Reference](https://docs.vmware.com/en/Services-Toolkit-for-VMware-Tanzu-Application-Platform/0.8/svc-tlk/GUID-usecases-direct_secret_references.html) use case is leveraged to provide SqlServer location information and Active Directory service account credentials to the application
- Convention Server, Sidecar App, and Demo App source code are all maintained in MacSux's [https://github.com/macsux/kerberos-buildpack](https://github.com/macsux/kerberos-buildpack) git repo.  Sidecar and Demo App are the same apps used in the Cloud Foundry solution.

![Solution](docs/solution.png)

## Guide

The following [guide](guide.md) should be followed for step-by-step instructions to deploy the solution.

## Windows Lab Setup

- Active Directory server exists
- Available domain joined windows workstation
- Test Active Directory user account representing application end user
- Application service account in active directory
- Sql Server is deployed on a domain joined server
    - Configured to expose connections using TLS and a cert signed by corporate CA
    - Application service account is granted permissions to access Sql Server database
- Configure SPNs in Active Directory
    - `SetSPN -S http/<YOUR_DEMO_APP_FQDN> <YOUR_AD_NAME>\<YOUR_APPLICATION_SVC_ACCOUNT>`
    - `SetSPN -S MSSQLSvc/<YOUR_AD_DOMAIN> <YOUR_AD_NAME>\<YOUR_APPLICATION_SVC_ACCOUNT>`

## User Persona Workflow

### Platform Operator

- Creates CustomStack and custom ClusterBuilder
- Deploys Kerberos Convention Server Web Workload
- Creates CustomPodConvention definion
- Works with the AD Service Opeartor to 
        - establish a `ad-dun-as` service class (including namespace for services instances and role)
        - Grants AD Service Operator access to create `Secrets` and `ResourceClaimPolicies` in the didicated `service-instances-ad-run-as` namespace
- Works with the Sql Server Service Operator to establish a `mssql` service class (including namespace for services instances and role)
        - establish a `mssql` service class (including namespace for services instances and role)
        - Grants AD Service Operator access to create `Secrets` and `ResourceClaimPolicies` in the didicated `service-instances-mssql` namespace

### App Operator

- Makes request for Platform Owner for a app namespace
- Makes request to App Directory Service Operator to create application service account with desire for app to run as and sql server access
- Makes request to Sql Server Operator to create database and provide permisisons for app service account

### Platform Operator

- Creates app namespace

## Active Directory Service Operator

- Creates application service account
- Creates http SPN for web application to run as service account
- Creates MSSQLSvc SPN for service account to access to be available to access Sql Server

- Creates `ad-run-as` service instance containing service account UN/PW and AD host and makes available to be claimed by app's desired namespace
        - Secret containing values and a label indicating username `user=<SVC_ACCOUNT_NAME>`
        - Resource claim policy allowing desired namespace to claim secret with label `user=<SVC_ACCOUNT_NAME>`

### Sql Server Service Operator

- Creates Database and grants applicaiton service account desired permissions

- Creates `mssql` service instance containing `Trusted_Server` connection string and makes available to be claimed by app's desired namespace
        - Secret containing values and a label indicating username `user_db_identifier=<SVC_ACCOUNT_NAME><DB_NAME>`
        - Resource claim policy allowing desired namespace to claim secret with label `user_db_identifier=<SVC_ACCOUNT_NAME><DB_NAME>`

### App Operator

- Claims the avialable `Ad-Run-As` service.
        - `tanzu service claimable list --class ad-run-as --namespace $APPS_NAMESPACE`
        - `tanzu service claim create $AD_RUN_AS_CLAIM_NAME --resource-name $FROM_ABOVE --resource-kind Secret --resource-api-version v1 --resource-namespace service-isntances-ad-run-as --namespace $APPS_NAMESPACE`
- Claims the available `mssql` service
        - `tanzu service claimable list --class mssql --namespace $APPS_NAMESPACE`
        - `tanzu service claim create $MSSQL_CLAIM_NAME --resource-name $FROM_ABOVE --resource-kind Secret --resource-api-version v1 --resource-namespace service-isntances-mssql --namespace $APPS_NAMESPACE`
- Creates workload
        - with label `kerberos=true` so that the kerberos convention server take action
        - param `clusterBuilder` set to the name of the custom ClusterBuilder
        - serviceClaim referencing the $AD_RUN_AS_CLAIM_NAME ResourceClaim
        - serviceClaim referencing the $MSSQL_CLAIM_NAME ResourceClaim

## References

This project is heavily inspired through work done by the following...

- [https://github.com/macsux/kerberos-buildpack](https://github.com/macsux/kerberos-buildpack) - Tanzu Application Service solution for the same problem space.
- [Kerberos Sidecar Container](https://cloud.redhat.com/blog/kerberos-sidecar-container) - Blog for implementing kerberos auth on OpenShift.  And associated [github repo](https://github.com/edseymour/kinit-sidecar/blob/master/openshift/example-client-deploy.yaml).
- [Kerberos Test Server](https://github.com/gcavalcante8808/docker-krb5-server) - Referenced in the Kerberos Sidecar Container blog.
