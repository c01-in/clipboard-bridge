# 自动构建与发布

普通使用者从 [GitHub Releases](https://github.com/c01-in/clipboard-bridge/releases) 下载 Android APK 或 Windows 安装 EXE，不需要构建环境。当前版本是 0.x 测试版，GitHub 发布标记为 prerelease，不宣称稳定。

## 维护者首次配置

1. 使用自己的持久 Android 签名密钥完成一次本地 `scripts/Build.ps1`。保留 `local/signing/` 的加密备份，不要每次重新生成。
2. 使用拥有仓库管理权限的 GitHub CLI 账号运行 `scripts/Configure-CISigning.ps1 -Repository <owner/repo>`。脚本把同一 keystore 和密码保存为仓库的 `ANDROID_KEYSTORE_BASE64`、`ANDROID_KEYSTORE_PASSWORD` 加密 Secrets，通过标准输入传入，避免出现在命令行参数中。
3. Secrets 不进入 Git、Release 资产或构建产物。修改发布 workflow、推送标签和使用发布 Secrets 的权限只应交给受信任维护者。不要为不受信任 PR 开放 Secrets。
4. 不要把 Firebase 发送私钥、`google-services.json`、手机令牌、DPAPI 配置或配对密钥上传到 Actions。构建和安装验证均无需这些数据。每台电脑自行生成 VAPID 身份，不需要 Firebase 发送凭据。

## 每次发版

两端可见版本必须一致，写在 `Directory.Build.props` 和 `android/app/build.gradle`。当前为 `0.1.2`。Android 的 `versionCode` 是独立、持续递增的内部升级编号；从早期内部 2.x 名称改成 0.x 时也必须递增，不能回退该编号或更换签名。

修改源码、两端版本与下载链接，提交并通过 CI 后推送对应标签：

```powershell
git tag v0.1.3
git push origin v0.1.3
```

示例下一版需要先把两端可见版本改成 `0.1.3`，并增加 Android `versionCode`；不能直接给旧版本代码打新版本标签。已有标签也可在 Actions 的 **Build and publish preview installers** 中手动运行，输入该标签。已发布版本不覆盖旧资产，应增加版本号重新发布。

发布任务自动检查标签与两端版本、源码及历史隐私、协议、语言、下载行为和 Android lint，然后使用原签名密钥构建 APK、打包 Windows EXE，在一次性 GitHub Windows runner 上执行全新安装及卸载，最后发布预发布版本和 SHA-256 校验文件。任一步失败不会发布安装包。

Windows 安装器使用系统自带的 .NET Framework 编译器，无需额外打包工具；安装后的三个程序自带 .NET 运行时。普通 PR 检查没有发布权限。签名 keystore 临时文件在任务结束时清理。

## 本地复现

```powershell
.\scripts\Build.ps1 -JavaHome 'C:\Tools\jdk-21' -AndroidSdk 'C:\Tools\android-sdk'
.\scripts\Package.ps1 -AndroidSdk 'C:\Tools\android-sdk'
```

输出位于 `dist/release/`，仅包含明确列出的公开产物。另提供 `ClipboardBridge-Android-Library-Sources.zip`，包含固定版本的 UnifiedPush 原始库源码与 LGPL-2.1 许可证；App 完整源码由 Release 标签提供。ZIP 不包含 `local/`、签名文件、原始 Firebase 配置或调试 PDB。Windows EXE 没有 Authenticode 签名，安装时会请求管理员权限。覆盖更新沿用已有服务的数据目录；全新 Windows 安装默认使用 `%ProgramData%\ClipboardBridge`。

GitHub 自动构建验证不能代替真实手机权限弹窗、配对、长时间锁屏与实际粘贴验收。
