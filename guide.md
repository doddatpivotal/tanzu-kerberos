# Execution Guide

## Prerequisites

You have the following clis on your workstation:

- `kp` for iteraction with Tanzu Build Service - [https://network.tanzu.vmware.com/products/build-service/](https://network.tanzu.vmware.com/products/build-service/)
- `yq` for yaml processing - [https://mikefarah.gitbook.io/yq/](https://mikefarah.gitbook.io/yq/)
- `ytt` for yaml template processing - [https://carvel.dev/ytt/](https://carvel.dev/ytt/)
- `tanzu` for enhanced methods of inspecting workloads on TAP.  Only required if you are doing the TAP option - [https://network.tanzu.vmware.com/products/tanzu-application-platform/](https://network.tanzu.vmware.com/products/tanzu-application-platform/)

The guide demonstrates an approach with and without Tanzu Application Platform.

- `Tanzu Build Service` and `Cloud Native Runtimes` (w/o TAP) - For simplicity, you will be working within a single namespace that has the ability to create images (permissions to your git repo and registry setup) and run your application workload as a Knative Service.
- `Tanzu Application Platform` - Again, for simplicity, assumes a `full` profile.  You will be working within a developer namespace, and also require cluster admin access to updates supply chains.

You have an accessible Active Directory server.

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

## Tanzu Advanced Method (TBS without TAP)

### 1. Create sample app image using TBS

This image uses the `base-kerberos` created in the previous section

```bash
# Apply image resources for TBS to create the image
ytt -f kerberos-demo-app/kerberos-demo-tbs-image.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Check build status and grab the image name for the successful build. 
kp build list kerberos-demo-tbs -n $DEV_NAMESPACE
# Update $PARAMS_YAML kerberos_demo_tbs_app.image field with the built image value.

# Check build logs, if you need to troubleshoot
kp build logs kerberos-demo-tbs -n $DEV_NAMESPACE
```

### 2. Deploy and Test Your .NET Core App

Create a we create a Knative Service for our .NET Core test app.  The Knative Service uses the kerberos sidecar and demo app.  This .NET Core test app is KerberosDemo is from the great work macsux did at [https://github.com/macsux/kerberos-buildpack](https://github.com/macsux/kerberos-buildpack).

```bash
# Knative Service in TAP 1.3 does not allow for EmptyDir volumes by default. This must be enabed via feature flag.
# Double-check that the feature flag is enabled.  Result of the following command should be "enabled".
# If blank or "disabled" then you must configure CNRS appropriately.
kubectl get cm config-features -n knative-serving -ojsonpath="{.data.kubernetes\.podspec-volumes-emptydir}"

# Apply the kservice resources for the test app
ytt -f kerberos-demo-app/kerberos-demo-tbs-kservice.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate the kservice is up and READY
kubectl get kservice kerberos-demo-tbs -n $DEV_NAMESPACE

# Check the app and you should see valid ticket diagnostic information
open $(kubectl get kservice kerberos-demo-tbs -n $DEV_NAMESPACE -ojsonpath="{.status.url}")/diag
```

# Tanzu Application Platform Method

Amoung many benefits, Tanzu Application Platform provides a secure supply chain for your application.  Relevent to this guide, the OOTB source-to-url supply chain allows developers to submit a Workload resource with reference to their application source code, and then TAP's Supply Chain Choreographer automates the steps to retrieve your source code, build the application, create a secure container, generage kubernetes manifests to run your application, and then deploy the app.

The default OOTB basic supply chain is almost perfect, however it not aware of our kerberos sidecar requirement.  However, as TAP is a programable platform, allowing platform engineers the ability to configure TAP for their unique reqirements.  We need the PodSpec to include the sidecar container and the requisite volume definions.  Here is the approach given.

- Create a custom ClusterConfigTemplate for the unique requirements of our PodSpec
- Create a custom Supply chain. Clone the source-to-image supply chain.  Update the workload type.  Then swap out references for our replacement ClusterConfigTemplates.

### 1. Create Custom Convention Template, Cluster Template, and Supply Chain

Create the new template and supply chain.  These need cusotmizations based upon our environment information in params.yaml file.  When executing these steps, you are logically assuming the role of a platform engineer.

```bash
ytt -f supply-chain/kerberos-convention-template.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -
kubectl apply -f supply-chain/kerberos-web-supply-chain.yaml
```

### 2. Submit Workload

Now it is time to put on your application developer hat.  Submit your worklaod.

```bash
# Create configmaps and secrets necessary for kerberos clients
ytt -f kerberos-demo-app/kerberos-demo-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Submit your workload
kubectl apply -f kerberos-demo-app/kerberos-demo-workload.yaml -n $DEV_NAMESPACE

# Check on status of your workload
tanzu apps workload get kerberos-demo -n $DEV_NAMESPACE
tanzu apps workload tail kerberos-demo -n $DEV_NAMESPACE

# Check the app and you should see valid ticket diagnostic information
open $(kubectl get kservice dotnet-sample-app -n $DEV_NAMESPACE -ojsonpath="{.status.url}")/diag
```
