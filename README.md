# Lab Desktop Client

面向 Windows 10/11 x64 的实验室图形客户端，通过 SSH 连接用户容器，支持：

- Linux 桌面：使用 TurboVNC Viewer；
- Isaac Sim GUI：使用客户端内置视图。

## 安装

从 [Releases](https://github.com/tzkd/Lab-Desktop-Client/releases) 下载：

- `LabDesktopClient-<版本>-win-x64-setup.exe`：安装版；
- `LabDesktopClient-<版本>-win-x64-portable.zip`：便携版，解压后运行 `LabDesktopClient.exe`。

程序已包含 .NET 运行环境。Linux 桌面需要 TurboVNC Viewer，Isaac Sim GUI 需要 Microsoft
Edge WebView2 Runtime；缺失时可按客户端中的“安装…”提示处理。

## 连接

1. 选择连接模式。
2. 填写管理员提供的服务器、SSH 端口和用户名。
3. 输入密码；需要保存时勾选“记住密码”。
4. 选择分辨率并连接。
5. 首次连接时，通过独立渠道核对管理员提供的 SSH 主机指纹。

服务器、端口、用户名、模式和分辨率会自动保存。

### Linux 桌面

客户端检测到 TurboVNC Viewer 后即可连接。未检测到时可点击“安装…”，或使用“选择…”指定
`vncviewer.bat` / `vncviewer.exe`。

### Isaac Sim GUI

管理员必须先为该容器用户安装 Isaac Sim。客户端负责启动和关闭当前用户的图形会话。

## 故障排查

- 无法连接：检查服务器、端口、网络和 SSH 凭据。
- 主机密钥变化：停止连接并联系管理员核实。
- 未找到 TurboVNC：点击“安装…”或手动选择 Viewer。
- 缺少 WebView2：按 Isaac 面板提示安装并点击“重新检测”。
- Isaac Sim 未安装：联系管理员为当前用户安装。
- 其他错误：点击“诊断文件”，将脱敏后的日志交给管理员。

## 开发

需要 .NET SDK 8、Node.js 20+ 和 npm 10+：

```powershell
.\scripts\build.ps1
.\scripts\publish.ps1 -Runtime win-x64
```

构建、测试和发布产物位于 `artifacts/`，不提交到 Git。

## 文档

- [架构](docs/ARCHITECTURE.md)
- [安全策略](docs/SECURITY.md)
- [第三方组件](docs/THIRD-PARTY-NOTICES.md)

项目代码采用 [MIT License](LICENSE)。
