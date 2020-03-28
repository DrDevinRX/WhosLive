using System;
using System.Reflection;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using LiteDB;
using WhosLive.Modules;

namespace WhosLive.Services
{
    public class CommandHandlerService
    {
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commands;
        private IServiceProvider _provider;
        private readonly Commands _cmd;

        public CommandHandlerService(DiscordSocketClient discord, CommandService commands, IServiceProvider provider, Commands cmd)
        {
            _discord = discord;
            _commands = commands;
            _provider = provider;
            _cmd = cmd;

            _discord.MessageReceived += MessageReceived;
        }

        private async Task MessageReceived(SocketMessage socketMessage)
        {
            if (!(socketMessage is SocketUserMessage message)) return;
            if (message.Source != Discord.MessageSource.User) return;

            int argPos = 0;

            if (!message.HasStringPrefix(Program.Prefix, ref argPos)) return;

            var context = new SocketCommandContext(_discord, message);
            var result = await _commands.ExecuteAsync(context, argPos, _provider);

            if (result.Error.HasValue &&
                result.Error.Value == CommandError.UnknownCommand)
                return;
            if (result.Error.HasValue &&
                result.Error.Value != CommandError.UnknownCommand)
            {
                await context.Channel.SendMessageAsync(result.ToString());
            }
        }

        public async Task InitializeAsync(IServiceProvider provider)
        {
            _provider = provider;
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), provider);
        }
    }
}
