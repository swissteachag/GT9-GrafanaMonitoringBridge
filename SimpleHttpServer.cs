using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace GrafanaMonitoringBridge
{
    public class SimpleHttpServer
    {
        private readonly object _appState;
        private readonly HttpListener _listener;
        private readonly string _apiKey;
        private bool _running;

        public SimpleHttpServer(object appState, int port, string apiKey = null)
        {
            _appState = appState;
            _apiKey = apiKey;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            BeginGetContext();
        }

        public void Stop()
        {
            _running = false;
            _listener.Stop();
            _listener.Close();
        }

        private void BeginGetContext()
        {
            if (_running)
            {
                _listener.BeginGetContext(ProcessRequest, null);
            }
        }

        private void ProcessRequest(IAsyncResult result)
        {
            if (!_running) return;

            try
            {
                var context = _listener.EndGetContext(result);
                BeginGetContext();
                HandleRequest(context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}");
                if (_running) BeginGetContext();
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            
            // Check API key if configured
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                var providedKey = request.Headers["X-API-Key"];
                if (string.IsNullOrWhiteSpace(providedKey) || providedKey != _apiKey)
                {
                    response.StatusCode = 401;
                    var errorBytes = System.Text.Encoding.UTF8.GetBytes("{\"error\":\"Unauthorized - Invalid or missing X-API-Key header\"}");
                    response.ContentType = "application/json";
                    response.ContentLength64 = errorBytes.Length;
                    response.OutputStream.Write(errorBytes, 0, errorBytes.Length);
                    response.OutputStream.Close();
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 401 Unauthorized - {request.Url.AbsolutePath}");
                    return;
                }
            }
            
            // CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-API-Key");
            
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            try
            {
                var path = request.Url.AbsolutePath.ToLower();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {request.HttpMethod} {path}");
                
                string responseText;
                
                switch (path)
                {
                    case "/":
                    case "/health":
                        responseText = HandleHealth();
                        response.ContentType = "application/json";
                        break;
                    case "/api/services":
                        responseText = HandleServices();
                        response.ContentType = "application/json";
                        break;
                    case "/api/usage":
                        responseText = HandleUsage();
                        response.ContentType = "application/json";
                        break;
                    case "/api/summary":
                        responseText = HandleSummary();
                        response.ContentType = "application/json";
                        break;
                    case "/api/metrics":
                        responseText = HandleMetrics();
                        response.ContentType = "text/plain; version=0.0.4";
                        break;
                    case "/api/v1/query":
                        responseText = HandlePrometheusQuery(request);
                        response.ContentType = "application/json";
                        break;
                    case "/api/v1/query_range":
                        responseText = HandlePrometheusQueryRange(request);
                        response.ContentType = "application/json";
                        break;
                    case "/api/v1/status/buildinfo":
                        responseText = HandlePrometheusBuildInfo();
                        response.ContentType = "application/json";
                        break;
                    case "/api/v1/label/__name__/values":
                    case "/api/v1/labels":
                        responseText = HandlePrometheusLabels();
                        response.ContentType = "application/json";
                        break;
                    default:
                        response.StatusCode = 404;
                        responseText = JsonConvert.SerializeObject(new { 
                            error = "Not found",
                            path = path,
                            availableEndpoints = new[] {
                                "/health",
                                "/api/services",
                                "/api/usage",
                                "/api/summary",
                                "/api/metrics"
                            }
                        });
                        response.ContentType = "application/json";
                        break;
                }
                
                SendResponse(response, responseText);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR handling request: {ex.Message}");
                response.StatusCode = 500;
                response.ContentType = "application/json";
                var errorResponse = JsonConvert.SerializeObject(new { 
                    error = ex.Message,
                    type = ex.GetType().Name
                });
                SendResponse(response, errorResponse);
            }
        }

        private void SendResponse(HttpListenerResponse response, string text)
        {
            var buffer = Encoding.UTF8.GetBytes(text);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private string HandleHealth()
        {
            try
            {
                var type = _appState.GetType();
                var pingMethod = type.GetMethod("Ping");
                var isHealthy = (bool)pingMethod.Invoke(_appState, null);
                return JsonConvert.SerializeObject(new
                {
                    status = isHealthy ? "healthy" : "unhealthy",
                    timestamp = DateTime.UtcNow,
                    service = "GrafanaMonitoringBridge",
                    version = "1.0.0"
                }, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new
                {
                    status = "unhealthy",
                    timestamp = DateTime.UtcNow,
                    service = "GrafanaMonitoringBridge",
                    error = ex.Message
                }, Formatting.Indented);
            }
        }

        private string HandleServices()
        {
            var type = _appState.GetType();
            var method = type.GetMethod("GetRunningServices");
            var servicesJson = (string)method.Invoke(_appState, null);
            // Format it nicely
            var obj = JsonConvert.DeserializeObject(servicesJson);
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }

        private string HandleUsage()
        {
            var type = _appState.GetType();
            var method = type.GetMethod("GetSystemUsage");
            var usage = method.Invoke(_appState, new object[] { false });
            return JsonConvert.SerializeObject(usage, Formatting.Indented);
        }

        private string HandleSummary()
        {
            var type = _appState.GetType();
            var method = type.GetMethod("GetAppStateSummary");
            var summary = method.Invoke(_appState, null);
            return JsonConvert.SerializeObject(summary, Formatting.Indented);
        }

        private string HandleMetrics()
        {
            var sb = new StringBuilder();
            
            try
            {
                // Services metrics
                var type = _appState.GetType();
                var method = type.GetMethod("GetRunningServices");
                var servicesJson = (string)method.Invoke(_appState, null);
                var services = JsonConvert.DeserializeObject<JArray>(servicesJson);
                
                sb.AppendLine("# HELP gt_services_total Total number of services");
                sb.AppendLine("# TYPE gt_services_total gauge");
                sb.AppendLine($"gt_services_total {services?.Count ?? 0}");
                
                sb.AppendLine("# HELP gt_services_running Number of running services");
                sb.AppendLine("# TYPE gt_services_running gauge");
                // Fix: Cast lambda to Func to avoid dynamic dispatch issues
                Func<JToken, bool> isRunningPredicate = s => s["IsRunning"]?.Value<bool>() == true;
                var runningCount = services?.Count(isRunningPredicate) ?? 0;
                sb.AppendLine($"gt_services_running {runningCount}");
                
                // Individual service metrics
                if (services != null && services.Count > 0)
                {
                    sb.AppendLine("# HELP gt_service_runs Total number of runs per service");
                    sb.AppendLine("# TYPE gt_service_runs counter");
                    foreach (var service in services)
                    {
                        string name = service["Name"]?.ToString()
                            .Replace(" ", "_")
                            .Replace("-", "_")
                            .Replace(".", "_")
                            .ToLower() ?? "unknown";
                        var numRuns = service["NumRuns"]?.Value<int>() ?? 0;
                        sb.AppendLine($"gt_service_runs{{service=\"{name}\"}} {numRuns}");
                    }
                    
                    sb.AppendLine("# HELP gt_service_time_used_ms Time used by service in milliseconds");
                    sb.AppendLine("# TYPE gt_service_time_used_ms counter");
                    foreach (var service in services)
                    {
                        string name = service["Name"]?.ToString()
                            .Replace(" ", "_")
                            .Replace("-", "_")
                            .Replace(".", "_")
                            .ToLower() ?? "unknown";
                        var timeUsed = service["TimeUsed"]?.Value<long>() ?? 0;
                        sb.AppendLine($"gt_service_time_used_ms{{service=\"{name}\"}} {timeUsed}");
                    }
                    
                    sb.AppendLine("# HELP gt_service_status Service status (1=running, 0=stopped)");
                    sb.AppendLine("# TYPE gt_service_status gauge");
                    foreach (var service in services)
                    {
                        string name = service["Name"]?.ToString() ?? "unknown";
                        var isRunning = service["IsRunning"]?.Value<bool>() == true ? 1 : 0;
                        sb.AppendLine($"gt_service_status{{name=\"{name}\"}} {isRunning}");
                    }
                    
                    sb.AppendLine("# HELP gt_service_errors Total number of errors per service");
                    sb.AppendLine("# TYPE gt_service_errors gauge");
                    foreach (var service in services)
                    {
                        string name = service["Name"]?.ToString() ?? "unknown";
                        var errorsStr = service["Errors"]?.ToString() ?? "0";
                        // Parse error count from string like "0" or "2 (2025-11-19 14:24:48 )"
                        int errorCount = 0;
                        var parts = errorsStr.Split(' ');
                        if (parts.Length > 0)
                        {
                            int.TryParse(parts[0], out errorCount);
                        }
                        sb.AppendLine($"gt_service_errors{{name=\"{name}\"}} {errorCount}");
                    }
                    
                    sb.AppendLine("# HELP gt_service_invokable Service is invokable (1=yes, 0=no)");
                    sb.AppendLine("# TYPE gt_service_invokable gauge");
                    foreach (var service in services)
                    {
                        string name = service["Name"]?.ToString() ?? "unknown";
                        var isInvokable = service["IsInvokable"]?.Value<bool>() == true ? 1 : 0;
                        sb.AppendLine($"gt_service_invokable{{name=\"{name}\"}} {isInvokable}");
                    }
                    
                    sb.AppendLine("# HELP gt_service_last_start_timestamp Unix timestamp of last service start");
                    sb.AppendLine("# TYPE gt_service_last_start_timestamp gauge");
                    foreach (var service in services)
                    {
                        string name = service["Name"]?.ToString() ?? "unknown";
                        var lastStartStr = service["LastStart"]?.ToString();
                        long timestamp = 0;
                        if (!string.IsNullOrEmpty(lastStartStr) && DateTime.TryParse(lastStartStr, out var lastStart))
                        {
                            if (lastStart.Year > 1900) // Ignore default DateTime values
                            {
                                timestamp = new DateTimeOffset(lastStart).ToUnixTimeSeconds();
                            }
                        }
                        sb.AppendLine($"gt_service_last_start_timestamp{{name=\"{name}\"}} {timestamp}");
                    }
                }
                
                // System usage metrics
                var usageMethod = type.GetMethod("GetSystemUsage");
                var usageObj = usageMethod.Invoke(_appState, new object[] { false });
                var usage = JObject.FromObject(usageObj);
                
                sb.AppendLine("# HELP gt_chat_rooms Total number of chat rooms");
                sb.AppendLine("# TYPE gt_chat_rooms gauge");
                sb.AppendLine($"gt_chat_rooms {usage["NumChatRooms"]?.Value<int>() ?? 0}");
                
                sb.AppendLine("# HELP gt_chat_users Total number of chat users");
                sb.AppendLine("# TYPE gt_chat_users gauge");
                sb.AppendLine($"gt_chat_users {usage["NumChatUsers"]?.Value<int>() ?? 0}");
                
                sb.AppendLine("# HELP gt_running_lessons Total number of running lessons");
                sb.AppendLine("# TYPE gt_running_lessons gauge");
                sb.AppendLine($"gt_running_lessons {usage["NumRunningLessons"]?.Value<int>() ?? 0}");
                
                // Sessions per domain
                var summaryMethod = type.GetMethod("GetAppStateSummary");
                var summaryObj = summaryMethod.Invoke(_appState, null);
                var summary = JObject.FromObject(summaryObj);
                var domains = summary["DomainSummaries"] as JArray;
                
                if (domains != null && domains.Count > 0)
                {
                    sb.AppendLine("# HELP gt_sessions_total Total number of sessions per domain");
                    sb.AppendLine("# TYPE gt_sessions_total gauge");
                    
                    foreach (var domain in domains)
                    {
                        var domainId = domain["DomainId"]?.Value<int>() ?? 0;
                        var sessions = domain["Sessions"] as JArray;
                        var sessionCount = sessions?.Count ?? 0;
                        sb.AppendLine($"gt_sessions_total{{domain=\"{domainId}\"}} {sessionCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"# Error generating metrics: {ex.Message}");
            }
            
            return sb.ToString();
        }

        private string HandlePrometheusQuery(HttpListenerRequest request)
        {
            try
            {
                // Parse query parameter - check both query string (GET) and form data (POST)
                string query = request.QueryString["query"];
                
                if (string.IsNullOrWhiteSpace(query) && request.HasEntityBody)
                {
                    // Try to read from POST body (form-urlencoded)
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        var body = reader.ReadToEnd();
                        var formData = System.Web.HttpUtility.ParseQueryString(body);
                        query = formData["query"];
                    }
                }
                
                if (string.IsNullOrWhiteSpace(query))
                {
                    return JsonConvert.SerializeObject(new
                    {
                        status = "error",
                        errorType = "bad_data",
                        error = "query parameter is required"
                    });
                }

                // Get current metric values
                var type = _appState.GetType();
                var usageMethod = type.GetMethod("GetSystemUsage");
                object usageObj = null;
                
                try
                {
                    usageObj = usageMethod.Invoke(_appState, new object[] { true }); // includeSuiSessions = true
                }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    // Log the inner exception which has the real error
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in GetSystemUsage: {ex.InnerException?.Message ?? ex.Message}");
                    return JsonConvert.SerializeObject(new
                    {
                        status = "error",
                        errorType = "internal",
                        error = ex.InnerException?.Message ?? ex.Message
                    });
                }
                
                var usage = JObject.FromObject(usageObj);

                // Simple query matching - extract metric name
                var metricName = query.Split('{')[0].Trim();
                object value = null;

                switch (metricName)
                {
                    case "gt_cpu_usage":
                        value = usage["CpuUsage"]?.Value<double>() ?? 0;
                        break;
                    case "gt_memory_usage_mb":
                        value = usage["MemoryUsageMB"]?.Value<double>() ?? 0;
                        break;
                    case "gt_uptime_seconds":
                        value = usage["UptimeSeconds"]?.Value<double>() ?? 0;
                        break;
                    case "gt_active_users":
                        value = usage["NumActiveUsers"]?.Value<int>() ?? 0;
                        break;
                    case "gt_chat_users":
                        value = usage["NumChatUsers"]?.Value<int>() ?? 0;
                        break;
                    case "gt_running_lessons":
                        value = usage["NumRunningLessons"]?.Value<int>() ?? 0;
                        break;
                    case "gt_service_status":
                    case "gt_service_errors":
                    case "gt_service_runs":
                    case "gt_service_time_used_ms":
                    case "gt_service_invokable":
                    case "gt_service_last_start_timestamp":
                        // For service metrics, return all services
                        var servicesMethod = type.GetMethod("GetRunningServices");
                        var servicesObj = servicesMethod.Invoke(_appState, null);
                        
                        // Check if it's a string (serialized) or object
                        JArray services;
                        if (servicesObj is string)
                        {
                            services = JArray.Parse((string)servicesObj);
                        }
                        else
                        {
                            services = JArray.FromObject(servicesObj);
                        }
                        
                        var results = new JArray();
                        foreach (var service in services)
                        {
                            var serviceName = service["Name"]?.Value<string>() ?? "unknown";
                            object metricValue = null;
                            
                            switch (metricName)
                            {
                                case "gt_service_status":
                                    metricValue = service["IsRunning"]?.Value<bool>() == true ? "1" : "0";
                                    break;
                                case "gt_service_errors":
                                    var errorsStr = service["Errors"]?.ToString() ?? "0";
                                    int errorCount = 0;
                                    var parts = errorsStr.Split(' ');
                                    if (parts.Length > 0)
                                    {
                                        int.TryParse(parts[0], out errorCount);
                                    }
                                    metricValue = errorCount.ToString();
                                    break;
                                case "gt_service_runs":
                                    metricValue = (service["NumRuns"]?.Value<int>() ?? 0).ToString();
                                    break;
                                case "gt_service_time_used_ms":
                                    metricValue = (service["TimeUsed"]?.Value<long>() ?? 0).ToString();
                                    break;
                                case "gt_service_invokable":
                                    metricValue = service["IsInvokable"]?.Value<bool>() == true ? "1" : "0";
                                    break;
                                case "gt_service_last_start_timestamp":
                                    var lastStartStr = service["LastStart"]?.ToString();
                                    long timestamp = 0;
                                    if (!string.IsNullOrEmpty(lastStartStr) && DateTime.TryParse(lastStartStr, out var lastStart))
                                    {
                                        if (lastStart.Year > 1900)
                                        {
                                            timestamp = new DateTimeOffset(lastStart).ToUnixTimeSeconds();
                                        }
                                    }
                                    metricValue = timestamp.ToString();
                                    break;
                            }
                            
                            results.Add(new JObject
                            {
                                ["metric"] = new JObject
                                {
                                    ["__name__"] = metricName,
                                    ["name"] = serviceName
                                },
                                ["value"] = new JArray { DateTimeOffset.UtcNow.ToUnixTimeSeconds(), metricValue }
                            });
                        }
                        
                        return JsonConvert.SerializeObject(new
                        {
                            status = "success",
                            data = new { resultType = "vector", result = results }
                        });
                }

                if (value != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        status = "success",
                        data = new
                        {
                            resultType = "vector",
                            result = new[]
                            {
                                new
                                {
                                    metric = new { __name__ = metricName },
                                    value = new object[] { DateTimeOffset.UtcNow.ToUnixTimeSeconds(), value.ToString() }
                                }
                            }
                        }
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    data = new { resultType = "vector", result = new JArray() }
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new
                {
                    status = "error",
                    errorType = "internal",
                    error = ex.Message
                });
            }
        }

        private string HandlePrometheusQueryRange(HttpListenerRequest request)
        {
            // For now, return instant query result (no historical data available)
            return HandlePrometheusQuery(request);
        }

        private string HandlePrometheusBuildInfo()
        {
            return JsonConvert.SerializeObject(new
            {
                status = "success",
                data = new
                {
                    version = "1.0.0",
                    revision = "gt-bridge",
                    branch = "main",
                    buildUser = "GrafanaMonitoringBridge",
                    buildDate = "2025-11-26",
                    goVersion = "n/a"
                }
            });
        }

        private string HandlePrometheusLabels()
        {
            return JsonConvert.SerializeObject(new
            {
                status = "success",
                data = new[]
                {
                    "gt_service_status",
                    "gt_service_errors",
                    "gt_service_runs",
                    "gt_service_time_used_ms",
                    "gt_service_invokable",
                    "gt_service_last_start_timestamp",
                    "gt_services_total",
                    "gt_services_running"
                }
            });
        }
    }
}
