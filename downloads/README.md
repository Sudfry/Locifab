# downloads

此目录用于存放 Microsoft Store「EXE/MSI」提交所需的直接下载文件。

上传脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\upload-raw-exe.ps1
```

生成地址：

```text
https://raw.githubusercontent.com/Sudfry/Locifab/main/downloads/Locifab.exe
```

注意：

- GitHub 单文件上限 100 MB。
- 当前 Locifab.exe 约 68.7 MB，可以上传。
- 不要把此文件放进 `Locifab-*` 发布目录后忘记提交。
- 每次发布新版本需要覆盖并提交。
