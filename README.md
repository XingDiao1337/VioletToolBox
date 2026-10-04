# Yuzaki工具箱（Yuzaki Tool Box）

<img src="VioletToolBox/logo2.ico" width="112" alt="YuzakiToolBox Logo">

一款面向 Android 设备的强大 Win32 风格多功能搞机工具箱。涵盖常用 ADB、Fastboot、Scrcpy 投屏、镜像刷入、分区管理及高通 9008 EDL 等功能。

## 功能一览

### 1. 设备主页与状态监控
- 自动识别设备连接状态与模式（系统开机 ADB / Fastboot / Recovery / EDL 9008 等）。
- 实时获取硬件配置、电池、内存与存储空间状态。
- 提供一键重启到常用模式（系统、Fastboot、FastbootD、Recovery、9008）及槽位切换。
- 提供手动刷新设备信息按钮。

### 2. 屏幕投屏
- 基于 scrcpy 的低延迟投屏与控制。
- 自动检测设备并一键启动投屏。
- 自由调整投屏窗口、比特率与控制参数。

### 3. 基本刷入
- 常用分区镜像快速刷入（boot、init_boot、recovery、vbmeta 等）。
- 一键解锁 / 回锁 Bootloader。
- 小米线刷脚本可视化执行与精简刷机。

### 4. 应用管理
- 快速读取并搜索第三方与系统已安装应用。
- 支持应用冻结、解冻与清除数据。
- 支持从电脑批量安装 APK。

### 5. Payload 提取
- 本地与云端 OTA 全量包 Payload.bin 流式解析与分区镜像单独提取。

### 6. 高通 9008 / EDL
- 支持高通设备 9008 模式下的分区读取、擦除与刷写。

## 构建与运行

开发环境：Windows 10/11，.NET 8 SDK 或更高版本。

```powershell
dotnet restore VioletToolBox/SmartTool.csproj
dotnet build VioletToolBox/SmartTool.csproj -c Release
```

发布 x64 独立版本：

```powershell
dotnet publish VioletToolBox/SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## 许可证

本项目采用 GNU General Public License v3.0（GPL-3.0）授权，详见 [LICENSE](LICENSE)。
