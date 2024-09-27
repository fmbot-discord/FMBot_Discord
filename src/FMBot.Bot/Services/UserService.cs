using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class UserService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly BotSettings _botSettings;
    private readonly CountryService _countryService;
    private readonly PlayService _playService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly FriendsService _friendsService;
    private readonly AdminService _adminService;
    private readonly TemplateService _templateService;

    public UserService(IMemoryCache cache,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IDataSourceFactory dataSourceFactory,
        IOptions<BotSettings> botSettings,
        CountryService countryService,
        PlayService playService,
        WhoKnowsArtistService whoKnowsArtistService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        WhoKnowsTrackService whoKnowsTrackService,
        FriendsService friendsService,
        AdminService adminService,
        TemplateService templateService)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._dataSourceFactory = dataSourceFactory;
        this._countryService = countryService;
        this._playService = playService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._friendsService = friendsService;
        this._adminService = adminService;
        this._templateService = templateService;
        this._botSettings = botSettings.Value;
    }

    public async Task<User> GetUserSettingsAsync(IUser discordUser)
    {
        return await GetUserAsync(discordUser.Id);
    }

    public async Task<User> GetUserAsync(ulong discordUserId)
    {
        var discordUserIdCacheKey = UserDiscordIdCacheKey(discordUserId);

        if (this._cache.TryGetValue(discordUserIdCacheKey, out User user))
        {
            return user;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user != null)
        {
            var lastFmCacheKey = UserLastFmCacheKey(user.UserNameLastFM);

            this._cache.Set(lastFmCacheKey, user, TimeSpan.FromSeconds(5));
            this._cache.Set(discordUserIdCacheKey, user, TimeSpan.FromSeconds(5));

            if (!PublicProperties.RegisteredUsers.ContainsKey(user.DiscordUserId))
            {
                PublicProperties.RegisteredUsers.TryAdd(user.DiscordUserId, user.UserId);
            }
        }

        return user;
    }

    public void RemoveUserFromCache(User user)
    {
        this._cache.Remove(UserDiscordIdCacheKey(user.DiscordUserId));
        this._cache.Remove(UserLastFmCacheKey(user.UserNameLastFM));
    }

    public static string UserDiscordIdCacheKey(ulong discordUserId)
    {
        return $"user-{discordUserId}";
    }

    public static string UserLastFmCacheKey(string userNameLastFm)
    {
        return $"user-{userNameLastFm.ToLower()}";
    }

    public async Task<User> GetUserForIdAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId);
    }

    public async Task<User> GetUserWithDiscogs(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.DiscogsReleases)
            .ThenInclude(i => i.Release)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public async Task<bool> UserRegisteredAsync(IUser discordUser)
    {
        var user = await GetUserAsync(discordUser.Id);

        return user != null;
    }

    public async Task<bool> UserBlockedAsync(ulong discordUserId)
    {
        var user = await GetUserAsync(discordUserId);

        return user?.Blocked == true;
    }

    public async Task<bool> UserHasSessionAsync(IUser discordUser)
    {
        var user = await GetUserSettingsAsync(discordUser);

        return !string.IsNullOrEmpty(user?.SessionKeyLastFm);
    }

    public async Task<Dictionary<int, User>> GetMultipleUsers(HashSet<int> userIds)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .Where(w => userIds.Contains(w.UserId))
            .ToDictionaryAsync(d => d.UserId, d => d);
    }

    public async Task UpdateUserLastUsedAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user != null)
        {
            user.LastUsed = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            db.Update(user);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Something went wrong while attempting to update user {userId} last used", user.UserId);
            }
        }
    }

    public async Task AddUserTextCommandInteraction(ShardedCommandContext context, string commandName)
    {
        var user = await GetUserSettingsAsync(context.User);
        PublicProperties.UsedCommandDiscordUserIds.TryAdd(context.Message.Id, context.User.Id);

        await Task.Delay(12000);

        try
        {
            if (user != null)
            {
                var commandResponse = CommandResponse.Ok;
                if (PublicProperties.UsedCommandsResponses.TryGetValue(context.Message.Id, out var fetchedResponse))
                {
                    commandResponse = fetchedResponse;
                }

                string errorReference = null;
                if (PublicProperties.UsedCommandsErrorReferences.TryGetValue(context.Message.Id, out var fetchedErrorId))
                {
                    errorReference = fetchedErrorId;
                }

                string artist = null;
                if (PublicProperties.UsedCommandsArtists.TryGetValue(context.Message.Id, out var fetchedArtist))
                {
                    artist = fetchedArtist;
                }

                string album = null;
                if (PublicProperties.UsedCommandsAlbums.TryGetValue(context.Message.Id, out var fetchedAlbum))
                {
                    album = fetchedAlbum;
                }

                string track = null;
                if (PublicProperties.UsedCommandsTracks.TryGetValue(context.Message.Id, out var fetchedTrack))
                {
                    track = fetchedTrack;
                }

                ulong? responseId = null;
                if (PublicProperties.UsedCommandsResponseMessageId.TryGetValue(context.Message.Id, out var fetchedResponseId))
                {
                    responseId = fetchedResponseId;
                }

                bool? hintShown = null;
                if (PublicProperties.UsedCommandsHintShown.Contains(context.Message.Id))
                {
                    hintShown = true;
                }

                var interaction = new UserInteraction
                {
                    Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    CommandContent = context.Message.Content,
                    CommandName = commandName,
                    UserId = user.UserId,
                    DiscordGuildId = context.Guild?.Id,
                    DiscordChannelId = context.Channel?.Id,
                    DiscordId = context.Message.Id,
                    DiscordResponseId = responseId,
                    Response = commandResponse,
                    Type = UserInteractionType.TextCommand,
                    ErrorReferenceId = errorReference,
                    Artist = artist,
                    Album = album,
                    Track = track,
                    HintShown = hintShown
                };

                await using var db = await this._contextFactory.CreateDbContextAsync();
                await db.UserInteractions.AddAsync(interaction);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "AddUserTextCommandInteraction: Error while adding user interaction");
        }
    }

    public async Task AddUserSlashCommandInteraction(ShardedInteractionContext context, string commandName)
    {
        var user = await GetUserSettingsAsync(context.User);
        PublicProperties.UsedCommandDiscordUserIds.TryAdd(context.Interaction.Id, context.User.Id);

        await Task.Delay(12000);

        try
        {
            if (user != null)
            {
                var commandResponse = CommandResponse.Ok;
                if (PublicProperties.UsedCommandsResponses.TryGetValue(context.Interaction.Id, out var fetchedResponse))
                {
                    commandResponse = fetchedResponse;
                }

                string errorReference = null;
                if (PublicProperties.UsedCommandsErrorReferences.TryGetValue(context.Interaction.Id, out var fetchedErrorId))
                {
                    errorReference = fetchedErrorId;
                }

                string artist = null;
                if (PublicProperties.UsedCommandsArtists.TryGetValue(context.Interaction.Id, out var fetchedArtist))
                {
                    artist = fetchedArtist;
                }

                string album = null;
                if (PublicProperties.UsedCommandsAlbums.TryGetValue(context.Interaction.Id, out var fetchedAlbum))
                {
                    album = fetchedAlbum;
                }

                string track = null;
                if (PublicProperties.UsedCommandsTracks.TryGetValue(context.Interaction.Id, out var fetchedTrack))
                {
                    track = fetchedTrack;
                }

                ulong? responseId = null;
                if (PublicProperties.UsedCommandsResponseMessageId.TryGetValue(context.Interaction.Id, out var fetchedResponseId))
                {
                    responseId = fetchedResponseId;
                }

                bool? hintShown = null;
                if (PublicProperties.UsedCommandsHintShown.Contains(context.Interaction.Id))
                {
                    hintShown = true;
                }

                var options = new Dictionary<string, string>();
                if (context.Interaction is SocketSlashCommand command)
                {
                    foreach (var option in command.Data.Options)
                    {
                        options.Add(option.Name, option.Value?.ToString());
                    }
                }

                var interaction = new UserInteraction
                {
                    Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    CommandName = commandName,
                    CommandOptions = options.Any() ? options : null,
                    UserId = user.UserId,
                    DiscordGuildId = context.Guild?.Id,
                    DiscordChannelId = context.Channel?.Id,
                    DiscordId = context.Interaction.Id,
                    DiscordResponseId = responseId,
                    Response = commandResponse,
                    Type = context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                           !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall) ?
                            UserInteractionType.SlashCommandUser : UserInteractionType.SlashCommandGuild,
                    ErrorReferenceId = errorReference,
                    Artist = artist,
                    Album = album,
                    Track = track,
                    HintShown = hintShown
                };

                await using var db = await this._contextFactory.CreateDbContextAsync();
                await db.UserInteractions.AddAsync(interaction);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "AddUserSlashCommandInteraction: Error while adding user interaction");
        }
    }

    public async Task UpdateInteractionContextThroughReference(ulong interactionId, bool updateDelay = false, bool updateInternal = true)
    {
        if (updateDelay)
        {
            await Task.Delay(12000);
        }

        if (!PublicProperties.UsedCommandsReferencedMusic.TryGetValue(interactionId, out var value))
        {
            return;
        }

        await UpdateInteractionContext(interactionId, value, updateInternal);
    }

    public async Task UpdateInteractionContext(ulong interactionId, ReferencedMusic responseContext, bool updateInternal = true)
    {
        if (updateInternal)
        {
            if (PublicProperties.UsedCommandsTracks.TryRemove(interactionId, out _))
            {
                PublicProperties.UsedCommandsTracks.TryAdd(interactionId, responseContext.Track);
            }
            if (PublicProperties.UsedCommandsAlbums.TryRemove(interactionId, out _))
            {
                if (responseContext.Album != null)
                {
                    PublicProperties.UsedCommandsAlbums.TryAdd(interactionId, responseContext.Album);
                }
            }
            if (PublicProperties.UsedCommandsArtists.TryRemove(interactionId, out _))
            {
                PublicProperties.UsedCommandsArtists.TryAdd(interactionId, responseContext.Artist);
            }
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingInteraction =
            await db.UserInteractions.FirstOrDefaultAsync(f => f.DiscordId == interactionId);

        if (existingInteraction != null)
        {
            existingInteraction.Track = responseContext.Track;
            existingInteraction.Album = responseContext.Album;
            existingInteraction.Artist = responseContext.Artist;

            db.UserInteractions.Update(existingInteraction);
            await db.SaveChangesAsync();
        }
    }

    public async Task<bool> InteractionExists(ulong contextMessageId)
    {
        if (PublicProperties.UsedCommandsResponseMessageId.ContainsKey(contextMessageId))
        {
            return true;
        }

        const string sql = "SELECT * FROM public.user_interactions WHERE discord_id = @lookupId AND discord_response_id IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var interaction = await connection.QueryFirstOrDefaultAsync<UserInteraction>(sql, new
        {
            lookupId = (decimal)contextMessageId
        });

        if (interaction == null || !interaction.DiscordResponseId.HasValue)
        {
            return false;
        }

        PublicProperties.UsedCommandsResponseMessageId.TryAdd(contextMessageId, interaction.DiscordResponseId.Value);
        return true;
    }

    public async Task<ReferencedMusic> GetReferencedMusic(ulong lookupId)
    {
        const string sql = "SELECT * FROM public.user_interactions WHERE discord_id = @lookupId OR discord_response_id = @lookupId ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var interaction = await connection.QueryFirstOrDefaultAsync<UserInteraction>(sql, new
        {
            lookupId = (decimal)lookupId
        });

        if (interaction is { Artist: not null })
        {
            return new ReferencedMusic
            {
                Artist = interaction.Artist,
                Album = interaction.Album,
                Track = interaction.Track
            };
        }

        return null;
    }

    public record ContextMessageIdAndUserId(ulong ContextId, ulong MessageId, ulong DiscordUserId);

    public async Task<ContextMessageIdAndUserId> GetMessageIdToDelete(ulong lookupId)
    {
        if (PublicProperties.UsedCommandsResponseContextId.ContainsKey(lookupId))
        {
            PublicProperties.UsedCommandsResponseContextId.TryGetValue(lookupId, out var originalMessageId);
            PublicProperties.UsedCommandDiscordUserIds.TryGetValue(originalMessageId, out var discordUserId);

            return new ContextMessageIdAndUserId(originalMessageId, lookupId, discordUserId);
        }

        // Lookup is on the command itself
        if (PublicProperties.UsedCommandsResponseMessageId.ContainsKey(lookupId) &&
            PublicProperties.UsedCommandDiscordUserIds.ContainsKey(lookupId))
        {
            PublicProperties.UsedCommandsResponseMessageId.TryGetValue(lookupId, out var responseId);
            PublicProperties.UsedCommandDiscordUserIds.TryGetValue(lookupId, out var discordUserId);

            return new ContextMessageIdAndUserId(lookupId, responseId, discordUserId);
        }

        const string sql = "SELECT * FROM public.user_interactions WHERE discord_id = @lookupId OR discord_response_id = @lookupId ";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var interaction = await connection.QueryFirstOrDefaultAsync<UserInteraction>(sql, new
        {
            lookupId = (decimal)lookupId
        });

        if (interaction?.DiscordResponseId != null && interaction.DiscordId.HasValue)
        {
            var user = await GetUserForIdAsync(interaction.UserId);
            if (user != null)
            {
                return new ContextMessageIdAndUserId(interaction.DiscordId.Value, interaction.DiscordResponseId.Value, user.DiscordUserId);
            }
        }

        return null;
    }

    public async Task<int> GetCommandExecutedAmount(int userId, string command, DateTime filterDateTime)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserInteractions
            .CountAsync(c => c.UserId == userId &&
                             c.Timestamp >= filterDateTime &&
                             c.Response == CommandResponse.Ok &&
                             c.CommandName == command);
    }

    public async Task<bool> HintShownBefore(int userId, string command)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserInteractions
            .AnyAsync(c => c.UserId == userId &&
                             c.Response == CommandResponse.Ok &&
                             c.CommandName == command &&
                             c.HintShown == true);
    }

    public async Task SetUserReactionsAsync(int userId, string[] reactions)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstAsync(f => f.UserId == userId);

        user.EmoteReactions = reactions;

        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);
    }

    public async Task<User> GetUserWithFriendsAsync(IUser discordUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.Friends)
            .ThenInclude(i => i.FriendUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
    }
    public async Task<User> GetUserWithFriendsAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.Friends)
            .ThenInclude(i => i.FriendUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public async Task<List<UserWithId>> GetAllDiscordUserIds()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var twoYears = DateTime.UtcNow.AddYears(-2);
        return await db.Users
            .AsNoTracking()
            .Where(w => w.LastUsed.HasValue && w.LastUsed >= twoYears)
            .Select(s => new UserWithId(s.DiscordUserId, s.UserId))
            .ToListAsync();
    }

    public record UserWithId(ulong DiscordUserId, int UserId);

    public async Task<User> GetFullUserAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var query = db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.Friends)
            .Include(i => i.FriendedByUsers);

        return await query
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public static async Task<string> GetNameAsync(IGuild guild, IUser user)
    {
        if (guild == null)
        {
            return user.GlobalName ?? user.Username;
        }

        var guildUser = await guild.GetUserAsync(user.Id, CacheMode.CacheOnly);

        return guildUser?.DisplayName ?? user.GlobalName ?? user.Username;
    }

    public async Task<UserType> GetRankAsync(IUser discordUser)
    {
        var user = await GetUserSettingsAsync(discordUser);

        return user?.UserType ?? UserType.User;
    }

    public async Task<string> GetUserTitleAsync(ICommandContext context)
    {
        var name = await GetNameAsync(context.Guild, context.User);
        var userType = await GetRankAsync(context.User);

        var title = name;

        title += $"{userType.UserTypeToIcon()}";

        return title;
    }

    public async Task<string> GetUserTitleAsync(IGuild guild, IUser user)
    {
        var name = await GetNameAsync(guild, user);
        var userType = await GetRankAsync(user);

        var title = name;

        title += $"{userType.UserTypeToIcon()}";

        return title;
    }

    public async Task<StringBuilder> GetFooterAsync(
        FmFooterOption footerOptions,
        UserSettingsModel userSettings,
        RecentTrack currentTrack,
        RecentTrack previousTrack,
        long totalScrobbles,
        ContextModel contextModel,
        Persistence.Domain.Models.Guild guild = null,
        IDictionary<int, FullGuildUser> guildUsers = null,
        bool useSmallMarkdown = false)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var footerContext = new TemplateContext
        {
            UserService = this,
            CurrentTrack = currentTrack,
            PreviousTrack = previousTrack,
            Connection = connection,
            Guild = guild,
            GuildUsers = guildUsers,
            WhoKnowsTrackService = this._whoKnowsTrackService,
            WhoKnowsAlbumService = this._whoKnowsAlbumService,
            WhoKnowsArtistService = this._whoKnowsArtistService,
            CountryService = this._countryService,
            PlayService = this._playService,
            UserSettings = userSettings,
            TotalScrobbles = totalScrobbles,
            DiscordContextGuild = contextModel.DiscordGuild,
            DiscordContextUser = contextModel.DiscordUser
        };

        var footer = await this._templateService.GetFooterAsync(footerOptions, footerContext);
        return CreateFooter(footer, footerContext.Genres, useSmallMarkdown);
    }

    public async Task<EmbedBuilder> GetTemplateFmAsync(
        int userId,
        UserSettingsModel userSettings,
        RecentTrack currentTrack,
        RecentTrack previousTrack,
        long totalScrobbles,
        Persistence.Domain.Models.Guild guild = null,
        IDictionary<int, FullGuildUser> guildUsers = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var footerContext = new TemplateContext
        {
            UserService = this,
            CurrentTrack = currentTrack,
            PreviousTrack = previousTrack,
            Connection = connection,
            Guild = guild,
            GuildUsers = guildUsers,
            WhoKnowsTrackService = this._whoKnowsTrackService,
            WhoKnowsAlbumService = this._whoKnowsAlbumService,
            WhoKnowsArtistService = this._whoKnowsArtistService,
            CountryService = this._countryService,
            PlayService = this._playService,
            UserSettings = userSettings,
            TotalScrobbles = totalScrobbles,

        };

        return await this._templateService.GetTemplateFmAsync(userId, footerContext);
    }

    private static int GetAgeInYears(DateTime birthDate)
    {
        var now = DateTime.UtcNow;
        var age = now.Year - birthDate.Year;

        if (now.Month < birthDate.Month || (now.Month == birthDate.Month && now.Day < birthDate.Day))
        {
            age--;
        }

        return age;
    }

    private static StringBuilder CreateFooter(IReadOnlyList<string> options, string genres, bool useSmallMarkdown)
    {
        var footer = new StringBuilder();

        var genresAdded = false;
        if (genres != null && genres.Length <= 48 && options.Count > 2)
        {
            if (useSmallMarkdown)
            {
                footer.Append("-# ");
            }
            footer.AppendLine(genres);
            if (useSmallMarkdown)
            {
                footer.Append("-# ");
            }
            genresAdded = true;
        }

        var lineLength = 0;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var nextOption = options.ElementAtOrDefault(index + 1);

            if ((lineLength > 38 || (lineLength > 28 && option.Length > 18)) && nextOption != null)
            {
                footer.AppendLine();
                if (useSmallMarkdown)
                {
                    footer.Append("-# ");
                }
                lineLength = option.Length;
                footer.Append(option);
            }
            else
            {
                if (lineLength != 0)
                {
                    footer.Append(" · ");
                }

                footer.Append(option);
                lineLength += option.Length;
            }

            if (nextOption == null)
            {
                footer.AppendLine();
            }
        }

        if (!genresAdded && genres != null)
        {
            if (useSmallMarkdown)
            {
                footer.Append("-# ");
            }
            footer.AppendLine(genres);
        }

        return footer;
    }

    public static (bool promo, string description) GetIndexCompletedUserStats(User user, IndexedUserStats stats)
    {
        var description = new StringBuilder();
        var promo = false;

        if (stats == null)
        {
            description.AppendLine("Full update could not complete, something went wrong. Please try again later.");
            return (false, description.ToString());
        }

        if (stats.UpdateError == true)
        {
            description.AppendLine($"❌ A (Last.fm) error occurred while attempting to update `{user.UserNameLastFM}`:");
            if (stats.FailedUpdates.HasFlag(UpdateType.Full))
            {
                description.AppendLine($"- Could not fetch user info from Last.fm");
            }
            if (stats.FailedUpdates.HasFlag(UpdateType.Artist))
            {
                description.AppendLine($"- Could not fetch top artists");
            }
            if (stats.FailedUpdates.HasFlag(UpdateType.Albums))
            {
                description.AppendLine($"- Could not fetch top albums");
            }
            if (stats.FailedUpdates.HasFlag(UpdateType.Tracks))
            {
                description.AppendLine($"- Could not fetch top tracks");
            }

            description.AppendLine("Please try again later.");
        }
        else
        {
            description.AppendLine($"✅ `{user.UserNameLastFM}` has been fully updated.");
            description.AppendLine();
            description.AppendLine("Cached the following playcounts:");

            if (user.UserType == UserType.User)
            {
                if (stats.PlayCount.HasValue)
                {
                    description.AppendLine($"- Last **{stats.PlayCount}** plays");
                }
                if (stats.ArtistCount.HasValue)
                {
                    description.AppendLine($"- Top **{stats.ArtistCount}** artists");
                }
                if (stats.AlbumCount.HasValue)
                {
                    description.AppendLine($"- Top **{stats.AlbumCount}** albums");
                }
                if (stats.TrackCount.HasValue)
                {
                    description.AppendLine($"- Top **{stats.TrackCount}** tracks");
                }
            }
            else
            {
                if (stats.PlayCount.HasValue)
                {
                    description.AppendLine($"- **{stats.PlayCount}** Last.fm plays");
                }
                if (stats.ArtistCount.HasValue)
                {
                    description.AppendLine($"- **{stats.ArtistCount}** top artists");
                }
                if (stats.AlbumCount.HasValue)
                {
                    description.AppendLine($"- **{stats.AlbumCount}** top albums");
                }
                if (stats.TrackCount.HasValue)
                {
                    description.AppendLine($"- **{stats.TrackCount}** top tracks");
                }

                if (stats.ImportCount != null)
                {
                    description.AppendLine();

                    var name = user.DataSource.GetAttribute<OptionAttribute>().Name;
                    description.AppendLine($"Import setting: {name}");
                    description.AppendLine($"Combined with your **{stats.ImportCount}** imported plays you have a total of **{stats.TotalCount}** plays.");
                }
            }

            if (user.UserType == UserType.User &&
                (stats.PlayCount >= 49900 ||
                 stats.TrackCount >= 5900 ||
                 stats.AlbumCount >= 4900 ||
                 stats.ArtistCount >= 3900))
            {
                description.AppendLine();
                description.AppendLine($"Want your full Last.fm history to be stored in the bot? [{Constants.GetSupporterButton}]({Constants.GetSupporterDiscordLink}).");
                promo = true;
            }
        }

        return (promo, description.ToString());
    }

    public async Task SetLastFm(IUser discordUser, User newUserSettings, bool updateSessionKey = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

        if (user == null)
        {
            var newUser = new User
            {
                DiscordUserId = discordUser.Id,
                UserType = UserType.User,
                UserNameLastFM = newUserSettings.UserNameLastFM,
                FmEmbedType = newUserSettings.FmEmbedType,
                SessionKeyLastFm = newUserSettings.SessionKeyLastFm,
                DataSource = DataSource.LastFm,
                PrivacyLevel = PrivacyLevel.Server,
                FmFooterOptions = FmFooterOption.TotalScrobbles
            };

            await db.Users.AddAsync(newUser);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in SetLastFM");
                throw;
            }

            var createdUser = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
            if (createdUser != null)
            {
                PublicProperties.RegisteredUsers.TryAdd(createdUser.DiscordUserId, createdUser.UserId);
            }
        }
        else
        {
            user.UserNameLastFM = newUserSettings.UserNameLastFM;
            user.FmEmbedType = newUserSettings.FmEmbedType;
            user.Mode = newUserSettings.Mode;
            if (updateSessionKey)
            {
                user.SessionKeyLastFm = newUserSettings.SessionKeyLastFm;
            }

            db.Update(user);

            await db.SaveChangesAsync();

            RemoveUserFromCache(user);
        }
    }

    public async Task<bool> GetAndStoreAuthSession(IUser contextUser, string token)
    {
        Log.Information("LastfmAuth: Login session starting for {user} | {discordUserId}", contextUser.Username, contextUser.Id);

        var loginDelay = 8000;
        for (var i = 0; i < 11; i++)
        {
            await Task.Delay(loginDelay);

            var authSession = await this._dataSourceFactory.GetAuthSession(token);

            if (authSession.Success)
            {
                var userSettings = new User
                {
                    UserNameLastFM = authSession.Content.Session.Name,
                    DataSource = DataSource.LastFm,
                    SessionKeyLastFm = authSession.Content.Session.Key,
                };

                Log.Information("LastfmAuth: User {userName} logged in with auth session (discordUserId: {discordUserId})", authSession.Content.Session.Name, contextUser.Id);
                await SetLastFm(contextUser, userSettings, true);
                return true;
            }

            if (!authSession.Success && i == 10)
            {
                Log.Information("LastfmAuth: Login timed out or auth not successful (discordUserId: {discordUserId})", contextUser.Id);
                return false;
            }
            if (!authSession.Success)
            {
                loginDelay += 3000;
            }
        }

        return false;
    }

    public async Task<PrivacyLevel> SetPrivacyLevel(int userId, PrivacyLevel privacyLevel)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users.FirstAsync(f => f.UserId == userId);

        user.PrivacyLevel = privacyLevel;
        db.Entry(user).State = EntityState.Modified;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user.PrivacyLevel;
    }

    public async Task<string> SetTimeZone(int userId, string timeZone)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users.FirstAsync(f => f.UserId == userId);

        if (timeZone == "null")
        {
            timeZone = null;
        }

        user.TimeZone = timeZone;
        db.Entry(user).State = EntityState.Modified;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user.TimeZone;
    }

    public async Task<User> SetSettings(User userToUpdate, FmEmbedType embedType, FmCountType? countType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.FmEmbedType = embedType;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(userToUpdate);

        return user;
    }

    public async Task<User> SetDataSource(User userToUpdate, DataSource dataSource)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        userToUpdate.DataSource = dataSource;

        user.DataSource = dataSource;
        db.Entry(user).State = EntityState.Modified;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(userToUpdate);

        return user;
    }

    public async Task<User> SetFooterOptions(User userToUpdate, FmFooterOption fmFooterOption)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.FmFooterOptions = fmFooterOption;
        db.Entry(user).State = EntityState.Modified;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user;
    }

    public async Task<User> SetResponseMode(User userToUpdate, ResponseMode mode)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.Mode = mode;

        db.Update(user);
        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user;
    }

    public async Task DeleteUser(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        try
        {
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.UserId == userId);

            if (user == null)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await using var deleteRelatedTables = new NpgsqlCommand(
                $"DELETE FROM public.user_artists WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.user_albums WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.user_tracks WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.friends WHERE user_id = {user.UserId} OR friend_user_id = {user.UserId}; " +
                $"UPDATE public.featured_logs SET user_id = NULL WHERE user_id = {user.UserId}; ",
                connection);

            await deleteRelatedTables.ExecuteNonQueryAsync();

            db.Users.Remove(user);

            await db.SaveChangesAsync();

            RemoveUserFromCache(user);

            PublicProperties.RegisteredUsers.TryRemove(user.DiscordUserId, out _);

            Log.Information("Deleted user {userId} - {discordUserId} - {userNameLastFm}", user.UserId, user.DiscordUserId, user.UserNameLastFM);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while deleting user!");
        }
    }

    public async Task<bool?> ToggleRymAsync(User user)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var userToUpdate = await db.Users.FindAsync(user.UserId);

        if (userToUpdate.RymEnabled != true)
        {
            userToUpdate.RymEnabled = true;
        }
        else
        {
            userToUpdate.RymEnabled = false;
        }

        db.Update(userToUpdate);
        db.Entry(userToUpdate).State = EntityState.Modified;

        await db.SaveChangesAsync();

        RemoveUserFromCache(userToUpdate);

        return userToUpdate.RymEnabled;
    }

    public async Task ToggleBotScrobblingAsync(int userId, bool? disabled)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users.FirstAsync(f => f.UserId == userId);

        user.MusicBotTrackingDisabled = disabled;
        db.Entry(user).State = EntityState.Modified;

        db.Update(user);

        RemoveUserFromCache(user);

        await db.SaveChangesAsync();
    }

    public async Task<int> GetTotalUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .CountAsync();
    }

    public async Task<int> GetTotalActiveUserCountAsync(int daysToGoBack)
    {
        var filterDate = DateTime.UtcNow.AddDays(-daysToGoBack);

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .CountAsync(c => c.LastUsed != null &&
                             c.LastUsed >= filterDate);
    }

    public async Task<int> GetTotalAuthorizedUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => w.SessionKeyLastFm != null)
            .CountAsync();
    }

    public async Task<int> GetTotalGroupedLastfmUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .GroupBy(g => g.UserNameLastFM)
            .CountAsync();
    }

    public async Task<int> DeleteInactiveUsers()
    {
        var deletedInactiveUsers = 0;

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var inactiveUsers = await db.InactiveUserLog
            .AsQueryable()
            .Include(i => i.User)
            .Where(w => w.ResponseStatus == ResponseStatus.MissingParameters)
            .GroupBy(g => g.UserId)
            .ToListAsync();

        foreach (var inactiveUser in inactiveUsers)
        {
            if (inactiveUser.First().User != null && (inactiveUser.First().User.LastUsed == null || inactiveUser.First().User.LastUsed < DateTime.UtcNow.AddDays(-30)))
            {
                var userExists =
                    await this._dataSourceFactory.LastFmUserExistsAsync(inactiveUser.First().UserNameLastFM);
                var profile =
                    await this._dataSourceFactory.GetLfmUserInfoAsync(inactiveUser.First().UserNameLastFM);

                if (!userExists && profile == null)
                {
                    await this._friendsService.RemoveAllFriendsAsync(inactiveUser.Key);
                    await this._friendsService.RemoveUserFromOtherFriendsAsync(inactiveUser.Key);

                    await DeleteUser(inactiveUser.Key);

                    Log.Information("DeleteInactiveUsers: User {userNameLastFm} | {userId} | {discordUserId} deleted", inactiveUser.First().User.UserNameLastFM, inactiveUser.Key, inactiveUser.First().User.DiscordUserId);
                    deletedInactiveUsers++;

                    var otherUsers = await this._adminService.GetUsersWithLfmUsernameAsync(inactiveUser.First().User.UserNameLastFM);
                    foreach (var otherUser in otherUsers.Where(w => w.UserId != inactiveUser.Key && (w.LastUsed == null || w.LastUsed < DateTime.UtcNow.AddDays(-30))))
                    {
                        await this._friendsService.RemoveAllFriendsAsync(otherUser.UserId);
                        await this._friendsService.RemoveUserFromOtherFriendsAsync(otherUser.UserId);

                        await DeleteUser(otherUser.UserId);

                        Log.Information("DeleteInactiveUsers: OtherUser {userNameLastFm} | {userId} | {discordUserId} deleted", otherUser.UserNameLastFM, otherUser.UserId, otherUser.DiscordUserId);
                        deletedInactiveUsers++;
                    }
                }
                else
                {
                    Log.Information("DeleteInactiveUsers: User {userNameLastFm} exists, so deletion cancelled", inactiveUser.First().User.UserNameLastFM);
                }

                Thread.Sleep(800);
            }
        }

        return deletedInactiveUsers;
    }

    public async Task<int> DeleteOldDuplicateUsers()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var counter = 0;

        var groupedUsers = await db.Users
            .GroupBy(g => g.UserNameLastFM)
            .ToListAsync();

        var usersWithTooManyAccounts = groupedUsers
            .Where(w => w.Count() > 1 && w.Any(a => a.LastUsed > DateTime.UtcNow.AddMonths(-12)));

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        foreach (var groupedUser in usersWithTooManyAccounts)
        {
            var lastUsedAccount = groupedUser.OrderByDescending(o => o.LastUsed).First();

            var anyDeleted = false;
            foreach (var oldUnusedAccount in groupedUser
                         .Where(w => w.LastUsed == null || w.LastUsed < DateTime.UtcNow.AddMonths(-24)))
            {
                //await PlayRepository.MoveFeaturedLogs(oldUnusedAccount.UserId, lastUsedAccount.UserId, connection);
                //await PlayRepository.MoveFriends(oldUnusedAccount.UserId, lastUsedAccount.UserId, connection);

                //await this._friendsService.RemoveAllFriendsAsync(oldUnusedAccount.UserId);
                //await DeleteUser(oldUnusedAccount.UserId);

                Log.Information("DeleteOldDuplicateUsers: User {userNameLastFm} | {userId} | {discordUserId} - Last used {lastUsed}", oldUnusedAccount.UserNameLastFM, oldUnusedAccount.UserId, oldUnusedAccount.DiscordUserId, oldUnusedAccount.LastUsed);
                counter++;
                anyDeleted = true;
            }

            if (anyDeleted)
            {
                Log.Information("DeleteOldDuplicateUsers: Main {userNameLastFm} | {userId} | {discordUserId} - Last used {lastUsed}", lastUsedAccount.UserNameLastFM, lastUsedAccount.UserId, lastUsedAccount.DiscordUserId, lastUsedAccount.LastUsed);
            }
        }

        await connection.CloseAsync();

        return counter;
    }
}
