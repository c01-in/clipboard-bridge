# 传输协议

## 自动发现与配对（0.1.1 测试版）

1. 电脑设置开启 120 秒可发现窗口；手机仅在 App 前台向 Wi-Fi 子网广播 UDP 45872。发现包上限 1400 字节，携带协议版本、随机标识、设备名称、32 字节随机数与 P-256 SPKI 公钥，不包含长期密钥或 Firebase 数据。
2. 双方构造顺序固定、换行分隔的 transcript：协议、手机标识、Base64 UTF-8 手机名、手机随机数、公钥、Base64 UTF-8 电脑名、会话标识、电脑随机数、公钥。使用原始 ECDH 共享秘密、SHA-256(transcript) 作为盐和 `ClipboardBridge.Pairing.v1` 作为 info，HKDF-SHA256 派生 32 字节临时密钥。确认码为 HMAC-SHA256(key, `confirmation-code`) 的前四字节大端无符号数对 1,000,000 取模，补足六位。
3. 手机加密确认通过 `/api/pairing/{id}/confirm`，电脑批准仅通过本机受限 IPC。两端都批准后，`/configuration` 才返回 AES-GCM 加密的随机长期密钥、电脑标识、地址和该电脑的 VAPID 公钥。不传输 VAPID 私钥。会话限制来源 IP、有效期 120 秒和最多 16 个同时请求。
4. 手机用 Keystore 加密保存待完成记录，用 VAPID 公钥注册 Web Push 接收地址和加密公钥，再加密提交 `/complete`。Windows 此时才提交新配对并撤销旧密钥。同一接收注册及暂停会话的完成重试幂等；若回执丢失，手机可以凭新长期密钥向 `/api/register` 恢复已提交的配对。注册请求携带新随机挑战，手机必须验证加密回执中的同一挑战，HTTP 200 本身不代表恢复成功。注册与配对提交共用锁，旧密钥不能越过新配对覆盖接收注册。
5. 旧配对保留到新配对完成。配对后，手机前台发带随机挑战和长期密钥 HMAC 的 reconnect；电脑证明响应后，手机才更新地址。旧版没有电脑标识的配置也能通过同一长期密钥迁移，序号不会因地址变化重置。

下载路由 `/app/android` 只提供签名 APK；安装二维码没有配对数据。新安装与配对均不需要 ADB。发现限同一 Wi-Fi 广播域，跨网 Tailscale 地址需要事先在高级设置添加；普通路由不会转发此广播。

## Windows → Android

Windows 按 RFC 8291 加密、RFC 8292 签名，直接向 Google Web Push endpoint 发送 `{v:"1",box:...}`。`box` 是配对密钥 AES-256-GCM 加密的内容清单，含手机当前 `receiveGeneration`。短文字内联，其他正文通过 `/blob/{id}` 下载密文。Android 校验序号、有效期、大小和 SHA-256，实际写入后向 `/api/ack` 回传加密确认。

## Android → Windows

1. 用户在前台 App 点击发送。此时读取系统剪贴板快照，支持 UTF-8 文字和一张可访问的图片。非 PNG 图片转为单张 PNG，不缩放、不截断超限内容。
2. 手机生成随机 32 位十六进制请求 ID、持久递增序号，以及含 MIME、120 秒有效期、字节数、SHA-256 的元数据。正文最大 16 MiB。
3. POST `/api/clipboard`，JSON 字段 `envelope` 为加密元数据，`data` 为单独加密的正文。两者复用现有配对密钥；每次加密使用独立随机 nonce，AAD 仍为 `ClipboardBridge/v1`。HTTP 请求上限 32 MiB，其余 POST 路由保留 12 KiB 上限。
4. 服务校验后，在内存中等待托盘程序，最长 20 秒。托盘通过原有受用户 SID 限制的命名管道获取 `receive`，在桌面 STA 线程写入 Windows 系统剪贴板，再调用 `receive-ack`。单个未完成请求期间拒绝其他内容；同一请求的重试等待同一回执。
5. 成功写入后，服务持久保存已完成的手机序号与 ID，并取消旧的待发手机内容。已完成请求再次上传会返回成功回执而不重复写入；旧序号拒绝覆盖。剪贴板写入失败不推进已完成序号。
6. HTTP 200 的 JSON `data` 是加密回执，包含 `id`、`sequence`、`status` 和可选 `detail`。手机核对 ID、序号和 `applied` 状态后才显示成功。HTTP 200 本身不代表写入成功。

Windows 使用系统剪贴板序号抑制本次接收产生的自动发送，后续真实复制继续自动同步。暂停开关仅控制 Windows 自动发送，手动发送和 Android 主动上传仍然有效。

两端正常使用均不依赖 ADB；手机反向发送不经过 Google，也不增加常驻服务。接口只面向配对的受信任设备和私有网络；不要直接暴露公网。

## 接收会话与暂停

手机每次暂停／恢复生成新的 `receiveGeneration`（32 位十六进制）和递增 `revision`。加密注册包含 subscription、设备名、会话、revision、paused 和随机 challenge；电脑拒绝旧 revision 及同 revision 的矛盾状态，回执同时绑定这些字段。
手机暂停立即阻止剪贴板写入并取消接收任务。恢复后旧 generation 消息被丢弃；电脑更新会话时清理 pending，不补发暂停内容。反向上传也携带 generation，电脑拒绝旧会话和暂停状态。

旧配置通过 `/api/push-configuration` 的加密随机挑战请求迁移，只有持有原配对密钥的手机能读取 VAPID 公钥；回复必须绑定 challenge 和电脑标识。Windows 首次迁移前完整保留旧 DPAPI 加密文件。
