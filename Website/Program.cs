/* Copyright © 2018 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using YetaWF2.Logger;

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
                    //$$logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.ClearProviders();

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
    }
}
