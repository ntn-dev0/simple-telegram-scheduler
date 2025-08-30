using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using TL;
using WTelegram;

// Run:
// dotnet run --chat "Group name" --text "Hello world!"
// or postpone:
// dotnet run --chat "@mygroup" --text "Hello world!" --when "2025-08-30 09:00" --tz "Europe/Kyiv"
Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.SetBasePath(Path.Combine(AppContext.BaseDirectory, "config"));
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddOptions<Settings>()
            .Bind(context.Configuration.GetSection("Tg"))
            .Validate(s => s.ApiId > 0 && !string.IsNullOrWhiteSpace(s.ApiHash) && !string.IsNullOrWhiteSpace(s.Number))
            .ValidateOnStart();
    }).Build();

(string chatArg, string textArg, string whenArg, string tzArg) = ParseArgs(args);
if (string.IsNullOrWhiteSpace(chatArg) || string.IsNullOrWhiteSpace(textArg))
{
    Console.WriteLine("Provide parameters: --chat <@username | chat name> --text <message> [--when \"yyyy-MM-dd HH:mm\"] [--tz <IANA TZ>]");
    return;
}

Settings tgConfig = host.Services.GetRequiredService<IOptions<Settings>>().Value;
string sessionPath = string.IsNullOrWhiteSpace(tgConfig.SessionPath)
        ? Path.Combine(AppContext.BaseDirectory, "config", "tg.session")
        : tgConfig.SessionPath;
Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);

using var client = new Client(what =>
{   
    return what switch
    {
        "api_id" => tgConfig.ApiId.ToString(),
        "api_hash" => tgConfig.ApiHash,
        "phone_number" => tgConfig.Number,
        "verification_code" => ReadLineMasked("Verification Code: "),
        "password" => ReadLineMasked("2FA Password: "),
        "session_pathname" => sessionPath,
        _ => null
    };
});

User me = await client.LoginUserIfNeeded();
Console.WriteLine($"Session has been created for: {me.username ?? me.first_name}");

InputPeer? peer = await ResolvePeer(client, chatArg);
if (peer is null)
{
    Console.WriteLine("Unable to find chat/contact. Please check --chat parameter.");
    return;
}

// Postpone (optional)
if (!string.IsNullOrWhiteSpace(whenArg))
{
    TimeZoneInfo tz = ResolveTz(tzArg);
    if (!DateTime.TryParseExact(whenArg, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime whenLocal))
    {
        Console.WriteLine("Invalid format --when. Use: yyyy-MM-dd HH:mm");
        return;
    }
    DateTime when = TimeZoneInfo.ConvertTimeToUtc(whenLocal, tz);
    TimeSpan delay = when - DateTime.UtcNow;
    if (delay.TotalSeconds > 0)
    {
        Console.WriteLine($"Waiting till {whenLocal} ({tz.Id})…");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            await Task.Delay(delay, cts.Token);
            me = await client.LoginUserIfNeeded();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Canceled."); return;
        }
    }
}

Console.WriteLine($"Signed in as: {me.username ?? me.first_name} (id={me.ID})");

try
{
    await client.SendMessageAsync(peer, textArg);
}
catch (RpcException ex) when (ex.Code == 420)
{
    // FLOOD_WAIT_x
    Console.WriteLine(ex.Message);
}
catch (RpcException ex) when (ex.Code == 400 || ex.Code == 403)
{
    // 400 PEER_ID_INVALID, 403 CHAT_WRITE_FORBIDDEN ect.
    Console.WriteLine($"Telegram RPC error {ex.Code}: {ex.Message}");
}

Console.WriteLine("Message has been sent.");

static (string chat, string text, string when, string tz) ParseArgs(string[] args)
{
    string chat = "", text = "", when = "", tz = "";
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--chat": chat = args.ElementAtOrDefault(++i) ?? ""; break;
            case "--text": text = args.ElementAtOrDefault(++i) ?? ""; break;
            case "--when": when = args.ElementAtOrDefault(++i) ?? ""; break;
            case "--tz": tz = args.ElementAtOrDefault(++i) ?? ""; break;
        }
    }
    return (chat, text, when, tz);
}

static string ReadLineMasked(string prompt)
{
    Console.Write(prompt);
    var s = new Stack<char>();
    ConsoleKeyInfo k;
    while ((k = Console.ReadKey(true)).Key != ConsoleKey.Enter)
    {
        if (k.Key == ConsoleKey.Backspace && s.Count > 0) { s.Pop(); Console.Write("\b \b"); }
        else if (!char.IsControl(k.KeyChar)) { s.Push(k.KeyChar); Console.Write("*"); }
    }
    Console.WriteLine();
    return new string([.. s.Reverse()]);
}

static async Task<InputPeer?> ResolvePeer(Client client, string chatArg)
{
    // Resolve @username / group / channel
    if (chatArg.StartsWith('@'))
    {
        Contacts_ResolvedPeer res = await client.Contacts_ResolveUsername(chatArg.TrimStart('@'));
        return res switch
        {
            { User: not null } => res.User,
            { Chat: not null } => res.Chat,
            { Channel: not null } => res.Channel,
            _ => null
        };
    }

    // Search by name
    Messages_Dialogs dialogs = await client.Messages_GetAllDialogs();
    foreach ((long id, User obj) in dialogs.users)
        if (obj is User u && !string.IsNullOrEmpty(u.first_name) &&
            $"{u.first_name} {u.last_name}".Trim().Equals(chatArg, StringComparison.OrdinalIgnoreCase))
            return u;

    foreach ((long id, ChatBase obj) in dialogs.chats)
        if (obj is ChatBase c && (c.Title?.Equals(chatArg, StringComparison.OrdinalIgnoreCase) ?? false))
            return c;

    return null;
}

static TimeZoneInfo ResolveTz(string? tzId)
{
    if (string.IsNullOrWhiteSpace(tzId)) return TimeZoneInfo.Local;
    try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
    catch
    {
        try { return TimeZoneConverter.TZConvert.GetTimeZoneInfo(tzId); }
        catch { return TimeZoneInfo.Local; }
    }
}

class Settings()
{
    public required int ApiId { get; init; }
    public required string ApiHash { get; init; }
    public required string Number { get; init; }
    public string? SessionPath { get; init; }
}
