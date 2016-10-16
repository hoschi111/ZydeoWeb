﻿using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace ZDO.CHSite
{
    public class Program
    {
        public static void Main(string[] args)
        {
            TextProvider.Init();

            var host = new WebHostBuilder()
               .UseUrls("http://127.0.0.1:5002")
               .UseKestrel()
               .UseContentRoot(Directory.GetCurrentDirectory())
               .ConfigureLogging(x => { })
               .UseStartup<Startup>()
               .CaptureStartupErrors(true)
               .Build();
            host.Run();
        }
    }
}
