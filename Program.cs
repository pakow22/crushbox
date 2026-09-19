using DatingMatchBot.Data;
using DatingMatchBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var platformPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(platformPort) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{platformPort}");
}

var builder = WebApplication.CreateBuilder(args);
var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ??
            builder.Configuration["Telegram:BotToken"];

if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException(
        "Set TELEGRAM_BOT_TOKEN or Telegram:BotToken in appsettings.json.");
}

var platformPublicDomain =
    Environment.GetEnvironmentVariable("RAILWAY_PUBLIC_DOMAIN")?.Trim() ??
    Environment.GetEnvironmentVariable("KOYEB_PUBLIC_DOMAIN")?.Trim();
var webhookUrl = builder.Configuration["Telegram:WebhookUrl"]?.TrimEnd('/');
if (string.IsNullOrWhiteSpace(webhookUrl) && !string.IsNullOrWhiteSpace(platformPublicDomain))
{
    webhookUrl = $"https://{platformPublicDomain.TrimEnd('/')}";
}

var webhookSecret = builder.Configuration["Telegram:WebhookSecret"];
var connectionString = Environment.GetEnvironmentVariable("CRUSHBOX_DB_CONNECTION") ??
                       builder.Configuration.GetConnectionString("DefaultConnection");
var storagePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "users.json");
MigrateExistingData(builder.Environment.ContentRootPath, storagePath);

builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(token));
if (!string.IsNullOrWhiteSpace(connectionString) &&
    !connectionString.Contains("YOUR_DB_PASSWORD", StringComparison.Ordinal))
{
    builder.Services.AddDbContextFactory<BotDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddSingleton(serviceProvider => new MatchmakingService(
        storagePath,
        serviceProvider.GetRequiredService<IDbContextFactory<BotDbContext>>()));
}
else
{
    builder.Services.AddSingleton(new MatchmakingService(storagePath));
}
builder.Services.AddSingleton<BotUpdateHandler>();

var app = builder.Build();
var deploymentVersion = Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA") ?? "local";
var botClient = app.Services.GetRequiredService<ITelegramBotClient>();
var handler = app.Services.GetRequiredService<BotUpdateHandler>();
var receiverOptions = new ReceiverOptions { AllowedUpdates = [UpdateType.Message] };

app.MapGet("/", () => Results.Ok(new { service = "CrushBox Telegram Bot", status = "running", version = deploymentVersion }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = deploymentVersion, utc = DateTimeOffset.UtcNow }));

if (!string.IsNullOrWhiteSpace(webhookUrl))
{
    if (string.IsNullOrWhiteSpace(webhookSecret))
    {
        throw new InvalidOperationException("Telegram:WebhookSecret is required when WebhookUrl is set.");
    }

    app.MapPost($"/telegram/{webhookSecret}", async (Update update, CancellationToken cancellationToken) =>
    {
        await handler.HandleUpdateAsync(botClient, update, cancellationToken);
        return Results.Ok();
    });

    await botClient.SetWebhook(
        $"{webhookUrl}/telegram/{webhookSecret}",
        allowedUpdates: [UpdateType.Message],
        cancellationToken: app.Lifetime.ApplicationStopping);
}
else
{
    await botClient.DeleteWebhook(cancellationToken: app.Lifetime.ApplicationStopping);
    botClient.StartReceiving(handler, receiverOptions, app.Lifetime.ApplicationStopping);
}

var me = await botClient.GetMe(app.Lifetime.ApplicationStopping);
app.Logger.LogInformation(
    "Bot @{Username} is running in {Mode} mode. Version: {Version}",
    me.Username,
    string.IsNullOrWhiteSpace(webhookUrl) ? "polling" : "webhook",
    deploymentVersion);

await app.RunAsync();

static void MigrateExistingData(string contentRootPath, string destinationPath)
{
    var sourcePath = Path.Combine(contentRootPath, "data", "users.json");
    if (System.IO.File.Exists(destinationPath) || !System.IO.File.Exists(sourcePath))
    {
        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
    System.IO.File.Copy(sourcePath, destinationPath);
}
