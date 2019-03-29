﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.Common;
using Sample.Service.Interfaces;
using Uragano.Abstractions;
using Uragano.Caching.Memory;
using Uragano.Caching.Redis;
using Uragano.Codec.MessagePack;
using Uragano.Consul;
using Uragano.Core;
using Uragano.Logging.Exceptionless;
using Uragano.Logging.Log4Net;
using Uragano.Logging.NLog;
using Uragano.Remoting.LoadBalancing;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;

namespace Sample.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);
            services.AddUragano(Configuration, builder =>
             {

                 builder.AddClient(LoadBalancing.Polling);
                 builder.AddServer();

                 builder.AddConsul();
                 builder.AddClientGlobalInterceptor<ClientGlobalInterceptor>();
                 builder.AddServerGlobalInterceptor<ServerGlobalInterceptor>();
                 builder.AddExceptionlessLogger();
                 //builder.AddLog4NetLogger();
                 //builder.AddNLogLogger();
                 builder.AddRedisPartitionCaching();
                 //builder.AddRedisCaching();
                 //builder.AddMemoryCaching();
                 builder.AddOption(UraganoOptions.Remoting_Invoke_CancellationTokenSource_Timeout, TimeSpan.FromSeconds(10));
                 builder.AddOptions();
             });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseMvc();
        }
    }
}
