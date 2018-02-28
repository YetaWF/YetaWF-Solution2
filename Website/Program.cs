﻿/* Copyright © 2018 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

namespace YetaWF.App_Start {

    public class Program {
        public static void Main(string[] args) {

            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("hosting.json", optional: false)
#if DEBUG
                .AddJsonFile("hosting.Debug.json", optional: true)
#endif
                .Build();

            IWebHost host = new WebHostBuilder()
                .UseConfiguration(config)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureLogging((hostingContext, logging) => {
                    logging.AddConsole();
#if DEBUG
                    //logging.AddDebug();
#endif
                    //https://github.com/aspnet/Announcements/issues/241
                    //https://github.com/aspnet/Hosting/issues/1069
                    //$$logging.UseConfiguration(hostingContext.Configuration.GetSection("Logging"));
                })
                .UseKestrel()
                .UseIISIntegration()
                .UseStartup<Startup6>()
                .Build();

            host.Run();
        }
    }
}
