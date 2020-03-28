using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace WhosLive
{

    class Program
    {
        private DiscordSocketClient _discord;
        private IConfiguration _config;
        public static string Prefix { get; private set; }
        public static ulong ChannelId { get; private set; }
        public static string TwitchClientId { get; set; }
        public static ulong GuildId { get; private set; }

        static void Main()
            => new Program().MainAsync().GetAwaiter().GetResult();

        private async Task MainAsync()
        {
            _discord = new DiscordSocketClient();

            _config = BuildConfig();

            Prefix = _config["prefix"];
            TwitchClientId = _config["twitch-client-id"];
            if (!ulong.TryParse(_config["live-channel-id"], out var cid))
            {
                await Console.Out.WriteLineAsync("Channel ID invalid");
                return;
            }

            if (!ulong.TryParse(_config["guild-id"], out var gid))
            {
                await Console.Out.WriteLineAsync("Guild ID invalid");
                return;
            }

            await Console.Out.WriteLineAsync($"gid = {gid}");
            await Console.Out.WriteLineAsync($"cid = {cid}");
            GuildId = gid;
            ChannelId = cid;

            var services = ConfigureServices();

            services.GetRequiredService<Services.LoggerService>();
            await services.GetRequiredService<Services.CommandHandlerService>().InitializeAsync(services);


            await _discord.LoginAsync(TokenType.Bot, _config["token"]);
            await _discord.StartAsync();

            //await services.GetRequiredService<Modules.Commands>().InitializeAsync();

            await Task.Delay(-1);
        }

        private IServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton(_discord)
                .AddSingleton<CommandService>()
                .AddSingleton<Services.CommandHandlerService>()
                .AddLogging()
                .AddSingleton<Services.LoggerService>()
                .AddSingleton(_config)
                .AddSingleton(new LiteDatabase(new ConnectionString(@"WhosLiveDB.db")))
                .AddSingleton<Modules.Commands>()
                .BuildServiceProvider();
        }

        private IConfiguration BuildConfig()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("botconfig.json")
                .Build();
        }
    }
}
