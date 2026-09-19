# Dating Match Telegram Bot

C#/.NET Telegram bot MVP for random active-user matching.

## Features

- `/start` registration flow
- user name, gender, looking-for preference, city, birth year, birth month, about, and optional photo
- editable looking-for preference with `/settings`
- editable full profile with `/editprofile` or `პროფილის შეცვლა`
- `Next` button for random active partner search
- matched profile preview with photo and details
- `დარჩი` / `Next` decision before chat starts
- anonymous text relay between matched users
- `Stop` or `/stop` to leave active search
- `/profile` to view current profile

## Run

Create a Telegram bot with BotFather, then set the token and run:

```powershell
$env:TELEGRAM_BOT_TOKEN = "123456:ABC..."
dotnet run
```

Or create `appsettings.json` from `appsettings.example.json` and paste the token there:

```json
{
  "Telegram": {
    "BotToken": "123456:ABC..."
  }
}
```

User data is persisted locally in `data/users.json`, including registration status, active status, and current partner.

## SQL Server

Set `ConnectionStrings:DefaultConnection` in `appsettings.json` or the
`CRUSHBOX_DB_CONNECTION` environment variable. On first startup, the bot creates
the `BotUsers` table. If the table is empty, existing local users are imported
from `App_Data/users.json` automatically.
