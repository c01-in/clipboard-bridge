# 安全与隐私

## 密钥与发布

0.1.1 不需要用户的 Firebase 项目。每台电脑独立生成 P-256 VAPID 密钥，私钥在 Windows DPAPI 数据目录保存，只把公钥通过加密配对传给手机。Android 的 UnifiedPush 密钥由 Keystore 保护，配对配置和接收地址也加密保存。
安装包不包含维护者的 Firebase 凭据、VAPID 私钥、设备接收地址或配对密钥。Actions 仅使用加密保存的 APK 签名 keystore 和密码，不使用运行时身份。只有受信任的发布工作流能读取签名 Secrets。
升级旧配置时先完整复制 `configuration.pre-webpush.dpapi`，再从活跃配置移除不用的 Firebase 字段。备份不提交；它仍包含旧凭据，须按私密文件保管。

不要公开 `local/`、`secrets/`、DPAPI/CLIXML 文件、服务账号 JSON、签名私钥、推送 endpoint 或旧的含密钥配对链接。发布前执行 `scripts/Check-Publish.ps1 -IncludeHistory -UseLocalSecrets`。扫描不是完整的安全审计。

## 传输边界

- 自动发现 UDP 45872 仅适用于本地子网。设备名和来源地址不构成身份验证。
- 配对需要在两块屏幕实际核对相同的六位确认码。ECDH / HKDF 派生临时密钥，双方确认后才能取得长期配对密钥、电脑地址和 VAPID 公钥。
- 短文字使用配对密钥 AES-256-GCM，再按 RFC 8291 加密；VAPID 按 RFC 8292 签名。长正文和图片通过局域网／Tailscale 下载加密 blob。
- 推送 endpoint 仅允许 Google 的 HTTPS Web Push 路径，禁止自定义主机、非默认端口和重定向，防止注册数据把发送端变为请求代理。
- Google Play 服务仍维持到 Google 的连接，Google 能观察时间、来源 IP、接收地址和密文大小。此方案不是脱离 Google 的离线推送。
- HTTP 201 只代表 Google 接受请求；只有手机实际写入并返回配对密钥认证的回执，Windows 才显示已写入。
- Windows HTTP 接口仅面向受信任的局域网／Tailscale。不要将 TCP 45871、UDP 45872 或命名管道转发到公网。

## 暂停

手机暂停与剪贴板写入共用锁；关闭后，排队任务和新消息不能继续写入。每次暂停和恢复切换接收会话，旧会话不能在恢复后补写。
电脑通过加密注册获知暂停状态；单调递增修订号阻止迟到请求撤销新的暂停。电脑离线不影响手机本地暂停生效。此前已完成的写入不会回滚。

## 已知限制

这是 0.x 预览版，未经过独立密码协议审计。局域网自动发现与六位确认码方案仅面向受信任网络，不应视为对所有主动攻击的完整防护。
Google Play 服务原生 Web Push 注册路径依赖 UnifiedPush 实现，兼容性可能随版本变化。强行停止 App、系统休眠及网络阻断会影响送达。不要把短时实测理解为可靠性承诺。
Windows 安装器尚未 Authenticode 签名。第三方库的许可证及来源见 THIRD_PARTY_NOTICES.md。

发现安全问题时，请私下联系仓库维护者，避免在公开 Issue 中上传真实剪贴板、接收地址或凭据。可提供脱敏日志、版本和复现步骤。
