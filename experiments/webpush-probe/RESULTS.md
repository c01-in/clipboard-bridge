# 实机验证结果：原生 Web Push / VAPID

测试日期：2026-10-15（电脑本地时区）。设备：Samsung SM-F9710，Google Play 服务 26.39.33。

结论：本次短时实测中，Windows 直接向 Google Web Push 接口发送，独立 Android APK 收到并成功解密，包含后台进程重建和强制深度 Doze 场景。未使用 Firebase 项目、服务账号或自建推送代理。

## 隔离范围

- 测试包：`com.colin.clipbridge.webpushprobe`，显示名“Web Push 验证”，版本 `0.1.0-probe`。
- 原生产包 `com.colin.clipbridge` 保留，未覆盖或修改其配对、签名、Firebase 配置。
- 本实验不访问剪贴板；测试正文为可识别的人工测试文本。
- Windows VAPID 私钥与推送接收地址通过当前用户 DPAPI 加密，保存在忽略目录 `local/webpush-probe/state.dpapi`。
- 最终 APK SHA-256：`7fe81c49267ad28c439eae6917480daf55ee7affaae1ef5706072543cfa68108`。

## 已验证的真实链路

每条消息有 Windows 随机生成的 ID。Google 返回 HTTP 201 后，还必须在手机私有文件中找到同一 ID 和 `decrypted=true`，才计为收到。
发送脚本禁用 HTTP 重定向与代理，目标限制为 `https://fcm.googleapis.com/fcm/send/...`。
未使用本地广播、ADB 注入消息或模拟成功回执。

| 场景 | Google | 手机实际事件 | 证据 |
|---|---|---|---|
| App 前台 | 201 | 已解密收到 | ID `f139ca94290b438628af668d030787bc` |
| 回到桌面 | 201 | 已解密收到，`foreground=false` | ID `c8f2b8a24a03b255c032df53a4e572a7` |
| 普通进程终止＋断开 ADB | 201 | 已解密收到，新进程启动 | ID `f6b63e7e2435d428c53309c171510478`，PID 24850 → 27560 |
| 熄屏 | 201 | 已解密收到，`interactive=false` | ID `08ff80d4e9e01923e81ebcbe3f055158` |
| 深度 Doze＋终止进程＋断开 ADB | 201 | 已解密收到，`deviceIdle=true`、`interactive=false`，新 PID 28000 | ID `27201f4c197aae387a2f9d7dc98a0dde` |
| 原地更新验证 APK 并重新注册 | 201 | 已解密收到，持久配置可继续使用 | ID `0d6904d6233f7c688c8b3e950f906647` |

`am kill` 第一次执行后进程未退出，因此那次仅计为“回到桌面”。随后用 `run-as` 在测试 App 自己的 UID 下对该包 PID 发 SIGKILL，并确认 `pidof` 无输出；没有使用 `force-stop`。
进程终止和 Doze 两次测试，在发件前断开 ADB，Google 接受请求后才重新连接读取收件证据。ADB 不参与发送或唤醒。
最终查询该包 Activity Manager 服务列表为 `(nothing)`，无常驻服务。

## 测试状态恢复

强制 Doze 已解除。随后用 `deviceidle motion` 退出深度休眠，最终状态为 `mForceIdle=false`、deep ACTIVE、light ACTIVE、屏幕 Awake。
原熄屏超时 300000 ms 保持不变。未改电池白名单、休眠名单、系统代理或任何生产 App 权限。
没有关闭用户自己开启的无线调试。

## 不能由本次测试推导的结论

- 仅 6 条短时消息，不能承诺长期锁屏、Google 限流或不同网络下的可靠性。
- `clockDeltaMs`（早期版本名为 `latencyMs`）为两台设备时钟之差，不能作为精确送达延迟。额外时钟采样发现手机约比电脑慢 0.2 秒，因此报告不列毫秒级延迟。
- 原生注册路径来自 UnifiedPush 实现，不是 Firebase Android SDK 的常规项目注册路径；Google Play 服务版本变化需要继续验证。
- 仍依赖 Google Play 服务和到 Google 的网络连接。
- 自动配对、生产剪贴板接收、图片传输、重启后恢复、多电脑以及正式发布均未由本实验实现或验收。

原始发件及手机事件文件只保存在 `local/webpush-probe/`，不含真实剪贴板。接收地址和认证材料不写入本报告或公开源码。
