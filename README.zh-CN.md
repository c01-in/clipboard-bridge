<div align="center">

<img src="docs/assets/clipboard-bridge-hero.png" alt="Clipboard Bridge：Windows 电脑与 Android 手机双向分享文字和图片" width="100%">

# Clipboard Bridge · 剪贴板同步

> **开发中的 0.2.0：多文件互传。** Android 可通过系统选择器或分享菜单发送多张图片、视频及其他文件；Windows 保存到可设置的接收文件夹，也可从托盘或资源管理器“发送到 → 剪贴板同步”多选发送，两端均有进度显示。剪贴板模式仍限文字或单张图片。本次更新尚未发布，下方下载链接仍是 0.1.4。详见[文件传输说明](docs/file-transfers.md)。

**Windows 自动发送，Android 点击发送。**

文字、单张图片，你自己的设备。免费、开源。

[![构建](https://github.com/c01-in/clipboard-bridge/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/c01-in/clipboard-bridge/actions/workflows/ci.yml)
[![预览版 0.1.4](https://img.shields.io/badge/preview-0.1.4-8B5CF6?style=flat-square)](https://github.com/c01-in/clipboard-bridge/releases/tag/v0.1.4)
[![MIT](https://img.shields.io/badge/app_license-MIT-14B8A6?style=flat-square)](LICENSE)

[English](README.md) · [简体中文](README.zh-CN.md) · [下载安装](#下载安装) · [快速上手](#安装配对开始复制) · [开发文档](#开发与贡献)

<sub>上方为 AI 生成的概念插画，并非 App 实际界面截图。</sub>

</div>

<p align="center">
  <img src="docs/assets/no-account.zh-CN.svg" width="270" alt="无需注册：不用创建账号">
  <img src="docs/assets/no-background.zh-CN.svg" width="270" alt="无需保持 App 常驻：由 Google Play 服务按需唤醒">
  <img src="docs/assets/no-firebase.zh-CN.svg" width="270" alt="无需配置 Firebase：不用申请项目或导入服务账号密钥">
</p>

> [!IMPORTANT]
> **仍然依赖 Google FCM。** 免注册、免 Firebase 配置不代表不依赖 Google：手机需要可用的 Google Play 服务，两端需要 Google 网络连接。**无法访问 Google 的地区或网络环境暂不支持。** Android App 无需常驻前台服务或自建长连接，由 Google Play 服务负责唤醒。

## 下载安装

<div align="center">

[![下载 Windows 版](https://img.shields.io/badge/Download_for_Windows-x64_EXE-6366F1?style=for-the-badge)](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.4/ClipboardBridge-Windows-x64-Setup.exe)
[![下载 Android 版](https://img.shields.io/badge/Download_for_Android-APK-14B8A6?style=for-the-badge&logo=android&logoColor=white)](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.4/ClipboardBridge.apk)

**安装两端，确认配对，开始复制。**

[Windows 手动安装 ZIP、库源码与 SHA-256 校验值](https://github.com/c01-in/clipboard-bridge/releases/tag/v0.1.4)

</div>

**当前版本：0.1.4 预览版。** 普通用户无需 SDK、自行 build 或开启手机调试。Windows 安装时需要管理员权限，安装器尚未使用 Authenticode 签名。Android 需要 Android 8.0 或更新版本及 Google Play 服务。

[代码签名政策（Code signing policy）](CODE_SIGNING_POLICY.md)：正在申请 SignPath Foundation 免费签名；现有 Windows 下载仍未签名。

## 为剪贴板而做

| | 你可以做什么 |
| :--- | :--- |
| **🖥️ Windows 复制即发送** | 自动发送文字或单张图片；也可使用 `Ctrl+Alt+V` 或托盘菜单手动发送。 |
| **📱 Android 直接分享** | 在其他 App 分享文字或单张图片，选择“剪贴板同步”；也可打开 App 点击“发送剪贴板到 Windows”。 |
| **🔗 配对不用输入** | 在同一 Wi-Fi 自动发现电脑，比较六位确认码并在两端确认，无需输入 IP 或配对链接。 |
| **⏸️ 随时暂停** | Windows 托盘可暂停自动发送；Android 可暂停新的收发，并保留配对。 |
| **🔐 内容加密传输** | 剪贴板采用 AES-GCM 加密。配对密钥和电脑推送身份保存在本地；Google 仍可看到推送元数据。 |
| **🌍 跟随系统语言** | 两端支持九种语言，其余语言回退英语。 |

支持 UTF-8 文字和**单张图片，正文最大 16 MiB**。图片按 PNG 传输；Android 粘贴图片取决于目标 App 是否支持图片剪贴板。超过限制会明确报错，不截断或缩小内容。

## 安装、配对、开始复制

1. **安装两端。** 从开始菜单打开 Windows 设置，再打开手机 App。连接同一 Wi-Fi，按手机内的引导允许通知。
2. **确认设备。** 手机选择自动发现的电脑，比较两端显示的六位确认码，并在两端确认。确认码不同就不要批准。
3. **试一次复制。** Windows 复制文字或单张图片，在 Android 粘贴。反向可在系统分享菜单选择“剪贴板同步”，或复制后打开 App 点击“发送剪贴板到 Windows”；只有 Windows 实际写入并返回确认，手机才显示成功。

还没安装电脑端？Android 的**获取 Windows 版**入口可复制或分享 [GitHub Releases 页面](https://github.com/c01-in/clipboard-bridge/releases)，在电脑上打开后自行选择安装包。

正常使用无需注册 App 账号、申请 Firebase 项目、导入凭据或开启调试模式。通知授权、配对确认和 Android 安装权限仍需用户同意。

> [!TIP]
> **先在同一 Wi-Fi 使用。** 需要时，剪贴板内容直接在两台设备之间传输。正常局域网使用无需安装额外的组网软件，Google 网络仍然必需。

## 推送不用自己申请项目

Windows 自动生成自己的 VAPID 密钥，直接向 Google 发送加密 Web Push。UnifiedPush 嵌入式接收库使用 Google FCM 基础设施，由现有 Google Play 服务按需唤醒 Android。

**无需我们维护的中转服务器，无共享发送密钥，无 Firebase 服务账号文件。**

长文字和图片直接通过局域网从电脑下载。首次发现要求同一 Wi-Fi 广播域。这条通道不能修复 Google 网络连接故障，见 [推送与升级说明](docs/webpush.md)。

<details>
<summary><strong>可选：跨网使用</strong></summary>

跨网时，可借助已有的私有网络（例如 Tailscale）承载直连通道。离开局域网前，在电脑高级设置中添加电脑可达的私有网络地址。这只是可选方案，两端在同一 Wi-Fi 时不需要安装。Google 网络仍然必需。

</details>

<details>
<summary><strong>暂停行为与 Android 权限</strong></summary>

- **Windows 托盘暂停**只影响电脑自动发送，仍可手动发送和接收手机内容。
- **Android“剪贴板同步”开关**暂停手机的新收发，保留配对；状态在退出和重启后保留。
- **恢复时建立新接收会话。** 旧排队消息不会补写剪贴板；电脑确认注册后，手机才恢复发送。

手机界面保留状态、同步开关、发送和必要的配对引导；更换电脑、重新连接、关于位于更多菜单。通知权限在 App 内请求；系统不再允许弹窗或通知通道被关闭时，才引导到系统设置。

</details>

<details>
<summary><strong>九种系统语言</strong></summary>

English · 简体中文 · 繁體中文 · Español · Português · Français · Deutsch · 日本語 · 한국어

两端跟随系统语言，其余语言回退英语。

</details>

<details>
<summary><strong>升级、隐私与许可证</strong></summary>

两端可见版本同为 0.1.4，Android 内部升级编号持续递增并沿用原签名。先更新 Windows，再更新手机并打开一次，原配对自动迁移。旧 Firebase 配置完整保存在原数据目录的 `configuration.pre-webpush.dpapi` 加密备份中，不再用于推送。

凭据、配对密钥、接收地址和运行数据保存在本地，不进入公开仓库或安装包。不要分享私密文件。剪贴板内容使用 AES-GCM 加密，Google 仍可看到推送元数据。请只在受信任的局域网或私有网络内使用，见 [SECURITY.md](SECURITY.md)。

项目自己的代码采用 [MIT](LICENSE)。UnifiedPush 接收库保留 LGPL-2.1，connector 使用 Apache-2.0；两份完整许可证包含在 APK 中，原始库源码随 Release 提供，见 [第三方组件说明](THIRD_PARTY_NOTICES.md)。0.1.2 修正了 0.1.1 的许可证打包标记，收发逻辑不变。

</details>

## 有验证，也有明确的限制

**85 项自动检查通过**，覆盖协议、IPC、配对、Web Push、暂停修订号、迁移、许可证、语言与真实 HTTP 下载。

- **原生推送探针：** 6 条短时消息到达，含进程终止和强制深度 Doze。[探针记录](experiments/webpush-probe/RESULTS.md)
- **正式版实机测试：** 原配对迁移、暂停持久化、旧会话拒收、文字与图片双向收发通过。[实机记录](docs/verification-0.1.1.md)
- **Android 快捷分享：** 12 项模拟器检查通过，包含其他 App 的图片 URI 授权和屏幕重建后不重复发送。微信里第二条复制 Toast 的来源仍未确认。[分享验证](docs/android-sharing.md)
- **自动发布：** 发布前执行测试、Android 签名、打包、Windows 全新安装和卸载。[GitHub Actions](https://github.com/c01-in/clipboard-bridge/actions/workflows/release.yml)

**尚未**验证所有 Android 厂商、长期锁屏、重启恢复及所有跨网组合。预览版不保证长期投递表现。

## 开发与贡献

| 从这里开始 | 更多说明 |
| :--- | :--- |
| [构建、安装与卸载](docs/build.md) | [协议](docs/protocol.md) |
| [GitHub Actions 发布](docs/releases.md) | [故障排查](docs/troubleshooting.md) |
| [贡献指南](CONTRIBUTING.md) | [安全](SECURITY.md) |
| [Google Play 发布评估](docs/google-play.md) | [隐私政策](PRIVACY.md) |

推送匹配的 `v0.x.y` 标签后，自动测试、签名、打包并发布两端应用。用户下载安装包；维护者无需每次在本地构建。

---

<div align="center">

**一个剪贴板桥梁，连接两台设备。**

[下载预览版](#下载安装) · [反馈问题](https://github.com/c01-in/clipboard-bridge/issues) · [English guide](README.md)

</div>
