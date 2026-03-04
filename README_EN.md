# CS2 Steam Launcher

English Version | [中文版](README.md)

A lightweight CS2 (Counter-Strike 2) desktop launcher with a WPF GUI. It uses the Steam protocol to launch the game and auto-connect to a specified server.

## Features

- 🚀 WPF GUI with one-click launch and connect
- 🔑 Supports password-protected servers, builds `steam://run/730//+connect {serverIp}:{serverPort}`
- 🧭 Auto-displays SteamID64 (refresh/copy)
- 🖥️ Detects Steam process and can attempt to start Steam
- 👀 Monitors CS2 process startup after invoking Steam
- 📝 Built-in SteamID converter; no Steamworks.NET dependency

## System Requirements

- Windows operating system
- Steam client installed
- CS2 (Counter-Strike 2) installed
- .NET 8.0 runtime environment

## Usage

### GUI Usage

1. Run the app (`dotnet run` or double-click the built/published exe).
2. Enter server IP, port (default 27015), and optional password.
3. If needed, click “Start Steam” to launch the Steam client.
4. Click “Launch and Connect”; the app invokes the Steam protocol and monitors whether CS2 starts.

## How It Works

### 1. Getting SteamID64

The program obtains the user's SteamID64 through the following methods:

1. Read the currently logged-in user's Steam account ID from Windows Registry
2. Extract account ID from Steam configuration file (loginusers.vdf)
3. Use SteamID conversion utility class to convert Account ID to SteamID64

### 2. SteamID Conversion Principle

SteamID64 is a 64-bit unique identifier with the following structure:

```
SteamID64 = Universe + Type + Instance + Account ID
```

Where:
- **Universe**: Steam universe identifier (Public Universe is 1)
- **Type**: Account type (Individual user is 1)
- **Instance**: Instance identifier (Desktop user is 1)
- **Account ID**: Unique identifier for Steam account

Conversion formula:
```csharp
SteamID64 = 76561197960265728 + AccountID
```

### 3. Launch Process

1. Check if Steam client is running, start Steam if not running
2. Build Steam URL: `steam://run/730//+connect {serverIp}:{serverPort}`
3. Add password to URL if server has password
4. Launch game and auto-connect to specified server through single Steam protocol

## SteamID Conversion Utility Class

The project includes a complete SteamID conversion utility class `SteamIDConverter` that supports the following conversions:

- Account ID ↔ SteamID64
- SteamID64 ↔ SteamID2 (STEAM_0:X:Y format)
- SteamID64 ↔ SteamID3 ([U:1:Z] format)

### Usage Examples

```csharp
// Convert Account ID to SteamID64
ulong steamId64 = SteamIDConverter.ConvertToSteamID64(12345678);

// Convert SteamID64 to Account ID
uint accountId = SteamIDConverter.ConvertToAccountID(76561197972611406);

// Convert SteamID64 to SteamID2
string steamId2 = SteamIDConverter.ConvertToSteamID2(76561197972611406);
// Result: "STEAM_0:0:12345678"

// Convert SteamID2 to SteamID64
ulong steamId64 = SteamIDConverter.ConvertFromSteamID2("STEAM_0:0:12345678");

// Convert SteamID3 to SteamID64
ulong steamId64 = SteamIDConverter.ConvertFromSteamID3("[U:1:12345678]");
```

## Build and Run

### Build the Project

```bash
dotnet restore
dotnet build
```

### Run the Program

```bash
dotnet run
```

Or run the generated executable directly:

```bash
bin\Debug\net8.0-windows\CS2Launcher.exe
```

### Publish

- Windows batch: `build.bat` / `publish.bat` (x64/x86, with/without runtime, single-file, zipped).
- macOS/Linux reference: `publish.sh` (currently outputs win-x64 self-contained single-file with zip).

## Troubleshooting

### Common Issues

1. **"Unable to get SteamID64"**
   - Ensure Steam client is logged in
   - Check if Steam client is running properly

2. **"Steam is not running"**
   - The program will automatically try to start the Steam client
   - If startup fails, please start the Steam client manually

3. **"Auto-connection failed"**
   - Check if server IP and port are correct
   - Ensure the server is running and accessible
   - If the server has a password, ensure the password is entered correctly

### Alternative Solution

If auto-connection fails, the program will provide manual connection instructions. You can enter in the game console (press ~ key):

```
connect <serverIP>:<serverPort>
password <password>
```

## Project Structure (excerpt)

```
CS2Launcher/
├── App.xaml                # WPF app entry
├── MainWindow.xaml(.cs)    # UI and code-behind
├── Program.cs              # Launcher logic used by UI
├── CS2Launcher.csproj      # Project config (UseWPF)
├── build.bat / build.sh    # Build scripts
├── publish.bat / publish.sh# Publish scripts
├── README.md / README_EN.md# Docs
└── .gitignore
```

## Tech Stack

- **Language**: C#
- **Framework**: .NET 8.0
- **Platform**: Windows
- **Dependencies**: No external dependencies (does not use Steamworks.NET)

## License

This project is licensed under the MIT License.

## Contributing

Issues and Pull Requests are welcome to improve this project.

## Changelog

### v1.0.0
- Initial version release
- Support launching CS2 and auto-connecting to servers via single Steam protocol
- Completely removed dependency on Steamworks.NET
- Added SteamID conversion utility class