/* Copyright © 2018 Softel vdm, Inc. - https://yetawf.com/Documentation/YetaWF/Licensing */

#if MVC6

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using YetaWF.Core;
using YetaWF.Core.Controllers;
using YetaWF.Core.DataProvider;
using YetaWF.Core.HttpHandler;
using YetaWF.Core.Identity;
using YetaWF.Core.Language;
using YetaWF.Core.Log;
using YetaWF.Core.Models.Attributes;
using YetaWF.Core.Packages;
using YetaWF.Core.Pages;
using YetaWF.Core.Site;
using YetaWF.Core.Support;
using YetaWF.Core.Views;
using YetaWF2.Middleware;
using YetaWF2.Support;

namespace YetaWF.App_Start {

    public class Startup6 {

        private IConfigurationRoot Configuration { get; }
        private IServiceProvider ServiceProvider = null;
        private IServiceCollection Services = null;

        private const string AppSettingsFile = "appsettings.json";
        private const string AppSettingsFileDebug = "appsettings.Debug.json";
        private const string LanguageSettingsFile = "LanguageSettings.json";

        public Startup6(IHostingEnvironment env, ILoggerFactory loggerFactory) {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile(Path.Combine(Globals.DataFolder, AppSettingsFile), optional: false, reloadOnChange: false)
#if DEBUG
                .AddJsonFile(Path.Combine(Globals.DataFolder, AppSettingsFileDebug), optional: true, reloadOnChange: false)
#endif
                ;
            //builder.AddEnvironmentVariables(); // not used
            Configuration = builder.Build();

            YetaWFManager.RootFolder = env.WebRootPath;
            YetaWFManager.RootFolderWebProject = env.ContentRootPath;

            WebConfigHelper.InitAsync(Path.Combine(YetaWFManager.RootFolderWebProject, Globals.DataFolder, AppSettingsFile)).Wait();
            LanguageSection.InitAsync(Path.Combine(YetaWFManager.RootFolderWebProject, Globals.DataFolder, LanguageSettingsFile)).Wait();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services) {

            Services = services;
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IViewRenderService, ViewRenderService>();

            services.AddResponseCompression();

            //TODO: Signalr.ConfigureServices(services);

            services.Configure<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new DataProtectionKeyRepository();
            });

            //https://stackoverflow.com/questions/43860631/how-do-i-handle-validateantiforgerytoken-across-linux-servers
            //https://nicolas.guelpa.me/blog/2017/01/11/dotnet-core-data-protection-keys-repository.html
            //https://long2know.com/2017/06/net-core-sql-dataprotection-key-storage-provider-using-entity-framework/
            var encryptionSettings = new AuthenticatedEncryptorConfiguration() {
                EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
                ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
            };
            services
                .AddDataProtection()
                .PersistKeysToAppSettings()
                .SetDefaultKeyLifetime(new TimeSpan(100*365, 0, 0, 0))
                .SetApplicationName("__YetaWFDP_" + YetaWFManager.DefaultSiteName)
                .UseCryptographicAlgorithms(encryptionSettings);

            // set antiforgery cookie
            services.AddAntiforgery(opts => {
                opts.Cookie.Name = "__ReqVerToken_" + YetaWFManager.DefaultSiteName;
                opts.SuppressXFrameOptionsHeader = true;
            });
            // antiforgery filter for conditional antiforgery attribute
            services.AddSingleton<ConditionalAntiForgeryTokenFilter>();

            // We replace the ApplicationPartManager so we can inject assemblies
            services.AddSingleton<ApplicationPartManager>(new YetaWFApplicationPartManager());

            services.AddMemoryCache();

            // Memory or distributed caching
            string distProvider = WebConfigHelper.GetValue<string>("SessionState", "Provider", "", Package: false).ToLower();
            if (distProvider == "redis") {
                string config = WebConfigHelper.GetValue<string>("SessionState", "RedisConfig", "localhost:6379", Package: false);
                services.AddDistributedRedisCache(o => {
                    o.Configuration = config;
                });
            } else if (distProvider == "sql") {
                string sqlConn = WebConfigHelper.GetValue<string>("SessionState", "SqlConnection", null, Package: false);
                string sqlSchema = WebConfigHelper.GetValue<string>("SessionState", "SqlSchema", null, Package: false);
                string sqlTable = WebConfigHelper.GetValue<string>("SessionState", "SqlTable", null, Package: false);
                if (string.IsNullOrWhiteSpace(sqlConn) || string.IsNullOrWhiteSpace(sqlSchema) || string.IsNullOrWhiteSpace(sqlTable)) {
                    services.AddDistributedMemoryCache();
                } else {
                    // Create sql table (in .\src folder): dotnet sql-cache create "Data Source=...;Initial Catalog=...;Integrated Security=True;" dbo SessionCache
                    // to use distributed sql server cache
                    // MAKE SURE TO CHANGE \\ TO \ WHEN COPYING THE CONNECTION STRING!!!
                    services.AddDistributedSqlServerCache(options => {
                        options.ConnectionString = sqlConn;
                        options.SchemaName = sqlSchema;
                        options.TableName = sqlTable;
                    });
                }
            } else {
                services.AddDistributedMemoryCache();
            }

            // Session state
            int sessionTimeout = WebConfigHelper.GetValue<int>("SessionState", "Timeout", 1440, Package: false);
            string sessionCookie = WebConfigHelper.GetValue<string>("SessionState", "CookieName", ".YetaWF.Session", Package: false);
            services.AddSession(options => {
                options.IdleTimeout = TimeSpan.FromMinutes(sessionTimeout);
                options.Cookie.Name = sessionCookie;
            });

            services.AddSingleton<IAuthorizationHandler, ResourceAuthorizeHandler>();
            services.AddAuthorization(options => {
                options.AddPolicy("ResourceAuthorize", policyBuilder => {
                    policyBuilder.Requirements.Add(new ResourceAuthorizeRequirement());
                });
            });

            // Find all the views/areas that are available to the website (i.e., core + modules)
            ViewEnginesStartup.Start(Services);

            // load assembly and initialize identity services
            IdentityCreator.Setup(services);
            IdentityCreator.SetupLoginProviders(services);

            // We need to replace the default ApplicationModelProvider so we can provide area information (without the need for [Area] attributes)
            services.AddTransient(typeof(IApplicationModelProvider), typeof(YetaWFApplicationModelProvider));

            // We need to replace the default Html Generator because it adds id= to every tag despite not explicitly requested, which is dumb and can cause duplicate ids (not
            // permitted  in w3c validation). Why would MVC6 start adding ids to tags when they're not requested. If they're not requested, does the caller really need or use them???
            services.AddSingleton(typeof(IHtmlGenerator), typeof(YetaWFDefaultHtmlGenerator));

            // Add framework services.
            services.AddMvc((options) => {
                // we have to remove the SaveTempDataAttribute filter, otherwise our ActionHelper.Action extension
                // doesn't work as the filter sets httpContext.Items[someobject] to signal that the action has completed.
                // obviously this doesn't work if there are multiple actions (which there always are).
                // YetaWF doesn't use tempdata so this is useless anyway. And this SaveTempDataAttribute seems borked...
                options.Filters.Remove(new Microsoft.AspNetCore.Mvc.ViewFeatures.SaveTempDataAttribute());
                // We need to roll our own support for AdditionalMetadataAttribute, IMetadataAware
                options.ModelMetadataDetailsProviders.Add(new AdditionalMetadataProvider());

                // Error handling for controllers, not used, we handle action errors instead so this is not needed
                // options.Filters.Add(new ControllerExceptionFilterAttribute()); // controller exception filter, not used
            });
            // We need our own view engine so we have more control over initialization/termination
            services.Configure<MvcViewOptions>(options => { });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider svp) {
            ConfigureAsync(app, env, svp).Wait(); // sync Wait because we want to be async in Configure()/ConfigureAsync()
        }
        public async Task ConfigureAsync(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider svp) {

            ServiceProvider = svp;

            IHttpContextAccessor httpContextAccessor = (IHttpContextAccessor)ServiceProvider.GetService(typeof(IHttpContextAccessor));
            IMemoryCache memoryCache = (IMemoryCache)ServiceProvider.GetService(typeof(IMemoryCache));
            YetaWFManager.Init(ServiceProvider, httpContextAccessor, memoryCache);

            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
                //app.UseBrowserLink();
            }

            RewriteOptions rewriteOptions = new RewriteOptions();
            if (File.Exists("web.config"))
                rewriteOptions.AddIISUrlRewrite(env.ContentRootFileProvider, "web.config");
            if (File.Exists(".htaccess"))
                rewriteOptions.AddApacheModRewrite(env.ContentRootFileProvider, ".htaccess");

            app.UseResponseCompression();

            app.UseSession();

            // Error handler for ajax/post exceptions - returns special text with errors for display client side
            // This must appear after more generic error handlers (like UseDeveloperExceptionPage)
            app.UseMiddleware(typeof(ErrorHandlingMiddleware));
            // Ignore extensions that are known not to be valid files
            app.UseMiddleware<IgnoreRouteMiddleware>();

            // Css Handler
            app.MapWhen(
                context => {
                    string path = context.Request.Path.ToString().ToLower();
                    return path.EndsWith(".css");
                },
                appBranch => {
                    appBranch.UseMiddleware<CssMiddleware>();
                });

            // Image Handler
            app.MapWhen(
                context => {
                    string path = context.Request.Path.ToString().ToLower();
                    return path == "/filehndlr.image";
                },
                appBranch => {
                    appBranch.UseMiddleware<ImageMiddleware>();
                });

            // Set up custom content types for static files based on MimeSettings.json
            {
                FileExtensionContentTypeProvider provider = new FileExtensionContentTypeProvider();
                MimeSection staticMimeSect = new MimeSection();
                await staticMimeSect.InitAsync(Path.Combine(Globals.DataFolder, MimeSection.MimeSettingsFile));
                List<MimeSection.MimeEntry> mimeTypes = staticMimeSect.GetMimeTypes();
                if (mimeTypes != null) {
                    foreach (MimeSection.MimeEntry entry in mimeTypes) {
                        string[] extensions = entry.Extensions.Split(new char[] { ';' });
                        foreach (string extension in extensions) {
                            if (!provider.Mappings.ContainsKey(extension.Trim()))
                                provider.Mappings.Add(extension.Trim(), entry.Type);
                        }
                    }
                }
                app.UseStaticFiles(new StaticFileOptions {
                    FileProvider = new PhysicalFileProvider(Path.Combine(YetaWFManager.RootFolderWebProject, @"node_modules")),
                    RequestPath = new PathString("/" + Globals.NodeModulesFolder),
                    OnPrepareResponse = (context) => {
                        YetaWFManager.SetStaticCacheInfo(context.Context);
                    }
                });
                app.UseStaticFiles(new StaticFileOptions {
                    FileProvider = new PhysicalFileProvider(Path.Combine(YetaWFManager.RootFolderWebProject, @"bower_components")),
                    RequestPath = new PathString("/" + Globals.BowerComponentsFolder),
                    OnPrepareResponse = (context) => {
                        YetaWFManager.SetStaticCacheInfo(context.Context);
                    }
                });
                app.UseStaticFiles(new StaticFileOptions {
                    ContentTypeProvider = provider,
                    OnPrepareResponse = (context) => {
                        YetaWFManager.SetStaticCacheInfo(context.Context);
                    }
                });
            }

            //TODO: Signalr.ConfigureHubs(app);

            // Everything else
            app.Use(async (context, next) => {
                await StartupRequest.StartRequestAsync(context, false);
                await next();
            });

            app.UseAuthentication();

            app.UseMvc(routes => {
                Logging.AddLog("Calling AreaRegistration.RegisterPackages()");
                AreaRegistrationBase.RegisterPackages(routes);

                Logging.AddLog("Adding catchall route");
                routes.MapRoute(name: "Page", template: "{*path}", defaults: new { controller = "Page", action = "Show" });
            });

            StartYetaWF();
        }

        private static AsyncLock _lockObject = new AsyncLock();

        public void StartYetaWF() {

            if (!YetaWF.Core.Support.Startup.Started) {

                using (_lockObject.Lock()) { // protect from duplicate startup

                    if (!YetaWF.Core.Support.Startup.Started) {

                        YetaWFManager.Syncify(async () => { // startup code

                            // Create a startup log file
                            StartupLogging startupLog = new StartupLogging();
                            await Logging.RegisterLoggingAsync(startupLog);

                            Logging.AddLog("StartYetaWF starting");

                            YetaWFManager manager = YetaWFManager.MakeInitialThreadInstance(new SiteDefinition() { SiteDomain = "__STARTUP" }); // while loading packages we need a manager
                            YetaWFManager.Syncify(async () => {
                                // External data providers
                                ExternalDataProviders.RegisterExternalDataProviders();
                                // Call all classes that expose the interface IInitializeApplicationStartup
                                await YetaWF.Core.Support.Startup.CallStartupClassesAsync();

                                if (!YetaWF.Core.Support.Startup.MultiInstance)
                                    await Package.UpgradeToNewPackagesAsync();

                                YetaWF.Core.Support.Startup.Started = true;
                            });

                            // Stop startup log file
                            Logging.UnregisterLogging(startupLog);

                            // start real logging
                            await Logging.SetupLoggingAsync();

                            YetaWFManager.RemoveThreadInstance(); // Remove startup manager

                            Logging.AddLog("StartYetaWF completed");
                        });
                    }
                }
            }
        }
    }
}
#else
#endif
