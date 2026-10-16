# Clipboard Bridge · 剪贴板同步

[English](README.md)

Windows 复制文字或单张图片后自动发送；Android 在 App 内点击发送，即可在 Windows 粘贴。两端自动发现、确认配对，不需要输入 IP 或配对链接。

- **无需注册账号。** 安装后直接配对自己的设备。
- **无需保持 Android App 后台常驻。** 不需要常驻前台服务或 App 自建长连接；由 Google Play 服务按需唤醒。
- **无需自行注册或配置 Google/Firebase 开发者服务。** 不需要 Firebase 项目、服务账号密钥或自建推送服务器。

> **仍依赖 Google FCM：** 推送使用 Google FCM 基础设施，需要 Google Play 服务及 Google 网络连接。无法访问 Google 网络的地区或网络环境暂不支持。

## 下载 0.1.2 预览版

- [Android APK](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.2/ClipboardBridge.apk)
- [Windows x64 安装程序](https://github.com/c01-in/clipboard-bridge/releases/download/v0.1.2/ClipboardBridge-Windows-x64-Setup.exe)
- [手动安装 ZIP、源码与校验值](https://github.com/c01-in/clipboard-bridge/releases/tag/v0.1.2)

普通使用者无需安装开发工具、自行 build 或开启手机调试。Windows 安装器请求管理员权限，尚未使用 Authenticode 签名。

## 开始使用

1. 安装 Windows 程序，从开始菜单打开“剪贴板同步”的设置。
2. 安装并打开 Android App，按 App 引导允许通知，两台设备连接同一 Wi-Fi。
3. 手机选择自动发现的电脑，实际比较两端的六位确认码，在两端确认。不需要输入确认码。
4. Windows 复制文字或单张图片后自动发送，也可用 `Ctrl+Alt+V` 或托盘菜单手动发送。
5. Android 复制内容后，在 App 内点击“发送剪贴板到 Windows”。只有 Windows 实际写入并返回确认后，手机才显示发送成功。

支持 UTF-8 文字和单张图片，正文最大 16 MiB。图片按 PNG 传输；Android 粘贴图片取决于目标 App 是否支持图片剪贴板。超过限制会明确报错，不截断或缩小内容。

## 自动配置推送

0.1.1 使用 UnifiedPush 嵌入式接收库、Web Push 和 VAPID：Windows 用本机生成的独立密钥，直接发送给 Google，手机由现有 Google Play 服务唤醒。
**无需 Firebase 项目、服务账号 JSON、共享发送密钥或我们维护的推送后端。** 手机不用保持 App 前台，也不维持自己的常驻连接。

长文字和图片仍通过局域网或 Tailscale 从电脑下载。首次发现要求同一 Wi-Fi 广播域；跨网前可在电脑高级设置添加 Tailscale 地址。
这仍然依赖 Google 网络，不能绕过 Google 连接故障。详情见 [推送与升级说明](docs/webpush.md)。

## 暂停与恢复

- Windows 托盘“暂停自动发送”保留原有语义：只暂停电脑自动发送，仍可手动发送和接收手机内容。
- 手机主界面“剪贴板同步”开关控制手机的新收发。关闭后保留配对、禁用发送；状态在退出和重启后保留。
- 手机恢复时使用新的接收会话，旧的排队消息不会补写到剪贴板。恢复完成前会显示等待电脑确认。

手机界面仅保留状态、同步开关、发送和必要的未配对引导；“更换电脑、重新连接、关于”位于更多菜单。正常通知授权在 App 内请求；系统不再允许弹窗或通知通道被关闭时，才引导到系统设置。

两端按系统语言支持英语、简体中文、繁体中文、西班牙语、葡萄牙语、法语、德语、日语、韩语，其余语言回退英语。

## 升级、隐私与开源

两端可见版本同为 0.1.2，Android 内部升级编号持续递增并沿用原签名。先更新 Windows，再更新手机并打开一次，原配对自动迁移。
旧 Firebase 配置完整保存在原数据目录的 `configuration.pre-webpush.dpapi` 加密备份中，不再用于推送。

凭据、配对密钥、接收地址和运行数据都放在本地，不进入公开仓库或安装包。不要分享这些私密文件。剪贴板使用 AES-GCM 加密，Google 仍可看到推送元数据。请只在受信任的局域网／Tailscale 网络内使用，详见 [SECURITY.md](SECURITY.md)。

项目自己的代码采用 [MIT](LICENSE)。UnifiedPush 接收库保留 LGPL-2.1，connector 使用 Apache-2.0；两份完整许可证包含在 APK 中，原始库源码随 Release 提供，见 [第三方组件说明](THIRD_PARTY_NOTICES.md)。0.1.2 修正了 0.1.1 的许可证打包标记，收发逻辑不变。

## 开发与验证

- [构建、安装和卸载](docs/build.md)
- [GitHub Actions 自动构建与发布](docs/releases.md)：推送匹配的 `v0.x.y` 标签后自动测试、签名、打包、发布，无需每次本地 build。
- [协议](docs/protocol.md)、[故障排查](docs/troubleshooting.md)、[贡献指南](CONTRIBUTING.md)

85 项协议、IPC、配对、Web Push、暂停修订号、旧配置迁移、许可、语言及真实 HTTP 下载检查通过。独立原生验证包的 6 条短时推送全部到达，含进程终止和强制深度 Doze，见 [探针记录](experiments/webpush-probe/RESULTS.md)。正式版原配对升级、暂停持久化、旧会话拒收、文字和图片双向收发通过，见 [0.1.1 实机记录](docs/verification-0.1.1.md)。
尚未验证所有 Android 厂商、长期锁屏、重启恢复及所有跨网组合；预览版不承诺这些场景的稳定性。
