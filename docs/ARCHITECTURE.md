# Architecture

## 设计目标

Lab Desktop Client 只负责收集连接配置、验证 SSH 主机身份，并运行一个图形连接工作流。

## 项目结构

```text
LabDesktop.Client.Core
  连接模型、校验、信任决策、工作流接口和生命周期编排

LabDesktop.Client.App
  WinForms、SSH.NET、TurboVNC、WebView2、设置、凭据和日志适配

LabDesktop.Client.Core.Tests
  核心逻辑及关键 Windows 适配层测试
```

`Core` 不依赖 WinForms、SSH.NET 或文件系统；`App` 实现外部系统适配。

## 核心模型

- `ConnectionProfile`：规范化并校验服务器、端口、用户名和分辨率。
- `HostTrustPolicy`：处理首次指纹、指纹匹配和指纹变化。
- `ConnectionCoordinator`：限制单活动会话，并按模式选择工作流。
- `DesktopConnectionWorkflow`：建立桌面隧道并管理 TurboVNC 生命周期。
- `IsaacConnectionWorkflow`：建立 Isaac 会话并管理内置 Viewer 生命周期。

两个工作流使用相同的退出和清理语义：Viewer 结束后关闭远程会话、端口转发和 SSH 连接。

## 外部适配

- `SshClientConnector`：SSH 认证、主机密钥验证和 KeepAlive。
- `SshDesktopTunnelFactory`：执行 `lab-desktop` 并建立 VNC 转发。
- `SshIsaacSessionFactory`：执行 `lab-isaac`，建立会话转发并维护租约。
- `TurboVncViewerLauncher`：发现和启动 TurboVNC Viewer。
- `IsaacWebViewerLauncher`：通过 WebView2 承载 Isaac WebRTC Viewer。
- `JsonSettingsStore`：原子保存设置，损坏时备份并恢复默认值。
- `WindowsCredentialStore`：保存用户主动选择记忆的 SSH 密码。
- `FileLogger`：写入受限大小的诊断日志。

## 远程协议

Linux 桌面使用：

```text
lab-desktop attach --geometry WIDTHxHEIGHT
```

Isaac Sim GUI 使用版本化的 `lab-isaac session open/renew/close` 协议。服务端返回当前用户的
临时会话信息；客户端不调用管理员命令，也不维护固定媒体端口。

## 持久化

配置文件只保存连接偏好和已确认的主机指纹。密码保存在 Windows 凭据管理器；Isaac 会话
标识和临时凭据不持久化。详细约束见 [SECURITY.md](SECURITY.md)。

## 测试

- 单元测试覆盖配置校验、模式路由、信任决策、并发限制和清理顺序。
- 适配层测试覆盖设置、凭据、Viewer 启动和发布资源。
- WinForms 冒烟测试覆盖默认值、可访问性和关键布局。
