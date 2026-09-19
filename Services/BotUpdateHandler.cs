using DatingMatchBot.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DatingMatchBot.Services;

public sealed class BotUpdateHandler(MatchmakingService matchmaking) : IUpdateHandler
{
    private const string NextText = "Next";
    private const string StayText = "დარჩი";
    private const string StopText = "Stop";
    private const string EditProfileText = "პროფილის შეცვლა";
    private const string CancelEditText = "გაუქმება";
    private const string SkipPhotoText = "გამოტოვება";
    private const string MaleText = "კაცი";
    private const string FemaleText = "ქალი";
    private const string OtherText = "სხვა";
    private const string LookingForMaleText = "ვეძებ კაცს";
    private const string LookingForFemaleText = "ვეძებ ქალს";
    private const string LookingForAnyText = "ვეძებ ყველას";

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message || message.From is null)
        {
            return;
        }

        var chatId = message.Chat.Id;
        var text = message.Text?.Trim();
        var user = matchmaking.GetOrCreate(message.From, chatId);

        if (IsCommand(text, "start"))
        {
            matchmaking.RestartRegistration(message.From, chatId);
            var stats = matchmaking.GetStats();
            await botClient.SendMessage(
                chatId,
                $"გამარჯობა!\n{FormatStats(stats)}\n\nდაწერე შენი სახელი.",
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            return;
        }

        if (IsCommand(text, "stats"))
        {
            await botClient.SendMessage(
                chatId,
                FormatStats(matchmaking.GetStats()),
                replyMarkup: user.IsRegistered ? MainKeyboard() : null,
                cancellationToken: cancellationToken);
            return;
        }

        if (IsCommand(text, "profile"))
        {
            await SendOwnProfile(botClient, chatId, user, cancellationToken);
            return;
        }

        if (IsCommand(text, "settings"))
        {
            await OpenSettings(botClient, chatId, user, cancellationToken);
            return;
        }

        if (IsCommand(text, "editprofile") || text is EditProfileText)
        {
            await StartProfileEdit(botClient, message.From, chatId, user, cancellationToken);
            return;
        }

        if (IsCommand(text, "stop") || text is StopText)
        {
            await StopChat(botClient, chatId, cancellationToken);
            return;
        }

        if (IsCommand(text, "next") || text is NextText)
        {
            await FindNext(botClient, chatId, cancellationToken);
            return;
        }

        if (IsCommand(text, "cancel") || text is CancelEditText)
        {
            await CancelProfileEdit(botClient, chatId, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingName)
        {
            await HandleName(botClient, chatId, text, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingGender)
        {
            await HandleGender(botClient, chatId, text, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingLookingFor)
        {
            await HandleLookingFor(botClient, chatId, text, user, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingCity)
        {
            await HandleCity(botClient, chatId, text, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingBirthYear)
        {
            await HandleBirthYear(botClient, chatId, text, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingBirthMonth)
        {
            await HandleBirthMonth(botClient, chatId, text, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingAbout)
        {
            await HandleAbout(botClient, chatId, text, cancellationToken);
            return;
        }

        if (user.Step == RegistrationStep.AwaitingPhoto)
        {
            await HandlePhoto(botClient, message, cancellationToken);
            return;
        }

        if (text is StayText)
        {
            await AcceptMatch(botClient, chatId, cancellationToken);
            return;
        }

        await RelayMessage(botClient, message, cancellationToken);
    }

    public Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        var error = exception switch
        {
            ApiRequestException apiException => $"Telegram API Error [{apiException.ErrorCode}]: {apiException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(error);
        return Task.CompletedTask;
    }

    private async Task HandleName(ITelegramBotClient botClient, long chatId, string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith('/'))
        {
            await botClient.SendMessage(chatId, "სახელი ტექსტით მომწერე.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
            return;
        }

        matchmaking.SetName(chatId, text);
        await botClient.SendMessage(chatId, "ახლა აირჩიე სქესი.", replyMarkup: GenderKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
    }

    private async Task HandleGender(ITelegramBotClient botClient, long chatId, string? text, CancellationToken cancellationToken)
    {
        var gender = text switch
        {
            MaleText => Gender.Male,
            FemaleText => Gender.Female,
            OtherText => Gender.Other,
            _ => (Gender?)null
        };

        if (gender is null)
        {
            await botClient.SendMessage(chatId, "გთხოვ, ღილაკებიდან აირჩიე სქესი.", replyMarkup: GenderKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
            return;
        }

        matchmaking.SetGender(chatId, gender.Value);
        await botClient.SendMessage(chatId, "ვის ეძებ?", replyMarkup: LookingForKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
    }

    private async Task OpenSettings(ITelegramBotClient botClient, long chatId, BotUser user, CancellationToken cancellationToken)
    {
        if (!user.IsRegistered)
        {
            await botClient.SendMessage(chatId, "settings-ის შეცვლამდე ჯერ რეგისტრაცია დაასრულე /start-ით.", cancellationToken: cancellationToken);
            return;
        }

        matchmaking.BeginSettingsEdit(chatId);
        await botClient.SendMessage(chatId, "ვის ეძებ? აირჩიე ახალი პარამეტრი.", replyMarkup: LookingForKeyboard(), cancellationToken: cancellationToken);
    }

    private async Task StartProfileEdit(
        ITelegramBotClient botClient,
        User telegramUser,
        long chatId,
        BotUser user,
        CancellationToken cancellationToken)
    {
        if (!user.IsRegistered)
        {
            await botClient.SendMessage(chatId, "პროფილის შეცვლამდე ჯერ რეგისტრაცია დაასრულე /start-ით.", cancellationToken: cancellationToken);
            return;
        }

        matchmaking.BeginProfileEdit(telegramUser, chatId);
        await botClient.SendMessage(
            chatId,
            "დავიწყოთ პროფილის შეცვლა. დაწერე შენი სახელი.",
            replyMarkup: CancelOnlyKeyboard(),
            cancellationToken: cancellationToken);
    }

    private async Task CancelProfileEdit(
        ITelegramBotClient botClient,
        long chatId,
        CancellationToken cancellationToken)
    {
        var restoredUser = matchmaking.CancelProfileEdit(chatId);
        if (restoredUser is null)
        {
            await botClient.SendMessage(
                chatId,
                "გასაუქმებელი პროფილის ცვლილება არ არის.",
                replyMarkup: MainKeyboard(),
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(
            chatId,
            "პროფილის შეცვლა გაუქმდა. ძველი პროფილი აღდგენილია.",
            replyMarkup: MainKeyboard(),
            cancellationToken: cancellationToken);
        await SendProfileCard(botClient, chatId, restoredUser, MainKeyboard(), cancellationToken);
    }

    private async Task HandleLookingFor(
        ITelegramBotClient botClient,
        long chatId,
        string? text,
        BotUser user,
        CancellationToken cancellationToken)
    {
        var lookingFor = text switch
        {
            LookingForMaleText => GenderPreference.Male,
            LookingForFemaleText => GenderPreference.Female,
            LookingForAnyText => GenderPreference.Any,
            _ => (GenderPreference?)null
        };

        if (lookingFor is null)
        {
            await botClient.SendMessage(chatId, "გთხოვ, ღილაკებიდან აირჩიე ვის ეძებ.", replyMarkup: LookingForKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
            return;
        }

        var updatedUser = matchmaking.SetLookingFor(chatId, lookingFor.Value);

        if (user.IsEditingSettings)
        {
            await botClient.SendMessage(
                chatId,
                $"პარამეტრი შეიცვალა: {FormatLookingFor(updatedUser.LookingFor)}",
                replyMarkup: MainKeyboard(),
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(chatId, "რომელ ქალაქში ხარ?", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
    }

    private async Task HandleCity(ITelegramBotClient botClient, long chatId, string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith('/'))
        {
            await botClient.SendMessage(chatId, "ქალაქი ტექსტით მომწერე.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
            return;
        }

        matchmaking.SetCity(chatId, text);
        await botClient.SendMessage(chatId, "დაწერე დაბადების წელი, მაგალითად 1998.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
    }

    private async Task HandleBirthYear(ITelegramBotClient botClient, long chatId, string? text, CancellationToken cancellationToken)
    {
        var maxYear = DateTime.UtcNow.Year - 18;
        const int minYear = 1940;

        if (!int.TryParse(text, out var birthYear) || birthYear < minYear || birthYear > maxYear)
        {
            await botClient.SendMessage(chatId, $"დაბადების წელი უნდა იყოს {minYear}-{maxYear} შუალედში.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
            return;
        }

        matchmaking.SetBirthYear(chatId, birthYear);
        await botClient.SendMessage(chatId, "აირჩიე დაბადების თვე.", replyMarkup: BirthMonthKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
    }

    private async Task HandleBirthMonth(ITelegramBotClient botClient, long chatId, string? text, CancellationToken cancellationToken)
    {
        if (!int.TryParse(text, out var birthMonth) || birthMonth < 1 || birthMonth > 12)
        {
            await botClient.SendMessage(chatId, "გთხოვ, აირჩიე თვე 1-დან 12-მდე.", replyMarkup: BirthMonthKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
            return;
        }

        matchmaking.SetBirthMonth(chatId, birthMonth);
        await botClient.SendMessage(chatId, "დაწერე მოკლედ შენ შესახებ.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
    }

    private async Task HandleAbout(ITelegramBotClient botClient, long chatId, string? text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.StartsWith('/'))
        {
            await botClient.SendMessage(chatId, "შენ შესახებ ტექსტი მომწერე.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
            return;
        }

        if (text.Length > 500)
        {
            await botClient.SendMessage(chatId, "აღწერა მაქსიმუმ 500 სიმბოლო იყოს.", replyMarkup: CancelKeyboardOrRemove(chatId), cancellationToken: cancellationToken);
            return;
        }

        matchmaking.SetAbout(chatId, text);
        await botClient.SendMessage(chatId, "ახლა ატვირთე ფოტო ან დააჭირე გამოტოვებას.", replyMarkup: SkipPhotoKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
    }

    private async Task HandlePhoto(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var text = message.Text?.Trim();

        if (text is SkipPhotoText)
        {
            var user = matchmaking.CompleteRegistration(chatId, null);
            await RegistrationCompleted(botClient, chatId, user, cancellationToken);
            return;
        }

        var photo = message.Photo?
            .OrderByDescending(item => item.FileSize ?? 0)
            .FirstOrDefault();

        if (photo is null)
        {
            await botClient.SendMessage(chatId, "გამომიგზავნე ფოტო ან დააჭირე გამოტოვებას.", replyMarkup: SkipPhotoKeyboard(matchmaking.IsEditingProfile(chatId)), cancellationToken: cancellationToken);
            return;
        }

        var completedUser = matchmaking.CompleteRegistration(chatId, photo.FileId);
        await RegistrationCompleted(botClient, chatId, completedUser, cancellationToken);
    }

    private async Task RegistrationCompleted(ITelegramBotClient botClient, long chatId, BotUser user, CancellationToken cancellationToken)
    {
        var stats = matchmaking.GetStats();
        await botClient.SendMessage(
            chatId,
            $"რეგისტრაცია დასრულდა.\n{FormatStats(stats)}\n\nშენი პროფილი ასე გამოჩნდება:",
            replyMarkup: MainKeyboard(),
            cancellationToken: cancellationToken);

        await SendProfileCard(botClient, chatId, user, MainKeyboard(), cancellationToken);
    }

    private async Task FindNext(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var result = matchmaking.FindNext(chatId);

        if (result.DisconnectedPartner is not null)
        {
            await botClient.SendMessage(result.DisconnectedPartner.ChatId, "პარტნიორმა Next დააჭირა. შენ ისევ აქტიურ ძებნაში ხარ.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
        }

        if (result.Status == MatchSearchStatus.NotRegistered)
        {
            await botClient.SendMessage(
                chatId,
                "ჯერ რეგისტრაცია უნდა დაასრულო. გააგრძელე მიმდინარე კითხვაზე პასუხით ან თავიდან დაიწყე /start-ით.",
                cancellationToken: cancellationToken);
            return;
        }

        if (result.Status == MatchSearchStatus.Waiting)
        {
            await botClient.SendMessage(chatId, "ამ მომენტში თავისუფალი აქტიური მომხმარებელი ვერ ვიპოვე. ცოტა ხანში ისევ დააჭირე Next-ს.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(chatId, "ვიპოვე ახალი პროფილი. აირჩიე დარჩი ან Next.", replyMarkup: MatchDecisionKeyboard(), cancellationToken: cancellationToken);
        await SendProfileCard(botClient, chatId, result.Partner!, MatchDecisionKeyboard(), cancellationToken);

        await botClient.SendMessage(result.Partner!.ChatId, "ვიპოვე ახალი პროფილი. აირჩიე დარჩი ან Next.", replyMarkup: MatchDecisionKeyboard(), cancellationToken: cancellationToken);
        await SendProfileCard(botClient, result.Partner.ChatId, result.User!, MatchDecisionKeyboard(), cancellationToken);
    }

    private async Task AcceptMatch(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var result = matchmaking.AcceptMatch(chatId);

        if (result.Partner is null)
        {
            await botClient.SendMessage(chatId, "ჯერ პარტნიორი არ გყავს. დააჭირე Next-ს.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(
            chatId,
            result.BothAccepted ? "ორივემ აირჩიეთ დარჩენა. ახლა შეგიძლია მიწერო." : "შენ დარჩი. ველოდებით პარტნიორის არჩევანს.",
            replyMarkup: MainKeyboard(),
            cancellationToken: cancellationToken);

        if (result.BothAccepted)
        {
            await botClient.SendMessage(result.Partner.ChatId, "ორივემ აირჩიეთ დარჩენა. ახლა შეგიძლიათ მიწეროთ.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
        }
    }

    private async Task StopChat(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken)
    {
        var result = matchmaking.Stop(chatId);

        if (result.DisconnectedPartner is not null)
        {
            await botClient.SendMessage(result.DisconnectedPartner.ChatId, "პარტნიორმა ჩატი შეწყვიტა. ახალი ადამიანის მოსაძებნად დააჭირე Next-ს.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
        }

        await botClient.SendMessage(chatId, "შენ აღარ ხარ აქტიურ ძებნაში. დაბრუნებისთვის დააჭირე Next-ს.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
    }

    private async Task RelayMessage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var partner = matchmaking.GetPartner(message.Chat.Id);

        if (partner is null)
        {
            await botClient.SendMessage(message.Chat.Id, "ჯერ პარტნიორი არ გყავს. დააჭირე Next-ს.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
            return;
        }

        if (!matchmaking.CanChat(message.Chat.Id))
        {
            await botClient.SendMessage(message.Chat.Id, "ჩატი ჯერ არ დაწყებულა. ორივემ უნდა დააჭიროთ დარჩი-ს.", replyMarkup: MatchDecisionKeyboard(), cancellationToken: cancellationToken);
            return;
        }

        if (message.Text is null)
        {
            await botClient.SendMessage(message.Chat.Id, "ამ ვერსიაში მხოლოდ ტექსტურ შეტყობინებებს ვაგზავნი.", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(partner.ChatId, $"პარტნიორი: {message.Text}", replyMarkup: MainKeyboard(), cancellationToken: cancellationToken);
    }

    private async Task SendOwnProfile(ITelegramBotClient botClient, long chatId, BotUser user, CancellationToken cancellationToken)
    {
        if (!user.IsRegistered)
        {
            await botClient.SendMessage(chatId, "პროფილი ჯერ არ გაქვს. დაიწყე /start-ით.", cancellationToken: cancellationToken);
            return;
        }

        await SendProfileCard(botClient, chatId, user, MainKeyboard(), cancellationToken);
    }

    private static async Task SendProfileCard(ITelegramBotClient botClient, long chatId, BotUser user, IReplyMarkup replyMarkup, CancellationToken cancellationToken)
    {
        var caption = FormatProfile(user);

        if (!string.IsNullOrWhiteSpace(user.PhotoFileId))
        {
            await botClient.SendPhoto(
                chatId,
                InputFile.FromFileId(user.PhotoFileId),
                caption,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
            return;
        }

        await botClient.SendMessage(chatId, caption, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
    }

    private static ReplyKeyboardMarkup MainKeyboard() =>
        new(new[]
        {
            new KeyboardButton[] { NextText },
            new KeyboardButton[] { EditProfileText },
            new KeyboardButton[] { StopText }
        })
        {
            ResizeKeyboard = true
        };

    private static ReplyKeyboardMarkup MatchDecisionKeyboard() =>
        new(new[]
        {
            new KeyboardButton[] { StayText, NextText }
        })
        {
            ResizeKeyboard = true
        };

    private IReplyMarkup CancelKeyboardOrRemove(long chatId) =>
        matchmaking.IsEditingProfile(chatId)
            ? CancelOnlyKeyboard()
            : new ReplyKeyboardRemove();

    private static ReplyKeyboardMarkup CancelOnlyKeyboard() =>
        new(new[]
        {
            new KeyboardButton[] { CancelEditText }
        })
        {
            ResizeKeyboard = true
        };

    private static ReplyKeyboardMarkup SkipPhotoKeyboard(bool allowCancel = false)
    {
        var rows = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { SkipPhotoText }
        };
        AddCancelRow(rows, allowCancel);

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
    }

    private static ReplyKeyboardMarkup GenderKeyboard(bool allowCancel = false)
    {
        var rows = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { MaleText, FemaleText },
            new KeyboardButton[] { OtherText }
        };
        AddCancelRow(rows, allowCancel);

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
    }

    private static ReplyKeyboardMarkup LookingForKeyboard(bool allowCancel = false)
    {
        var rows = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { LookingForFemaleText },
            new KeyboardButton[] { LookingForMaleText },
            new KeyboardButton[] { LookingForAnyText }
        };
        AddCancelRow(rows, allowCancel);

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
    }

    private static ReplyKeyboardMarkup BirthMonthKeyboard(bool allowCancel = false)
    {
        var rows = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { "1", "2", "3" },
            new KeyboardButton[] { "4", "5", "6" },
            new KeyboardButton[] { "7", "8", "9" },
            new KeyboardButton[] { "10", "11", "12" }
        };
        AddCancelRow(rows, allowCancel);

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
    }

    private static void AddCancelRow(List<KeyboardButton[]> rows, bool allowCancel)
    {
        if (allowCancel)
        {
            rows.Add([CancelEditText]);
        }
    }

    private static string FormatProfile(BotUser user) =>
        $"სახელი: {user.Name}\n" +
        $"სქესი: {FormatGender(user.Gender)}\n" +
        $"ეძებს: {FormatLookingFor(user.LookingFor)}\n" +
        $"ქალაქი: {user.City}\n" +
        $"დაბადება: {user.BirthMonth}/{user.BirthYear}\n" +
        $"ჩემ შესახებ: {user.About}";

    private static string FormatGender(Gender? gender) =>
        gender switch
        {
            Gender.Male => MaleText,
            Gender.Female => FemaleText,
            Gender.Other => OtherText,
            _ => "-"
        };

    private static string FormatLookingFor(GenderPreference? lookingFor) =>
        lookingFor switch
        {
            GenderPreference.Male => "კაცს",
            GenderPreference.Female => "ქალს",
            GenderPreference.Any => "ყველას",
            _ => "-"
        };

    private static string FormatStats(UserStats stats) =>
        $"რეგისტრირებული მომხმარებლები: {stats.RegisteredUsers}\nაქტიური მომხმარებლები: {stats.ActiveUsers}";

    private static bool IsCommand(string? text, string command)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Equals($"/{command}", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith($"/{command}@", StringComparison.OrdinalIgnoreCase);
    }
}
