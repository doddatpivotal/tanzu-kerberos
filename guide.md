# Tanzu Application Platform Execution Guide

The guide demonstrates an approach with the Tanzu Application Platform.  For simplicity, assumes a `full` profile.  You will be working within a developer namespace, and also require cluster admin access to updates supply chains.

>Note: An alternative verision of this guide for use with Tanzu Advanced (`Tanzu Build Service` and `Cloud Native Runtimes`) can be found at [Tanzu Advanced Guide](./ta-guide.md).

## Prerequisites

You have the following clis on your workstation:

- `kp` for iteraction with Tanzu Build Service - [https://network.tanzu.vmware.com/products/build-service/](https://network.tanzu.vmware.com/products/build-service/)
- `yq` for yaml processing - [https://mikefarah.gitbook.io/yq/](https://mikefarah.gitbook.io/yq/)
- `ytt` for yaml template processing - [https://carvel.dev/ytt/](https://carvel.dev/ytt/)
- `tanzu` for enhanced methods of inspecting workloads on TAP.  Only required if you are doing the TAP option - [https://network.tanzu.vmware.com/products/tanzu-application-platform/](https://network.tanzu.vmware.com/products/tanzu-application-platform/)

You have an accessible Active Directory server.

- SPN has been setup for ????
- Service Account has been created and you have username and password aviable

You have an accessible Sql Server server.

- SPN has been setup for ????
- Service Account has been created and you have username and password aviable

The lab environment used for testing is accessible over the internet, but a firewall restricts by IP.  Your environment may vary.  The following quick process is used to retrieve retrieve the public NAT IP address for the test cluster.

```bash
kubectl run busybox -it --rm --image busybox -n $(yq e .dev_namespace $PARAMS_YAML) -- /bin/sh
# may take a moment for the pod to become ready
wget -qO- https://httpbin.org/get
# retrieve the IP address
exit
```

## Setup environment

Update the params.yaml file with your environment specific values and then setup shell variables.

```bash
cp local-config/params-REDACTED.yaml local-config/params.yaml
# Update params.yaml based upon your environment
PARAMS_YAML=local-config/params.yaml
DEV_NAMESPACE=$(yq e .dev_namespace $PARAMS_YAML)
```

## Ensure Knative Serving is Configured for EmptyDir Volumes

```bash
# Knative Service in TAP 1.3 does not allow for EmptyDir volumes by default. This must be enabed via feature flag.
# Double-check that the feature flag is enabled.  Result of the following command should be "enabled".
# If blank or "disabled" then you must configure CNRS appropriately.
kubectl get cm config-features -n knative-serving -ojsonpath="{.data.kubernetes\.podspec-volumes-emptydir}"
```

## Configure TBS and Create Sidecar Image

### 1. Create TBS Custom Stack and Custom Builder for Kerberos

The default base images shipped with TBS do not include kerberos modules.  However TBS provides a mechanism for users to customize default images through a resource called [CustomStack](https://docs.vmware.com/en/Tanzu-Build-Service/1.6/vmware-tanzu-build-service/GUID-managing-custom-stacks.html).  A TBS controller processes this resource and adds to an existing stack.  Using this method, each time TBS base image is updated, the CustomStack will be rebuilt and the additions will be be applied to the updated stack.

```bash
# Create the custom stack
ytt -f build-service/base-kerberos-custom-stack.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate that base-kerberos clusterstack is READY.  It may take a minute or so for the operator to create the clusterstack 
# from the customstack.
kp clusterstack list

# Create the custom builder.  The new cluster stack must be READY for the builder to be successful
ytt -f build-service/base-kerberos-cluster-builder.yaml --data-values-file $PARAMS_YAML | kubectl apply  -f -

# Validate that base-kerberos clusterbuilder is READY
kp clusterbuilder list
```

### 2. Create sidecar image using TBS

This image uses the `default` ClusterBuilder.  

```bash
ytt -f kerberos-sidecar/kerberos-sidecar-image.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Check build status and grab the image name for the successful build. 
kp build list kerberos-sidecar -n $DEV_NAMESPACE
# Update $PARAMS_YAML sidecar.image field with the built image value.

# Check build logs, if you need to troubleshoot
kp build logs kerberos-sidecar -n $DEV_NAMESPACE
```

# Tanzu Application Platform Method

Amoung many benefits, Tanzu Application Platform provides a secure supply chain for your application.  Relevent to this guide, the OOTB source-to-url supply chain allows developers to submit a Workload resource with reference to their application source code, and then TAP's Supply Chain Choreographer automates the steps to retrieve your source code, build the application, create a secure container, generage kubernetes manifests to run your application, and then deploy the app.

The default OOTB basic supply chain is almost perfect, however it not aware of our kerberos sidecar requirement.  However, as TAP is a programable platform, allowing platform engineers the ability to configure TAP for their unique reqirements.  

UPDATE THIS TEXT

### Create Convention Server

```bash
# Deploy the convention service using the OOTB supply chain
ytt -f convention-service/convention-service-workload.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate success. It may take a few minutes to be healty
tanzu apps workload get kerberos-convention-server -n $DEV_NAMESPACE
```

### Configure Convention

```bash
# Deploy the ClusterPodConvention
ytt -f convention-service/kerberos-convention-server-convention.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate ClsuterPodConvention is Ready
kubectl get clusterpodconventions.conventions.carto.run -A
```

### Setup Sql Server and AD Run As Services within Services Toolkit

```bash
# Create services and namsepace for each
kubectl apply -f services/

# Validate
tanzu services classes list
```

### Create individual SqlServer and AD Run As services instances for Kerberos Demo app

```bash
# Create service instances and set claim policy
ytt -f service-instances --data-values-file $PARAMS_YAML | kubectl apply -f -

# Validate AD Run As instance is available
tanzu services claimable list --class ad-run-as -n $DEV_NAMESPACE

# Validate Sql Server instance is available
tanzu services claimable list --class mssql -n $DEV_NAMESPACE
```

### Submit Workload

Now it is time to put on your application developer hat.  Submit your worklaod.

```bash
# Create resource claims for the Sql Server and AD Run As Services instances
kubectl apply -f kerberos-demo-app/kerberos-demo-resource-claims.yaml -n $DEV_NAMESPACE

# Validate both claims are ready
kubectl get resourceclaims -n $DEV_NAMESPACE

# Submit your workload
ytt -f kerberos-demo-app/kerberos-demo-workload.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Check on status of your workload
tanzu apps workload get kerberos-demo -n $DEV_NAMESPACE
tanzu apps workload tail kerberos-demo -n $DEV_NAMESPACE

# Check the app and you should see valid ticket diagnostic information
open $(kubectl get kservice kerberos-demo -n $DEV_NAMESPACE -ojsonpath="{.status.url}")/diag

# Check the app and you should see valid Sql Server connection information
open $(kubectl get kservice kerberos-demo -n $DEV_NAMESPACE -ojsonpath="{.status.url}")/sql

```
