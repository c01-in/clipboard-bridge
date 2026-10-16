# 原生 Web Push 独立验证

包名为 `com.colin.clipbridge.webpushprobe`，与生产 App 完全隔离。版本为 `0.1.0-probe`。

接收组件使用 UnifiedPush `connector:3.0.10` 和 `embedded-fcm-distributor:3.0.0` 原始库。
不初始化 Firebase SDK，不需要 Firebase 项目、服务账号 JSON 或我们部署的代理。
Windows 使用本机持久生成的 P-256 VAPID 密钥，按 RFC 8291 加密、RFC 8292 签名，直接 POST 到 Google。

本实验不读写剪贴板。收到消息后先校验解密状态、消息 ID、有效期，然后写入 App 私有证据文件并显示通知。
没有自行启动的前台服务、开机接收器、常驻网络连接或后台轮询。
依赖 Google Play 服务的网络连接；本实验不能证明绕开 Google 的网络限制。

## 构建与测试

使用仓库现有 JDK / SDK / Gradle wrapper：

```powershell
android/gradlew.bat -p experiments/webpush-probe assembleDebug lintDebug --console=plain
python -X utf8 experiments/webpush-probe/probe.py init
python -X utf8 experiments/webpush-probe/probe.py install --serial PHONE
python -X utf8 experiments/webpush-probe/probe.py register --serial PHONE
python -X utf8 experiments/webpush-probe/probe.py collect --serial PHONE
python -X utf8 experiments/webpush-probe/probe.py send --text 'Foreground probe'
python -X utf8 experiments/webpush-probe/probe.py results --serial PHONE
```

Python 需要 `cryptography`。Windows 私钥和接收地址保存在忽略目录 `local/webpush-probe/state.dpapi`，通过当前 Windows 用户的 DPAPI 加密。
ADB 只负责安装、配置公钥和读取证据；`send` 不调用 ADB。测试时不要用本地广播模拟 Google 推送。
发送端 HTTP 成功仅表示 Google 接受；必须匹配 App 私有事件文件中的相同 ID 且 `decrypted=true` 才判定到达。
应分别验证前台、退到桌面、进程自然/普通终止、锁屏以及 Doze，并恢复任何改变的测试状态。
`am force-stop` 是用户强行停止的语义，不能当作普通进程终止。
`clockDeltaMs` 是手机收件时钟减去电脑发件时钟，不是精确网络延迟；早期验证包的 `latencyMs` 字段也是这个未经校准的差值。

## 第三方组件与源码

本实验自己编写的文件沿用仓库 MIT 许可证；依赖库按各自许可证使用。
UnifiedPush connector 的根许可证为 Apache-2.0，embedded-fcm-distributor 的根许可证为 LGPL-2.1；发布实验 APK 时同时提供对应许可证、原始库源码和本实验源码。

- https://github.com/UnifiedPush/android-embedded_fcm_distributor
- https://github.com/UnifiedPush/android-connector
- https://unifiedpush.org/developers/implementations/
- https://unifiedpush.org/kdoc/embedded_fcm_distributor/

目前仅构建实验工具，真实设备结果必须以独立证据为准，不能据此宣称生产功能已经可用。
