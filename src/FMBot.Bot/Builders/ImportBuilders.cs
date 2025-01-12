using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class ImportBuilders
{
    private readonly PlayService _playService;

    public ImportBuilders(PlayService playService)
    {
        this._playService = playService;
    }

    public static ResponseModel ImportSupporterRequired(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ContextUser.UserType == UserType.User)
        {
            response.Embed.WithDescription($"Only supporters can import and use their Spotify or Apple Music history.");

            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault)
                .WithButton("Import info", style: ButtonStyle.Link, url: "https://fmbot.xyz/importing/");
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public ResponseModel ImportInstructionsPickSource(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);


        var description = new StringBuilder();

        description.AppendLine("What music service history would you like to import?");

        response.Components = new ComponentBuilder()
            .WithButton("Spotify", InteractionConstants.ImportInstructionsSpotify,
                emote: Emote.Parse("<:spotify:882221219334725662>"))
            .WithButton("Apple Music", InteractionConstants.ImportInstructionsAppleMusic,
                emote: Emote.Parse("<:apple_music:1218182727149420544>"));

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public async Task<ResponseModel> GetSpotifyImportInstructions(ContextModel context, bool warnAgainstPublicFiles = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.SpotifyColorGreen);

        response.Embed.WithTitle("Spotify import instructions");

        var requestDescription = new StringBuilder();

        requestDescription.AppendLine(
            "1. Go to your **[Spotify privacy settings](https://www.spotify.com/us/account/privacy/)**");
        requestDescription.AppendLine("2. Scroll down to \"Download your data\"");
        requestDescription.AppendLine("3. Select **Extended streaming history**");
        requestDescription.AppendLine("4. De-select the other options");
        requestDescription.AppendLine("5. Press request data");
        requestDescription.AppendLine("6. Confirm your data request through your email");
        requestDescription.AppendLine("7. Wait up to 30 days for Spotify to deliver your files");
        response.Embed.AddField($"{DiscordConstants.Spotify} Requesting your data from Spotify",
            requestDescription.ToString());

        var importDescription = new StringBuilder();

        importDescription.AppendLine("1. Download the file Spotify provided");
        importDescription.AppendLine($"2. Use the `/import spotify` slash command and add the `.zip` file as an attachment through the options");
        importDescription.AppendLine("3. Having issues? You can also attach each `.json` file separately");
        response.Embed.AddField($"{DiscordConstants.Imports} Importing your data into .fmbot",
            importDescription.ToString());

        var notesDescription = new StringBuilder();
        notesDescription.AppendLine(
            "- We filter out duplicates and skips, so don't worry about submitting the same file twice");
        notesDescription.AppendLine("- The importing service is only available with an active supporter subscription");
        response.Embed.AddField("📝 Notes", notesDescription.ToString());

        var allPlays = await this._playService.GetAllUserPlays(context.ContextUser.UserId, false);
        var count = allPlays.Count(w => w.PlaySource == PlaySource.SpotifyImport);
        if (count > 0)
        {
            response.Embed.AddField($"⚙️ Your imported Spotify plays",
                $"You have already imported **{count}** {StringExtensions.GetPlaysString(count)}. To configure how these are used and combined with your Last.fm scrobbles, use the button below.");
        }

        var footer = new StringBuilder();
        if (warnAgainstPublicFiles)
        {
            footer.AppendLine("Do not share your import files publicly");
        }

        footer.AppendLine("Having issues with importing? Please open a help thread on discord.gg/fmbot");

        response.Embed.WithFooter(footer.ToString());
        response.Components = new ComponentBuilder()
            .WithButton("Spotify privacy page", style: ButtonStyle.Link,
                url: "https://www.spotify.com/us/account/privacy/");

        if (count > 0)
        {
            response.Components.WithButton("Manage import settings", InteractionConstants.ImportManage,
                style: ButtonStyle.Secondary);
        }

        return response;
    }

    public async Task<ResponseModel> GetAppleMusicImportInstructions(ContextModel context, bool warnAgainstPublicFiles = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        response.Embed.WithTitle("Apple Music import instructions");

        var requestDescription = new StringBuilder();
        requestDescription.AppendLine("1. Go to your **[Apple privacy settings](https://privacy.apple.com/)**");
        requestDescription.AppendLine("2. Sign in to your account");
        requestDescription.AppendLine("3. Click on **Request a copy of your data**");
        requestDescription.AppendLine("4. Select **Apple Media Services Information**");
        requestDescription.AppendLine("5. De-select the other options");
        requestDescription.AppendLine("6. Press **Continue**");
        requestDescription.AppendLine("7. Press **Complete request**");
        requestDescription.AppendLine("8. Wait up to 7 days for Apple to deliver your files");
        response.Embed.AddField($"{DiscordConstants.AppleMusic} Requesting your data from Apple",
            requestDescription.ToString());

        var importDescription = new StringBuilder();
        importDescription.AppendLine("1. Download the file Apple provided");
        importDescription.AppendLine(
            "2. Use the `/import applemusic` slash command and add the `.zip` file as an attachment through the options");
        importDescription.AppendLine(
            "3. Got multiple zip files? You can try them all until one succeeds. Only one of them contains your play history");
        importDescription.AppendLine(
            "4. Having issues? You can also attach the `Apple Music Play Activity.csv` file separately");

        response.Embed.AddField($"{DiscordConstants.Imports} Importing your data into .fmbot",
            importDescription.ToString());

        var notes = new StringBuilder();
        notes.AppendLine(
            "- Apple provides their history data without artist names. We try to find these as best as possible based on the album and track name.");
        notes.AppendLine(
            "- Exceeding Discord file limits? Try on [our server](https://discord.gg/fmbot) in #commands.");
        notes.AppendLine("- The importing service is only available with an active supporter subscription");
        response.Embed.AddField("📝 Notes", notes.ToString());

        var allPlays = await this._playService.GetAllUserPlays(context.ContextUser.UserId, false);
        var count = allPlays.Count(w => w.PlaySource == PlaySource.AppleMusicImport);
        if (count > 0)
        {
            response.Embed.AddField($"⚙️ Your imported Apple Music plays",
                $"You have already imported **{count}** {StringExtensions.GetPlaysString(count)}. To configure how these are used and combined with your Last.fm scrobbles, use the button below.");
        }

        var footer = new StringBuilder();
        if (warnAgainstPublicFiles)
        {
            footer.AppendLine("Do not share your import files publicly");
        }

        footer.AppendLine("Having issues with importing? Please open a help thread on discord.gg/fmbot");

        response.Embed.WithFooter(footer.ToString());

        response.Components = new ComponentBuilder()
            .WithButton("Apple Data and Privacy", style: ButtonStyle.Link, url: "https://privacy.apple.com/");

        if (count > 0)
        {
            response.Components.WithButton("Manage import settings", InteractionConstants.ImportManage,
                style: ButtonStyle.Secondary);
        }

        return response;
    }

    public async Task<string> GetImportedYears(int userId, PlaySource playSource)
    {
        var years = new StringBuilder();
        var allPlays = await this._playService
            .GetAllUserPlays(userId, false);

        var yearGroups = allPlays
            .Where(w => w.PlaySource == playSource)
            .OrderBy(o => o.TimePlayed)
            .GroupBy(g => g.TimePlayed.Year);

        foreach (var year in yearGroups)
        {
            var playcount = year.Count();
            years.AppendLine(
                $"**`{year.Key}`** " +
                $"- **{playcount}** {StringExtensions.GetPlaysString(playcount)}");
        }

        return years.Length > 0 ? years.ToString() : null;
    }

    public async Task<ResponseModel> ImportModify(ContextModel context, int userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var importModify = new SelectMenuBuilder()
            .WithPlaceholder("Select modification")
            .WithCustomId(InteractionConstants.ImportModify)
            .WithMinValues(1)
            .WithMaxValues(1);

        var allPlays = await this._playService.GetAllUserPlays(userId, false);
        var hasImported = allPlays.Any(a =>
            a.PlaySource == PlaySource.SpotifyImport || a.PlaySource == PlaySource.AppleMusicImport);

        if (!hasImported && context.ContextUser.DataSource == DataSource.LastFm)
        {
            importModify.IsDisabled = true;
        }

        foreach (var option in ((ImportModifyPick[])Enum.GetValues(typeof(ImportModifyPick))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var value = Enum.GetName(option);

            importModify.AddOption(new SelectMenuOptionBuilder(name, value));
        }

        response.Components = new ComponentBuilder().WithSelectMenu(importModify);

        response.Embed.WithAuthor("Modify your .fmbot imports");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var importSource = "import data";
        if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport) &&
            allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
        {
            importSource = "Apple Music & Spotify";
        }
        else if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
        {
            importSource = "Apple Music";
        }
        else if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
        {
            importSource = "Spotify";
        }

        var embedDescription = new StringBuilder();

        embedDescription.AppendLine("Modify your imported .fmbot data with the options below.");
        embedDescription.AppendLine();
        embedDescription.AppendLine(
            "Please keep in mind that this only modifies imports that are stored in .fmbot. It doesn't modify any of your Last.fm scrobbles or data.");
        embedDescription.AppendLine();

        if (!hasImported)
        {
            embedDescription.AppendLine();
            embedDescription.AppendLine(
                "Run the `.import` command to see how to request your data and to get started with imports. " +
                "After importing you'll be able to change these settings.");
        }
        else
        {
            var storedDescription = new StringBuilder();
            if (allPlays.Any(a => a.PlaySource == PlaySource.AppleMusicImport))
            {
                storedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.AppleMusicImport)} imported Apple Music plays");
            }

            if (allPlays.Any(a => a.PlaySource == PlaySource.SpotifyImport))
            {
                storedDescription.AppendLine(
                    $"- {allPlays.Count(c => c.PlaySource == PlaySource.SpotifyImport)} imported Spotify plays");
            }

            response.Embed.AddField($"{DiscordConstants.Imports} Your stored imports", storedDescription.ToString());

            var noteDescription = new StringBuilder();
            if (context.ContextUser.DataSource == DataSource.ImportThenFullLastFm)
            {
                noteDescription.AppendLine(
                    "Because you have selected the mode **Imports, then full Last.fm** not all imports might be used. This mode only uses your imports up until you started scrobbling on Last.fm.");
            }

            if (noteDescription.Length > 0)
            {
                response.Embed.AddField($"📝 How your imports are used", noteDescription.ToString());
            }
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }
}
