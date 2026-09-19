namespace DatingMatchBot.Models;

public sealed class BotUser
{
    public long ChatId { get; init; }
    public string? TelegramUsername { get; set; }
    public string? Name { get; set; }
    public Gender? Gender { get; set; }
    public GenderPreference? LookingFor { get; set; }
    public string? City { get; set; }
    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public string? About { get; set; }
    public string? PhotoFileId { get; set; }
    public RegistrationStep Step { get; set; } = RegistrationStep.AwaitingName;
    public bool IsEditingSettings { get; set; }
    public bool IsActive { get; set; }
    public long? CurrentPartnerChatId { get; set; }
    public bool HasAcceptedCurrentMatch { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRegistered =>
        Step == RegistrationStep.Complete &&
        !string.IsNullOrWhiteSpace(Name) &&
        Gender is not null &&
        LookingFor is not null &&
        !string.IsNullOrWhiteSpace(City) &&
        BirthYear is not null &&
        BirthMonth is not null &&
        !string.IsNullOrWhiteSpace(About);
}
