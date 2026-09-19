using DatingMatchBot.Data;
using DatingMatchBot.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Telegram.Bot.Types;

namespace DatingMatchBot.Services;

public sealed class MatchmakingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _lock = new();
    private readonly Dictionary<long, BotUser> _users = [];
    private readonly Random _random = new();
    private readonly string _storagePath;
    private readonly IDbContextFactory<BotDbContext>? _dbContextFactory;

    public MatchmakingService(
        string storagePath,
        IDbContextFactory<BotDbContext>? dbContextFactory = null)
    {
        _storagePath = storagePath;
        _dbContextFactory = dbContextFactory;
        LoadUsers();
    }

    public BotUser GetOrCreate(User telegramUser, long chatId)
    {
        lock (_lock)
        {
            var shouldSave = false;

            if (!_users.TryGetValue(chatId, out var user))
            {
                user = new BotUser { ChatId = chatId };
                _users[chatId] = user;
                shouldSave = true;
            }

            if (user.TelegramUsername != telegramUser.Username)
            {
                user.TelegramUsername = telegramUser.Username;
                shouldSave = true;
            }

            user.LastSeenAt = DateTimeOffset.UtcNow;
            if (shouldSave)
            {
                SaveUsersInsideLock();
            }

            return Clone(user);
        }
    }

    public BotUser RestartRegistration(User telegramUser, long chatId)
    {
        lock (_lock)
        {
            DisconnectInsideLock(chatId);

            var user = new BotUser
            {
                ChatId = chatId,
                TelegramUsername = telegramUser.Username,
                Step = RegistrationStep.AwaitingName,
                LastSeenAt = DateTimeOffset.UtcNow
            };

            _users[chatId] = user;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetName(long chatId, string name)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.Name = name.Trim();
            user.Step = RegistrationStep.AwaitingGender;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetGender(long chatId, Gender gender)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.Gender = gender;
            user.Step = RegistrationStep.AwaitingLookingFor;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser BeginSettingsEdit(long chatId)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            DisconnectInsideLock(chatId);
            user.Step = RegistrationStep.AwaitingLookingFor;
            user.IsEditingSettings = true;
            user.HasAcceptedCurrentMatch = false;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetLookingFor(long chatId, GenderPreference lookingFor)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.LookingFor = lookingFor;
            user.Step = user.IsEditingSettings ? RegistrationStep.Complete : RegistrationStep.AwaitingCity;
            user.IsEditingSettings = false;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetCity(long chatId, string city)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.City = city.Trim();
            user.Step = RegistrationStep.AwaitingBirthYear;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetBirthYear(long chatId, int birthYear)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.BirthYear = birthYear;
            user.Step = RegistrationStep.AwaitingBirthMonth;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetBirthMonth(long chatId, int birthMonth)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.BirthMonth = birthMonth;
            user.Step = RegistrationStep.AwaitingAbout;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser SetAbout(long chatId, string about)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.About = about.Trim();
            user.Step = RegistrationStep.AwaitingPhoto;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public BotUser CompleteRegistration(long chatId, string? photoFileId)
    {
        lock (_lock)
        {
            var user = _users[chatId];
            user.PhotoFileId = photoFileId;
            user.Step = RegistrationStep.Complete;
            user.IsActive = true;
            user.HasAcceptedCurrentMatch = false;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return Clone(user);
        }
    }

    public MatchSearchResult FindNext(long chatId)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(chatId, out var user) || !user.IsRegistered)
            {
                return MatchSearchResult.NotRegistered();
            }

            var disconnectedPartner = DisconnectInsideLock(chatId);
            user.IsActive = true;
            user.LastSeenAt = DateTimeOffset.UtcNow;

            var candidates = _users.Values
                .Where(candidate =>
                    candidate.ChatId != chatId &&
                    candidate.IsRegistered &&
                    candidate.IsActive &&
                    candidate.CurrentPartnerChatId is null &&
                    IsPreferenceMatch(user, candidate))
                .ToList();

            if (candidates.Count == 0)
            {
                SaveUsersInsideLock();
                return MatchSearchResult.Waiting(Clone(user), disconnectedPartner);
            }

            var partner = candidates[_random.Next(candidates.Count)];
            user.CurrentPartnerChatId = partner.ChatId;
            partner.CurrentPartnerChatId = user.ChatId;
            user.HasAcceptedCurrentMatch = false;
            partner.HasAcceptedCurrentMatch = false;
            user.IsActive = true;
            partner.IsActive = true;

            SaveUsersInsideLock();
            return MatchSearchResult.Matched(Clone(user), Clone(partner), disconnectedPartner);
        }
    }

    public StopResult Stop(long chatId)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(chatId, out var user))
            {
                return new StopResult(null, null);
            }

            var partner = DisconnectInsideLock(chatId);
            user.IsActive = false;
            user.LastSeenAt = DateTimeOffset.UtcNow;
            SaveUsersInsideLock();
            return new StopResult(Clone(user), partner);
        }
    }

    public BotUser? GetPartner(long chatId)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(chatId, out var user) || user.CurrentPartnerChatId is not { } partnerChatId)
            {
                return null;
            }

            return _users.TryGetValue(partnerChatId, out var partner) ? Clone(partner) : null;
        }
    }

    public AcceptMatchResult AcceptMatch(long chatId)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(chatId, out var user) || user.CurrentPartnerChatId is not { } partnerChatId)
            {
                return new AcceptMatchResult(null, null, false);
            }

            user.HasAcceptedCurrentMatch = true;
            user.LastSeenAt = DateTimeOffset.UtcNow;

            var partner = _users.TryGetValue(partnerChatId, out var foundPartner) ? foundPartner : null;
            var bothAccepted = partner?.HasAcceptedCurrentMatch == true;

            SaveUsersInsideLock();
            return new AcceptMatchResult(Clone(user), partner is null ? null : Clone(partner), bothAccepted);
        }
    }

    public bool CanChat(long chatId)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(chatId, out var user) || user.CurrentPartnerChatId is not { } partnerChatId)
            {
                return false;
            }

            return user.HasAcceptedCurrentMatch &&
                   _users.TryGetValue(partnerChatId, out var partner) &&
                   partner.HasAcceptedCurrentMatch;
        }
    }

    public BotUser? GetUser(long chatId)
    {
        lock (_lock)
        {
            return _users.TryGetValue(chatId, out var user) ? Clone(user) : null;
        }
    }

    public UserStats GetStats()
    {
        lock (_lock)
        {
            var registered = _users.Values.Count(user => user.IsRegistered);
            var active = _users.Values.Count(user => user.IsRegistered && user.IsActive);
            return new UserStats(registered, active);
        }
    }

    private BotUser? DisconnectInsideLock(long chatId)
    {
        if (!_users.TryGetValue(chatId, out var user) || user.CurrentPartnerChatId is not { } partnerChatId)
        {
            return null;
        }

        user.CurrentPartnerChatId = null;
        user.HasAcceptedCurrentMatch = false;

        if (!_users.TryGetValue(partnerChatId, out var partner))
        {
            return null;
        }

        partner.CurrentPartnerChatId = null;
        partner.HasAcceptedCurrentMatch = false;
        partner.IsActive = true;
        return Clone(partner);
    }

    private void LoadUsers()
    {
        if (_dbContextFactory is not null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.Database.EnsureCreated();

            var databaseUsers = dbContext.Users.AsNoTracking().ToList();
            if (databaseUsers.Count > 0)
            {
                foreach (var user in databaseUsers)
                {
                    _users[user.ChatId] = user;
                }

                return;
            }
        }

        if (!System.IO.File.Exists(_storagePath))
        {
            return;
        }

        var json = System.IO.File.ReadAllText(_storagePath);
        var users = JsonSerializer.Deserialize<List<BotUser>>(json, JsonOptions) ?? [];

        foreach (var user in users)
        {
            _users[user.ChatId] = user;
        }

        if (_dbContextFactory is not null && _users.Count > 0)
        {
            SaveUsersInsideLock();
        }
    }

    private void SaveUsersInsideLock()
    {
        if (_dbContextFactory is not null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var storedUsers = dbContext.Users.ToDictionary(user => user.ChatId);

            foreach (var user in _users.Values)
            {
                if (storedUsers.TryGetValue(user.ChatId, out var storedUser))
                {
                    dbContext.Entry(storedUser).CurrentValues.SetValues(user);
                }
                else
                {
                    dbContext.Users.Add(Clone(user));
                }
            }

            using var transaction = dbContext.Database.BeginTransaction();
            dbContext.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [BotUsers] ON");

            try
            {
                dbContext.SaveChanges();
                dbContext.Database.ExecuteSqlRaw("SET IDENTITY_INSERT [BotUsers] OFF");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            return;
        }

        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var users = _users.Values
            .OrderBy(user => user.ChatId)
            .ToList();

        var json = JsonSerializer.Serialize(users, JsonOptions);
        var tempPath = $"{_storagePath}.tmp";

        System.IO.File.WriteAllText(tempPath, json);
        System.IO.File.Move(tempPath, _storagePath, overwrite: true);
    }

    private static BotUser Clone(BotUser user) =>
        new()
        {
            ChatId = user.ChatId,
            TelegramUsername = user.TelegramUsername,
            Name = user.Name,
            Gender = user.Gender,
            LookingFor = user.LookingFor,
            City = user.City,
            BirthYear = user.BirthYear,
            BirthMonth = user.BirthMonth,
            About = user.About,
            PhotoFileId = user.PhotoFileId,
            Step = user.Step,
            IsEditingSettings = user.IsEditingSettings,
            IsActive = user.IsActive,
            CurrentPartnerChatId = user.CurrentPartnerChatId,
            HasAcceptedCurrentMatch = user.HasAcceptedCurrentMatch,
            LastSeenAt = user.LastSeenAt
        };

    private static bool IsPreferenceMatch(BotUser user, BotUser candidate) =>
        MatchesPreference(user.LookingFor, candidate.Gender) &&
        MatchesPreference(candidate.LookingFor, user.Gender);

    private static bool MatchesPreference(GenderPreference? preference, Gender? gender) =>
        preference switch
        {
            GenderPreference.Any => true,
            GenderPreference.Male => gender == Gender.Male,
            GenderPreference.Female => gender == Gender.Female,
            _ => false
        };
}

public sealed record MatchSearchResult(
    MatchSearchStatus Status,
    BotUser? User,
    BotUser? Partner,
    BotUser? DisconnectedPartner)
{
    public static MatchSearchResult NotRegistered() =>
        new(MatchSearchStatus.NotRegistered, null, null, null);

    public static MatchSearchResult Waiting(BotUser user, BotUser? disconnectedPartner) =>
        new(MatchSearchStatus.Waiting, user, null, disconnectedPartner);

    public static MatchSearchResult Matched(BotUser user, BotUser partner, BotUser? disconnectedPartner) =>
        new(MatchSearchStatus.Matched, user, partner, disconnectedPartner);
}

public enum MatchSearchStatus
{
    NotRegistered,
    Waiting,
    Matched
}

public sealed record StopResult(BotUser? User, BotUser? DisconnectedPartner);

public sealed record UserStats(int RegisteredUsers, int ActiveUsers);

public sealed record AcceptMatchResult(BotUser? User, BotUser? Partner, bool BothAccepted);
