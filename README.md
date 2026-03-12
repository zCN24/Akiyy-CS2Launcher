# CS2 Steam Launcher

一个简洁的 CS2（Counter-Strike 2）桌面启动器，基于 WPF 提供图形界面，通过 Steam 协议启动游戏并自动连接到指定服务器。支持保存常用连接配置，提供换肤站快捷打开。

## 功能特点

- 🚀 WPF 图形界面，一键启动并连接服务器
- 🔑 支持密码服务器：无密码时一次直连；有密码时先写入固定 cfg（`cs2launcher.cfg`）并通过 `+exec` 启动，再在检测到游戏运行后延迟 10 秒发送 `+connect`
- 🧭 自动显示 SteamID64（可刷新/复制）
- 🖥️ 检测 Steam 进程并可尝试启动 Steam
- 👀 连接后自动监控 CS2 进程启动状态
- 📝 内置 SteamID 转换工具类，不依赖 Steamworks.NET
- 💾 内置配置文件：程序目录下 `config.json`（可由 `config.default.json` 首次自动生成）
- ♻️ 首次启动策略：仅在“第一次执行启动命令”时会检查并结束已运行的 CS2，后续不会再自动结束
- 🔄 内置更新模块：支持启动后自动检查、手动“检查更新”、下载更新包并校验 SHA256
- 🛠️ 外部 Updater：更新时拉起 `Updater.exe` 替换文件并自动重启主程序

## 系统要求

- Windows操作系统
- 已安装Steam客户端
- 已安装CS2（Counter-Strike 2）
- .NET 8.0运行环境

## 使用方法

### 图形界面使用

1. 运行应用（`dotnet run` 或双击构建/发布后的 exe）。
2. 首次运行：若当前目录不存在 `config.json`，程序会用内置默认值或同目录的 `config.default.json` 自动生成。
3. 填写服务器 IP、端口（默认 27015）、可选密码与自定义启动项。
4. 如需，点击“启动 Steam”唤起 Steam 客户端。
5. 点击“启动并连接”，程序将调用 Steam 协议并自动监控 CS2 是否拉起（有密码时会先 `+exec cs2launcher.cfg` 启动，检测到运行 10 秒后再自动连接），同时写回 `config.json`。
6. 需要换肤/外部站点时，可点击“打开换肤网站”按钮直接带入当前 SteamID64。
7. 首次点击“启动并连接”时若发现 CS2 已在运行，程序会先结束 CS2 再重启；首次命令执行后该行为不再触发。
8. 可点击“检查更新”手动检查新版本；若开启自动更新检查，程序启动后会自动检查一次。

## 自动更新模块

### 整体流程

1. 主程序读取 `config.json` 中的更新配置。
2. 请求远程 manifest（支持单对象或按 channel 分组）。
3. 比较当前版本与最新版本。
4. 有更新时提示用户（可选“跳过此版本”）。
5. 下载更新包并校验 SHA256。
6. 启动 `Updater.exe`，等待主程序退出后解压覆盖并重启。

### manifest 格式

单通道格式：

```json
{
   "version": "1.2.0",
   "packageUrl": "https://example.com/CS2Launcher-1.2.0.zip",
   "sha256": "<sha256 hex>",
   "releaseNotes": "修复连接流程并优化稳定性",
   "mandatory": false
}
```

多通道格式：

```json
{
   "channels": {
      "stable": {
         "version": "1.2.0",
         "packageUrl": "https://example.com/CS2Launcher-1.2.0.zip",
         "sha256": "<sha256 hex>",
         "releaseNotes": "稳定版更新",
         "mandatory": false
      },
      "beta": {
         "version": "1.3.0",
         "packageUrl": "https://example.com/CS2Launcher-1.3.0-beta.zip",
         "sha256": "<sha256 hex>",
         "releaseNotes": "测试版更新",
         "mandatory": false
      }
   }
}
```

### 更新包要求

- 当前 Updater 仅支持 `.zip`。
- zip 可直接平铺应用文件，也可外层包含单个根目录（Updater 会自动识别）。
- 建议将发布输出目录内容完整打包（包含主程序依赖文件）。
- 若目标目录已有 `config.json`，Updater 会保留本地配置，不覆盖该文件。

### Updater 参数（由主程序传入）

```text
--package <更新包路径>
--targetDir <应用目录>
--processId <主程序进程ID>
--exeName <主程序EXE文件名>
```

## 工作原理

### 1. 获取SteamID64

程序通过以下方式获取用户的SteamID64：

1. 从Windows注册表读取当前登录用户的Steam账户ID
2. 从Steam配置文件（loginusers.vdf）中提取账户ID
3. 使用SteamID转换工具类将Account ID转换为SteamID64

### 2. SteamID转换原理

SteamID64是一个64位的唯一标识符，其结构如下：

```
SteamID64 = Universe + Type + Instance + Account ID
```

其中：

- **Universe**：Steam标识符（公共Universe为1）
- **Type**：账户类型（个体用户为1）
- **Instance**：实例标识符（桌面用户为1）
- **Account ID**：Steam账户的唯一标识符

转换公式：

```csharp
SteamID64 = 76561197960265728 + AccountID
```

### 3. 启动流程

1. 检查Steam客户端是否运行，如未运行则启动Steam
2. 仅首次执行启动命令时，若 CS2 已在运行，则先结束进程再启动；后续不再自动结束
3. 无密码：构建 `steam://rungameid/730//+connect {serverIp}:{serverPort}`（可附带自定义启动项）并直接启动
4. 有密码：
   - 定位 CS2 的 `game/csgo/cfg` 目录（优先 Steamworks API，失败回退 VDF 扫描）
   - 写入或更新固定文件 `cs2launcher.cfg`，内容为 `password "<密码>"`
   - 首次启动使用 `steam://rungameid/730//+exec cs2launcher.cfg`（可附带自定义启动项）
   - 检测到进程后等待 10 秒，再发送 `steam://rungameid/730//+connect {serverIp}:{serverPort}`
5. 通过 Steam 协议完成启动与连接

## SteamID转换工具类

项目包含一个完整的SteamID转换工具类`SteamIDConverter`，支持以下转换：

- Account ID ↔ SteamID64
- SteamID64 ↔ SteamID2（STEAM_0:X:Y格式）
- SteamID64 ↔ SteamID3（[U:1:Z]格式）

### 使用示例

```csharp
// Account ID转SteamID64
ulong steamId64 = SteamIDConverter.ConvertToSteamID64(12345678);

// SteamID64转Account ID
uint accountId = SteamIDConverter.ConvertToAccountID(76561197972611406);

// SteamID64转SteamID2
string steamId2 = SteamIDConverter.ConvertToSteamID2(76561197972611406);
// 结果: "STEAM_0:0:12345678"

// SteamID2转SteamID64
ulong steamId64 = SteamIDConverter.ConvertFromSteamID2("STEAM_0:0:12345678");

// SteamID3转SteamID64
ulong steamId64 = SteamIDConverter.ConvertFromSteamID3("[U:1:12345678]");
```

## 配置文件

- 位置：程序所在目录 `config.json`
- 首次生成：
   - 如果存在 `config.default.json`，会以其内容为模板写出 `config.json`
   - 否则按内置默认值生成
- 保存时机：点击“启动并连接”会将当前表单值写回 `config.json`
- 关键字段：
   - `ServerIp` / `ServerPort` / `ServerPassword` / `CustomLaunchOptions`
   - `HasExecutedFirstLaunchCommand`（首次启动命令是否已执行，用于控制一次性重启策略）
   - `AutoCheckUpdates`（是否启动后自动检查更新）
   - `UpdateManifestUrl`（更新清单 URL）
   - `UpdateChannel`（更新通道，如 `stable` / `beta`）
   - `SkippedVersion`（被用户跳过的版本号）

## 密码CFG文件

- 文件名固定：`cs2launcher.cfg`
- 位置：CS2 安装目录下 `game/csgo/cfg`
- 行为：
   - 存在则覆盖更新密码行，不创建新文件
   - 不存在则创建后写入


## 构建和运行

### 构建项目

```bash
dotnet restore
dotnet build
```

### 运行程序

```bash
dotnet run
```

或直接运行生成的可执行文件：

```bash
bin\Debug\net8.0-windows\CS2Launcher.exe
```

## 故障排除

### 常见问题

1. **"无法获取SteamID64"**
   - 确保Steam客户端已登录
   - 检查Steam客户端是否正常运行

2. **"Steam未运行"**
   - 程序会自动尝试启动Steam客户端
   - 如果启动失败，请手动启动Steam客户端

3. **"自动连接失败"**
   - 检查服务器IP和端口是否正确
   - 确保服务器正在运行且可访问
   - 如果服务器有密码，确保密码输入正确

4. **"检查更新失败"**
   - 确认 `UpdateManifestUrl` 可访问并返回合法 JSON
   - 确认 manifest 的 `version` 与 `packageUrl` 字段存在
   - 若启用 SHA256 校验，确认 `sha256` 与下载文件一致

5. **"无法启动 Updater"**
   - 确认程序输出目录存在 `Updater.exe`
   - 确认更新包为 zip，且可正常解压
   - 查看 Updater 日志：`<应用目录>/logs/updater-*.log`

### 备用方案

如果自动连接失败，程序会提供手动连接的指令，您可以在游戏内控制台（按~键）依次输入：

```
password <密码>
connect <服务器IP>:<服务器端口>
```

## 项目结构（节选）

```
CS2Launcher/
├── App.xaml                # WPF 应用入口
├── MainWindow.xaml(.cs)    # 主窗口界面与逻辑
├── Program.cs              # 启动器业务逻辑类（供 UI 调用）
├── UpdateService.cs         # 自动更新检查/下载/校验与启动 Updater
├── CS2Launcher.Updater/     # 独立更新器项目
│   ├── CS2Launcher.Updater.csproj
│   └── Program.cs
├── CS2Launcher.csproj      # 项目配置（UseWPF）
├── README.md               # 文档
└── .gitignore
```

## 技术栈

- **语言**: C#
- **框架**: .NET 8.0
- **平台**: Windows
- **依赖**: 无外部依赖（不使用Steamworks.NET）

## 许可证

本项目采用MIT许可证。

## 贡献

欢迎提交Issue和Pull Request来改进这个项目！无论是功能增强、bug修复还是文档改进，都非常感谢您的贡献。