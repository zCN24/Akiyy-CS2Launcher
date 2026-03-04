# CS2 Steam Launcher

[English Version](README_EN.md) | 中文版

一个简洁的 CS2（Counter-Strike 2）桌面启动器，基于 WPF 提供图形界面，通过 Steam 协议启动游戏并自动连接到指定服务器。

## 功能特点

- 🚀 WPF 图形界面，一键启动并连接服务器
- 🔑 支持密码服务器，自动拼接 `steam://run/730//+connect {serverIp}:{serverPort}`
- 🧭 自动显示 SteamID64（可刷新/复制）
- 🖥️ 检测 Steam 进程并可尝试启动 Steam
- 👀 连接后自动监控 CS2 进程启动状态
- 📝 内置 SteamID 转换工具类，不依赖 Steamworks.NET

## 系统要求

- Windows操作系统
- 已安装Steam客户端
- 已安装CS2（Counter-Strike 2）
- .NET 8.0运行环境

## 使用方法

### 图形界面使用

1. 运行应用（`dotnet run` 或双击构建/发布后的 exe）。
2. 填写服务器 IP、端口（默认 27015）、可选密码。
3. 如需，点击“启动 Steam”唤起 Steam 客户端。
4. 点击“启动并连接”，程序将调用 Steam 协议并自动监控 CS2 是否拉起。

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
- **Universe**：Steam宇宙标识符（公共Universe为1）
- **Type**：账户类型（个体用户为1）
- **Instance**：实例标识符（桌面用户为1）
- **Account ID**：Steam账户的唯一标识符

转换公式：
```csharp
SteamID64 = 76561197960265728 + AccountID
```

### 3. 启动流程

1. 检查Steam客户端是否运行，如未运行则启动Steam
2. 构建Steam URL：`steam://run/730//+connect {serverIp}:{serverPort}`
3. 如果服务器有密码，将密码添加到URL中
4. 通过单一Steam协议启动游戏并自动连接到指定服务器

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

### 发布

- Windows 批处理：`build.bat` / `publish.bat`（含 x64/x86、带/不带运行时，单文件并打包压缩）。
- macOS/Linux：可参考 `publish.sh`（当前输出 win-x64 自包含单文件并打 zip）。

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

### 备用方案

如果自动连接失败，程序会提供手动连接的指令，您可以在游戏内控制台（按~键）输入：

```
connect <服务器IP>:<服务器端口>
password <密码>
```

## 项目结构（节选）

```
CS2Launcher/
├── App.xaml                # WPF 应用入口
├── MainWindow.xaml(.cs)    # 主窗口界面与逻辑
├── Program.cs              # 启动器业务逻辑类（供 UI 调用）
├── CS2Launcher.csproj      # 项目配置（UseWPF）
├── build.bat / build.sh    # 构建脚本
├── publish.bat / publish.sh# 发布脚本
├── README.md / README_EN.md# 文档
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

欢迎提交Issue和Pull Request来改进这个项目。

## 更新日志

### v1.0.0
- 初始版本发布
- 支持通过单一Steam协议启动CS2并自动连接服务器
- 完全移除对Steamworks.NET的依赖
- 添加SteamID转换工具类

### v1.0.1
- 基于 WPF 提供图形界面