# 配置自己的 Firebase

本工具没有公共共享后端。每位使用者的 Windows 和 Android 配置必须来自同一个 Firebase 项目。你不需要、也不应索取维护者的 Firebase 凭据。

## 普通用户能否免配 FCM？

技术上可以，由开发者提供公共推送中继，并把发送权限保存在后端。使用云托管服务不需要自己管理虚拟机，但仍需维护程序、密钥、访问控制和费用。当前 0.1.0 尚未部署公共中继，所以仍是自托管配置；公开安装包不包含维护者的 Firebase 配置或私钥。FCM 本身属于免费产品，中继托管可能另有费用，见 [Firebase 定价](https://firebase.google.com/pricing)。

## 1. 创建项目并注册 Android App

1. 打开 [Firebase Console](https://console.firebase.google.com/)，使用自己的 Google 账号创建项目。
2. 本工具不要求启用 Analytics、Gemini、Firestore、Storage 或 Cloud Functions。请按自己的需要选择其他服务，不需要为这两个设备传图开通云存储。
3. 添加 Android 应用，包名填写 **`com.colin.clipbridge`**。使用仓库默认构建时必须完全一致；如果修改包名，需要同步修改 Android provider authority、Windows 的包名校验以及客户端配置。
4. 下载 `google-services.json`，保存在仓库的 `local/credentials/`。
5. 在项目设置中确认 **Firebase Cloud Messaging API（HTTP v1）** 已启用。它通常会随新 Firebase 项目启用；若发送返回 API 未启用错误，请按控制台链接启用该 API。

Android 客户端配置在配对时动态导入，**不要把 `google-services.json` 放进源码、APK 资源或 Git 历史**。没有它也能构建 App，但不能注册真实推送。

## 2. 创建 Windows 发送账号

1. 打开该项目的 Google Cloud Console → IAM 与管理 → 服务账号。
2. 创建一个专供本工具使用的服务账号，例如 `clipboard-sender`。
3. 只授予本项目的 **Firebase Cloud Messaging API Admin** 角色（`roles/firebasecloudmessaging.admin`），无需 Owner 或 Editor。该预定义角色包含推送及 FCM API 相关权限，并非 Google 账号全局管理权限。
4. 在该服务账号的“密钥”中创建 JSON 私钥，保存到 `local/credentials/firebase-service-account.json`。
5. 不要把这个私钥复制到 Android。Windows 会用它生成短期 OAuth access token，并通过 FCM HTTP v1 发送消息。

请遵循组织自己的凭据和密钥政策；如果组织禁止长期服务账号 JSON 密钥，当前发送端没有实现工作负载身份联合，不能绕过组织限制。

## 3. 在两端完成配对

1. 启动 Windows 托盘程序，双击图标打开设置。
2. 展开“高级设置”，点击“导入 Firebase 发送凭据”，选择自己的服务账号 JSON。
3. 点击“导入 Android Firebase 配置”，选择同项目的 `google-services.json`。已有有效配置的升级用户无需再次导入。
4. 手机和电脑连接同一 Wi-Fi，保持手机 App 与电脑设置打开。首次使用自动搜索；已配对用户想更换配对时，从手机右上角菜单选择“更换电脑”。电脑自动发现手机并显示六位确认码。
5. 比较两端显示的设备名称和六位码，在手机和电脑各点击确认，无需输入确认码、IP 或链接。等待推送注册完成。允许通知及系统要求的本地网络权限。使用 Tailscale 时可在高级设置添加 `http://<电脑Tailscale地址>:45871`，随后在局域网重新配对传入备用地址。
6. Windows 显示手机名称后，复制一条测试文字。确认状态由“已提交 FCM”变为“手机已确认”，再在手机实际粘贴。随后再测试单张图片。

配对只支持一台受信任手机。两端确认且手机推送注册完成后才替换当前配对；此前保留原配置。发现广播不包含长期密钥或 Firebase 配置。安装二维码仅包含 APK 下载地址；旧版含密钥的配对链接仍需保密。

## 升级、换电脑与恢复

- 保留 `local/runtime/` 和 `local/signing/`。覆盖升级不需要重新创建 Firebase 项目或 APK 签名密钥。
- Android 卸载会丢失配对配置；重新安装后重新配对。
- Windows DPAPI 配置不能简单复制到另一套 Windows 后直接解密。换电脑时，使用安全保存的服务账号 JSON 和 Android 配置重新导入、重新配对。
- APK 升级需要同一签名密钥。签名密码的 DPAPI 保护也与原 Windows 用户环境有关；迁移前按自己的安全流程准备可恢复的签名备份。

参考：[Firebase Android 配置](https://firebase.google.com/docs/android/setup)、[FCM HTTP v1 身份认证](https://firebase.google.com/docs/cloud-messaging/send/v1-api)、[FCM IAM 角色](https://docs.cloud.google.com/iam/docs/roles-permissions/firebasecloudmessaging)。
