using Microsoft.Owin;
using Owin;
using AwsomeChat;
using Microsoft.AspNet.SignalR;
using System;

[assembly: OwinStartup(typeof(AwsomeChat.Startup))]
namespace AwsomeChat
{ 
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.MapSignalR();
            app.MapSignalR("/signalr", new HubConfiguration() { EnableDetailedErrors = true });
        }
    }

    public class AwsomeChatHub : Hub
    {
        public void NewEvent(string data)
        {
            Clients.All.GetLastEvent(data);
        }
    }    
} 