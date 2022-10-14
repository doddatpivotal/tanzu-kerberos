Assumes
- External-DNS
- Cert-Manager
- Contour
- TBS or TAP Full Profile
    - TBS
        - Namespace with proper registry secrets already established
    - TAP
        - Developer namespace setup
Client Requirements
- kp, yq, ytt

# Execution Guide

```bash
export PARAMS_YAML=local-config/params.yaml
```

1. Deploy KDC Server

```bash
ytt -f kdc/kdc.yaml --data-values-file $PARAMS_YAML| kubectl apply -f -
```

2. Generate Client Creds

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

3. Create Custom Stack and Custom Builder
```bash
ytt -f build-service --data-values-file $PARAMS_YAML | kubectl apply -f -
# Retrieve the image reference for the base-kerberos-run image
```

4. Test out the sidecar using simple client
```bash
# Create the test client deployment and validate activity
ytt -f kdc-client/kdc-client-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -n kdc -f -

# Create the test client deployment and validate activity
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

# Tanzu Advanced Method

1. Create Container Image for your .NET Core App

```bash
# Apply image resources for TBS to create the image
ytt -f test-app-tbs/test-app-image.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -

# Check build status and grab the image name for the successful build. Update $PARAMS_YAML test_app.image field with the built image value.
kp build list kerberos-web 

# Check build logs, if you need to troubleshoot
kp build logs kerberos-web 
```

2. Deploy and Test Your .NET Core App

```bash
# Create the test client deployment and validate activity
ytt -f kdc-client/kdc-client-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -

# Apply the deployment, service, and ingress resources for the test app
ytt -f test-app-tbs/test-app.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -

# Validate the deployment is up and running
kubectl get deployment kerberos-web

# Check the app and you should see valid ticket diagnostic information
open https://kerberos-web.$(yq e .base_url $PARAMS_YAML)
```

# TAP Method

1. Create Custom Convention Template, Cluster Template, and Supply Chain
```bash
ytt -f supply-chain/kerberos-config-template.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -
ytt -f supply-chain/kerberos-convention-template.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -
kubectl apply -f supply-chain/kerberos-web-supply-chain.yaml
```

2. Submit Workload
```bash
# Create the test client deployment and validate activity
ytt -f kdc-client/kdc-client-dependencies.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -

# Apply the deployment, service, and ingress resources for the test app
ytt -f test-app-tbs/test-app.yaml --data-values-file $PARAMS_YAML | kubectl apply -f -

# Validate the deployment is up and running
kubectl get deployment kerberos-web

# Check the app and you should see valid ticket diagnostic information
open https://kerberos-web.$(yq e .base_url $PARAMS_YAML)
```
