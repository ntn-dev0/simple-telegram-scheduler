# Telegram MTProto Scheduler (.NET)

## 1) What it is and how to use it
This is a .NET console utility that sends **messages from your personal Telegram account** to a given chat/group/channel. It talks to Telegram via MTProto using **WTelegramClient** and can:
- send a message **immediately**;
- **schedule** delivery for a specific time in a specific time zone;
- automatically persist and reuse a **session** (`tg.session`), so the verification code / 2FA is typically required only on the very first login.

**CLI parameters:**
- `--chat` (**required**): chat/group name **or** `@username` of a channel/group/user.
- `--text` (**required**): message text.
- `--when` (optional): date and time in `yyyy-MM-dd HH:mm` (interpreted in the selected TZ).
- `--tz` (optional): IANA time zone, e.g. `Europe/Kyiv`. If omitted, the system time zone is used.

> On the first run the app creates a session and may prompt for a **Verification Code** and **2FA password**. Subsequent runs will reuse the session and will not ask again unless you revoke that session in Telegram or change account settings.

**Examples**  
Send a message **right now**:
```bash
dotnet run -- --chat "@mygroup" --text "Hello world!"
```

Send **later** (Kyiv time):
```bash
dotnet run -- --chat "Group name" --text "Good morning!" --when "2025-08-30 09:00" --tz "Europe/Kyiv"
```

---

## 2) Configuration (`config/appsettings.json`)
The program reads settings from `config/appsettings.json` (and optionally `config/appsettings.local.json`) in the **`Tg`** section:

```json
{
  "Tg": {
    "ApiId": "",       "/* your api_id from https://my.telegram.org (number; may be quoted) */",
    "ApiHash": "",     "/* your api_hash from https://my.telegram.org */",
    "Number": "",      "/* phone number of your account in +380... format */",
    "SessionPath": ""  "/* (optional) path to the session file; defaults to config/tg.session */"
  }
}
```

> **Notes**
> - If `SessionPath` is empty, the session will be stored at `config/tg.session`.
> - After the first successful login the session file allows sending messages without entering code/2FA again.

---

## 3) Run from source
Install .NET 8+ and restore packages:
```bash
dotnet restore
# If needed:
# dotnet add package WTelegramClient
# dotnet add package TimeZoneConverter
```

Create `config/appsettings.json` as shown above and run:
```bash
dotnet run -- --chat "@mygroup" --text "Hello world!"
```

> Config files must be located in the `config` folder next to the project/executable.

---

## 4) Publish (self-contained, single-file)

### Windows (win-x64)
```powershell
dotnet publish `
    -c Release -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o "C:\TgSheduler"
```
Run the produced executable with parameters, e.g.:
```powershell
C:\TgSheduler\YourAppName.exe --chat "@mygroup" --text "Hello from published build"
```

### macOS (Apple Silicon, osx-arm64)
```bash
dotnet publish     -c Release -r osx-arm64     --self-contained true     /p:PublishSingleFile=true     /p:DebugType=None     /p:DebugSymbols=false     /p:IncludeNativeLibrariesForSelfExtract=true     -o "$HOME/TgScheduler"
```

Run it from the output folder:
```bash
"$HOME/TgScheduler/YourAppName" --chat "@mygroup" --text "Hello from published build"
```

> **Tip for macOS:** if Gatekeeper blocks the app (unidentified developer), open it via **Right click → Open** or remove the quarantine attribute:  
> `xattr -dr com.apple.quarantine "$HOME/TgScheduler/YourAppName"`

---

All set. Do the first login once (verification code / 2FA) and the utility will work autonomously using the saved session.
