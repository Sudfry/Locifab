# 上传到 GitHub 的备选方案

目标仓库：<https://github.com/Sudfry/Locifab>

当前环境访问 GitHub 被网络策略拦截：

```text
CONNECT tunnel failed, response 403
```

源码已整理并完成本地提交，以下任意一种方式都可以上传。

---

## 方案 A：GitHub Desktop（推荐，最简单）

1. 安装/打开 GitHub Desktop。
2. 登录账号 `Sudfry`。
3. 选择 **File → Add local repository**。
4. 选择目录：
   `C:\Users\zxyzx\Desktop\LoacalCraft`
5. 点击 **Publish repository**。
6. 仓库名填 `Locifab`，选择 Public 或 Private。
7. 发布后检查 GitHub 仓库文件。

本地已有提交：

```text
Locifab 11.0.1: offline file workbench with reversible pipeline
Ignore legacy test data
```

---

## 方案 B：使用 Personal Access Token（命令行）

在 GitHub → Settings → Developer settings → Personal access tokens 创建一个有 `repo` 权限的 token。

```powershell
cd C:\Users\zxyzx\Desktop\LoacalCraft

git remote set-url origin https://<TOKEN>@github.com/Sudfry/Locifab.git
git push -u origin main
```

注意：

- 不要把 token 写进任何提交文件。
- 上传完成后可把 remote 改回：
  `git remote set-url origin https://github.com/Sudfry/Locifab.git`

---

## 方案 C：SSH Key（长期使用）

```powershell
ssh-keygen -t ed25519 -C "Sudfry"
# 把 ~/.ssh/id_ed25519.pub 添加到 GitHub → SSH and GPG keys

git remote set-url origin git@github.com:Sudfry/Locifab.git
git push -u origin main
```

---

## 方案 D：网页上传压缩包

如果命令行始终无法联网：

1. 打开 `dist-upload/Locifab-11.0.1-source.zip`。
2. 在 GitHub 仓库页面选择 **Add file → Upload files**。
3. 解压后的文件拖入网页。
4. 填写提交信息并提交。

限制：网页上传不适合大量二进制截图，建议优先方案 A/B/C。

---

## 方案 E：用 Git Bundle 迁移到另一台电脑

`dist-upload/Locifab-11.0.1.bundle` 包含完整提交历史。

在能联网的电脑上：

```powershell
git clone Locifab-11.0.1.bundle Locifab
cd Locifab
git remote set-url origin https://github.com/Sudfry/Locifab.git
git push -u origin main
```

---

## 当前源码状态

- 已初始化 Git 仓库。
- 已提交源码、文档、测试脚本、测试模板。
- 已忽略：
  - `bin/`、`obj/`
  - `publish/`
  - 历史版本 `Locifab-*`
  - 本地 NuGet 缓存
  - 生成的测试数据
  - 个人文档 `Document/`
- 远程地址：
  `https://github.com/Sudfry/Locifab.git`
- 分支：`main`

查看状态：

```powershell
cd C:\Users\zxyzx\Desktop\LoacalCraft
git status
git log --oneline -5
```

---

## 仓库建议设置

- Description：`Fully offline Windows file workbench with reversible batch operations and pipelines`
- Topics：`csharp`, `dotnet`, `wpf`, `offline`, `file-manager`, `batch-rename`, `pipeline`
- License：MIT
- Releases：上传 `Locifab-11.0.1/Locifab.exe` 与 `测试数据与模板.zip`
