using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Handlers;

public class InteractionHandler
{
    private readonly DiscordShardedClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _provider;
    private readonly UserService _userService;
    private readonly GuildService _guildService;

    private readonly IGuildDisabledCommandService _guildDisabledCommandService;
    private readonly IChannelDisabledCommandService _channelDisabledCommandService;

    public InteractionHandler(DiscordShardedClient client, InteractionService interactionService, IServiceProvider provider, UserService userService, GuildService guildService, IGuildDisabledCommandService guildDisabledCommandService, IChannelDisabledCommandService channelDisabledCommandService)
    {
        this._client = client;
        this._interactionService = interactionService;
        this._provider = provider;
        this._userService = userService;
        this._guildService = guildService;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._channelDisabledCommandService = channelDisabledCommandService;
        this._client.SlashCommandExecuted += HandleInteractionAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction socketInteraction)
    {
        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var contextUser = await this._userService.GetUserAsync(context.User.Id);

        if (socketInteraction is SocketSlashCommand socketSlashCommand)
        {
            var command = this._interactionService.SlashCommands.FirstOrDefault(f => f.Name == socketSlashCommand.CommandName);

            if (command == null)
            {
                Log.Error("Someone tried to execute a non-existent slash command! {slashCommand}", socketSlashCommand.CommandName);
                return;
            }

            if (contextUser?.Blocked == true)
            {
                await UserBlockedResponse(context);
                return;
            }

            if (!await CommandDisabled(context, command))
            {
                return;
            }

            if (command.Attributes.OfType<UsernameSetRequired>().Any())
            {
                if (contextUser == null)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    var userNickname = (context.User as SocketGuildUser)?.Nickname;
                    embed.UsernameNotSetErrorResponse("/", userNickname ?? context.User.Username);
                    await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }
            }
            if (command.Attributes.OfType<UserSessionRequired>().Any())
            {
                if (contextUser?.SessionKeyLastFm == null)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    embed.SessionRequiredResponse("/");
                    await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }
            }
            if (command.Attributes.OfType<GuildOnly>().Any())
            {
                if (context.Guild == null)
                {
                    await context.Interaction.RespondAsync("This command is not supported in DMs.");
                    context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                    return;
                }
            }
            if (command.Attributes.OfType<RequiresIndex>().Any() && context.Guild != null)
            {
                var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.Guild);
                if (lastIndex == null)
                {
                    var embed = new EmbedBuilder();
                    embed.WithDescription("To use .fmbot commands with server-wide statistics you need to index the server first.\n\n" +
                                          $"Please run `/index` to index this server.\n" +
                                          $"Note that this can take some time on large servers.");
                    await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                    context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }
                if (lastIndex < DateTime.UtcNow.AddDays(-120))
                {
                    var embed = new EmbedBuilder();
                    embed.WithDescription("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                          $"Please run `/index` to re-index this server.");
                    await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                    context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }
            }
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.SlashCommandsExecuted.Inc();
        _ = this._userService.UpdateUserLastUsedAsync(context.User.Id);
    }

    private async Task<bool> CommandDisabled(ShardedInteractionContext context, SlashCommandInfo searchResult)
    {
        if (context.Guild != null)
        {
            var disabledGuildCommands = this._guildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
            if (disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Name.Equals))
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this server.",
                    ephemeral: true);
                return false;
            }

            var disabledChannelCommands = this._channelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
            if (disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(searchResult.Name.Equals) &&
                context.Channel != null)
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this channel.",
                    ephemeral: true);
                return false;
            }
        }

        return true;
    }

    private static async Task UserBlockedResponse(ShardedInteractionContext shardedCommandContext)
    {
        var embed = new EmbedBuilder().WithColor(DiscordConstants.LastFmColorRed);
        embed.UserBlockedResponse("/");
        await shardedCommandContext.Channel.SendMessageAsync("", false, embed.Build());
        shardedCommandContext.LogCommandUsed(CommandResponse.UserBlocked);
        return;
    }
}