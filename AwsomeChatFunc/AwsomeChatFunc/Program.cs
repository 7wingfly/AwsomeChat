using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System;
using Microsoft.ServiceBus.Messaging;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AwsomeChatFunc
{
    public class Program
    {
        static string EventHubName = "AwsomeChat";
        static string connectionString = "Endpoint=sb://awsomechat.servicebus.windows.net/...";

        private static SqlConnection CreateSQLConnection()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = "awsomechat.database.windows.net";
            builder.UserID = "...";
            builder.Password = "...";
            builder.MultipleActiveResultSets = true;
            builder.InitialCatalog = "AwsomeChat";

            return new SqlConnection(builder.ConnectionString);
        }

        public static async Task<HttpResponseMessage> NewUser(HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"AwsomeChat Function App received a NewUser request. URI={req.RequestUri}");
            string Name = req.GetQueryNameValuePairs().FirstOrDefault(q => q.Key.ToLower() == "name").Value;
            dynamic data = await req.Content.ReadAsAsync<object>();
            Name = Name ?? data?.name;

            if (Name == null)
                return req.CreateResponse(HttpStatusCode.BadRequest, $"No name specified");

            using (SqlConnection sql = new SqlConnection(CreateSQLConnection().ConnectionString))
            {
                sql.Open();

                string ExistingName = string.Empty;

                using (SqlCommand command = new SqlCommand($"SELECT TOP 1 [Name] FROM [dbo].[Users] WHERE [Name] = '{Name}'", sql))
                    using (SqlDataReader reader = command.ExecuteReader())
                        while (reader.Read())
                            ExistingName = reader.GetString(0);

                if (ExistingName != Name)
                {
                    new SqlCommand($"INSERT INTO [dbo].[Users] ([Name], [LastMessage], [Status]) VALUES ('{Name}', '{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss")}', 'Online')", sql).ExecuteNonQuery();
                    sql.Close();

                    var eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, EventHubName);
                    eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeMessage("SYS", $"{Name} has joined the conversation.")))));                    
                    eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeCommand("ADDUSER", new string[]{Name})))));                    

                    return req.CreateResponse(HttpStatusCode.OK, $"{Name} has joined the conversation.");
                }
                else
                {
                    sql.Close();
                    return req.CreateResponse(HttpStatusCode.Conflict, $"Name {Name} already exists.");
                }                
            }  
        }

        public static async Task<HttpResponseMessage> DeleteUser(HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"AwsomeChat Function App received a DeleteUser request. URI={req.RequestUri}");
            string Name = req.GetQueryNameValuePairs().FirstOrDefault(q => q.Key.ToLower() == "name").Value;
            dynamic data = await req.Content.ReadAsAsync<object>();
            Name = Name ?? data?.name;

            if (Name == null)
                return req.CreateResponse(HttpStatusCode.BadRequest, $"No name specified");

            using (SqlConnection sql = new SqlConnection(CreateSQLConnection().ConnectionString))
            {
                sql.Open();

                int RowsAffected = new SqlCommand($"DELETE FROM [dbo].[Users] WHERE [Name] = '{Name}'", sql).ExecuteNonQuery();

                sql.Close();

                if (RowsAffected > 0)
                {
                    var eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, EventHubName);
                    eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeMessage("SYS", $"{Name} has left the conversation.")))));                    
                    eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeCommand("DELETEUSER", new string[] { Name })))));                    

                    return req.CreateResponse(HttpStatusCode.OK, $"User {Name} deleted.");
                }
                else
                {
                    sql.Close();
                    return req.CreateResponse(HttpStatusCode.NotFound, $"Name {Name} does not exist.");
                }
            }
        }

        public static HttpResponseMessage CheckLastMessageTimes(TimerInfo myTimer, TraceWriter log)
        {
            string UsersUpdated = string.Empty;
            string UsersDeleted = string.Empty;

            using (SqlConnection sql = new SqlConnection(CreateSQLConnection().ConnectionString))
            {
                sql.Open();

                using (SqlCommand command = new SqlCommand($"SELECT [Name],[LastMessage],[Status] FROM [dbo].[Users] WHERE [LastMessage] <= '{DateTime.Now.Subtract(new TimeSpan(0, 30, 0)).ToString("yyyy-MM-dd hh:mm:ss")}' AND [Status] = 'Away'", sql))
                using (SqlDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        string Name = reader.GetString(0);

                        new SqlCommand($"DELETE FROM [dbo].[Users] WHERE [Name] = '{Name}'", sql).ExecuteNonQuery();

                        var eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, EventHubName);

                        eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeMessage("SYS", $"{Name} is clearly not comming back and has been kicked off.")))));                        
                        eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeCommand("DELETEUSER", new string[] { Name })))));

                        UsersDeleted += Name + "\n";
                    }

                using (SqlCommand command = new SqlCommand($"SELECT [Name],[LastMessage],[Status] FROM [dbo].[Users] WHERE [LastMessage] <= '{DateTime.Now.Subtract(new TimeSpan(0, 5, 0)).ToString("yyyy-MM-dd hh:mm:ss")}' AND [Status] = 'Online'", sql))
                using (SqlDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        string Name = reader.GetString(0);

                        new SqlCommand($"UPDATE [dbo].[Users] SET [Status] = 'Away' WHERE [Name] = '{Name}'", sql).ExecuteNonQuery();

                        var eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, EventHubName);
                        eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeMessage("SYS", $"{Name} has wandered off somewhere.")))));                        
                        eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeCommand("UPDATEUSER", new string[] { Name })))));                        

                        UsersUpdated += Name + "\n";
                    }

                sql.Close();
            }

            log.Info($"\nThe following users have beeen updated:\n{UsersUpdated}\nThe following users have been deleted:\n{UsersDeleted}\n");

            return new HttpRequestMessage().CreateResponse(HttpStatusCode.OK, $"The following users have beeen updated:\n{UsersUpdated}. The following users have been deleted:\n{UsersDeleted}");
        }

        public static async Task<HttpResponseMessage> SendMessage(HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"AwsomeChat Function App received a SendMessage request. URI={req.RequestUri}");
            string Name = req.GetQueryNameValuePairs().FirstOrDefault(q => q.Key.ToLower() == "name").Value;
            string Message = req.GetQueryNameValuePairs().FirstOrDefault(q => q.Key.ToLower() == "message").Value;

            dynamic data = await req.Content.ReadAsAsync<object>();

            Name = Name ?? data?.name;
            Message = Message ?? data?.Message;

            if (Name == null)
                return req.CreateResponse(HttpStatusCode.BadRequest, $"No name specified");
            if (Message == null)
                return req.CreateResponse(HttpStatusCode.BadRequest, $"No message specified");

            using (SqlConnection sql = new SqlConnection(CreateSQLConnection().ConnectionString))
            {
                sql.Open();

                int RowsAffected = new SqlCommand($"UPDATE [dbo].[Users] SET [Status] = 'Online', [LastMessage] = '{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss")}' WHERE [Name] = '{Name}'", sql).ExecuteNonQuery();

                sql.Close();

                if (RowsAffected > 0)
                {
                    var eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, EventHubName);
                    eventHubClient.Send(new EventData(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new AwsomeMessage(Name, Message)))));                    

                    return req.CreateResponse(HttpStatusCode.OK, $"User {Name} said {Message}.");
                }
                else
                {                    
                    return req.CreateResponse(HttpStatusCode.NotFound, $"Name {Name} does not exist.");
                }
            }
        }

        public static HttpResponseMessage GetAllUsers(HttpRequestMessage req, TraceWriter log)
        {
            log.Info($"AwsomeChat Function App received a GetAllUsers request.");

            List<AwsomeUser> Users = new List<AwsomeUser>();

            using (SqlConnection sql = new SqlConnection(CreateSQLConnection().ConnectionString))
            {
                sql.Open();

                using (SqlCommand command = new SqlCommand($"SELECT TOP 100 [Name],[LastMessage],[Status] FROM [dbo].[Users]", sql))
                using (SqlDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        string Name = reader.GetString(0);
                        string LastMessage = reader.GetDateTime(1).Ticks.ToString();
                        string Status = reader.GetString(2);

                        Users.Add(new AwsomeUser(Name, LastMessage, Status));
                    }
            }

            return req.CreateResponse(HttpStatusCode.OK, new JavaScriptSerializer().Serialize(Users));
        }
    }
      
    public class AwsomeUser
    {        
        public string Name { get; set; }
        public string LastMessage { get; set; }
        public string Status { get; set; }

        public AwsomeUser(string Name, string LastMessage, string Status)
        {
            this.Name = Name;
            this.LastMessage = LastMessage;
            this.Status = Status;
        }
    }

    public class AwsomeMessage
    {
        public string type = "Message";
        public string From { get; set; }
        public string Content { get; set; }

        public AwsomeMessage(string From, string Content)
        {
            this.From = From;
            this.Content = Content;
        }
    }

    public class AwsomeCommand
    {
        public string type = "Command";
        public string Instruction { get; set; }
        public string[] Parameters { get; set; }

        public AwsomeCommand(string Instruction, string[] Parameters)
        {
            this.Instruction = Instruction;
            this.Parameters = Parameters;
        }
    }
}