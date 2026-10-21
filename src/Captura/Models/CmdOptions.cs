using CommandLine;

namespace Captura
{
    /// <summary>
    /// Command-line options for the WPF app.
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class CmdOptions
    {
        [Option("reset", HelpText = "将所有设置恢复为默认值。")]
        public bool Reset { get; set; }

        [Option("tray", HelpText = "启动时最小化到系统托盘。")]
        public bool Tray { get; set; }
        
        [Option("no-hotkey", HelpText = "不注册全局快捷键。")]
        public bool NoHotkeys { get; set; }

        [Option("no-persist", HelpText = "不保存本次设置更改。")]
        public bool NoPersist { get; set; }

        [Option("settings", HelpText = "设置目录")]
        public string Settings { get; set; }
    }
}
