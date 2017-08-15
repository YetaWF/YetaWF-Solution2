/* Copyright © 2017 Softel vdm, Inc. - http://yetawf.com/Documentation/YetaWF/Licensing */

#if MVC6

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
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
using YetaWF.Core;
using YetaWF.Core.Controllers;
using YetaWF.Core.Extensions;
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
using YetaWF2.Support.ViewEngine;

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
                .AddJsonFile(Path.Combine(Globals.DataFolder, AppSettingsFile), optional: false, reloadOnChange: true)
#if DEBUG
                .AddJsonFile(Path.Combine(Globals.DataFolder, AppSettingsFileDebug), optional: true)
#endif
                ;
            //builder.AddEnvironmentVariables(); // not used
            Configuration = builder.Build();

            YetaWFManager.RootFolder = env.WebRootPath;
            YetaWFManager.RootFolderWebProject = env.ContentRootPath;

            WebConfigHelper.Init(Path.Combine(YetaWFManager.RootFolderWebProject, Globals.DataFolder, AppSettingsFile));
            LanguageSection.Init(Path.Combine(YetaWFManager.RootFolderWebProject, Globals.DataFolder, LanguageSettingsFile));
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services) {

            Services = services;
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IViewRenderService, ViewRenderService>();

            services.AddResponseCompression();

            // set antiforgery cookie
            services.AddAntiforgery(opts => opts.Cookie.Name = "__ReqVerToken_" + YetaWFManager.DefaultSiteName);
            // antiforgery filter for conditional antiforgery attribute
            services.AddSingleton<ConditionalAntiForgeryTokenFilter>();

            // We need our own view engine so we have more control over initialization/termination
            services.AddSingleton<IRazorViewEngine, YetaWFRazorViewEngine>();

            // We replace the ApplicationPartManager so we can inject assemblies
            services.AddSingleton<ApplicationPartManager>(new YetaWFApplicationPartManager());

            // We need to handle ModelDirective to derive the Model type (otherwise we'll end up with "dynamic") for all our views
            // This is most likely going to change as we're using an unreleased version of MVC
            // Such a hack... TODO: Research if there is a cleaner way in ModelDirective
            services.AddSingleton(typeof(RazorEngine), s => {
                return RazorEngine.Create(builder => {
                    YetaWFInjectDirective.Register(builder);//<<<
                    YetaWFModelDirective.Register(builder);//<<<
                    NamespaceDirective.Register(builder);
                    PageDirective.Register(builder);

                    FunctionsDirective.Register(builder);
                    InheritsDirective.Register(builder);
                    SectionDirective.Register(builder);

                    builder.AddTargetExtension(new TemplateTargetExtension()
                    {
                        TemplateTypeName = "global::Microsoft.AspNetCore.Mvc.Razor.HelperResult",
                    });

                    builder.Features.Add(new ModelExpressionPass());
                    builder.Features.Add(new PagesPropertyInjectionPass());
                    builder.Features.Add(new ViewComponentTagHelperPass());
                    builder.Features.Add(new RazorPageDocumentClassifierPass());
                    builder.Features.Add(new MvcViewDocumentClassifierPass());
                    builder.Features.Add(new AssemblyAttributeInjectionPass());

                    if (!builder.DesignTime) {
                        builder.Features.Add(new InstrumentationPass());
                    }
                });
            });

            services.AddMemoryCache();

            // Memory or distributed caching
            string sqlConn =  WebConfigHelper.GetValue<string>("SessionState", "SqlCache-Connection", null, Package: false);
            string sqlSchema =  WebConfigHelper.GetValue<string>("SessionState", "SqlCache-Schema", null, Package: false);
            string sqlTable =  WebConfigHelper.GetValue<string>("SessionState", "SqlCache-Table", null, Package: false);
            if (string.IsNullOrWhiteSpace(sqlConn) || string.IsNullOrWhiteSpace(sqlSchema) || string.IsNullOrWhiteSpace(sqlTable)) {
                services.AddDistributedMemoryCache();
            } else {
                // Create sql table (in .\src folder): dotnet sql-cache create "Data Source=...;Initial Catalog=...;Integrated Security=True;" dbo SessionCache
                // to use distributed sql server cache
                services.AddDistributedSqlServerCache(options => {
                    options.ConnectionString = sqlConn;
                    options.SchemaName = sqlSchema;
                    options.TableName = sqlTable;
                });
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

            ServiceProvider = svp;

            IHttpContextAccessor httpContextAccessor = (IHttpContextAccessor)ServiceProvider.GetService(typeof(IHttpContextAccessor));
            IMemoryCache memoryCache = (IMemoryCache)ServiceProvider.GetService(typeof(IMemoryCache));
            YetaWFManager.Init(ServiceProvider, httpContextAccessor, memoryCache);

            Logging.SetupLogging();

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
                    bool match = path.EndsWith(".css");
                    if (match)
                        StartRequest(context);
                    return match;
                },
                appBranch => {
                    appBranch.UseMiddleware<CssMiddleware>();
                });

            // Image Handler
            app.MapWhen(
                context => {
                    string path = context.Request.Path.ToString().ToLower();
                    bool match = path == "/filehndlr.image" || path == "/file.image";
                    if (match)
                        StartRequest(context);
                    return match;
                },
                appBranch => {
                    appBranch.UseMiddleware<ImageMiddleware>();
                });

            // Set up custom content types for static files based on MimeSettings.json
            {
                FileExtensionContentTypeProvider provider = new FileExtensionContentTypeProvider();
                MimeSection staticMimeSect = new MimeSection();
                staticMimeSect.Init(Path.Combine(Globals.DataFolder, MimeSection.MimeSettingsFile));
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
                    RequestPath = new PathString("/node_modules"),
                    OnPrepareResponse = (context) => {
                        YetaWFManager.SetStaticCacheInfo(context.Context.Response);
                    }
                });
                app.UseStaticFiles(new StaticFileOptions {
                    FileProvider = new PhysicalFileProvider(Path.Combine(YetaWFManager.RootFolderWebProject, @"bower_components")),
                    RequestPath = new PathString("/bower_components"),
                    OnPrepareResponse = (context) => {
                        YetaWFManager.SetStaticCacheInfo(context.Context.Response);
                    }
                });
                app.UseStaticFiles(new StaticFileOptions {
                    ContentTypeProvider = provider,
                    OnPrepareResponse = (context) => {
                        YetaWFManager.SetStaticCacheInfo(context.Context.Response);
                    }
                });
            }

            // Everything else
            app.Use(async (context, next) => {
                StartRequest(context);
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

        private static object lockObject = new object();

        public void StartYetaWF() {

            if (!YetaWF.Core.Support.Startup.Started) {

                lock (lockObject) {

                    if (!YetaWF.Core.Support.Startup.Started) {

                        Logging.AddLog("StartYetaWF starting");

                        YetaWFManager manager = YetaWFManager.MakeInitialThreadInstance(new SiteDefinition() { SiteDomain = "__STARTUP" }); // while loading packages we need a manager

                        // Call all classes that expose the interface IInitializeApplicationStartup
                        YetaWF.Core.Support.Startup.CallStartupClasses();

                        Package.UpgradeToNewPackages();

                        YetaWF.Core.Support.Startup.Started = true;

                        YetaWFManager.RemoveThreadInstance(); // Remove startup manager

                        Logging.AddLog("StartYetaWF completed");
                    }
                }
            }
        }

        public void StartRequest(HttpContext httpContext) {

            HttpRequest httpReq = httpContext.Request;

            // Determine which Site folder to use based on URL provided
            bool forcedHost = false, newSwitch = false;
            string host = httpReq.Query[Globals.Link_ForceSite];
            if (!string.IsNullOrWhiteSpace(host)) {
                newSwitch = true;
                YetaWFManager.SetRequestedDomain(host);
            }
            if (string.IsNullOrWhiteSpace(host) && httpContext.Session != null)
                host = httpContext.Session.GetString(Globals.Link_ForceSite);
            if (string.IsNullOrWhiteSpace(host))
                host = httpReq.Host.Host;
            else
                forcedHost = true;
            // beautify the host name a bit
            if (host.Length > 1)
                host = char.ToUpper(host[0]) + host.Substring(1).ToLower();
            else
                host = host.ToUpper();

            string host2 = null;

            // check if such a site definition exists (accounting for www. or other subdomain)
            string[] domParts = host.Split(new char[] { '.' });
            if (domParts.Length > 2) {
                if (domParts.Length > 3 || domParts[0] != "www")
                    host2 = host;
                host = string.Join(".", domParts, domParts.Length - 2, 2);// get just domain as a fallback
            }
            SiteDefinition site = null;
            if (!string.IsNullOrWhiteSpace(host2)) {
                site = SiteDefinition.LoadSiteDefinition(host2);
                if (site != null)
                    host = host2;
            }
            if (site == null) {
                site = SiteDefinition.LoadSiteDefinition(host);
                if (site == null) {
                    if (forcedHost) { // non-existent site requested
                        Logging.AddErrorLog("404 Not Found");
                        httpContext.Response.StatusCode = 404;
                        return;
                    }
                    site = SiteDefinition.LoadSiteDefinition(null);
                    if (site == null) {
                        if (SiteDefinition.INITIAL_INSTALL) {
                            // use a skeleton site for initial install
                            // this will be updated when the model is installed
                            site = new SiteDefinition {
                                Identity = SiteDefinition.SiteIdentitySeed,
                                SiteDomain = host,
                            };
                        } else {
                            throw new InternalError("Couldn't obtain a SiteDefinition object");
                        }
                    }
                }
            }
            // We have a valid request for a known domain or the default domain
            // create a YetaWFManager object to keep track of everything (it serves
            // as a global anchor for everything we need to know while processing this request)
            YetaWFManager manager = YetaWFManager.MakeInstance(httpContext, host);
            // Site properties are ONLY valid AFTER this call to YetaWFManager.MakeInstance

            manager.CurrentSite = site;

            manager.HostUsed = httpReq.Host.Host;
            manager.HostPortUsed = httpReq.Host.Port ?? 80;
            manager.HostSchemeUsed = httpReq.Scheme;

            Uri uri = new Uri(UriHelper.GetDisplayUrl(httpReq));
            if (forcedHost && newSwitch) {
                if (!manager.HasSuperUserRole) { // if superuser, don't log off (we could be creating a new site)
                    // A somewhat naive way to log a user off, but it's working well and also handles 3rd party logins correctly.
                    // Since this is only used during site development, it's not critical
                    string logoffUrl = WebConfigHelper.GetValue<string>("MvcApplication", "LogoffUrl", null, Package:false);
                    if (string.IsNullOrWhiteSpace(logoffUrl))
                        throw new InternalError("MvcApplication LogoffUrl not defined in web.cofig/appsettings.json - this is required to switch between sites so we can log off the site-specific currently logged in user");
                    Uri newUri;
                    if (uri.IsLoopback) {
                        // add where we need to go next (w/o the forced domain, we're already on this domain (localhost))
                        newUri = RemoveQsKeyFromUri(uri, httpReq.Query, Globals.Link_ForceSite);
                    } else {
                        newUri = new Uri("http://" + host);// new site to display
                    }
                    logoffUrl += YetaWFManager.UrlEncodeArgs(newUri.ToString());
                    logoffUrl += (logoffUrl.Contains("?") ? "&" : "?") + "ResetForcedDomain=false";
                    Logging.AddLog("302 Found - {0}", logoffUrl).Truncate(100);
                    httpContext.Response.StatusCode = 302;
                    httpContext.Response.Headers.Add("Location", manager.CurrentSite.MakeUrl(logoffUrl));
                    return;
                }
            }
            // Make sure we're using the "official" URL, otherwise redirect 301
            if (site.EnforceSiteUrl) {
                if (uri.IsAbsoluteUri) {
                    if (!manager.IsLocalHost && !forcedHost && string.Compare(manager.HostUsed, site.SiteDomain, true) != 0) {
                        UriBuilder newUrl = new UriBuilder(uri);
                        newUrl.Host = site.SiteDomain;
                        Logging.AddLog("301 Moved Permanently - {0}", newUrl.ToString()).Truncate(100);
                        httpContext.Response.StatusCode = 301;
                        httpContext.Response.Headers.Add("Location", newUrl.ToString());
                        return;
                    }
                }
            }
            // IE rejects our querystrings that have encoded "?" (%3D) even though that's completely valid
            // so we have to turn of XSS protection (which is not necessary in YetaWF anyway)
            httpContext.Response.Headers.Add("X-Xss-Protection", "0");
        }

        private Uri RemoveQsKeyFromUri(Uri uri, IQueryCollection queryColl, string qsKey) {
            UriBuilder newUri = new UriBuilder(uri);
            QueryHelper query = QueryHelper.FromQueryCollection(queryColl);
            query.Remove(qsKey);
            newUri.Query = query.ToQueryString();
            return newUri.Uri;
        }
    }
}
#else
#endif
