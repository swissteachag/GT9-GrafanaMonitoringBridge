using System;
using System.Configuration;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;

namespace GrafanaMonitoringBridge
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("===================================");
            Console.WriteLine("GT Grafana Monitoring Bridge");
            Console.WriteLine("===================================\n");
            
            // Read configuration
            string appStateHost = System.Configuration.ConfigurationManager.AppSettings["AppState.Host"] ?? "localhost";
            int appStatePort = int.Parse(System.Configuration.ConfigurationManager.AppSettings["AppState.Port"] ?? "20010");
            int httpPort = int.Parse(System.Configuration.ConfigurationManager.AppSettings["Http.Port"] ?? "8080");
            string apiKey = System.Configuration.ConfigurationManager.AppSettings["Http.ApiKey"];
            
            try
            {
                // Initialize .NET Remoting client
                Console.WriteLine($"Connecting to ApplicationState service...");
                Console.WriteLine($"  Host: {appStateHost}");
                Console.WriteLine($"  Port: {appStatePort}");
                
                if (ChannelServices.GetChannel("GrafanaBridgeClient") == null)
                {
                    var clientChannel = new TcpClientChannel("GrafanaBridgeClient", null);
                    ChannelServices.RegisterChannel(clientChannel, false);
                    Console.WriteLine("  Remoting channel registered");
                }
                
                // Connect to remote object
                // We need to load the actual type from the GT libraries
                var remotingUrl = $"tcp://{appStateHost}:{appStatePort}/ApplicationState";
                
                // Load the ApplicationState type from Twi.Gt.Lms.dll
                string lmsAssemblyPath = null;
                
                // Check if path is configured
                var configuredPath = ConfigurationManager.AppSettings["Lms.DllPath"];
                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    lmsAssemblyPath = System.IO.Path.Combine(configuredPath, "Twi.Gt.Lms.dll");
                    Console.WriteLine($"  Using configured path: {lmsAssemblyPath}");
                    
                    if (!System.IO.File.Exists(lmsAssemblyPath))
                    {
                        throw new Exception($"Could not find Twi.Gt.Lms.dll at configured path.\nSearched: {lmsAssemblyPath}\nPlease check the 'Lms.DllPath' setting in App.config");
                    }
                }
                else
                {
                    // Fallback: search relative to executable (for development)
                    lmsAssemblyPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        @"..\..\..\core\libraries\Twi.Gt.Lms\bin\Debug\Twi.Gt.Lms.dll"
                    );
                    
                    if (!System.IO.File.Exists(lmsAssemblyPath))
                    {
                        // Try Release folder
                        lmsAssemblyPath = System.IO.Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            @"..\..\..\core\libraries\Twi.Gt.Lms\bin\Release\Twi.Gt.Lms.dll"
                        );
                    }
                    
                    if (!System.IO.File.Exists(lmsAssemblyPath))
                    {
                        throw new Exception($"Could not find Twi.Gt.Lms.dll. Please build the GT libraries first OR set 'Lms.DllPath' in App.config.\nSearched: {lmsAssemblyPath}");
                    }
                }
                
                Console.WriteLine($"  Loading type from: {System.IO.Path.GetFileName(lmsAssemblyPath)}");
                var lmsAssembly = Assembly.LoadFrom(lmsAssemblyPath);
                var appStateType = lmsAssembly.GetType("Twi.Gt.ApplicationState.ApplicationState");
                
                if (appStateType == null)
                {
                    throw new Exception("Could not find ApplicationState type in Twi.Gt.Lms.dll");
                }
                
                // Set up assembly resolver to handle missing assemblies during deserialization
                var lmsDirectory = System.IO.Path.GetDirectoryName(lmsAssemblyPath);
                AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
                {
                    var assemblyName = new AssemblyName(resolveArgs.Name);
                    var fileName = assemblyName.Name + ".dll";
                    var filePath = System.IO.Path.Combine(lmsDirectory, fileName);
                    
                    if (System.IO.File.Exists(filePath))
                    {
                        Console.WriteLine($"  Resolving assembly: {assemblyName.Name}");
                        return Assembly.LoadFrom(filePath);
                    }
                    return null;
                };
                
                var handle = Activator.GetObject(appStateType, remotingUrl);
                
                if (handle == null)
                {
                    throw new Exception("Failed to get remote object reference");
                }
                
                // Test connection using reflection
                Console.Write("  Testing connection... ");
                var type = handle.GetType();
                var pingMethod = type.GetMethod("Ping");
                if (pingMethod == null)
                {
                    throw new Exception("Ping method not found on remote object");
                }
                
                bool pingResult = (bool)pingMethod.Invoke(handle, null);
                
                if (pingResult)
                {
                    Console.WriteLine("OK\n");
                }
                else
                {
                    Console.WriteLine("FAILED\n");
                    throw new Exception("Ping failed");
                }
                
                // Start HTTP server with reflection wrapper
                Console.WriteLine($"Starting HTTP server...");
                Console.WriteLine($"  Port: {httpPort}");
                
                SimpleHttpServer httpServer = null;
                try
                {
                    httpServer = new SimpleHttpServer(handle, httpPort, apiKey);
                    httpServer.Start();
                    
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        Console.WriteLine("  Authentication: ENABLED");
                        Console.WriteLine("  API Key: " + new string('*', apiKey.Length));
                    }
                    else
                    {
                        Console.WriteLine("  Authentication: DISABLED (set Http.ApiKey in config to enable)");
                    }
                    Console.WriteLine("  Server started\n");
                }
                catch (System.Net.HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
                {
                    Console.WriteLine("\nERROR: Access denied when starting HTTP listener.");
                    Console.WriteLine("\nTo fix this, run ONE of these commands as Administrator:\n");
                    Console.WriteLine($"Option 1 - Reserve URL for your user account:");
                    Console.WriteLine($"  netsh http add urlacl url=http://+:{httpPort}/ user={Environment.UserDomainName}\\{Environment.UserName}");
                    Console.WriteLine($"\nOption 2 - Reserve URL for all users:");
                    Console.WriteLine($"  netsh http add urlacl url=http://+:{httpPort}/ user=Everyone");
                    Console.WriteLine($"\nOption 3 - Run this application as Administrator");
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                    return;
                }
                
                Console.WriteLine("===================================");
                Console.WriteLine("READY - Available endpoints:");
                Console.WriteLine("===================================");
                Console.WriteLine($"  http://localhost:{httpPort}/health");
                Console.WriteLine($"  http://localhost:{httpPort}/api/services");
                Console.WriteLine($"  http://localhost:{httpPort}/api/usage");
                Console.WriteLine($"  http://localhost:{httpPort}/api/summary");
                Console.WriteLine($"  http://localhost:{httpPort}/api/metrics");
                Console.WriteLine("\nPress Ctrl+C to stop...\n");
                
                bool keepRunning = true;
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    keepRunning = false;
                    Console.WriteLine("\n\nStopping server...");
                    httpServer.Stop();
                    Console.WriteLine("Stopped.");
                };
                
                // Keep running
                while (keepRunning)
                {
                    System.Threading.Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
                Console.WriteLine("\nDetails:");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }
        }
    }
    
}
