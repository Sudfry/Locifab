# Windows 代码签名教程

本文说明如何给 `LocalCraft.exe` 做 Authenticode 签名，以及为什么“改 Company”不能消除 Windows 的发布者提示。

## 1. 先分清三种提示

| 提示 | 原因 | 能否靠代码消除 |
|---|---|---|
| UAC“是否允许此应用对你的设备进行更改” | 程序请求管理员权限 | 可以；LocalCraft 已使用 `asInvoker`，正常双击不请求提权 |
| SmartScreen“Windows 已保护你的电脑” | 文件来自网络、发布者未知或信誉不足 | 不能；需要受信任的代码签名和信誉 |
| “未知发布者” | exe 没有受信任的 Authenticode 签名 | 不能；必须用 CA 签发的代码签名证书签名 |

`Company=Sudfry` 只是版本资源，不是数字签名；Windows 不会因此信任 exe。

## 2. 选择证书

正式分发建议购买 **代码签名证书**：

- **OV 代码签名证书**：组织验证，价格较低；SmartScreen 信誉需要逐步积累。
- **EV 代码签名证书**：扩展验证，通常能更快建立 SmartScreen 信誉；价格较高。
- **Azure Trusted Signing**：微软云签名服务，需要 Azure 订阅和组织验证，使用不同的签名流程。
- **自签名证书**：只适合本机测试，**不能**消除其他电脑上的 SmartScreen/未知发布者提示，不要用它冒充正式发布者。

2023 年之后，CA 签发的代码签名私钥通常要求放在硬件令牌或 HSM 中，不一定能导出为 `.pfx`。购买前确认签名方式。

## 3. 如何获得 PFX

PFX（`.pfx`/`.p12`）是一个包含**证书 + 私钥**的加密容器。受信任的 PFX 不能凭空生成，必须由 CA 完成身份验证后签发。

### 3.1 购买证书

1. 选择 CA：DigiCert、Sectigo、GlobalSign、SSL.com、Certum 等。
2. 选择产品：**OV 代码签名**、**EV 代码签名**，或 Azure Trusted Signing。
3. 提交身份材料：
   - 企业：营业执照/注册证明、地址、电话、税号、联系人。
   - 个人：身份证件、地址、电话；部分 CA 只向企业签发，购买前确认。
4. CA 审核（通常 1–5 个工作日，EV 更长）。
5. 按 CA 指引领取证书。

购买前必须确认三件事：

- 是否支持 **Windows Authenticode**（不是 SSL/TLS 证书）。
- 私钥是 **可导出 PFX**，还是必须放在硬件令牌/HSM。
- 是否包含 **RFC 3161 时间戳** 所需的签名权限（通常只是签名时的服务，不额外收费）。

### 3.2 如果 CA 给的是硬件令牌

很多 OV/EV 证书现在只提供 USB Token 或云 HSM，私钥不能导出。此时**不要**尝试导出私钥，直接使用证书指纹签名：

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
  Format-Table Subject, Thumbprint, NotAfter

signtool sign /sha1 <证书指纹> /fd SHA256 /td SHA256 `
  /tr http://timestamp.digicert.com LocalCraft.exe
```

### 3.3 如果已有证书和私钥，导出 PFX

证书已经导入 Windows 证书存储，并且带有私钥时：

```powershell
# 找到代码签名证书
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
  Format-Table Subject, Thumbprint, HasPrivateKey

# 导出 PFX（会提示设置导出密码）
$thumb = "<证书指纹>"
$cert = Get-Item "Cert:\CurrentUser\My\$thumb"
Export-PfxCertificate -Cert $cert `
  -FilePath .\sudfry-code-signing.pfx `
  -Password (Read-Host "设置 PFX 密码" -AsSecureString)
```

如果只有 `.cer` 和单独的私钥，需要先合并：

```powershell
certutil -mergepfx .\sudfry.cer .\sudfry.pfx
```

合并时证书和私钥必须匹配，否则会失败。

### 3.4 自签名 PFX（只用于测试）

```powershell
$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject "CN=Sudfry Test" `
  -CertStoreLocation Cert:\CurrentUser\My

$pwd = ConvertTo-SecureString "test-password" -AsPlainText -Force
Export-PfxCertificate -Cert $cert `
  -FilePath .\sudfry-test.pfx `
  -Password $pwd
```

这个 PFX 可以给 LocalCraft 签名，但**不会**让其他电脑信任它；SmartScreen 仍可能拦截。它只适合本机或受控实验室。

## 4. 安装签名工具

安装 Windows SDK 的 **Signing Tools**，或只安装 Windows SDK 后找到：

```text
C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64\signtool.exe
```

验证：

```powershell
signtool /?
```

## 5. 用 PFX 证书签名

如果证书是 `.pfx` 文件：

```powershell
signtool sign `
  /fd SHA256 `
  /td SHA256 `
  /tr http://timestamp.digicert.com `
  /f "C:\secure\sudfry-code-signing.pfx" `
  /p "<证书密码>" `
  "C:\path\to\LocalCraft.exe"
```

项目已提供封装脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-win-x64.ps1 `
  -PfxPath C:\secure\sudfry-code-signing.pfx `
  -Password "<证书密码>"
```

签名脚本会自动定位 `signtool.exe`，签名后输出证书信息。

> 注意：把密码写在命令行会留在命令历史中。更安全的方式是把证书导入当前用户的“个人/证书”存储，然后用指纹签名；或者使用硬件令牌厂商的签名工具。

## 6. 用证书存储/硬件令牌签名

证书已安装到当前用户证书存储时，先查指纹：

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
  Format-Table Subject, Thumbprint, NotAfter
```

然后用指纹签名：

```powershell
signtool sign `
  /sha1 <证书指纹> `
  /fd SHA256 `
  /td SHA256 `
  /tr http://timestamp.digicert.com `
  "C:\path\to\LocalCraft.exe"
```

如果使用 USB 硬件令牌，`signtool` 可能会弹出令牌 PIN 输入框；按厂商说明操作即可。EV 令牌通常还需要安装厂商的 CSP/驱动。

## 7. 验证签名

```powershell
signtool verify /pa /v "C:\path\to\LocalCraft.exe"
Get-AuthenticodeSignature "C:\path\to\LocalCraft.exe" |
  Format-List Status, StatusMessage, SignerCertificate
```

期望结果：

```text
Status : Valid
```

`SignerCertificate.Subject` 应显示你的组织名称（例如 `CN=Sudfry`）。如果仍是 `NotSigned`，说明签名没有成功应用。

## 8. 时间戳

`/tr` 和 `/td` 用于 RFC 3161 时间戳。没有时间戳的签名在证书过期后会失效；带时间戳的签名在证书有效期内签署后可以长期验证。

常用时间戳服务：

```text
http://timestamp.digicert.com
http://timestamp.sectigo.com
```

如果时间戳服务器不可达，可以换一个；不要在正式发布时省略时间戳。

## 9. SmartScreen 与 UAC 的真实关系

- **UAC**：由清单里的 `requestedExecutionLevel` 决定。LocalCraft 使用 `asInvoker`，不请求管理员权限，所以正常双击不会弹“是否允许更改设备”。
- **SmartScreen**：由文件信誉、签名证书和下载来源共同决定。签名后仍可能需要一段时间积累信誉；EV 证书通常更快。
- **未知发布者**：签名证书必须被 Windows 信任。自签名证书只有在目标电脑手动信任根证书后才可能显示发布者，不适合分发。
- **下载锁定**：从浏览器下载的 exe 可能带有 `Zone.Identifier`。可运行：

```powershell
Unblock-File .\LocalCraft.exe
```

或使用项目的 `scripts/unblock-win-x64.ps1`。这只清除“来自其他计算机”的标记，**不等于代码签名**。

## 10. 自签名证书（仅本机测试）

```powershell
$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject "CN=Sudfry Test" `
  -CertStoreLocation Cert:\CurrentUser\My

$pwd = ConvertTo-SecureString -String "test-password" -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath .\sudfry-test.pfx -Password $pwd

signtool sign /fd SHA256 /f .\sudfry-test.pfx /p "test-password" `
  ".\publish\win-x64\LocalCraft.exe"
```

这台电脑可以验证签名，但其他电脑仍会认为发布者未知。**不要把这个证书用于正式发布。**

## 11. 常见错误

| 错误 | 处理 |
|---|---|
| `No certificates were found that meet all the given criteria` | 证书没有私钥、指纹不对，或证书不在当前用户存储 |
| `The specified PFX password is not correct` | 检查 PFX 密码；注意密码中的特殊字符 |
| `SignTool Error: No file digest algorithm specified` | 加 `/fd SHA256` |
| `The timestamp server returned an error` | 换时间戳 URL，或检查网络/代理 |
| 签名后仍是 `NotSigned` | 确认签的是发布目录里的 exe，而不是 `bin` 里的旧文件 |
| SmartScreen 仍提示 | 检查 `Status` 是否为 `Valid`；新证书需要信誉积累；EV 也不保证所有环境立即无提示 |

## 12. 信任环境下的替代方案

如果目标机器属于你，或属于受管理的企业终端，可以按场景选择以下方式。它们能减少或绕过提示，但**都不能把未签名 exe 变成“已验证发布者”**。

| 场景 | 方法 | 代价/限制 |
|---|---|---|
| 单台自己的电脑 | 在提示中点“更多信息 → 仍要运行” | 只对当前文件生效，换机器仍会提示 |
| 下载/拷贝后提示 | `Unblock-File .\LocalCraft.exe` 清除 Mark-of-the-Web | 只清除“来自其他计算机”标记，不改变签名 |
| 小范围实验室 | 自签名证书 + 将根证书和发布者证书装到目标机 | 每台机器都要手动信任；不适合公开分发 |
| 企业统一管理 | 内部 CA/GPO/Intune 分发根证书，或 AppLocker/WDAC 按哈希/路径放行 | 需要管理员和终端管理体系；不是普通用户方案 |
| 正式公开分发 | CA 代码签名证书；或发布到 Microsoft Store 由商店签名 | 成本/审核；EV 也不能保证所有环境立即无提示 |
| Windows 11 智能应用控制 | 在“Windows 安全中心 → 应用和浏览器控制 → 智能应用控制”中关闭 | 系统级设置，关闭后通常需要重置/重装才能重新开启；不建议普通用户关闭 |

### 单台电脑最简做法

```powershell
Unblock-File .\LocalCraft.exe
Start-Process .\LocalCraft.exe
```

当前发布目录中的 exe **没有** `Zone.Identifier`（不是从网络下载的标记），本机测试通常不会触发“来自 Internet”的 SmartScreen。如果仍被拦截，更可能是目标机器上的 SmartScreen 信誉或智能应用控制，而不是文件锁定。

### 自签名证书（仅限受控机器）

```powershell
# 1. 在受控机器生成本机测试证书
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
  -Subject "CN=Sudfry Internal Test" `
  -CertStoreLocation Cert:\CurrentUser\My

# 2. 导入到“受信任的根证书颁发机构”和“受信任的发布者”
Export-Certificate -Cert $cert -FilePath .\sudfry-test.cer
certutil -addstore Root .\sudfry-test.cer
certutil -addstore TrustedPublisher .\sudfry-test.cer

# 3. 签名
$pwd = ConvertTo-SecureString "test-password" -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath .\sudfry-test.pfx -Password $pwd
signtool sign /fd SHA256 /f .\sudfry-test.pfx /p "test-password" `
  .\publish\win-x64\LocalCraft.exe
```

这只能让**安装了该证书的机器**承认签名；其他机器仍会提示未知发布者。不要把测试根证书分发给最终用户。

### 不建议的做法

- 关闭整个系统的 SmartScreen/Defender 实时保护：影响所有程序，不建议。
- 修改注册表伪装发布者、伪造证书链、使用泄露证书：属于不安全/不合法做法。
- 用“压缩包/快捷方式/改扩展名”绕过：只能改变触发路径，不能建立信任，且可能带来更大安全风险。

## 13. 发布前检查清单

- [ ] 使用 CA 签发的代码签名证书，而不是自签名证书
- [ ] 对 `publish\win-x64\LocalCraft.exe` 签名，不要签错构建
- [ ] 使用 SHA-256 和 RFC 3161 时间戳
- [ ] `signtool verify /pa /v` 返回成功
- [ ] `Get-AuthenticodeSignature` 的 `Status` 为 `Valid`
- [ ] 重新计算 exe 的 SHA-256，并随发布说明提供
- [ ] 不要把 PFX、密码或私钥提交到代码仓库
- [ ] 在干净电脑上做一次下载/双击验证
