﻿#if TESTING
using System;
using Microsoft.AspNet.Authentication.OAuth;
using Microsoft.AspNet.Authentication.Twitter;
using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Diagnostics.Entity;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Data.Entity;
using Microsoft.Framework.Caching.Memory;
using Microsoft.Framework.Configuration;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.Logging;
using Microsoft.Dnx.Runtime;
using MusicStore.Components;
using MusicStore.Mocks.Common;
using MusicStore.Mocks.Facebook;
using MusicStore.Mocks.Google;
using MusicStore.Mocks.MicrosoftAccount;
using MusicStore.Mocks.Twitter;
using MusicStore.Models;

namespace MusicStore
{
    public class StartupSocialTesting
    {
        private readonly IRuntimeEnvironment _runtimeEnvironment;

        public StartupSocialTesting(IApplicationEnvironment appEnvironment, IRuntimeEnvironment runtimeEnvironment)
        {
            //Below code demonstrates usage of multiple configuration sources. For instance a setting say 'setting1' is found in both the registered sources, 
            //then the later source will win. By this way a Local config can be overridden by a different setting while deployed remotely.
            var builder = new ConfigurationBuilder(appEnvironment.ApplicationBasePath)
                        .AddJsonFile("config.json")
                        .AddEnvironmentVariables() //All environment variables in the process's context flow in as configuration values.
                        .AddJsonFile("configoverride.json", optional: true); // Used to override some configuration parameters that cannot be overridden by environment.

            Configuration = builder.Build();
            _runtimeEnvironment = runtimeEnvironment;
        }

        public IConfiguration Configuration { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            //Sql client not available on mono
            var useInMemoryStore = Configuration["UseInMemoryStore"] == "true" ?
                true :
                _runtimeEnvironment.RuntimeType.Equals("Mono", StringComparison.OrdinalIgnoreCase);

            // Add EF services to the services container
            if (useInMemoryStore)
            {
                services.AddEntityFramework()
                        .AddInMemoryDatabase()
                        .AddDbContext<MusicStoreContext>(options =>
                            options.UseInMemoryDatabase());
            }
            else
            {
                services.AddEntityFramework()
                        .AddSqlServer()
                        .AddDbContext<MusicStoreContext>(options =>
                            options.UseSqlServer(Configuration["Data:DefaultConnection:ConnectionString"]));
            }

            // Add Identity services to the services container
            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
                    {
                        options.Cookies.ApplicationCookie.AccessDeniedPath = new PathString("/Home/AccessDenied");
                    })
                    .AddEntityFrameworkStores<MusicStoreContext>()
                    .AddDefaultTokenProviders();

            services.AddFacebookAuthentication(options =>
            {
                options.AppId = "[AppId]";
                options.AppSecret = "[AppSecret]";
                options.Events = new OAuthEvents()
                {
                    OnAuthenticated = TestFacebookEvents.OnAuthenticated,
                    OnReturnEndpoint = TestFacebookEvents.OnReturnEndpoint,
                    OnApplyRedirect = TestFacebookEvents.OnApplyRedirect
                };
                options.BackchannelHttpHandler = new FacebookMockBackChannelHttpHandler();
                options.StateDataFormat = new CustomStateDataFormat();
                options.Scope.Add("email");
                options.Scope.Add("read_friendlists");
                options.Scope.Add("user_checkins");
            });

            services.AddGoogleAuthentication(options =>
            {
                options.ClientId = "[ClientId]";
                options.ClientSecret = "[ClientSecret]";
                options.AccessType = "offline";
                options.Events = new OAuthEvents()
                {
                    OnAuthenticated = TestGoogleEvents.OnAuthenticated,
                    OnReturnEndpoint = TestGoogleEvents.OnReturnEndpoint,
                    OnApplyRedirect = TestGoogleEvents.OnApplyRedirect
                };
                options.StateDataFormat = new CustomStateDataFormat();
                options.BackchannelHttpHandler = new GoogleMockBackChannelHttpHandler();
            });

            services.AddTwitterAuthentication(options =>
            {
                options.ConsumerKey = "[ConsumerKey]";
                options.ConsumerSecret = "[ConsumerSecret]";
                options.Events = new TwitterEvents()
                {
                    OnAuthenticated = TestTwitterEvents.OnAuthenticated,
                    OnReturnEndpoint = TestTwitterEvents.OnReturnEndpoint,
                    OnApplyRedirect = TestTwitterEvents.OnApplyRedirect
                };
                options.StateDataFormat = new CustomTwitterStateDataFormat();
                options.BackchannelHttpHandler = new TwitterMockBackChannelHttpHandler();
            });

            services.AddMicrosoftAccountAuthentication(options =>
            {
                options.Caption = "MicrosoftAccount - Requires project changes";
                options.ClientId = "[ClientId]";
                options.ClientSecret = "[ClientSecret]";
                options.Events = new OAuthEvents()
                {
                    OnAuthenticated = TestMicrosoftAccountEvents.OnAuthenticated,
                    OnReturnEndpoint = TestMicrosoftAccountEvents.OnReturnEndpoint,
                    OnApplyRedirect = TestMicrosoftAccountEvents.OnApplyRedirect
                };
                options.BackchannelHttpHandler = new MicrosoftAccountMockBackChannelHandler();
                options.StateDataFormat = new CustomStateDataFormat();
                options.Scope.Add("wl.basic");
                options.Scope.Add("wl.signin");
            });

            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", builder =>
                {
                    builder.WithOrigins("http://example.com");
                });
            });

            // Add MVC services to the services container
            services.AddMvc();

            //Add InMemoryCache
            services.AddSingleton<IMemoryCache, MemoryCache>();

            // Add session related services.
            services.AddCaching();
            services.AddSession();

            // Add the system clock service
            services.AddSingleton<ISystemClock, SystemClock>();

            // Configure Auth
            services.Configure<AuthorizationOptions>(options =>
            {
                options.AddPolicy("ManageStore", new AuthorizationPolicyBuilder().RequireClaim("ManageStore", "Allowed").Build());
            });
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(minLevel: LogLevel.Warning);

            app.UseStatusCodePagesWithRedirects("~/Home/StatusCodePage");

            // Error page middleware displays a nice formatted HTML page for any unhandled exceptions in the request pipeline.
            // Note: Not recommended for production.
            app.UseDeveloperExceptionPage();

            app.UseDatabaseErrorPage(DatabaseErrorPageOptions.ShowAll);

            // Add the runtime information page that can be used by developers
            // to see what packages are used by the application
            // default path is: /runtimeinfo
            app.UseRuntimeInfoPage();

            // Configure Session.
            app.UseSession();

            // Add static files to the request pipeline
            app.UseStaticFiles();

            // Add cookie-based authentication to the request pipeline
            app.UseIdentity();

            app.UseFacebookAuthentication();

            app.UseGoogleAuthentication();

            app.UseTwitterAuthentication();

            app.UseMicrosoftAccountAuthentication();

            // Add MVC to the request pipeline
            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "areaRoute",
                    template: "{area:exists}/{controller}/{action}",
                    defaults: new { action = "Index" });

                routes.MapRoute(
                    name: "default",
                    template: "{controller}/{action}/{id?}",
                    defaults: new { controller = "Home", action = "Index" });

                routes.MapRoute(
                    name: "api",
                    template: "{controller}/{id?}");
            });

            //Populates the MusicStore sample data
            SampleData.InitializeMusicStoreDatabaseAsync(app.ApplicationServices).Wait();
        }
    }
}
#endif