# 发布流程（维护者）

打 tag 后由 GitHub Actions 全自动出包并发布，正常发版不需要手工构建。

## 一、正常发版

```bash
git tag -a v1.0.1 -m "Bing 壁纸 1.0.1"     # 版本号必须 v<major>.<minor>.<patch>，预发布加 -beta.1 之类后缀
git push origin v1.0.1
```

`.github/workflows/release.yml` 会：

1. 用 `windows-latest` 跑 `tools/release.ps1`：自包含单文件 exe → 便携 zip → Inno Setup 安装包 → `SHA256SUMS.txt`
2. 产物上传为 Actions artifact
3. `gh release create` 创建 Release，挂上 setup / 便携 zip / 校验和，notes 用 `--generate-notes` 自动生成
4. tag 里带 `-`（如 `v1.0.1-beta.1`）时自动标记为 Pre-release

> 重新发同一个版本：先 `gh release delete v1.0.1 --yes`（不删 tag），再重跑 workflow；
> 直接重跑会因 Release 已存在而失败。

## 二、不发版的演练

Actions → Release → Run workflow，填一个版本号（如 `0.0.0-test`）：只构建并上传 artifact，
**不会**创建 Release。用于验证 workflow 改动。

## 三、本地出包

```powershell
pwsh tools/release.ps1 -Version 1.0.1                  # 需要 Inno Setup 6.3+（https://jrsoftware.org/isdl.php）
pwsh tools/release.ps1 -Version 1.0.1 -SkipInstaller   # 只出 exe + 便携 zip
pwsh tools/publish.ps1 -SelfContained                  # 只编译自包含单文件 exe
```

产物都在 `dist\`（已被 .gitignore 忽略）。版本号也可以带 `v` 前缀或 `-beta.1` 后缀。

## 四、安装包真机验收清单

CI 只能证明"编译得出来"，装/卸行为必须在一台真 Windows 上过一遍：

- [ ] 全新安装：无 UAC 弹窗、无需预装 .NET；开始菜单出现「Bing 壁纸」；启动后托盘图标出现、自动换图正常
- [ ] 首次运行后注册表 `HKCU\...\Run\BingWallpaper` 出现，重启后自启生效
- [ ] 程序运行中覆盖安装新版本：旧进程被结束、安装成功、自启仍指向同一路径
- [ ] 卸载：程序目录、开始菜单项、`HKCU\...\Run\BingWallpaper`、`%LOCALAPPDATA%\BingWallpaper`（图片/索引/日志）全部清掉
- [ ] 便携版：解压即用；移动位置后托盘菜单「开机自启」关→开 能修复路径
- [ ] SmartScreen 提示「更多信息 → 仍要运行」后可正常安装

## 五、代码签名（待办）

现状：安装包未签名，SmartScreen 会拦一下。开源项目可以申请
[SignPath Foundation](https://signpath.io/foundation/) 的免费 OV 证书，流程：

1. **前置条件**：OSI 认可的开源许可证（本项目 MIT ✔）、产物公开发布、构建全部由 CI 完成（本项目满足）。
2. 在 https://signpath.io/foundation/ 提交申请：填 GitHub 仓库地址、许可证、发布方式（GitHub Releases）、维护者信息。
   审核通常要几天到几周。
3. 通过后 SignPath 会创建组织；在 GitHub 仓库安装 SignPath GitHub App，然后在 SignPath 里建
   Project、Artifact Configuration、Signing Policy（一般叫 `release-signing`）。
4. 仓库 Settings → Secrets/Variables 配置：
   - Secret `SIGNPATH_API_TOKEN`
   - Variables `SIGNPATH_ORGANIZATION_ID`、`SIGNPATH_PROJECT_SLUG`、`SIGNPATH_POLICY_SLUG`、`SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`
5. 在 `release.yml` 的"编译并打包"之后、"创建 Release"之前插入官方 action
   `signpath/github-action-submit-signing-request`：把未签名的 setup 作为输入，取回已签名的 setup 再上传 Release。
6. 注意：免费证书是 OV 级，SmartScreen 声誉仍需随下载量积累（但比不签好得多）；
   签署要求二进制完全来自 CI，不能手工上传。

## 六、目录速查

| 文件 | 作用 |
| --- | --- |
| `.github/workflows/release.yml` | tag / 手动演练：出包 + 发 Release |
| `.github/workflows/ci.yml` | push / PR：完整出包冒烟（不发 Release） |
| `tools/release.ps1` | 本地与 CI 共用的发布入口 |
| `tools/publish.ps1` | dotnet publish 封装（`-Version` 覆盖版本号） |
| `installer/bing-wallpaper.iss` | Inno Setup 安装脚本（per-user、卸载清数据/自启项、强杀旧进程） |
| `installer/languages/ChineseSimplified.isl` | 安装向导中文语言文件（Inno 官方仓库 `Files/Languages`，6.5.0+） |
