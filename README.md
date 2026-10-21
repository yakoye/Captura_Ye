# Captura Ye 中文版

基于 [Mathew Sachin / Captura](https://github.com/MathewSachin/Captura) 的 Windows 截图、屏幕与音频录制工具。保留上游功能，默认使用简体中文，补齐界面、悬停说明、录制模式、通知和错误提示。

项目地址：[yakoye/Captura_Ye](https://github.com/yakoye/Captura_Ye)。安装包与便携包见[发行版](https://github.com/yakoye/Captura_Ye/releases)。

## 使用

支持 Windows 10/11，需 .NET Framework 4.7.2 或更高版本。安装程序会检查框架版本；安装时可选择简体中文或英文。应用首次启动默认使用简体中文，可在“配置 → 界面”切换语言。

- 安装版：运行 `CapturaYe-v版本号-Setup.exe`。
- 便携版：将 `CapturaYe-v版本号-Portable.zip` **完整解压**，运行 `CapturaYe/captura.exe`。请保留同目录中的 `lib`、`Languages`、`Settings` 和配置文件。
- 录制 MP4 等格式需要 FFmpeg。程序会从已配置目录、程序目录或系统 PATH 查找，也可在“配置 → FFmpeg”指定目录。
- 截图、AVI 录制不需要额外下载 FFmpeg。摄像头和音频录制需要可用设备。
- Imgur、YouTube 等网络上传功能需要自行配置服务凭据；本项目不内置私有凭据。

## 双击编译与发布

入口文件名均为英文，脚本提示和说明使用中文。

| 入口 | 操作 |
| --- | --- |
| `build.cmd` | 编译 Release、检查中文资源、运行核心测试和实际截图/录制检查、生成便携 ZIP 和中文安装 EXE、静默安装/卸载与哈希验证 |
| `release.cmd` | 自动选择未发布版本，执行上述全部步骤，提交当前项目修改，推送源码和版本标签，创建 GitHub Release，回下载校验，再复制到本地及 NAS |

第一次构建需安装 Git、支持 .NET Framework 构建的 .NET SDK（本次使用 .NET SDK 10.0.401）。发布还需 GitHub CLI，并使用 `gh auth login` 登录有仓库写权限的账号。NuGet 会自动还原依赖；首次打包会下载固定版本 Inno Setup 6.7.3，验证 SHA-256 和发布者签名后，准备到 `temp/build-tools/Inno`。本仓库包含匹配的简体中文安装语言文件。

双击 `release.cmd` 即表示发布**当前工作区中的项目修改**。脚本会通过 `git add --all` 提交未提交文件；发布前应将凭据和私人文件移出项目。构建中间文件、日志和发布输出已排除在 Git 之外。脚本保留原有 `origin`，另用 `publish` 远程指向本仓库，不使用强制推送。

输出位置：`dist/v版本号/`。日志：`temp/logs/日期时间/`。源码版本来自 `src/Captura/Properties/AssemblyInfo.cs`；已存在同版本发行版时，`release.cmd` 自动增加补丁版本。关于页面的发行日期会在构建时更新。

高级用法（自动化也调用同一脚本）：

```powershell
# 指定版本编译、测试和打包
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 9.0.2

# 发布已经验证的现有包，不重新编译
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Publish -Archive -ReusePackage

# 仅对已有发布包执行双归档
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/archive-release.ps1 -Source dist/v9.0.1 -Version 9.0.1
```

归档目录固定为：

- `C:\Users\color\Downloads\yakoye-release\Captura_Ye\v版本号\`
- `\\192.168.7.100\Downloads\yakoye-release\Captura_Ye\v版本号\`

同时保存安装 EXE、便携 ZIP、解压后的完整便携目录（含 `captura.exe`）、发布说明和 SHA-256 清单。保留旧版本；同名文件内容不一致时停止，绝不覆盖。NAS 不可达时会报告未完成，并保留已成功校验的本地归档。GitHub 发布和归档分别验证，任一失败都会返回非零退出代码。

## 验证边界

脚本自动验证中文资源和引用、默认语言、安装语言配置、核心录制与图像对象逻辑、实际截图及 AVI 录制（有系统 FFmpeg 时也检查 MP4）、EXE 版本、运行依赖，以及隔离测试目录内的中文静默安装、载荷哈希和卸载。安装测试使用独立应用标识并禁用快捷方式，不替换用户已经安装的版本。

当前版本另外检查主窗口、设置、关于、颜色选择器、安装语言选择和录制来源提示。设备驱动、各显卡硬件编码、云端上传授权及所有第三方服务不属于完整兼容性保证；FFmpeg 的原始编码参数、设备名称和异常堆栈保留技术信息。

## 开源许可

项目遵循 [MIT 许可证](LICENSE.md)，保留原作者 Mathew Sachin 的版权。依赖许可见 `licenses` 目录。中文维护：yakoye。
