using System.Threading.Tasks;
using System;
using Npgsql;
using Serilog;
using Dapper;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Repositories;

public class UserRepository
{
    public static async Task<ImportUser> GetImportUserForLastFmUserName(string lastFmUserName, NpgsqlConnection connection)
    {   
        const string getUserQuery = "SELECT user_id, discord_user_id, user_name_last_fm, data_source, " +
                                    "(SELECT time_played FROM user_play_ts WHERE play_source != 0 ORDER BY time_played DESC LIMIT 1) AS last_import_play " +
                                    "FROM users " +
                                    "WHERE UPPER(user_name_last_fm) = UPPER(@lastFmUserName) " +
                                    "AND last_used is not null " +
                                    "AND data_source != 1 " +
                                    "ORDER BY last_used DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var user = await connection.QueryFirstOrDefaultAsync<ImportUser>(getUserQuery, new
        {
            lastFmUserName
        });

        return user;
    }

    public static async Task<ImportUser> GetImportUserForLastFmUserId(int userId, NpgsqlConnection connection)
    {   
        const string getUserQuery = "SELECT user_id, discord_user_id, user_name_last_fm, data_source, " +
                                    "(SELECT time_played FROM user_play_ts WHERE play_source != 0 ORDER BY time_played DESC LIMIT 1) AS last_import_play " +
                                    "FROM users " +
                                    "WHERE user_id = @userId " +
                                    "AND last_used is not null " +
                                    "AND data_source != 1 " +
                                    "ORDER BY last_used DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var user = await connection.QueryFirstOrDefaultAsync<ImportUser>(getUserQuery, new
        {
            userId
        });

        return user;
    }

    public static async Task SetUserIndexTime(int userId, DateTime now, DateTime lastScrobble, NpgsqlConnection connection)
    {
        Log.Information($"Setting user index time for user {userId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now:u}', last_updated='{now:u}', last_scrobble_update = '{lastScrobble:u}' WHERE user_id = {userId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public static async Task<DateTime> SetUserSignUpTime(int userId, DateTime signUpDateTime, NpgsqlConnection connection,
        bool lastfmPro)
    {
        Log.Information($"Setting user index signup time ({signUpDateTime}) for user {userId}");

        await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET registered_last_fm='{signUpDateTime:u}', lastfm_pro = '{lastfmPro}' WHERE user_id = {userId};", connection);
        await setIndexTime.ExecuteNonQueryAsync().ConfigureAwait(false);

        return signUpDateTime;
    }
}