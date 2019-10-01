/* Copyright © 2019 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using YetaWF.Core;
using YetaWF.Core.Support;
using YetaWF2.Logger;

namespace YetaWF.App_Start {

    public class Program {

        public static void Main(string[] args) {

            string currPath = Directory.GetCurrentDirectory();

            if (Startup.RunningInContainer) {
                string dataFolder = Path.Combine(currPath, Globals.DataFolder);
                if (!Directory.Exists(dataFolder)) {
                    // If we don't have a Data folder, copy the /DataInit folder to /Data
                    // This is needed with Docker during first-time installs.
                    string dataInitFolder = Path.Combine(currPath, "DataInit");
                    CopyFiles(dataInitFolder, dataFolder);
                }
                string maintFolder = Path.Combine(currPath, "wwwroot", "Maintenance");
                if (!Directory.Exists(maintFolder)) {
                    // If we don't have a Maintenance folder, copy the MaintenanceInit folder to Maintenance
                    // This is needed with Docker during first-time installs.
                    string maintInitFolder = Path.Combine(currPath, "wwwroot", "MaintenanceInit");
                    CopyFiles(maintInitFolder, maintFolder);
                }
            }

            string appSettings = GetAppSettingsFile();

            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(currPath)
                .AddJsonFile("hosting.json", optional: false)
#if DEBUG
                .AddJsonFile("hosting.Debug.json", optional: true)
#endif
                .AddJsonFile(appSettings)
                .AddEnvironmentVariables()
                .Build();

            IWebHost host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseContentRoot(currPath)
                .ConfigureLogging((hostingContext, logging) => {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                    logging.AddYetaWFLogger();
#if DEBUG
                    logging.AddDebug();
#endif
                })
                .UseKestrel()
                .UseIISIntegration()
                .UseStartup<Startup6>()
                .Build();

            host.Run();
        }

        public static string GetAppSettingsFile() {
            if (_AppSettingsFile == null) {
                string prod;
#if DEBUG
                prod = "";
#else
                prod = ".Prod";
#endif
                string file = null;
                string currPath = Directory.GetCurrentDirectory();
                string dataFolder = Path.Combine(currPath, Globals.DataFolder);

                if (file == null && Startup.RunningInContainer) {
                    string f = Path.Combine(dataFolder, $"AppSettings.Docker{prod}.json");
                    if (File.Exists(f))
                        file = f;
                }
                if (file == null && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                    string f = Path.Combine(dataFolder, $"AppSettings.Windows{prod}.json");
                    if (File.Exists(f))
                        file = f;
                }
                if (file == null && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) {
                    string f = Path.Combine(dataFolder, $"AppSettings.Linux{prod}.json");
                    if (File.Exists(f))
                        file = f;
                }
                if (file == null) {
                    string f = Path.Combine(dataFolder, $"AppSettings{prod}.json");
                    if (File.Exists(f)) {
                        file = f;
                    } else {
                        f = Path.Combine(dataFolder, $"AppSettings.json");
                        if (!File.Exists(f))
                            throw new InternalError($"File {f} doesn't exist");
                        file = f;
                    }
                }
                _AppSettingsFile = file;
            }
            return _AppSettingsFile;
        }
        private static string _AppSettingsFile = null;

        private static void CopyFiles(string srcInitFolder, string srcFolder) {
            Directory.CreateDirectory(srcFolder);
            string[] files = Directory.GetFiles(srcInitFolder);
            foreach (string file in files) {
                File.Copy(file, Path.Combine(srcFolder, Path.GetFileName(file)));
            }
            string[] dirs = Directory.GetDirectories(srcInitFolder);
            foreach (string dir in dirs) {
                CopyFiles(dir, Path.Combine(srcFolder, Path.GetFileName(dir)));
            }
        }
    }
}
