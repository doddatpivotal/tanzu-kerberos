# Tanzu Advanced Execution Guide

Follow all steps from the [Tanzu Application Platform Guide](./guide.md) up to and included `Create sidecar image using TBS`.  Then pickup here...

## Tanzu Advanced Method (TBS without TAP)

### 1. Create sample app image using TBS

This image uses the `base-kerberos` created in the previous section

```bash
# Apply image resources for TBS to create the image
ytt -f kerberos-demo-app-tbs/kerberos-demo-tbs-image.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Check build status and grab the image name for the successful build. 
kp build list kerberos-demo-tbs -n $DEV_NAMESPACE
# Update $PARAMS_YAML kerberos_demo_app.image field with the built image value.

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

# Apply the secrets containing location and crednetial information
ytt -f kerberos-demo-app-tbs/kerberos-demo-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Apply the kservice resources for the test app
ytt -f kerberos-demo-app-tbs/kerberos-demo-tbs-kservice.yaml --data-values-file $PARAMS_YAML | kubectl apply -n $DEV_NAMESPACE -f -

# Validate the kservice is up and READY
kubectl get kservice kerberos-demo-tbs -n $DEV_NAMESPACE

# Check the app and you should see valid ticket diagnostic information
open $(kubectl get kservice kerberos-demo-tbs -n $DEV_NAMESPACE -ojsonpath="{.status.url}")/diag

# Check the app and you should see valid Sql Server connection information
open $(kubectl get kservice kerberos-demo-tbs -n $DEV_NAMESPACE -ojsonpath="{.status.url}")/sql

```

