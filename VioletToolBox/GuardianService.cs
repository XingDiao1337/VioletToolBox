using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace WpfApp1
{
    /// <summary>
    /// 内存守护进程服务：
    /// 在工具箱启动时于内存中释放并启动常驻守护程序；
    /// 当检测到用户在关闭二次确认窗口中点击“确认”后，在主程序退出时彻底清理 C:\Yuzaki Tool Box 目录并自毁；
    /// 若用户未确认（取消、直接杀死进程等），则不执行删除。
    /// </summary>
    public static class GuardianService
    {
        public const string TargetDirectoryPath = @"C:\Yuzaki Tool Box";

        private static EventWaitHandle? _confirmEvent;
        private static Process? _guardianProcess;
        private static readonly object _syncLock = new object();
        private static bool _isInitialized = false;

        /// <summary>
        /// 工具箱开始运行时调用，在内存中释放并启动守护程序
        /// </summary>
        public static void InitializeGuardian()
        {
            lock (_syncLock)
            {
                if (_isInitialized) return;
                _isInitialized = true;

                try
                {
                    int mainPid = Environment.ProcessId;
                    string eventName = "Yuzaki_Exit_Confirm_" + mainPid;

                    // 创建进程间同步事件句柄（纯内存内核对象，不在磁盘写入任何文件）
                    _confirmEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);

                    // 守护脚本直接通过 PowerShell 隐式在内存中执行，无临时可执行文件残留
                    string script = $@"
$mainPid = {mainPid}
$eventName = '{eventName}'
$targetDir = '{TargetDirectoryPath}'

# 连接内存内核事件
$ev = $null
for ($i = 0; $i -lt 50; $i++) {{
    try {{
        $ev = [System.Threading.EventWaitHandle]::OpenExisting($eventName)
        if ($ev) {{ break }}
    }} catch {{
        Start-Sleep -Milliseconds 100
    }}
}}
if (-not $ev) {{ exit 0 }}

# 守护进程等待主程序退出
try {{
    $mainProc = [System.Diagnostics.Process]::GetProcessById($mainPid)
    $mainProc.WaitForExit()
}} catch {{}}

# 给予系统短暂时间释放主程序文件句柄
Start-Sleep -Milliseconds 300

# 检测用户是否在退出二次确认窗口中点击了“确认”
$confirmed = $ev.WaitOne(0)

if ($confirmed) {{
    # 1. 强制终止可能残留并占用工具箱目录的后台工具进程
    $lingering = @('adb', 'fastboot', 'scrcpy', 'scrcpy-server')
    foreach ($name in $lingering) {{
        try {{
            Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        }} catch {{}}
    }}
    Start-Sleep -Milliseconds 300

    # 2. 检查写死的 C:\Yuzaki Tool Box 目录，若存在则彻底强制删除
    if (Test-Path -LiteralPath $targetDir) {{
        for ($attempt = 1; $attempt -le 10; $attempt++) {{
            try {{
                # 去除目录下所有文件的只读、隐藏、系统属性
                & cmd.exe /c ""attrib -r -s -h `""$targetDir\*.*`"" /s /d 2>nul & rd /s /q `""$targetDir`"""" 2>&1 | Out-Null
                if (-not (Test-Path -LiteralPath $targetDir)) {{ break }}
                Remove-Item -LiteralPath $targetDir -Recurse -Force -ErrorAction SilentlyContinue
                if (-not (Test-Path -LiteralPath $targetDir)) {{ break }}
            }} catch {{}}
            Start-Sleep -Milliseconds 500
        }}
    }}

    # 3. 若桌面上有残留的 Smart Tool Download 临时目录，一并清理
    try {{
        $desktopDir = [System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'Smart Tool Download')
        if (Test-Path -LiteralPath $desktopDir) {{
            & cmd.exe /c ""rd /s /q `""$desktopDir`"""" 2>&1 | Out-Null
            Remove-Item -LiteralPath $desktopDir -Recurse -Force -ErrorAction SilentlyContinue
        }}
    }} catch {{}}
}}

# 关闭事件句柄，内存中的守护程序自毁并完全退出
try {{ $ev.Close() }} catch {{}}
exit 0
";

                    byte[] scriptBytes = Encoding.Unicode.GetBytes(script);
                    string base64Script = Convert.ToBase64String(scriptBytes);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {base64Script}",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        // 设置工作目录为系统目录，确保守护进程自身不占用目标目录文件句柄
                        WorkingDirectory = Environment.SystemDirectory
                    };

                    _guardianProcess = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GuardianService] 启动守护进程失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 当用户在退出二次确认窗口中点击“确认”时调用，
        /// 通知守护程序在主进程退出后执行写死目录的删除与自毁
        /// </summary>
        public static void SignalExitConfirmed()
        {
            try
            {
                _confirmEvent?.Set();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GuardianService] 触发退出确认信号失败: {ex.Message}");
            }
        }
    }
}
