﻿using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Serilog;

using ZD.LangUtils;
using ZDO.CHSite.Logic;
using ZDO.CHSite.Entities;

namespace ZDO.CHSite
{
    public class Startup
    {
        private readonly IHostingEnvironment env;
        private readonly Mutation mut;
        private readonly ILoggerFactory loggerFactory;
        private readonly IConfigurationRoot config;

        public Startup(IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            this.env = env;
            this.loggerFactory = loggerFactory;

            // What am I today? HanDeDict or CHDICT?
            if (Environment.GetEnvironmentVariable("MUTATION") == "CHD") mut = Mutation.CHD;
            else if (Environment.GetEnvironmentVariable("MUTATION") == "HDD") mut = Mutation.HDD;
            else throw new Exception("Environment variable MUTATION missing value invalid. Supported: CHD, HDD.");
            // Now that we know our mutatio, init text provider singleton.
            TextProvider.Init(mut);

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.devenv.json", optional: true)
                .AddEnvironmentVariables();
            // Config specific to mutation and hostong environment
            string cfgFileName = null;
            if (env.IsProduction() && mut == Mutation.HDD) cfgFileName = "/etc/zdo/zdo-hdd-live/appsettings.json";
            if (env.IsStaging() && mut == Mutation.HDD) cfgFileName = "/etc/zdo/zdo-hdd-stage/appsettings.json";
            if (env.IsProduction() && mut == Mutation.CHD) cfgFileName = "/etc/zdo/zdo-chd-live/appsettings.json";
            if (env.IsStaging() && mut == Mutation.CHD) cfgFileName = "/etc/zdo/zdo-chd-stage/appsettings.json";
            if (cfgFileName != null && File.Exists(cfgFileName)) builder.AddJsonFile(cfgFileName, optional: false);
            config = builder.Build();

            // If running in production or staging, will log to file. Initialize Serilog here.
            if (!env.IsDevelopment())
            {
                var seriConf = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(config["logFileName"]);
                if (config["logLevel"] == "Trace") seriConf.MinimumLevel.Verbose();
                else if (config["logLevel"] == "Debug") seriConf.MinimumLevel.Debug();
                else if (config["logLevel"] == "Information") seriConf.MinimumLevel.Information();
                else if (config["logLevel"] == "Warning") seriConf.MinimumLevel.Warning();
                else if (config["logLevel"] == "Error") seriConf.MinimumLevel.Error();
                else seriConf.MinimumLevel.Fatal();
                Log.Logger = seriConf.CreateLogger();
            }

            // Log to console (debug) or file (otherwise).
            // Must do here, so log capture errors if singleton services throw at startup.
            LogLevel ll = LogLevel.Critical;
            if (config["logLevel"] == "Trace") ll = LogLevel.Trace;
            else if (config["logLevel"] == "Debug") ll = LogLevel.Debug;
            else if (config["logLevel"] == "Information") ll = LogLevel.Information;
            else if (config["logLevel"] == "Warning") ll = LogLevel.Warning;
            else if (config["logLevel"] == "Error") ll = LogLevel.Error;
            if (env.IsDevelopment()) loggerFactory.AddConsole(ll);
            else loggerFactory.AddSerilog();
        }

        private void initDB()
        {
            try
            {
                DB.Init(config["dbServer"], uint.Parse(config["dbPort"]), config["dbDatabase"], 
                    config["dbUserID"], config["dbPass"]);
            }
            catch (Exception ex)
            {
                // TO-DO: Log.
            }
        }

        public void ConfigureServices(IServiceCollection services)
        {
            // Init low-level DB singleton
            initDB();
            // Application-specific singletons.
            services.AddSingleton(new PageProvider(loggerFactory, env.IsDevelopment(), mut, config["baseUrl"]));
            services.AddSingleton(new LangRepo(Path.Combine(config["dataFolder"], "unihanzi.bin")));
            services.AddSingleton(new SqlDict(loggerFactory));
            // MVC for serving pages and REST
            services.AddMvc();
            // Configuration singleton
            services.AddSingleton<IConfiguration>(sp => { return config; });
        }

        public void Configure(IApplicationBuilder app, IApplicationLifetime appLife)
        {
            // Sign up to application shutdown so we can do proper cleanup
            appLife.ApplicationStopping.Register(onApplicationStopping);
            // Static file options: inject caching info for all static files.
            StaticFileOptions sfo = new StaticFileOptions
            {
                OnPrepareResponse = (context) =>
                {
                    // For all stuff coming from "/static/**", add directly to cache indefinitely.
                    if (context.Context.Request.Path.Value.StartsWith("/static/"))
                    {
                        context.Context.Response.Headers["Cache-Control"] = "private, max-age=31536000";
                        context.Context.Response.Headers["Expires"] = DateTime.UtcNow.AddYears(1).ToString("R");
                    }
                    // For everything coming from "/files/**", disable caching
                    else if (context.Context.Request.Path.Value.StartsWith("/files/"))
                    {
                        context.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
                        context.Context.Response.Headers["Pragma"] = "no-cache";
                        context.Context.Response.Headers["Expires"] = "0";
                    }
                }
            };
            // Static files (JS, CSS etc.) served directly.
            app.UseStaticFiles(sfo);
            // Serve our (single) .cshtml file, and serve API requests.
            app.UseMvc(routes =>
            {
                routes.MapRoute("api", "api/{controller}/{action}/{*paras}", new { paras = "" });
                routes.MapRoute("default", "{*paras}", new { controller = "Index", action = "Index", paras = "" });
            });
        }

        private void onApplicationStopping()
        {
        }
    }
}
