using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.ServiceBus.Messaging;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;
using Microsoft.AspNet.SignalR;

namespace AwsomeChat
{
    public class SimpleEventProcessor : IEventProcessor
    {
        Stopwatch checkpointStopWatch;

        async Task IEventProcessor.CloseAsync(PartitionContext context, CloseReason reason)
        {
            Console.WriteLine("Processor Shutting Down. Partition '{0}', Reason: '{1}'.", context.Lease.PartitionId, reason);
            if (reason == CloseReason.Shutdown)
            {
                await context.CheckpointAsync();
            }
        }

        Task IEventProcessor.OpenAsync(PartitionContext context)
        {
            Console.WriteLine("SimpleEventProcessor initialized.  Partition: '{0}', Offset: '{1}'", context.Lease.PartitionId, context.Lease.Offset);
            this.checkpointStopWatch = new Stopwatch();
            this.checkpointStopWatch.Start();
            return Task.FromResult<object>(null);
        }

        async Task IEventProcessor.ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
        {
            foreach (EventData eventData in messages)
            {
                string data = Encoding.UTF8.GetString(eventData.GetBytes());

                if (data != null && data != string.Empty)
                {
                    var hubContext = GlobalHost.ConnectionManager.GetHubContext<AwsomeChatHub>();
                    hubContext.Clients.All.NewEvent(data);

                    string[] Last5Messages_temp = new string[20];

                    Array.Copy(MvcApplication.RecentEvents, 1, Last5Messages_temp, 0, MvcApplication.RecentEvents.Length - 1);
                    Last5Messages_temp[19] = data;
                    MvcApplication.RecentEvents = Last5Messages_temp;
                }
            }

            //Call checkpoint every 5 minutes, so that worker can resume processing from 5 minutes back if it restarts.
            if (this.checkpointStopWatch.Elapsed > TimeSpan.FromMinutes(5))
            {
                await context.CheckpointAsync();
                this.checkpointStopWatch.Restart();
            }
        }
    }
}