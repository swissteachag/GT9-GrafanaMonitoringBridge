# GT Grafana Monitoring Bridge

A standalone .NET Framework 4.8 application that exposes GT ApplicationState metrics via a Prometheus-compatible HTTP API for Grafana monitoring.

## Overview

This bridge connects to the GT ApplicationState service via .NET Remoting and exposes system and service metrics through HTTP endpoints that can be consumed by Grafana's Prometheus data source.

## Prerequisites

- .NET Framework 4.8 Runtime
- Access to GT ApplicationState service
- GT binaries folder containing `Twi.Gt.Lms.dll` and dependencies
- HTTP port reservation or Administrator privileges

## Configuration

Edit `GrafanaMonitoringBridge.exe.config` to configure the bridge:

### ApplicationState Connection

```xml
<!-- ApplicationState service connection -->
<add key="AppState.Host" value="localhost" />
<add key="AppState.Port" value="20010" />
```

- **AppState.Host**: Hostname or IP address of the GT ApplicationState service (default: `localhost`)
- **AppState.Port**: TCP port for ApplicationState remoting endpoint (default: `20010`)

### HTTP Server Settings

```xml
<!-- HTTP server port for Grafana -->
<add key="Http.Port" value="8080" />
```

- **Http.Port**: Port for the HTTP API server (default: `8080`)

### Security

```xml
<!-- API Key for authentication (leave empty to disable authentication) -->
<!-- Grafana will send this in X-API-Key header -->
<add key="Http.ApiKey" value="" />
```

- **Http.ApiKey**: Optional API key for authentication
  - Leave empty to disable authentication
  - When set, all requests must include `X-API-Key` header with this value
  - Recommended for production deployments

### DLL Path Configuration

```xml
<!-- Path to folder containing Twi.Gt.Lms.dll (e.g., C:\GT\DT_FlowDev\Twi.Gt.SvcHost) -->
<!-- If not specified, will search relative to this executable -->
<add key="Lms.DllPath" value="" />
```

- **Lms.DllPath**: Absolute path to folder containing GT binaries
  - **Required** Must contain `Twi.Gt.Lms.dll` and its dependencies
  - If empty, searches relative paths (for development only)

## Installation & Setup

### 1. Deploy the Bridge

Copy the following files to your deployment location:
- `GrafanaMonitoringBridge.exe`
- `GrafanaMonitoringBridge.exe.config`
- `Newtonsoft.Json.dll`

### 2. Configure Settings

Edit `GrafanaMonitoringBridge.exe.config`:
- Set `Lms.DllPath` to your GT installation folder
- Set `AppState.Host` and `AppState.Port` if not using localhost
- Set `Http.ApiKey` for security (recommended)
- Optionally change `Http.Port` if 8080 is in use

### 3. HTTP Port Reservation

The bridge needs permission to listen on the HTTP port. Choose one option:

**Option A - Run as Administrator:**
```cmd
Run GrafanaMonitoringBridge.exe as Administrator
```

**Option B - Reserve URL (Recommended):**

Run this command **as Administrator** (one-time setup):
```cmd
netsh http add urlacl url=http://+:8080/ user=Everyone
```

Replace `8080` with your configured `Http.Port` if different.

To remove the reservation later:
```cmd
netsh http delete urlacl url=http://+:8080/
```

### 4. Run the Bridge

```cmd
GrafanaMonitoringBridge.exe
```

Verify the output shows:
```
===================================
GT Grafana Monitoring Bridge
===================================

Connecting to ApplicationState service...
  Host: localhost
  Port: 20090
  Testing connection... OK

Starting HTTP server...
  Port: 8080
  Authentication: ENABLED (or DISABLED)
  Server started

READY - Available endpoints:
  http://localhost:8080/health
  http://localhost:8080/api/metrics
  ...
```

### 5. Run as Windows Service (Optional)

To run the bridge as a Windows Service, use [NSSM](https://nssm.cc/):

```cmd
nssm install GTMonitoringBridge "C:\Path\To\GrafanaMonitoringBridge.exe"
nssm set GTMonitoringBridge AppDirectory "C:\Path\To"
nssm set GTMonitoringBridge DisplayName "GT Grafana Monitoring Bridge"
nssm set GTMonitoringBridge Description "Exposes GT metrics to Grafana via Prometheus API"
nssm start GTMonitoringBridge
```

## Grafana Integration

### Add Prometheus Data Source

1. In Grafana, go to **Configuration** → **Data Sources** → **Add data source**
2. Select **Prometheus**
3. Configure:
   - **Name**: `GT Monitoring`
   - **URL**: `http://your-server:8080`
   - **HTTP Method**: `POST`

4. If using API key authentication:
   - Under **Custom HTTP Headers**, click **Add header**
   - **Header**: `X-API-Key`
   - **Value**: Your configured API key from `Http.ApiKey`

5. Click **Save & Test**

### Available Metrics

#### Service Metrics
- `gt_service_status{name="Service Name"}` - Service status (1=running, 0=stopped)
- `gt_service_errors{name="Service Name"}` - Number of errors for service
- `gt_service_runs{service="service_name"}` - Total number of runs
- `gt_service_time_used_ms{service="service_name"}` - Total execution time in milliseconds
- `gt_service_invokable{name="Service Name"}` - Whether service is manually invokable (1=yes, 0=no)
- `gt_service_last_start_timestamp{name="Service Name"}` - Unix timestamp of last start time
- `gt_services_total` - Total number of services
- `gt_services_running` - Number of currently running services

### Example Grafana Queries

**Service Status:**
```promql
gt_service_status{name="Dynamic Group Refresher Service"}
```

**Service Error Count:**
```promql
gt_service_errors{name="Class Status Change Service"}
```

**Service Execution Rate:**
```promql
rate(gt_service_runs{service="notification_service"}[5m])
```

**Services with Errors:**
```promql
gt_service_errors > 0
```

**Active Services:**
```promql
count(gt_service_status == 1)
```

## API Endpoints

### Health Check
```
GET http://localhost:8080/health
```

Returns bridge health status and connection state.

### Services
```
GET http://localhost:8080/api/services
```

Returns JSON array of all services with detailed information.

### System Usage
```
GET http://localhost:8080/api/usage
```

Returns system usage statistics in JSON format.

### Application Summary
```
GET http://localhost:8080/api/summary
```

Returns application state summary including domain sessions.

### Prometheus Metrics
```
GET http://localhost:8080/api/metrics
```

Returns all metrics in Prometheus text format (for scraping).

### Prometheus Query API
```
POST http://localhost:8080/api/v1/query
Content-Type: application/x-www-form-urlencoded

query=gt_cpu_usage
```

Prometheus-compatible query endpoint (used by Grafana).

## Troubleshooting

### "Access is denied" when starting HTTP server

Run one of these commands as Administrator:
```cmd
netsh http add urlacl url=http://+:8080/ user=Everyone
```
Or run the application as Administrator.

### "Could not find Twi.Gt.Lms.dll"

Set the `Lms.DllPath` configuration to point to your GT installation folder:
```xml
<add key="Lms.DllPath" value="C:\GT\DT_FlowDev\Twi.Gt.SvcHost" />
```

### "Unable to find assembly 'Twi.Gt.Lms'" during queries

Ensure all GT dependencies are in the same folder as `Twi.Gt.Lms.dll`. The bridge will automatically load dependencies from that folder.

### Grafana shows "401 Unauthorized"

Check that the `X-API-Key` header in Grafana matches the `Http.ApiKey` value in the config file.

### No data in Grafana

1. Verify the bridge is running and shows "READY"
2. Test the connection: `curl http://localhost:8080/health`
3. Check the bridge console for error messages
4. Verify Grafana data source is configured correctly
5. Ensure ApplicationState service is running on the configured port

### Connection to ApplicationState fails

1. Verify `AppState.Host` and `AppState.Port` are correct
2. Check if ApplicationState service is running
3. Verify network connectivity (firewall, ports)
4. Check GT service host configuration

## Security Considerations

1. **Enable API Key Authentication**: Set `Http.ApiKey` to prevent unauthorized access
2. **Firewall Rules**: Restrict access to the HTTP port to trusted networks only
3. **HTTPS**: Consider using a reverse proxy (nginx, IIS) for HTTPS termination
4. **Network Segmentation**: Run on the same network as GT for better security
5. **Minimal Permissions**: Run as a dedicated service account with minimal privileges

## Building from Source

```cmd
dotnet build GrafanaMonitoringBridge.Standalone.csproj --configuration Release
```

Output will be in `bin\Release\`

## License

MIT

## Support

This software is not part of the Global Teach application suite ad will not be activly maintained and supported! It serves no purpose without an active license of Global Teach, which can be purchased from Swissteach AG. Use at your won risk 