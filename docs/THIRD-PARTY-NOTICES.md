# 第三方组件声明

项目代码采用 [MIT License](../LICENSE)。以下组件遵循各自许可证。

## 随客户端发布

| 组件 | 版本 | 许可证 | 用途 |
| --- | --- | --- | --- |
| [SSH.NET](https://github.com/sshnet/SSH.NET) | `2026.0.0` | MIT | SSH 客户端 |
| `BouncyCastle.Cryptography` | `2.7.0` | MIT | SSH.NET 传递依赖 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `8.0.2` | MIT | 传递依赖 |
| `Microsoft.Extensions.Logging.Abstractions` | `8.0.3` | MIT | 传递依赖 |
| [Microsoft Edge WebView2 SDK](https://developer.microsoft.com/microsoft-edge/webview2/) | `1.0.4129.50` | Microsoft 许可 | 内置网页视图 |
| `@nvidia/ov-web-rtc` | `6.6.0` | NVIDIA 专有许可 | Isaac Sim 画面与输入 |

WebView2 Evergreen Runtime 不随客户端发布，由 Windows 或用户独立安装。NVIDIA 组件的公开
分发必须符合其包内 `LICENSE.txt`；本项目的 MIT License 不扩展 NVIDIA 授权。

TypeScript 和 Vite 仅用于构建。Web 依赖版本由 `web/isaac/package-lock.json` 固定。

## 由客户端引导安装

### TurboVNC 3.3

- 项目：[TurboVNC](https://github.com/TurboVNC/turbovnc)
- 许可证：GNU General Public License v2
- [官方安装程序](https://github.com/TurboVNC/turbovnc/releases/download/3.3/TurboVNC-3.3.exe)
- [对应源代码](https://github.com/TurboVNC/turbovnc/releases/download/3.3/turbovnc-3.3.tar.gz)
- 安装程序 SHA-256：
  `29882a078de6cc9c12da97be4eab42299c1206c6a78ba77bbd89377c45d7d89d`

TurboVNC 不包含在仓库、便携版或安装包中。客户端仅在用户确认后下载、校验并启动其官方
安装程序。
