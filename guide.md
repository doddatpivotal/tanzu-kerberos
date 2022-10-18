# Execution Guide

## Prerequisites

You have the following clis on your workstation:

- `kp` for iteraction with Tanzu Build Service - https://network.tanzu.vmware.com/products/build-service/
- `yq` for yaml processing - https://mikefarah.gitbook.io/yq/
- `ytt` for yaml template processing - https://carvel.dev/ytt/
- `tanzu` for enhanced methods of inspecting workloads on TAP.  Only required if you are doing the TAP option. - https://network.tanzu.vmware.com/products/tanzu-application-platform/

The guide demonstrates an approach with and without Tanzu Application Platform.

- `Tanzu Build Service` and `Cloud Native Runtimes` (w/o TAP) - For simplicity, you will be working within a single namespace that has the ability to create images (permissions to your git repo and registry setup) and run your application workload as a Knative Service.
- `Tanzu Application Platform` - Again, for simplicity, assumes a `full` profile.  You will be working within a developer namespace, and also require cluster admin access to updates supply chains.

### Setup environment

Update the params.yaml file with your environment specific values and then setup shell variables.

```bash
cp local-config/params-REDACTED.yaml local-config/params.yaml
# Update params.yaml based upon your environment
PARAMS_YAML=local-config/params.yaml
DEV_NAMESPACE=$(yq e .dev_namespace $PARAMS_YAML)
```

## KDC Server Setup

### 1. Deploy KDC Server

Here we deploy the KDC server as a deployment and expose it as a service.  It will go in its own namespace.  This is the only container image that is not built by TBS as it is for test only.  In a real implementation you would likely target your Active Directory Domain Controller.  We are adapting the work by `gcavalcante8808` at [Kerberos Test Server](https://github.com/gcavalcante8808/docker-krb5-server).

```bash
ytt -f kdc/kdc.yaml --data-values-file $PARAMS_YAML| kubectl apply -f -

# Validate deployment is up and running
kubectl get deploy -n kdc
```

### 2. Generate Client Creds

Now we access the KDC in order to create a service principle that we want our test application to assume.  We also generate a keytab file.  This file is similar to a private key and is used to assert identiy.  The base64 encoded keytab file will be coped to your params.yaml file.  This approach is adapted from Ed Seymour's [Kerberos Sidecar Container](https://cloud.redhat.com/blog/kerberos-sidecar-container) blog.

```bash
# Retrieve kdc pod name
KDC_SERVER_POD_NAME=$(kubectl get po -n kdc -l app=kdc -ojsonpath='{.items[0].metadata.name}')

# Start interactive session with kdc container
kubectl exec -it $KDC_SERVER_POD_NAME -n kdc -- sh

# Edit the following line with your desired username and password
USER_PASSWORD=SuperSecret!
USER_NAME=client1

# Create the user principal
echo $KRB5_PASS | kadmin -r $KRB5_REALM -p admin/admin@$KRB5_REALM -q "addprinc -pw $USER_PASSWORD -requires_preauth $USER_NAME@$KRB5_REALM"

# Generate a keytab file
echo $KRB5_PASS | kadmin -r $KRB5_REALM -p admin/admin@$KRB5_REALM -q "ktadd $USER_NAME@$KRB5_REALM"

# Output the base64 output of the keytab file so that you can copy and paste into your $PARAMS_FILE.  This data should be protected.
base64 /etc/krb5.keytab

# All done, exit the iteractive session
exit
```

## Configure TBS and Validate Kerberos Iteractions

### 3. Create Custom Stack and Custom Builder

The default base images shipped with TBS do not include kerberos modules.  However TBS provides a mechanism for users to customize default images through a resource called [CustomStack](https://docs.vmware.com/en/Tanzu-Build-Service/1.6/vmware-tanzu-build-service/GUID-managing-custom-stacks.html).  A TBS controller processes this resource and adds to an existing stack.  Using this method, each time TBS base image is updated, the CustomStack will be rebuilt and the additions will be be applied to the updated stack.

```bash
# Create the custom stack
ytt -f build-service/base-kerberos-custom-stack.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate that base-kerberos clusterstack is READY.  It may take a minute or so for the operator to create the clustestack 
# from the customstack.
kp clusterstack list

# Create the custom builder.  The new cluster stack must be READY for the builder to be successful
ytt -f build-service/base-kerberos-cluster-builder.yaml --data-values-file $PARAMS_YAML | kubectl apply  -f -

# Validate that base-kerberos clusterbuilder is READY
kp clusterbuilder list
```

### 4. Test out the sidecar using simple client

In order to validate our sidecar approach, we create a deployment where the sidecar is responsible for authenticating with the KDC and retrieving a valid ticket.  The primary workload container validates use of the ticket.  Both containers us simple shell scripts and interact with cli's included with the kerberos module added to the image. 

```bash
# Create configmaps and secrets necessary for kerberos clients
ytt -f kdc-client/kdc-client-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -n kdc -f -

# Create the test client deployment
ytt -f kdc-client/kdc-client.yaml --data-values-file $PARAMS_YAML | kubectl apply -n kdc -f -

# Validate the deployment is ready
kubectl get deploy kdc-client -n kdc

# Check out the sidecar container logs.
kubectl logs deployment/kdc-client -n kdc -c kdc-sidecar

######### Example Output #########
# Found 2 pods, using pod/kdc-client-54dd94c978-7q5sn
# *** using client keytab
# *** kinit at +2022-10-14
# Using default cache: /dev/shm/ccache
# Using principal: client1@WINTERFELL.COM
# Authenticated to Kerberos v5
# Ticket cache: FILE:/dev/shm/ccache
# Default principal: client1@WINTERFELL.COM

# Valid starting     Expires            Service principal
# 10/14/22 12:55:41  10/15/22 00:55:41  krbtgt/WINTERFELL.COM@WINTERFELL.COM
#         renew until 10/15/22 12:55:41
# *** Waiting for 3600 seconds

# Check out the client container logs.
kubectl logs deployment/kdc-client -n kdc -c kdc-client

######### Example Output #########
# *** checking if authenticated
# klist: No credentials cache found (filename: /dev/shm/ccache)
# *** checking if authenticated
# Ticket cache: FILE:/dev/shm/ccache
# Default principal: client1@WINTERFELL.COM

# Valid starting     Expires            Service principal
# 10/14/22 12:55:41  10/15/22 00:55:41  krbtgt/WINTERFELL.COM@WINTERFELL.COM
#         renew until 10/15/22 12:55:41
# *** checking if authenticated
# Ticket cache: FILE:/dev/shm/ccache
# Default principal: client1@WINTERFELL.COM

# Valid starting     Expires            Service principal
# 10/14/22 12:55:41  10/15/22 00:55:41  krbtgt/WINTERFELL.COM@WINTERFELL.COM
#         renew until 10/15/22 12:55:41
```

## Tanzu Advanced Method (TBS without TAP)

### 1. Create Container Image for your .NET Core App

A sample .NET Core application is included in this repository.  Use the TBS to create a container image from the application source code.  The `Image` resource specifically refers to the `base-kerberos` ClusterBuilder that was created in a previous step.

```bash
# Apply image resources for TBS to create the image
ytt -f test-app-tbs/test-app-image.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Check build status and grab the image name for the successful build. 
kp build list kerberos-web-tbs -n $DEV_NAMESPACE
# Update $PARAMS_YAML test_app.image field with the built image value.

# Check build logs, if you need to troubleshoot
kp build logs kerberos-web-tbs -n $DEV_NAMESPACE
```

### 2. Deploy and Test Your .NET Core App

Using the same approach as the our simple script based sidecar validation, however, here we we create a Knative Service for our .NET Core test app.  This .NET Core test app is adapted from the great work macsux did at [https://github.com/macsux/kerberos-buildpack](https://github.com/macsux/kerberos-buildpack).

```bash
# Create configmaps and secrets necessary for kerberos clients
ytt -f kdc-client/kdc-client-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Knative Service in TAP 1.3 does not allow for EmptyDir volumes by default. This must be enabed via feature flag.
# Double-check that the feature flag is enabled.  Result of the following command should be "enabled".
# If blank or "disabled" then you must configure CNRS appropriately.
kubectl get cm config-features -n knative-serving -ojsonpath="{.data.kubernetes\.podspec-volumes-emptydir}"

# Apply the kservice resources for the test app
ytt -f test-app-tbs/test-app.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate the kservice is up and READY
kubectl get kservice kerberos-web-tbs -n $DEV_NAMESPACE

# Check the app and you should see valid ticket diagnostic information
open $(kubectl get kservice kerberos-web-tbs -n $DEV_NAMESPACE -ojsonpath="{.status.url}")
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
ytt -f kdc-client/kdc-client-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Submit your workload
kubectl apply -f kerberos-web/config/workload.yaml -n $DEV_NAMESPACE

# Check on status of your workload
tanzu apps workload get kerberos-web -n $DEV_NAMESPACE
tanzu apps workload tail kerberos-web -n $DEV_NAMESPACE

# Check the app and you should see valid ticket diagnostic information
open $(kubectl get kservice kerberos-web -n $DEV_NAMESPACE -ojsonpath="{.status.url}")
```
