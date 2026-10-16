# 自动配置推送

0.1.1 使用 UnifiedPush 的嵌入式接收库和 Web Push / VAPID。用户无需创建 Firebase 项目、登录 Google 开发者控制台或导入任何 JSON。无需自建后端。

1. 在同一 Wi-Fi 打开 Windows 设置和手机 App。
2. 选择发现的电脑，实际比较两块屏幕的六位确认码，并在两端确认。
3. Windows 仅把本机 VAPID 公钥经加密配对会话交给手机。
4. 手机自动向已有的 Google Play 服务注册，把接收地址及加密公钥安全传回电脑。
5. Windows 直接向 Google 提交 RFC 8291 加密、RFC 8292 签名的推送；手机实际写入剪贴板后才确认成功。

电脑的 VAPID 私钥随机生成并保存在 DPAPI 配置中，安装包不包含共享发送私钥。手机的私钥由 UnifiedPush 库通过 Android Keystore 保护；配对配置和接收地址另经 Keystore AES-GCM 加密保存。

## 手机暂停

主界面的“剪贴板同步”开关可暂停／恢复收发。状态在 App 退出及重启后保留，不删除配对。
暂停立即阻止手机写入新的剪贴板，禁用手动发送，并通知电脑停止提交推送。电脑离线时也能在手机本地立即生效。
每次切换生成新的接收会话及递增修订号。恢复后仅接受新会话内容；暂停前排队或暂停期间延迟到达的消息不会补写。
恢复需电脑在线确认注册，之后才能发送。此前已经完成的剪贴板写入不会撤销。

Windows 托盘原有暂停仅控制电脑自动发送，手动发送及手机主动上传不受该电脑开关影响。手机暂停则控制手机两方向的新收发。

## 从旧版升级

先更新 Windows，再更新 Android，打开手机 App 一次。沿用原长期配对密钥，通过带随机挑战的加密请求取得 VAPID 公钥并自动注册，无需重新配对或重填文件。
Windows 旧配置完整保存在原数据目录的 `configuration.pre-webpush.dpapi`；旧 Firebase 凭据不再参与发送，但也没有删除或上传。
保留整个数据目录与 Android 签名密钥。若主动重装、清除数据或更换配对，则需要重新确认两端设备。

## 网络边界

这仍然是 Google 推送：两端需要到 Google 的网络连接，手机需要可用的 Google Play 服务。它不能修复运营商、代理或系统休眠策略导致的 Google 连接问题。
文字以加密清单发送，长文字和单张图片从电脑的局域网／Tailscale 地址下载。图片传输时电脑必须可达。
用户在系统中“强行停止”App 后，需重新打开才能继续；普通后台进程结束与强行停止不同。
原生嵌入式注册使用 UnifiedPush 的实现，需要继续验证各厂商及 Google Play 服务版本的兼容性。短时实测不代表长时间可靠性保证。

参考：[UnifiedPush 实现](https://unifiedpush.org/developers/implementations/)、[嵌入式接收库](https://unifiedpush.org/kdoc/embedded_fcm_distributor/)、[RFC 8291](https://www.rfc-editor.org/rfc/rfc8291)、[RFC 8292](https://www.rfc-editor.org/rfc/rfc8292)。
