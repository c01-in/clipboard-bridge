# 参与开发

欢迎提交修复、文档和体验改进。提交前请先说明可以复现的问题或预期行为。

## 开发与检查

构建环境见 [构建文档](docs/build.md)。开发不需要维护者的 Firebase 项目或任何私钥，Android 的调试构建和 lint 可直接运行。

```powershell
dotnet build .\windows\Service\Service.csproj -c Release
dotnet build .\windows\Tray\Tray.csproj -c Release
dotnet run --project .\windows\Tests\Tests.csproj -c Release
.\android\gradlew.bat -p .\android assembleDebug lintDebug
.\scripts\Check-Publish.ps1 -IncludeHistory
```

真实推送测试使用你自己的 Firebase 项目。报告时区分“FCM 接受消息”“手机完成写入”和“目标 App 实际粘贴”，不要仅凭构建通过声称同步成功。

## Pull Request

- 描述问题、最终行为和实际验证。
- 保护既有安装的凭据、手机配对及 APK 签名；不要在升级时生成新密钥替换原有密钥。
- 不提交 `local/`、`dist/`、服务账号文件、设备日志或含配对信息的截图。
- 界面和文档使用清晰直接的文字；不要为使测试通过增加替代真实功能的演示路径。
- 新贡献按本仓库 MIT 许可证发布。第三方代码和资源应保留对应许可和来源。
