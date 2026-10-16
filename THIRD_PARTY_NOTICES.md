# 第三方组件

Clipboard Bridge 自身采用 MIT 许可证。依赖组件保留其各自许可，生成或分发二进制时也应保留依赖自带的版权和许可文件。

| 组件 | 用途 | 许可与来源 |
| --- | --- | --- |
| .NET / Windows Forms / Microsoft.Extensions | Windows 托盘、服务和运行时 | [dotnet/runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)、[dotnet/winforms](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT)，MIT |
| QRCoder | Windows 配对二维码 | [QRCoder](https://github.com/codebude/QRCoder/blob/master/LICENSE.txt)，MIT |
| UnifiedPush connector 3.0.10 | 原生 Web Push 加密及接收连接 | [connector](https://github.com/UnifiedPush/android-connector)，Apache-2.0；完整许可证位于 APK assets/third-party/UnifiedPush-connector-Apache-2.0.txt |
| UnifiedPush embedded-fcm-distributor 3.0.0 | 向 Google Play 服务注册原生 Web Push | [embedded distributor](https://github.com/UnifiedPush/android-embedded_fcm_distributor)，按上游根 LICENSE 的 LGPL-2.1 提供；完整许可证位于 APK assets/third-party/UnifiedPush-LGPL-2.1.txt；两个原始库源码均随 Release 的 ClipboardBridge-Android-Library-Sources.zip 提供 |
| Google Tink | UnifiedPush Web Push 解密 | [Tink](https://github.com/tink-crypto/tink-java/blob/main/LICENSE)，Apache-2.0；connector 自带的修正版 Web Push 类包含在其源码包中 |
| AndroidX WorkManager / Concurrent | 短暂接收任务与重试 | [AndroidX](https://android.googlesource.com/platform/frameworks/support/+/androidx-main/LICENSE.txt)，Apache-2.0 |
| Gradle Wrapper | 可复现的 Gradle 版本入口 | [Gradle](https://github.com/gradle/gradle/blob/master/LICENSE)，Apache-2.0 |
| Pillow | 可选的图标生成脚本 | [Pillow](https://github.com/python-pillow/Pillow/blob/main/LICENSE)，HPND 及文件注明的许可 |

图标由仓库中的确定性绘图脚本生成，并随项目按 MIT 许可提供。

本项目未修改上述 UnifiedPush 库，也未混淆发布 APK。使用者可以从对应 Release 标签取得本项目完整源码，结合附带的库源码和构建文件重新构建、更换库或修改用于个人使用。Google Play 服务是设备现有组件，不随安装包分发，遵循其适用条款。
