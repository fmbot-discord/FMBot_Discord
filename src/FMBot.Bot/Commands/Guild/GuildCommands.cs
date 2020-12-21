using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.Guild
{
    [Name("Server settings")]
    [Summary("Server staff only")]
    public class GuildCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly GuildService _guildService;
        private readonly SettingService _settingService;

        private readonly IPrefixService _prefixService;
        private readonly IGuildDisabledCommandService _guildDisabledCommandService;
        private readonly IChannelDisabledCommandService _channelDisabledCommandService;

        private readonly CommandService _commands;

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public GuildCommands(IPrefixService prefixService,
            GuildService guildService,
            CommandService commands,
            AdminService adminService,
            IGuildDisabledCommandService guildDisabledCommandService,
            IChannelDisabledCommandService channelDisabledCommandService,
            SettingService settingService)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._commands = commands;
            this._guildDisabledCommandService = guildDisabledCommandService;
            this._channelDisabledCommandService = channelDisabledCommandService;
            this._settingService = settingService;
            this._adminService = adminService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("serverset", RunMode = RunMode.Async)]
        [Summary("Sets the global FMBot settings for the server.")]
        [Alias("serversetmode")]
        [GuildOnly]
        public async Task SetServerAsync([Summary("The default mode you want to use.")]
            string chartType = "embedmini", [Summary("The default timeperiod you want to use.")]
            string chartTimePeriod = "monthly")
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (chartType == "help")
            {
                await ReplyAsync(
                    "Sets the global default for your server. `.fmserverset 'embedfull/embedmini/textfull/textmini' 'Weekly/Monthly/Yearly/AllTime'` command.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }


            if (!Enum.TryParse(chartType, true, out FmEmbedType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }


            if (!Enum.TryParse(chartTimePeriod, true, out ChartTimePeriod chartTimePeriodEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'weekly', 'monthly', 'yearly', or 'overall'.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, chartTimePeriodEnum, chartTypeEnum);

            await ReplyAsync("The .fmset default chart type for your server has been set to " + chartTypeEnum +
                             " with the time period " + chartTimePeriodEnum + ".");
            this.Context.LogCommandUsed();
        }

        [Command("serverreactions", RunMode = RunMode.Async)]
        [Summary("Sets reactions for some server commands.")]
        [Alias("serversetreactions")]
        [GuildOnly]
        public async Task SetGuildReactionsAsync(params string[] emotes)
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (emotes.Count() > 3)
            {
                await ReplyAsync("Sorry, max amount emote reactions you can set is 3!");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (emotes.Length == 0)
            {
                await this._guildService.SetGuildReactionsAsync(this.Context.Guild, null);
                await ReplyAsync(
                    "Removed all server reactions!");
                this.Context.LogCommandUsed();
                return;
            }

            if (!this._guildService.ValidateReactions(emotes))
            {
                await ReplyAsync(
                    "Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                    "Please check if you have a space between every emote.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetGuildReactionsAsync(this.Context.Guild, emotes);

            var message = await ReplyAsync("Emote reactions have been set! \n" +
                                           "Please check if all reactions have been applied to this message correctly. If not, you might have used an emote from a different server.");
            await this._guildService.AddReactionsAsync(message, this.Context.Guild);
            this.Context.LogCommandUsed();
        }

        [Command("togglesupportermessages", RunMode = RunMode.Async)]
        [Summary("Sets reactions for some server commands.")]
        [Alias("togglesupporter", "togglesupporters", "togglesupport")]
        [GuildOnly]
        public async Task ToggleSupportMessagesAsync()
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var messagesDisabled = await this._guildService.ToggleSupporterMessagesAsync(this.Context.Guild);

            if (messagesDisabled == true)
            {
                await ReplyAsync(".fmbot supporter messages have been disabled. Supporters are still visible in `.fmsupporters`, but they will not be shown in `.fmchart` or other commands anymore.");
            }
            else
            {
                await ReplyAsync($".fmbot supporter messages have been re-enabled. These have a 1 in {Constants.SupporterMessageChance} chance of showing up on certain commands.");
            }

            this.Context.LogCommandUsed();
        }

        [Command("export", RunMode = RunMode.Async)]
        [Summary("Gets Last.fm usernames from your server members in json format.")]
        [Alias("getmembers", "exportmembers")]
        [GuildOnly]
        public async Task GetMembersAsync()
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            try
            {
                var serverUsers = await this._guildService.FindAllUsersFromGuildAsync(this.Context);

                if (serverUsers.Count == 0)
                {
                    await ReplyAsync("No members found on this server.");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var userJson = JsonSerializer.Serialize(serverUsers, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await this.Context.User.SendFileAsync(StringToStream(userJson),
                    $"users_{this.Context.Guild.Name}_UTC-{DateTime.UtcNow:u}.json");

                await ReplyAsync("Check your DMs!");
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while creating an export.");
            }

        }

        /// <summary>
        /// Changes the prefix for the server.
        /// </summary>
        /// <param name="prefix">The desired prefix.</param>
        [Command("prefix", RunMode = RunMode.Async)]
        [GuildOnly]
        public async Task SetPrefixAsync(string prefix = null)
        {
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrEmpty(prefix) || prefix.ToLower() == "remove" || prefix.ToLower() == "delete")
            {
                await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
                this._prefixService.RemovePrefix(this.Context.Guild.Id);
                await ReplyAsync("Removed prefix!");
                this.Context.LogCommandUsed();
                return;
            }
            if (prefix.ToLower() == ".fm")
            {
                await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
                this._prefixService.RemovePrefix(this.Context.Guild.Id);
                await ReplyAsync("Reset to default prefix `.fm`!");
                this.Context.LogCommandUsed();
                return;
            }

            if (prefix.Length > 20)
            {
                await ReplyAsync("Max prefix length is 20 characters...");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            if (prefix.Contains("*") || prefix.Contains("`") || prefix.Contains("~"))
            {
                await ReplyAsync("You can't have a custom prefix that contains ** * **or **`** or **~**");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, prefix);
            this._prefixService.StorePrefix(prefix, this.Context.Guild.Id);

            this._embed.WithTitle("Successfully added custom prefix!");
            this._embed.WithDescription("Examples:\n" +
                                        $"- `{prefix}fm`\n" +
                                        $"- `{prefix}chart 8x8 monthly`\n" +
                                        $"- `{prefix}whoknows` \n \n" +
                                        "Reminder that you can always mention the bot followed by your command. \n" +
                                        $"The [.fmbot docs]({Constants.DocsUrl}) will still have the `.fm` prefix everywhere. " +
                                        $"Custom prefixes are still in the testing phase so please note that some error messages and other places might not show your prefix yet.\n\n" +
                                        $"To remove the custom prefix, do `{prefix}prefix remove`");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }


        /// <summary>
        /// Toggles commands for a server
        /// </summary>
        [Command("toggleservercommand", RunMode = RunMode.Async)]
        [Alias("toggleservercommands", "toggleserver", "servertoggle")]
        [GuildOnly]
        public async Task ToggleGuildCommand(string command = null)
        {
            var disabledCommandsForGuild = await this._guildService.GetDisabledCommandsForGuild(this.Context.Guild);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            this._embed.WithFooter(
                $"Toggling server-wide for all channels\n" +
                $"to toggle per channel use {prfx}togglecommand");

            if (string.IsNullOrEmpty(command))
            {
                var description = new StringBuilder();
                if (disabledCommandsForGuild != null && disabledCommandsForGuild.Length > 0)
                {
                    description.AppendLine("Currently disabled commands in this server:");
                    foreach (var disabledCommand in disabledCommandsForGuild)
                    {
                        description.Append($"`{disabledCommand}` ");
                    }
                }
                else
                {
                    description.Append("This server currently has all commands enabled. \n" +
                                  $"To disable a command, enter the command name like this: `{prfx}toggleservercommand chart`");
                }

                this._embed.WithDescription(description.ToString());
                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to toggle commands. Only users with the 'Ban Members' permission, server admins or FMBot admins disable/enable commands.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var searchResult = this._commands.Search(command.ToLower());

            if (searchResult.Commands == null || command.ToLower() == "togglecommand" || command.ToLower() == "toggleservercommand")
            {
                this._embed.WithDescription("No commands found or command can't be disabled.\n" +
                                            "Remember to remove the `.fm` prefix.");
                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (disabledCommandsForGuild != null && disabledCommandsForGuild.Contains(command.ToLower()))
            {
                var newDisabledCommands = await this._guildService.RemoveGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());

                this._guildDisabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Guild.Id);

                this._embed.WithDescription($"Re-enabled command `{command.ToLower()}` for this server.");
            }
            else
            {
                var newDisabledCommands = await this._guildService.AddGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());

                this._guildDisabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Guild.Id);

                this._embed.WithDescription($"Disabled command `{command.ToLower()}` for this server.");
            }

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }

        [Command("togglecommand", RunMode = RunMode.Async)]
        [Alias("togglecommands", "channeltoggle", "togglechannel", "togglechannelcommand", "togglechannelcommands")]
        [GuildOnly]
        public async Task ToggleChannelCommand(string command = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var disabledCommands = await this._guildService.GetDisabledCommandsForChannel(this.Context.Channel);

            this._embed.WithFooter(
                $"Toggling per channel\n" +
                $"To toggle server-wide use {prfx}toggleservercommand");

            if (string.IsNullOrEmpty(command))
            {
                var description = new StringBuilder();
                if (disabledCommands != null &&  disabledCommands.Length > 0)
                {
                    description.AppendLine("Currently disabled commands in this channel:");
                    foreach (var disabledCommand in disabledCommands)
                    {
                        description.AppendLine($"- `{disabledCommand}`");
                    }
                }

                if (disabledCommands == null || disabledCommands.Length == 0)
                {
                    description.AppendLine("This channel currently has all commands enabled. \n" +
                                           $"To disable a command, enter the command name like this: `{prfx}togglecommand chart`");
                }

                if (guild.Channels != null && guild.Channels.Any() && guild.Channels.Any(a => a.DisabledCommands != null && a.DisabledCommands.Length > 0))
                {
                    description.AppendLine("Currently disabled commands in this server per channel:");
                    foreach (var channel in guild.Channels.Where(a => a.DisabledCommands != null && a.DisabledCommands.Length > 0))
                    {
                        description.Append($"<#{channel.DiscordChannelId}> - ");
                        foreach (var disabledCommand in channel.DisabledCommands)
                        {
                            description.Append($"`{disabledCommand}` ");
                        }
                    }

                    description.AppendLine();
                }

                this._embed.WithDescription(description.ToString());
                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to toggle commands. Only users with the 'Ban Members' permission, server admins or FMBot admins disable/enable commands.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var searchResult = this._commands.Search(command.ToLower());

            if (searchResult.Commands == null || command.ToLower() == "togglecommand" || command.ToLower() == "toggleservercommand")
            {
                this._embed.WithDescription("No commands found or command can't be disabled.\n" +
                                            "Remember to remove the `.fm` prefix.");
                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (disabledCommands != null && disabledCommands.Contains(command.ToLower()))
            {
                var newDisabledCommands = await this._guildService.RemoveChannelDisabledCommandAsync(this.Context.Channel, command.ToLower());

                this._channelDisabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Channel.Id);

                this._embed.WithDescription($"Re-enabled command `{command.ToLower()}` for this channel.");
            }
            else
            {
                var newDisabledCommands = await this._guildService.AddChannelDisabledCommandAsync(this.Context.Channel, guild.GuildId, command.ToLower());

                this._channelDisabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Channel.Id);

                this._embed.WithDescription($"Disabled command `{command.ToLower()}` for this channel.");
            }

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }

        private static Stream StringToStream(string str)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(str);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}