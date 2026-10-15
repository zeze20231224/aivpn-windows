# AIVPN Windows - public source mirror

This repository contains the Windows client source already distributed with AIVPN RC14. It does not include AI WorkPort server source, credentials, VPN device profiles, user data, or signing keys.

Released source baseline: https://aiworkport.com/downloads/windows-auto-0.1.0-rc14/AIVPN-Windows-0.1.0-rc14-source.zip

Baseline SHA-256: `2150296110aeb79af13efe27b737a53001a1238bcfad9343658a92cde2705e96`

## Code signing policy

See [Code signing policy](CODE_SIGNING_POLICY.md). SignPath eligibility is under review; no acceptance or signing certificate is claimed. Published RC14 artifacts remain unsigned.

## Build provenance

The manually triggered GitHub workflow builds unsigned review artifacts on GitHub-hosted Windows runners. Third-party build inputs are fetched from the pinned public RC14 archive and checked by hash, with their original licenses and signatures preserved. No signing credentials are needed for this unsigned build.

## Privacy disclosures

See [client data disclosures](PRIVACY.md). This document is a draft; service-side policy and SignPath-specific requirements still require confirmation.

---
# AIVPN Windows 0.1.0-rc14

独立安装测试候选，Windows 10 22H2 / Windows 11 x64，.NET Framework 4.8。
目标：安装 → AI WorkPort 账号登录 → 点击连接。不需要导入配置或安装另一款 VPN 客户端。

连接后界面显示实时下载/上传速率与本次连接累计流量。数据读取本设备服务端 Peer 的真实 VPN 收发计数；自动分流下国内直连不计入，空闲时速率为 0，不执行主动测速。

RC9：自动分流模式将 `*.oaiusercontent.com` 的 DNS 与连接设为本机网络直连，用于修复 ChatGPT Windows 图片/附件上传失败；附件仍由 HTTPS 加密传输，但此域名的上传连接不经过 AIVPN。其余流量继续按原自动分流规则；兼容模式仍保持全部流量经 AIVPN。

RC10：泽 Remote 专用中继 `207.148.117.198/32` 在自动与全局模式下均明确 DIRECT，不依赖是否开启 AIVPN；其他流量保持既有 AIVPN 分流策略。连接策略已经过离线规则与 Mihomo 配置校验，真实安装、VPN切换和手机远控稳定性仍需实机验收。

RC11：前台网络状态查询可能等待最长25秒，而服务也在25秒收不到状态请求时按 `E_CLIENT_GONE` 清理隧道。新增独立后台5秒状态心跳，避免前台API查询暂时变慢时误断；若AIVPN进程实际退出，服务仍按原租约自动清理。此候选尚未完成真实长时连接验收。

## 当前状态

已修正普通用户连接服务的身份识别问题。rc1安装后更新为rc2的电脑已完成真实账号登录、VPN握手、双向流量与国内/海外HTTPS短时检查，并与安卓同时连接。本次完整rc2安装包的内嵌文件已验证，尚未在干净Windows环境重新安装验收；其他电脑与长期稳定性仍待验证。

本包尚无 AI WorkPort 的代码签名证书。第三方 Wintun 和 WebView2 文件有原厂签名，但这不等于本客户端/安装包已签名。Windows 可能显示未知发布者；测试人员应核对交付摘要，不应关闭系统安全保护。正式向客户分发前需完成签名和真机门禁。

## 安装与使用

1. 运行 `AIVPN-Windows-0.1.0-rc14-Setup.exe`，确认系统管理员安装授权。支持从已登记且路径匹配的 AIVPN Candidate 测试版原地升级；安装器会验证服务路径、关闭旧客户端并停止服务，失败时尝试恢复旧程序。加密账号数据位于安装目录之外，会保留。若旧安装登记异常，安装器会停止而不覆盖。
2. 缺少 WebView2 时，安装程序会调用随包附带、经微软签名验证的官方引导程序联网安装；不关闭证书验证或修改浏览器策略。
3. 从开始菜单正常打开“AIVPN 测试版”，不要以管理员身份运行主客户端。
4. 用 AI WorkPort 账号登录。先在其他 VPN 中点击断开，再点击 AIVPN 的“连接”。
5. 首次连接会按现有账号权限自动创建独立 Windows 设备；设备已达上限时提示管理设备，不自动删除手机或其他电脑。
6. 关闭窗口会确认断开。Windows 应用列表可卸载测试版；加密账号数据保留，其他 VPN 不受影响。

“已连接”必须同时满足专属网卡可用、实际 HTTPS 出口等于节点、国内 HTTPS 可达，以及后台本设备新鲜握手/双向流量。测试未覆盖前不能把开关状态当作成功。

## 安全与实现

- 普通权限 WinForms / WebView2 前台；仅受保护的 Program Files 中的服务以 LocalSystem 运行，浏览器永不提升权限。
- 服务仅使用受 ACL 保护的本机命名管道：拒绝 Network SID；校验两端实际进程路径、交互用户 SID 与 PID。命令白名单、64 KiB 帧上限、固定超时、会话所有权与25秒租约。无公网管理接口、无任意进程执行或配置路径接口。
- 连接内核、驱动、规则摘要固定；服务只从受保护安装目录启动固定内核。Windows 默认路由冲突时拒绝连接，不自动停止其他 VPN、修改系统代理、关闭防火墙或放宽 TLS。
- 现有 Windows 后台接口为服务端生成专属密钥。本候选沿用此接口，不假称与安卓一样本机生成私钥。配置经认证 HTTPS 自动取得；不导出给客户、不记录密钥/Token到日志、不修改后台权限。
- 登录 Token 使用 Windows 当前用户 DPAPI 加密，目录 ACL 限制当前用户/管理员/SYSTEM。服务运行配置短暂位于 SYSTEM/管理员专属目录，停止及重启清理；内核放入 kill-on-close Job，服务崩溃时结束子进程。
- 本机稳定随机标识用于专属设备名称；创建请求结果不确定时先查询已有设备，不自动重复提交和耗尽名额。后台目前不提供 Windows 幂等键，永久未确认时需要人工核对，不自行修改生产接口。
- 分流沿用安卓规则快照：大陆域名/IP直连，未知目标VPN，IPv6捕获拒绝，故障最多从自动切换兼容模式。不是在线更新规则，也不是任意断线情况下的系统级永久 kill switch。

## 开发与许可

`build.ps1` 编译、运行合成数据测试并用真实 Windows 内核执行 `-t` 配置检查，不启动 TUN。
`package.ps1` 验证第三方摘要与签名，构建自包含安装载荷、源码ZIP及摘要报告。
不在此任务中自动执行安装包、注册服务或切换当前电脑网络。安装、真实账号及隧道测试需要用户明确同意。

新增代码按 GPL-3.0 提供。内核 Mihomo v1.19.30（含 gvisor）、Wintun 0.14.1、WebView2 SDK 1.0.4191.47；对应源码、依赖锁定和许可随交付提供。Microsoft WebView2 按其原始分发条款，Wintun 按随包许可提供。

## 发布验收门禁

未通过的项目不得标记完成：干净 Windows 安装与卸载、登录取消/退出/重新登录、受限与无限设备账号、创建结果不确定恢复、本机SID隔离/恶意管道拒绝、非管理员连接、真实国内直连与海外出口/DNS/IPv6、与其他VPN切换、睡眠/网络变化、兼容回退、进程崩溃/服务停止后的路由和临时文件清理、至少一次持续使用测试、代码签名及升级保留账号。

## 0.1.0-rc3

新增显式 --connect 启动参数，通过现有登录态、路由冲突检查及服务认证路径连接，供有独立失败回退保护的本机切换流程使用。没有绕过双全局隧道保护。冲突提示改为描述实际检测到的全局路由，不再推断用户未点击断开。启动参数本身不会关闭其他VPN；切换与回退是另行运行的本机维护流程，不是客户端已内置的一键切换功能。


RC12：主界面新增账号本月 VPN 累计流量（含该账号全部设备），每分钟读取认证 /usage。当前连接实时速率/本次流量保持独立。

`nRC14：已验证连接遇到状态API临时超时、网络异常或408/429/5xx时保留现有隧道并重试；首次验证和认证失败仍遵循原断开逻辑。补记轮询失败错误码。支持匹配的AIVPN Candidate原地升级并保留加密登录数据。尚未完成安装器升级/回滚及长时稳定性验收。


