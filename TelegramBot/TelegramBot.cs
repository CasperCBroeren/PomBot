﻿using System;
using System.Threading.Tasks;
using TelegramBot.ResponseObjects;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Text;

namespace TelegramBot
{
    public delegate void UpdateReceived(Update update);
    /// <summary>
    /// Inspiration from https://github.com/kolar/telegram-poll-bot/blob/master/TelegramBot.php
    /// Conforms to https://core.telegram.org/bots/api
    /// </summary>
    public class TelegramBot
    {
        ///<summary>
        /// The amount of updates to receive before exit longpolling
        ///</summary>
        public int UpdatesLimit { get; set; } = 30;
        ///<summary>
        /// The timeout in seconds before timing out the longpolling
        ///</summary>
        public int UpdatesTimeout { get; set; } = 10;

        /// <summary>
        /// The updateID to start from when longpolling
        ///</summary>
        public int? UpdatesOffset { get; set; } = null;
        /// <summary>
        /// The connection timeout on the longpoll connection
        /// </summary>
        public int NetConnectionTimeout { get; set; }

        /// <summary>
        /// The delegate called when a message is received
        /// </summary>
        public UpdateReceived OnUpdateReceived { get; set; }
        /// <summary>
        /// The Telegram bot token, this can be retrieved from the BotFather bot.
        /// </summary>
        public string Token { get; set; }
        /// <summary>
        /// State indicates that the bot has been inited and the getMe request has been send and result was true
        /// </summary>
        public bool Inited { get; private set; }

        /// <summary>
        /// The url to wich we communicate, constructed in the ctor 
        /// </summary>
        public string ApiURL { get; private set; }
        /// <summary>
        /// The BotID is filled when the Init has been called and true has been received from the Telegram server
        /// </summary>
        public int BotId { get; private set; }
        /// <summary>
        /// The Bot username is filled when the Init has been called and true has been received from the Telegram server
        /// </summary>
        public string BotUsername { get; set; }

        ///<summary>
        /// The constructor which sets the token and ApiURL
        ///</summary>
        public TelegramBot(string token, TelegramBotOptions options = null)
        {
            if (options == null) options = new TelegramBotOptions();
            Token = token;
            string protoPart = (options.Port == 443) ? "https" : "http";
            string portPart = (options.Port == 443 || options.Port == 80) ? "" : ":" + options.Port;
            ApiURL = $"{protoPart}://{options.Host}{portPart}/bot{Token}";
        }

      
        /// <summary>
        /// The init which calls the getMe method of the Telegram api. When called multiple times and the first call was OK, the sequential calls don't call the API
        /// </summary>
        public async Task<bool> InitAsync()
        {
            if (Inited) return true;

            var response = await DoRequest<GetMeResponse>("getMe", null);
            if (!response.Ok)
                throw new Exception("Can't connect to server");

            BotId = response.Result.ID;
            BotUsername = response.Result.UserName;

            Inited = true;
            return true;
        }
        /// <summary>
        /// Sends a MessageToSend to a chatID or user. 
        /// </summary>
        public async Task<MessageResponse> SendMessageAsync(MessageToSend message)
        {
            await InitAsync();
            var result = await DoRequest<MessageResponse>("sendMessage", new
            {
                Method = "JSON",
                Payload = message
            });

            return result;
        }
        /// <summary>
        /// Executes the longpolling and gives updates through the supplied delegate UpdateReceived
        /// </summary>
        public async Task RunLongPollAsync(UpdateReceived onUpdateReceived, CancellationToken token)
        {
            await InitAsync();
            await LongPoll(onUpdateReceived, token);
        }
        /// <summary>
        /// Sets the webhook to the url. The URL must start with HTTPS as required by the Telegram API
        /// </summary>
        public async Task<bool> SetWebHookAsync(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.ToLower().StartsWith("https:")) throw new ArgumentException("The url should start with https");
            await InitAsync();
            var result = await DoRequest<WebHookResponse>("setWebhook", new
            {
                Method = "POST",
                URL = url
            });
            return result.Ok;
        }
        /// <summary>
        /// Removes the webhook, which basicly calls setWebhook with empty url parameter
        /// </summary>
        public async Task<bool> RemoveWebHookAsync()
        {
            await InitAsync();
            var result = await DoRequest<WebHookResponse>("setWebhook", new
            {
                Method = "POST",
                URL = ""
            });
            return result.Ok;
        }
        /// <summary>
        /// Internal Longpoll method which can be call recursively
        /// </summary>
        private async Task LongPoll(UpdateReceived onUpdateReceived, CancellationToken cancelToken)
        {

            var getUpdateTask = Task.Run(()=> DoRequest<UpdateResponse>("getUpdates", new
            {
                Method = "POST",
                Limit = UpdatesLimit,
                Timeout = UpdatesTimeout,
                Offset = UpdatesOffset
            }), cancelToken);
            var result = await getUpdateTask;

            if (result.Ok)
            {
                if (result.Result != null)
                {
                    foreach (var update in result.Result)
                    {
                        UpdatesOffset = update.UpdateID + 1;
                        if (onUpdateReceived != null)
                            onUpdateReceived.Invoke(update);
                    }
                }

            }
           
            await LongPoll(onUpdateReceived, cancelToken);
        }

        /// <summary>
        /// Internal method to make the GET/POST request to the Telegram API
        /// </summary>
        private async Task<T> DoRequest<T>(string action, dynamic options) where T : new()
        {

            using (var client = new HttpClient())
            {
                 
                    var uri = new System.Uri($"{ApiURL}/{action}");
                    var request = new HttpRequestMessage(HttpMethod.Get, uri); 

                    if (options != null && options.Method == "POST")
                    {
                        request.Headers.Add("ContentType", "application/x-www-form-urlencoded");
                        request.Method = HttpMethod.Post;
                        var values = new List<KeyValuePair<string, string>>();
                        if (IsPropertyExist(options, "URL")) values.Add(new KeyValuePair<string, string>("url", options.URL));
                        if (IsPropertyExist(options, "Limit")) values.Add(new KeyValuePair<string, string>("limit", options.Limit.ToString()));
                        if (IsPropertyExist(options, "Timeout")) values.Add(new KeyValuePair<string, string>("timeout", options.Timeout.ToString()));
                        if (IsPropertyExist(options, "Offset") && options.Offset != null) values.Add(new KeyValuePair<string, string>("offset", options.Offset.ToString()));
                        if (action == "getUpdates")
                        {
                            client.Timeout = TimeSpan.FromSeconds(NetConnectionTimeout + options.Limit + 2);
                        }

                        request.Content = new FormUrlEncodedContent(values);

                    }
                    if (options != null && options.Method == "JSON")
                    {

                     
                        request.Method = HttpMethod.Post;
                        if (IsPropertyExist(options, "Payload"))
                        {
                            string serialized = await JsonConvert.SerializeObjectAsync(options.Payload, Formatting.None, new JsonSerializerSettings()
                            {
                                NullValueHandling = NullValueHandling.Ignore
                            });
                            request.Content = new StringContent(serialized, Encoding.UTF8, "application/json"); 
                        }
                    }

                    var response = await client.SendAsync(request);

                    var result = await response.Content.ReadAsStringAsync();
                    T returnObject = default(T);
                    await Task.Factory.StartNew(() =>
                    {
                        returnObject = JsonConvert.DeserializeObject<T>(result);
                    });

                    return returnObject;
                
            }

        }
        /// <summary>
        /// Checks on a dynamic if the property exists
        /// </summary>
        public static bool IsPropertyExist(dynamic item, string name)
        {
            return item != null && !String.IsNullOrEmpty(name) && item.GetType().GetProperty(name) != null;
        }
    }
}
