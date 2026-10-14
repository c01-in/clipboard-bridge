# Clipboard Bridge

<img src="windows/Tray/Assets/ClipboardBridge.png" width="72" height="72" alt="Clipboard Bridge 图标">

Windows 复制，Android 直接粘贴。支持文字和单张图片，手机通过 FCM 按需接收，不需要运行常驻接收服务。

[English](README.en.md) · [配置 Firebase](docs/firebase-setup.md) · [构建与安装](docs/build.md) · [问题排查](docs/troubleshooting.md) · [安全说明](SECURITY.md)

## 功能

- Windows 托盘自动监听剪贴板；可暂停自动同步，或用 `Ctrl+Alt+V` 手动发送。
- 支持文字、原生剪贴板图片及复制的单个 PNG/JPEG/BMP/GIF 图片文件。图片转换为单张 PNG，内容上限 16 MiB。
- Android 通过 Firebase Cloud Messaging 唤醒并写入系统剪贴板。每次同步显示普通结果通知，无持续运行的接收前台服务。
- 剪贴板内容使用 AES-256-GCM 加密；手机检查有效期、序号和 SHA-256。
- Windows 区分“已提交 FCM”和“手机已确认写入”；失败重试，过期内容不会继续覆盖新剪贴板。
- 局域网直连，也可使用自己配置的 Tailscale 地址。

![深色与浅色背景下的托盘图标](windows/Tray/Assets/icon-preview.png)

## 开始使用

**这是自托管工具。每位使用者使用自己的 Firebase 项目，仓库不提供维护者的 Firebase 配置或私钥。**

1. 按 [构建文档](docs/build.md) 准备 Windows、.NET 10、JDK 21 和 Android SDK 36，然后运行 `scripts/Build.ps1`。
2. 管理员 PowerShell 执行 `dist/scripts/Install.ps1`，在手机安装 `dist/ClipboardBridge.apk`。
3. 按 [Firebase 配置文档](docs/firebase-setup.md) 创建自己的项目、注册 `com.colin.clipbridge`，取得 Android 配置和专用发送账号 JSON。
4. 在托盘设置导入这两个配置，填写手机能连接的电脑地址，生成配对链接，在 Android App 中确认配对并允许通知。
5. 复制文字，等手机确认后实际粘贴；再测试一张图片。

```powershell
git clone https://github.com/c01-in/clipboard-bridge.git
cd clipboard-bridge
.\scripts\Build.ps1 -JavaHome 'C:\Tools\jdk-21' -AndroidSdk 'C:\Tools\android-sdk'
# 管理员 PowerShell
.\dist\scripts\Install.ps1
```

正常使用不依赖 ADB，也不需要保持无线调试开启。

## 网络与限制

- FCM 需要可用的 Google Play 服务和 Google 网络，不能保证每条消息立即送达。系统后台限制可能导致延迟；强行停止 App 后需手动打开恢复。
- 短文字可直接包含在加密推送中。较长文字和图片需要手机通过局域网或 Tailscale 访问电脑 TCP 45871；回执也需要这条连接。
- 当前是一台 Windows 电脑和一台受信任手机的单向同步。不支持 Android → Windows、iOS、任意文件或多图同步。
- 图片粘贴取决于目标 App 是否支持图片剪贴板。
- 公开源码不等于零配置，也不等于所有 Google 产品免费；是否启用其他产品及相关费用由各使用者自行决定。

## 私密文件放在哪里

```text
clipboard-bridge/
├─ windows/              Windows 源码
├─ android/              Android 源码与 Gradle Wrapper
├─ scripts/              构建、安装、迁移与发布检查
├─ docs/                 配置、构建与排错文档
├─ dist/                 本机构建产物，不提交 Git
└─ local/                整个目录不提交 Git
   ├─ runtime/           加密发送凭据、设备配对与备份
   ├─ signing/           APK 签名密钥与受保护密码
   └─ credentials/       自己的 Firebase 原始配置
```

Windows 程序安装在 `%ProgramFiles%\ClipboardBridge`。从源码仓库安装时，服务使用 `local/runtime/`，升级保留已有数据路径与签名密钥。移动仓库前先停止服务并按新路径重新安装。

不要公开 `local/`、服务账号文件、签名密钥或配对二维码。DPAPI 保护依赖原 Windows 环境，重装或换电脑前需按自己的安全流程备份。详见 [安全说明](SECURITY.md)。

## 开发与验证

```powershell
dotnet run --project .\windows\Tests\Tests.csproj -c Release
.\android\gradlew.bat -p .\android assembleDebug lintDebug
.\scripts\Check-Publish.ps1 -IncludeHistory
```

本地验证过 Windows 原生剪贴板读取、真实 FCM 投递、Android 写入与回执，以及文字和图片实际粘贴。CI 验证构建、lint 和本地协议检查，不会使用任何人的 Firebase 凭据做真实推送。

没有完成全部 Android 版本、厂商、长时间锁屏和跨网组合测试。欢迎按 [贡献指南](CONTRIBUTING.md) 提供可复现反馈。

## 许可证

[MIT](LICENSE)。可以免费使用、修改和分发，也允许商业使用；分发时保留许可证和版权声明。第三方组件遵循各自许可证，见 [组件说明](THIRD_PARTY_NOTICES.md)。
