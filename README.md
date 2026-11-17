# 校园 VPN（托盘版）

当前版本 **1.1.0**。把开源的 Rust 内核 [SHIEP-Pipeline](https://github.com/Yan233th/SHIEP-Pipeline)（v3.0.0，Windows x64）包了一层轻量原生 GUI：
常量级开销、无 Electron、无运行时依赖、不装驱动、不改系统路由、不需要管理员权限。

## 快速开始

```powershell
# 1. 从本仓库 Release 下载 VpnTray-1.1.0-win-x64.zip 解压（里面只有 GUI，不含内核）
# 2. 拉取内核（AGPL-3.0，不随本仓库分发）
powershell -NoProfile -ExecutionPolicy Bypass -File fetch-kernel.ps1
# 3. 双击 VpnTray.exe，在设置里填账号密码，点连接
```

## 功能

- 托盘常驻：左键开主界面，右键有连接/断开、设置、开机自启、退出；图标按状态变色（灰/黄/绿/红）
- 一键连接；断开自动还原系统代理；失败给人话原因
- 凭据 DPAPI 加密存本地，密码走环境变量传给内核（不出现在命令行）
- 开机自启（可选只进托盘 / 显示主界面 / 自启后自动连接）、关闭窗口行为可选
- 浅色 / 深色 / 跟随系统主题
- 断线自动重连（5/10/15 秒退避，最多 3 次）、系统代理被抢占时自动改回
- **窗口可自由缩放**：拖边框或最大化，日志面板跟着变长（最小尺寸 420x592，DPI 自适应）
- 日志面板分级着色、可复制清空、落盘

```
浏览器/其他应用
   ↓  系统代理（连接时指向 http=127.0.0.1:1080;https=127.0.0.1:1080，断开还原）
本程序「协议前门」（VpnTray.exe，约 92 KB，WinForms/.NET Framework 4.8）
   ↓  按首字节分流：0x05 透传 / 0x04 转 SOCKS4 / 其它当 HTTP 代理翻译
内核 pipeline.exe（3.0 MB，纯 Rust，只听 127.0.0.1:（端口+1），不对外）
   ├─ 命中学校网关下发的路由表 → 走 VPN 隧道
   └─ 其他流量 → fallback 到你的上游代理（默认 socks5h://127.0.0.1:7890）
```

### 为什么要有「协议前门」

内核只讲 SOCKS5，而 Windows 的 `socks=` 系统代理在 Chromium / WinINET 下会被解析成 **SOCKS4**。
实测（Edge headless 撞探针）：`ProxyServer=socks=127.0.0.1:P` 时客户端首字节是 `0x04`，
内核于是把每一帧都判成 `unsupported socks version`，浏览器全部打不开。

所以本程序做两件事：

1. 系统代理改用 `http=…;https=…`（实测客户端改为发 `GET http://host/…` 与 `CONNECT host:443`）；
2. 对外端口内置前门，同时接受 SOCKS5 / SOCKS4·4a / HTTP 代理，一律翻译成内核的 SOCKS5。

附带好处：HTTP 代理与 SOCKS4a 都把**主机名**交给隧道，由内核按路由表解析 DNS，
不会在本机先解析一遍（内网域名因此也能通）。


## 首次使用（三步）

1. 双击 `VpnTray.exe`（或桌面「校园 VPN」快捷方式）。首次启动会从本机原版 EasyConnect 的日志里**自动嗅探服务器地址**并填好。
2. 点主界面的「设置」→「常规」，填 **用户名** 和 **密码** → 保存。密码用 Windows DPAPI 加密，只有当前 Windows 账户能解开。
3. 回主界面点「连接」。连上后托盘图标变绿，系统代理自动切到本地端口；点「断开」自动还原。

关窗口默认只是收进托盘，不退出；托盘图标左键打开主界面，右键有连接/断开、设置、开机自启、退出。

## ⚠️ 和 FlClash 共存（第一次必须处理）

你的系统代理平时由 FlClash 指向 `127.0.0.1:7890`。本程序要「一键」就得插在它前面：

```
应用 → 1080（本程序）→ 校园流量走 VPN；其余 → 7890（FlClash，规则照旧）
```

所以：

- **推荐（接管模式）**：在 **FlClash 里把「系统代理」开关关掉**。之后由本程序独占系统代理：连接时指向 1080，断开时还你 7890（等于没变）。可在「设置 → 系统代理」里开「发现被抢占时自动改回」兜底。
- **不想动系统代理（旁路模式）**：把「设置 → 系统代理 → 连接时接管 Windows 系统代理」取消勾选，然后在同一页点「生成 Clash 规则片段…」复制，粘进 FlClash 配置。此时系统代理始终留在 7890，引擎只提供本地 1080。

两条路都不影响 FlClash 的分流规则。

## 设置项

| 页 | 项 | 说明 |
|---|---|---|
| 常规 | 服务器地址 | 形如 `https://vpn.xxx.edu.cn`，首次自动嗅探 |
| 常规 | 用户名/密码 | 密码 DPAPI 加密存储，不写命令行 |
| 常规 | 本地端口 | 默认 1080；内核自己占用它的**下一个**端口，只给前门用 |
| 常规 | 非校园流量交上游代理 | 填你的 Clash 混合端口，留空则非校园流量直连 |
| 常规 | 自动重连 | 内核异常退出后最多重试 3 次（5/10/15 秒退避） |
| 系统代理 | 接管系统代理 | 见上一节 |
| 系统代理 | 清理内网直连例外 | 去掉 `10.*`、`172.16-31.*`、`192.168.*` 的直连例外，否则校园内网 IP 会绕过代理直连而打不开；断开时精确还原 |
| 启动与外观 | 开机自启动 | 写 `HKCU\...\Run`，无需管理员 |
| 启动与外观 | 自启后 | 只进托盘（默认）/ 显示主界面 |
| 启动与外观 | 自启后自动连接 | 想让开机即连就勾上 |
| 启动与外观 | 关闭窗口时 | 最小化到托盘（默认）/ 退出程序 |
| 启动与外观 | 主题 | 浅色 / 深色 / 跟随系统 |
| 高级 | 内核路径 | 默认程序同目录的 `pipeline.exe` |
| 高级 | 日志/数据目录、清除密码 | 排错用 |

## 排错

- 主界面「运行日志」面板就是内核的 stderr 原文，按级别着色。
- 文件日志：`%APPDATA%\VpnTray\logs\app-YYYYMMDD.log`。
  （若该目录不可写，会自动回退到 `%LOCALAPPDATA%\VpnTray` 或程序目录下的 `data\`。）
- 启动阶段问题：程序目录下的 `trace.log`。
- 自检：`VpnTray.exe --selftest`，结果写程序目录的 `selftest.log`，覆盖配置读写、DPAPI、系统代理接管/还原、开机自启、内核文件、Clash 片段生成，以及**前门协议转换**（SOCKS4 转 SOCKS5、SOCKS5 透传、HTTP CONNECT、HTTP 绝对 URI 改写）。
  在受限沙箱里跑时，「系统代理接管与还原」「开机自启」两项会因拒绝写 HKCU 而 FAIL，那是环境限制；用桌面快捷方式或资源管理器启动时正常。
- 布局自检：`VpnTray.exe --layouttest`，按四个尺寸把主界面摆一遍，校验控件不越界、不重叠、日志面板吃满宽度，并在程序目录留 `layout-*.png` 供肉眼确认（不联网、不碰系统代理）。
- 常见报错：
  - 「登录失败：账号或密码不对（或网关拒绝）」——凭据问题，或网关不接受该账号类型。
  - 「端口已被占用」——换本地端口。
  - 「系统代理已被其他软件改回」——FlClash 抢了开关，见上一节。
  - 日志刷 `unsupported socks version`——说明内核端口被某个讲话非 SOCKS5 的客户端直接访问（老版本缺陷，本版已由前门消化；仍出现就把日志发我）。

## 已知限制（内核层面，不是 GUI 能补的）

- 内核本身只提供 **SOCKS5（仅 TCP）**，UDP 被明确拒绝、IPv6 TCP 目标一律走 fallback。前门只做协议翻译，不放宽这两条。
- 不会像官方 EasyConnect 那样接管整块网卡，所以只有**遵循系统代理**的程序（浏览器、Edge、大部分桌面应用）才会走它；不吃系统代理的程序需要自己配 1080。
- 内核不校验网关证书（沿用上游行为），且 legacy TLS 无前向保密，属上游已知取舍。
- 官方客户端仍可保留共存；本程序不装驱动、不写系统路由，卸载只需删目录 + 关自启。

## 重建 / 升级内核

```powershell
# 重建 GUI（需要 .NET Framework 4.8 自带的 csc.exe）
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1

# 升级内核：从上游 Release 下 v3.x 的 windows-x64.exe，替换本目录 pipeline.exe
# https://github.com/Yan233th/SHIEP-Pipeline/releases
```

源码在 `src\`：`Program.cs`（托盘与状态机总控）、`MainForm.cs`（主界面与可缩放布局）、`SettingsForm.cs`、`CoreRunner.cs`（内核进程与日志/状态）、`Bridge.cs`（协议前门）、`Native.cs`（系统代理、自启、Job 对象）、`Settings.cs`（配置与 DPAPI）、`Theme.cs`、`SelfTest.cs`、`LayoutTest.cs`、`Version.cs`、`Ui.cs`、`Diag.cs`。

## 许可证

- 本仓库（GUI 壳）：**MIT**，见 `LICENSE`。
- 内核 `pipeline.exe` 来自 [SHIEP-Pipeline](https://github.com/Yan233th/SHIEP-Pipeline)，**AGPL-3.0**，由上游作者分发，本仓库**不包含**它的二进制，也不对其再授权。
- GUI 与内核是两个独立进程（子进程 + 本地 TCP），通过 `fetch-kernel.ps1` 各自获取。

## 卸载

1. 托盘右键 → 退出（会还原系统代理）。
2. 设置里关掉开机自启（或删注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `VpnTray`）。
3. 删程序目录与 `%APPDATA%\VpnTray`。
