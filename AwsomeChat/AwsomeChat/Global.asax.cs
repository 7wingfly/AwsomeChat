using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Microsoft.ServiceBus.Messaging;

namespace AwsomeChat
{
    public class MvcApplication : System.Web.HttpApplication
    {       
        public static string[] RecentEvents = new string[20];

        static string eventHubConnectionString = "Endpoint=sb://awsomechat.servicebus.windows.net/;...";
        static string eventHubName = "awsomechat";
        static string storageAccountName = "awsomechat";
        static string storageAccountKey = "...";
        static string storageConnectionString = string.Format("DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1}", storageAccountName, storageAccountKey);

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            string eventProcessorHostName = Guid.NewGuid().ToString();
            EventProcessorHost eventProcessorHost = new EventProcessorHost(eventProcessorHostName, eventHubName, EventHubConsumerGroup.DefaultGroupName, eventHubConnectionString, storageConnectionString);
            Console.WriteLine("Registering EventProcessor...");
            var options = new EventProcessorOptions();
            options.ExceptionReceived += (sender, e) => { Console.WriteLine(e.Exception); };
            eventProcessorHost.RegisterEventProcessorAsync<SimpleEventProcessor>(options).Wait();
        }
    }
}
