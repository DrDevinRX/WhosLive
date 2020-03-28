using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Rest;
using Discord.WebSocket;
using LiteDB;
using Newtonsoft.Json.Linq;

namespace WhosLive.Modules
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        private ulong GuildId = Program.GuildId;
        private readonly Twitch twitch;
        private LiteDatabase database { get; set; }
        private readonly object dbLock = new object();
        private readonly TimeSpan WaitPeriod = TimeSpan.FromSeconds(10);
        private readonly DiscordSocketClient client;
        private SocketGuild guild;
        private const ulong YinId = 132557773987643392;

        public Commands(LiteDatabase db, DiscordSocketClient client)
        {
            this.client = client;
            database = db;
            twitch = new Twitch();

            // Run the update and query threads
            Task.Run(UpdateAndCheckStreamers);
        }

        public async Task UpdateAndCheckStreamers()
        {
            while (client.ConnectionState != ConnectionState.Connected)
            {
                await Console.Out.WriteLineAsync("Waiting to connect to Discord");
                await Task.Delay(1000);
            }

            guild = client.GetGuild(GuildId);
            Console.WriteLine($"Guild = {guild.Name}");
            while (true)
            {
                // Task 1: Check if streamers are streaming
                // Task 2: Update the messages
                // NOTE: still don't know how to dump the message cache
                //       to check if message truly exists in the channel or not
                await Task.Delay(WaitPeriod);
                await Task.Run(CheckStreamerStatus);
                await Task.Delay(TimeSpan.FromMinutes(1));
                await Task.Run(UpdateMessages);
            }
        }

        private Task CheckStreamerStatus()
        {
            LiteCollection<Streamer> streamers = null;

            lock (dbLock)
            {
                streamers = database.GetCollection<Streamer>("streamers");
            }

            foreach (var streamer in streamers.FindAll())
            {
                var info = twitch.TwitchQuery(Twitch.HelixStrings.Streams, streamer.Name.ToLower());
                if (info == null)
                {
                    if (streamer.IsStreaming)
                    {
                        streamer.IsStreaming = false;
                        lock (dbLock)
                        {
                            streamers.Update(streamer);
                        }
                    }
                    continue;
                }

                var gamePlayed = twitch.TwitchQuery(Twitch.HelixStrings.Games, (string)info.Result["game_id"]).Result;

                streamer.Game = gamePlayed == null ? "" : (string)gamePlayed["name"];

                if (!streamer.IsStreaming)
                {
                    streamer.IsStreaming = true;
                }

                streamer.StreamTitle = (string)info.Result["title"];

                if (streamer.MessageId == null)
                {
                    var user = guild.GetUser(streamer.Id);
                    var content = string.Format(Twitch.DefaultStreamMessage, $"**{(user as IGuildUser)?.Nickname}**", streamer.StreamUrl);
                    var embed = CreateMessageEmbed(streamer).Result;
                    var liveChannel = guild.GetTextChannel(Program.ChannelId);
                    var msg = liveChannel.SendMessageAsync(content, embed: embed).Result;
                    streamer.MessageId = (msg as IUserMessage).Id;
                    Console.Out.WriteLineAsync($"Set streamer {streamer.Name} to msgId {streamer.MessageId}");
                }

                lock (dbLock)
                {
                    streamers.Update(streamer);
                }
            }

            return Task.CompletedTask;
        }

        private Task UpdateMessages()
        {
            LiteCollection<Streamer> streamers = null;

            lock (dbLock)
            {
                streamers = database.GetCollection<Streamer>("streamers");
            }

            foreach (var streamer in streamers.FindAll())
            {
                if (!streamer.IsStreaming && streamer.MessageId != null)
                {
                    var liveChannel = guild.GetTextChannel(Program.ChannelId);
                    var msgToDelete = (liveChannel as ITextChannel).GetMessageAsync(streamer.MessageId.Value);

                    if (msgToDelete != null)
                    {
                        liveChannel.DeleteMessageAsync(msgToDelete.Result);
                    }

                    streamer.MessageId = null;

                    lock (dbLock)
                    {
                        streamers.Update(streamer);
                    }
                    continue;
                }

                if (streamer.IsStreaming)
                {
                    // Shouldn't happen, clean up and wait for next pass
                    if (streamer.MessageId == null)
                    {
                        streamer.IsStreaming = false;

                        lock (dbLock)
                        {
                            streamers.Update(streamer);
                        }
                    }

                    if (streamer.MessageId != null)
                    {
                        var liveChannel = guild.GetTextChannel(Program.ChannelId);
                        var msg = (liveChannel as ITextChannel).GetMessageAsync(streamer.MessageId.Value);
                        Console.Out.WriteLineAsync($"msg is null: {msg == null}, id: {streamer.MessageId.Value}");

                        if (msg == null)
                        {
                            var user = (liveChannel as ITextChannel).GetUserAsync(streamer.Id).Result;
                            var content = string.Format(Twitch.DefaultStreamMessage, $"**{(user as IGuildUser)?.Nickname ?? "Someone"}**", streamer.StreamUrl);
                            var embed = CreateMessageEmbed(streamer).Result;
                            var newMsg = liveChannel.SendMessageAsync(content, embed: embed).Result;
                            streamer.MessageId = (msg.Result as IUserMessage).Id;

                            lock (dbLock)
                            {
                                streamers.Update(streamer);
                            }
                        }
                        else
                        {
                            (msg.Result as IUserMessage).ModifyAsync(x => x.Embed = CreateMessageEmbed(streamer).Result);
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }

        private Task<Embed> CreateMessageEmbed(Streamer streamer)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append($"**Title**: {streamer.StreamTitle}\n");
            sb.Append($"**Game**: {streamer.Game}\n");

            if (streamer.CustomMessage != null)
            {
                sb.Append(streamer.CustomMessage);
            }

            var embed = new EmbedBuilder()
            .WithUrl(streamer.StreamUrl)
            .WithTitle(streamer.StreamUrl)
            .WithDescription(sb.ToString())
            .WithThumbnailUrl(streamer.AvatarUrl)
            .Build();

            return Task.FromResult(embed);
        }

        #region commands
        [Command("adduser"), RequireUserPermission(GuildPermission.ManageChannels)]
        public Task AddUser(IUser user, [Remainder]string twitchUrl)
        {
            if (user.IsBot)
                return Task.CompletedTask;

            if (string.IsNullOrWhiteSpace(twitchUrl))
                return Task.CompletedTask;

            LiteCollection<Streamer> streamers = null;
            const string twitchRegex = @"https?:\/\/.*twitch.tv\/(.*)";

            lock (dbLock)
            {
                streamers = database.GetCollection<Streamer>("streamers");
            }

            if (new Regex(twitchRegex).IsMatch(twitchUrl))
            {
                var matches = new Regex(twitchRegex).Matches(twitchUrl);
                twitchUrl = matches[0].Groups[1].Value;
            }

            var userIsValid = false;

            userIsValid = twitch.IsUserValid(twitchUrl.Trim()).Result;

            if (!userIsValid)
            {
                ReplyAsync("Twitch user is not valid");
                return Task.CompletedTask;
            }


            System.Collections.Generic.Dictionary<string, JToken> dict = twitch.TwitchQuery(Twitch.HelixStrings.Users, twitchUrl).Result;

            if (streamers.FindOne(x => x.Id == user.Id) != null)
            {
                ReplyAsync("User already exists in database");
                return Task.CompletedTask;
            }

            var newUser = new Streamer
            {
                Id = user.Id,
                Name = (string)dict["login"],
                StreamUrl = "https://www.twitch.tv/" + dict["login"],
                AvatarUrl = (string)dict["profile_image_url"]
            };

            bool update = false;

            lock (dbLock)
            {
                update = streamers.Upsert(newUser);
            }


            if (!update)
            {
                ReplyAsync("Could not add user to the database");
                return Task.CompletedTask;
            }

            ReplyAsync($"Added {user.Mention} to the database");

            return Task.CompletedTask;
        }

        [Command("deluser"), RequireUserPermission(GuildPermission.ManageChannels)]
        public Task DeleteUser(IUser user)
        {
            if (user.IsBot)
                return Task.CompletedTask;

            LiteCollection<Streamer> streamers = null;

            lock (dbLock)
            {
                streamers = database.GetCollection<Streamer>("streamers");
            }

            var streamer = streamers.FindOne(x => x.Id == user.Id);

            if (streamer == null)
            {
                ReplyAsync("User is not in database");
                return Task.CompletedTask;
            }

            int delete = -1;

            lock (dbLock)
            {
                delete = streamers.Delete(x => x.Id == user.Id);
            }

            if (delete > 0)
            {
                ReplyAsync($"User {(user as IGuildUser)?.Nickname ?? "User"} has been deleted from database. Status = {delete}");
            }
            else
            {
                ReplyAsync($"Failed to delete user {(user as IGuildUser)?.Nickname ?? "User"} from the database. Status = {delete}");
            }

            return Task.CompletedTask;
        }

        [Command("custommessage"), Alias("acm")]
        public Task AddCustomMessage(IUser user, [Remainder]string message)
        {

            if (string.IsNullOrWhiteSpace(message))
            {
                return Task.CompletedTask;
            }

            if (user != Context.Message.Author)
            {

                var guilduser = Context.Message.Author as Discord.IGuildUser;

                if (guilduser == null)
                {
                    ReplyAsync("Guild User invalid.");
                    return Task.CompletedTask;
                }

                if (!guilduser.GuildPermissions.ManageChannels)
                {
                    return ReplyAsync("You cannot modify custom messages other than your own");
                }
            }

            if (message.Contains("@everyone"))
            {
                return ReplyAsync("The server owner is disappointed in you for your use of `@`everyone. :rage:");
            }

            LiteCollection<Streamer> streamers = null;

            lock (dbLock)
            {
                streamers = database.GetCollection<Streamer>("streamers");
            }

            var streamer = streamers.FindOne(x => x.Id == user.Id);

            if (streamer == null)
            {
                return ReplyAsync("User not found in database.");
            }

            streamer.CustomMessage = message.Trim();

            bool updated = false;

            lock (dbLock)
            {
                updated = streamers.Update(streamer);
            }

            if (updated)
                return ReplyAsync("Custom message was added successfully");
            else
                return ReplyAsync("Could not update database");
        }

        [Command("removecustommessage"), Alias("rcm")]
        public Task RemoveCustomMessage(IUser user)
        {
            if (user.IsBot)
                return Task.CompletedTask;

            if (user != Context.Message.Author)
            {
                var guilduser = Context.Message.Author as Discord.IGuildUser;

                if (!guilduser.GuildPermissions.ManageChannels)
                {
                    ReplyAsync("You cannot modify custom messages other than your own");
                    return Task.CompletedTask;
                }
            }

            LiteCollection<Streamer> streamers = null;

            lock (dbLock)
            {
                streamers = database.GetCollection<Streamer>("streamers");
            }

            var streamer = streamers.FindOne(x => x.Id == user.Id);

            if (streamer == null)
            {
                ReplyAsync("User not found in database.");
                return Task.CompletedTask;
            }

            streamer.CustomMessage = null;

            bool update = false;

            lock (dbLock)
            {
                update = streamers.Update(streamer);
            }

            if (update)
            {
                ReplyAsync("Custom message deleted successfully");
            }
            else
            {
                ReplyAsync("Could not delete custom message");
            }

            return Task.CompletedTask;
        }

        [Command("delete"), RequireUserPermission(GuildPermission.ManageMessages), Alias(new[] { "del", "rm", "clear" })]
        public Task DeleteMessages(int amt = 100)
        {
            // Delete the delete command message
            var firstMessage = Context.Channel.GetMessagesAsync(1).Flatten().OrderByDescending(x => x.Timestamp).FirstOrDefault().Result;

            Context.Channel.DeleteMessageAsync(firstMessage);

            amt = (amt > 100) ? 100 : (amt < 0) ? 0 : amt;

            // Now delete the actual amount requested
            var messages = Context.Channel.GetMessagesAsync(amt).Flatten().OrderByDescending(x => x.Timestamp).ToList().Result;

            var requestOptions = new RequestOptions
            {
                RetryMode = RetryMode.RetryRatelimit,
                AuditLogReason = $"Batch delete messages for channel (id: {Context.Channel.Id}; name: {Context.Channel.Name})"
            };

            foreach (var m in messages)
            {
                Context.Channel.DeleteMessageAsync(m, requestOptions);
            }

            return Task.CompletedTask;
        }

        [Command("info"), Alias("about")]
        public Task Info()
        {
            StringBuilder sb = new StringBuilder();
            var u = Context.Guild.Users.First(x => x.Id == YinId);
            sb.Append($"Hello, I am a bot that will display streaming Twitch channels inside this discord within" +
                $" the {Context.Guild.GetTextChannel(Program.ChannelId).Mention} channel.\n");
            sb.Append("Ask an admin to be added to the streamer database.\n");
            sb.Append("\n**Commands**:\n");
            sb.Append("custommessage       (acm)    Add a custom stream message for your live message\n");
            sb.Append("removecustommessage (rcm)    Remove custom stream message\n");
            sb.Append($"Bot created by {$"{u.Username}#{u.Discriminator}" ?? "Yin#5666"}\n");
            sb.Append($"Source: https://github.com/DrDevinRX/WhosLive");

            var embed = new EmbedBuilder()
                .WithDescription(sb.ToString())
                .WithImageUrl(Context.Client.CurrentUser.GetAvatarUrl())
                .WithThumbnailUrl(Context.Guild.Users.First(x => x.Id == YinId)?.GetAvatarUrl())
                .Build();

            return ReplyAsync("", embed: embed);
        }
        #endregion
    }
}
