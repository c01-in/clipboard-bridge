# 构建、安装与卸载

## 环境

| 项目 | 版本 / 条件 |
| --- | --- |
| Windows | Windows 10/11 x64；安装系统服务需要管理员权限 |
| PowerShell | 7.0+，用于构建与发布检查 |
| .NET SDK | 10.0；构建产物自带运行时 |
| JDK | 21，设置 `JAVA_HOME` |
| Android SDK | Platform 36、Build Tools 36.0.0，设置 `ANDROID_HOME` |
| Gradle | 仓库 Wrapper 固定为 8.13，并验证发行包 SHA-256 |
| Android 手机 | Android 8.0+；自动推送需要可用的 Google Play 服务和 FCM 网络 |

Android 8.0+ 是代码的最低版本声明，当前真实设备验证集中在较新的三星设备，不代表全部最低版本或所有厂商均已实测。

## 获取与构建

```powershell
git clone https://github.com/c01-in/clipboard-bridge.git
cd clipboard-bridge
# 以下使用你自己的 JDK / SDK 安装路径
$env:JAVA_HOME = 'C:\Tools\jdk-21'
$env:ANDROID_HOME = 'C:\Tools\android-sdk'
.\scripts\Build.ps1
```

如尚未安装 SDK 组件，可使用 Android Studio SDK Manager，或：

```powershell
& "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat" 'platforms;android-36' 'build-tools;36.0.0' 'platform-tools'
```

请按工具要求阅读并接受适用的软件许可。第一次构建会联网获取 NuGet、Gradle 和 Maven 依赖。

输出：

```text
dist/
├─ ClipboardBridge.apk
├─ windows/
│  ├─ service/ClipboardBridge.Service.exe
│  ├─ tray/ClipboardBridge.exe
│  └─ cli/ClipboardBridge.Cli.exe
└─ scripts/
```

构建脚本第一次运行会在 `local/signing/` 创建自己的签名密钥；后续复用已有密钥。没有 Firebase 配置也能构建，真实使用再按 [Firebase 配置文档](firebase-setup.md) 配对。

## 安装到 Windows

在管理员 PowerShell 中：

```powershell
.\dist\scripts\Install.ps1
```

这会安装 `ClipboardBridge` 系统服务、当前用户登录启动的托盘程序，以及只允许局域网和 Tailscale 来源访问 TCP 45871 的入站规则。默认安装程序位于 `%ProgramFiles%\ClipboardBridge`，仓库部署的数据位于 `local/runtime/`。

同一个用户完成安装和日常使用；当前安装脚本为执行安装的 Windows 用户配置托盘启动和 IPC 权限。若需要不同服务管理员和桌面用户的企业部署，应先调整安装脚本的用户绑定逻辑。

可显式指定数据路径：

```powershell
.\dist\scripts\Install.ps1 -DataRoot 'D:\PrivateData\ClipboardBridge'
```

升级时重新构建并执行安装脚本。脚本会沿用已安装服务的数据路径，保留凭据和配对；若显式指定不同的空数据目录，则需要重新配置。

## 安装到 Android

将 `dist/ClipboardBridge.apk` 传到手机，按系统提示安装。也可以在开发时使用已授权的 ADB：

```powershell
adb install --user 0 -r .\dist\ClipboardBridge.apk
```

`--user 0` 只安装到主用户，避免在工作资料或安全文件夹产生额外安装。ADB 只用于安装和调试，正常同步不依赖 ADB，也不用保持无线调试开启。

## 验证与卸载

```powershell
dotnet run --project .\windows\Tests\Tests.csproj -c Release
.\android\gradlew.bat -p .\android assembleDebug lintDebug
.\scripts\Check-Publish.ps1 -IncludeHistory
```

原生剪贴板测试脚本会实际覆盖当前剪贴板并触发自动同步，仅在使用自己的测试设备时运行：

```powershell
powershell.exe -NoProfile -STA -File .\scripts\Test-ClipboardInput.ps1 -Mode Text
powershell.exe -NoProfile -STA -File .\scripts\Test-ClipboardInput.ps1 -Mode Image -ImagePath .\test-image.png
```

卸载 Windows 程序：

```powershell
.\scripts\Uninstall.ps1
```

服务、托盘启动项、防火墙规则和安装文件会删除，数据目录与签名密钥保留。不要公开上传这些保留文件。
