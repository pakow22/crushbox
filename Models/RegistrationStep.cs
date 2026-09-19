namespace DatingMatchBot.Models;

public enum RegistrationStep
{
    AwaitingName,
    AwaitingGender,
    AwaitingLookingFor,
    AwaitingCity,
    AwaitingBirthYear,
    AwaitingBirthMonth,
    AwaitingAbout,
    AwaitingPhoto,
    Complete
}
