# 0.1.1 验证记录

2026-10-16 在 Windows 11 x64 和 Samsung SM-F9710 主用户实测，使用原长期配对及原 Android 签名更新。只记录已完成的检查。

## 自动检查

- Windows Service、Tray、CLI 构建通过，无编译警告或错误。
- Android debug / 原签名 release 构建及 lint 通过。
- 83 项协议、配对、IPC、HTTP APK 下载、Web Push、旧配置迁移、暂停修订号和九种语言检查通过。
- Web Push 离线检查使用明确的捕获 HTTP handler，不当作真实 Google 送达证据；以下实机检查单独验证实际链路。

## 实机与安装

| 检查 | 实际结果 |
|---|---|
| 从 0.1.0 更新两端 | 原配对保留，手机自动取得 VAPID 公钥并注册，电脑 configured=true，无需 Firebase 文件或重配 |
| Windows → Android 文字 | Google 真实发送后，Windows 收到手机的实际写入确认 |
| 手机暂停开关 | UI 关闭后 Windows phonePaused=true，拒绝新的发送；手机发送按钮禁用 |
| 暂停持久化 | 强行停止并重新打开 App 后仍暂停；测试后已重新打开并恢复 |
| 恢复 | 新注册会话得到确认，新的文字推送收到写入回执 |
| Windows → Android 单张 PNG | 原图片经局域网加密 blob 获取，校验并写入后返回成功回执 |
| Android → Windows 图片 | 手机前台点击实际发送按钮，Windows 托盘写入后，手机显示 Windows 已确认 |
| 延迟旧会话 | 向 Google 真实提交有效加密但 receiveGeneration 错误的消息，Google 201；手机未覆盖新文字，随后手机发回的原生 Windows 剪贴板仍等于预期新测试文字 |
| 旧会话不消耗新序号 | 上述旧消息故意使用较大的序号，之后正常新推送仍可接收 |
| Android → Windows 文字 | 实际点击发送后，Windows 原生剪贴板读取并精确匹配已知测试文字，不输出其他剪贴板内容 |
| 最终 Windows 安装 EXE | 实际覆盖安装成功，服务运行，控制面板 DisplayVersion=0.1.1 |

原生独立推送探针已另测进程退出、ADB 断开、熄屏及强制深度 Doze，记录见 `experiments/webpush-probe/RESULTS.md`。那份探针结果不能代替正式 App 的所有长期锁屏场景。

## 隐私与状态

旧 Firebase 凭据保存在原数据目录 `configuration.pre-webpush.dpapi`。新的 VAPID 私钥、接收地址和配对密钥留在本地加密配置中，没有上传 GitHub。
公开 APK 和 Windows ZIP 对照本地 Firebase 值、活跃 VAPID 私钥、配对密钥、接收能力地址和认证秘密进行扫描；扫描未输出秘密。源码与可达历史另外执行发布检查。
最终手机同步开关恢复开启；未更改手机电池白名单、系统代理、熄屏超时或其他 App 权限。

## 尚未验收

正式版本的全新真机配对、手机重启后恢复、长期休眠、跨网与不同厂商组合尚未全部实测。协议级全新配对有自动检查，但不替代真实首次权限流程。
用户在系统中强行停止 App 后需重新打开。本次暂停持久化测试已在结束后重新打开 App。
