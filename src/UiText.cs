using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CodexAutoResume
{
    internal static class UiText
    {
        sealed class Binding { internal string Source, Rendered; }
        static readonly ConditionalWeakTable<Control, Binding> bindings = new ConditionalWeakTable<Control, Binding>();
        static readonly Dictionary<string, string> english = new Dictionary<string, string>
        {
            { "后台控件状态", "Background control state" },
            { "聊天已变化，请手动核对额度暂停", "Chat changed; check the quota pause manually" },
            { "后台支持遮挡；接口可核验时也支持最小化\n最小化时接口不可用则等待；前台模式需按钮可见", "Background supports covered windows and verified minimized controls\nMinimized targets wait if controls are unavailable; foreground needs a visible button" },
            { "后台控件暂不可核验，等待重新确认；不会使用旧截图点击", "Background controls unavailable; waiting for a verified context" },
            { "后台控件检测到问答或其他待处理状态，等待用户", "A question or another pending state needs your attention" },
            { "后台操作已投递，等待原聊天控件确认运行", "Background action attempted; waiting for the original chat to confirm running" },
            { "后台操作前状态变化，重新核验原聊天", "State changed before the action; rechecking the original chat" },
            { "原对话有草稿，等待用户处理", "The original chat has a draft; waiting for you" },
            { "详细诊断（含原始窗口截图）", "Detailed diagnostics (includes window images)" },
            { "只读 UIA 对照（可能影响目标重绘）", "Read-only UIA comparison (may affect repainting)" },
            { "仅本机保存，可能包含聊天内容；本次开启最多 1 小时\n截图按 180 组 / 128 MB 滚动，文本可复制，诊断包包含近期可用截图", "Saved locally; may include chats; enabled for up to 1 hour\nRolling limit: 180 sets / 128 MB; export includes available recent images" },
            { "复制近 30 分钟日志", "Copy last 30 minutes of logs" },
            { "导出近 30 分钟诊断包", "Export last 30 minutes of diagnostics" },
            { "已复制近 30 分钟文本日志，不含截图", "Copied the last 30 minutes of text logs (no images)" },
            { "诊断日志", "Diagnostic logs" },
            { "复制失败，请使用导出诊断包", "Copy failed; try exporting diagnostics" },
            { "诊断包已导出；分享前请检查截图中的聊天内容", "Diagnostics exported; review chat content in images before sharing" },
            { "导出失败，原文件已保留", "Export failed; the original file was retained" },
            { "返回监测", "Back" },
            { "暂停监测", "Pause" },
            { "恢复记录", "Recoveries" },
            { "全部活动", "Activity" },
            { "恢复范围", "Recovery scope" },
            { "读取项目和聊天", "Read projects and chats" },
            { "应用范围", "Apply scope" },
            { "当前监测显示中的聊天", "Monitor the current chat" },
            { "范围需重新确认；确认前不会自动操作", "Confirm scope again before automatic actions" },
            { "一次性任务指令", "One-time instructions" },
            { "新增并绑定当前聊天", "Add for current chat" },
            { "保存并绑定选中任务", "Save and bind selected" },
            { "取消选中任务", "Cancel selected" },
            { "修改后点击保存；每个任务保存自己的指令", "Save edits; each task keeps its own instruction" },
            { "等待执行", "Pending" },
            { "等待条件", "Waiting for conditions" },
            { "执行中", "Submitting" },
            { "已完成", "Completed" },
            { "已取消", "Canceled" },
            { "需重新绑定", "Rebind required" },
            { "结果不明，请核对", "Outcome uncertain; review" },
            { "计划已保存，未启用执行", "Schedule saved; auto-resume is off" },
            { "已发出操作，等待确认", "Action sent; awaiting confirmation" },
            { "已到时间，等待恢复条件（具体原因未核实）", "Time reached; waiting for verified conditions" },
            { "等待可确认的额度", "Waiting for verified quota" },
            { "没有暂停三角形；此恢复计划不会主动发送消息", "No resume triangle; this recovery schedule does not send messages" },
            { "到点后仍须满足已启用的恢复条件，不会主动暂停或发送任务指令\n仅本次运行有效；停止监测、切换目标或退出即取消", "Enabled recovery conditions still apply; this plan does not pause or send instructions\nThis session only; stopping, changing targets or exiting cancels it" },
            { "先在 Codex 打开目标聊天，再绑定当前聊天。到点后等待空框、额度与输入安全条件\n重启后需重新绑定；结果不明的任务不会自动重试", "Open the target chat in Codex and bind it. After the deadline, wait for safe input and quota conditions\nRebind after restart; uncertain tasks never automatically repeat" },
            { "额度自动暂停与恢复", "Pause and resume by quota" },
            { "定时恢复", "Scheduled recovery" },
            { "设置一次性恢复时间", "Set one-time recovery" },
            { "取消定时", "Cancel schedule" },
            { "未设置定时恢复", "No recovery time set" },
            { "等待指定时间", "Waiting for scheduled time" },
            { "已到时间，等待恢复条件", "Time reached; waiting for recovery conditions" },
            { "时间或目标已变化，定时恢复已取消，请重新设置", "Time or target changed; schedule canceled, please set it again" },
            { "请选择无夏令时歧义的有效时间", "Choose a valid time without daylight-saving ambiguity" },
            { "请选择未来的日期和时间", "Choose a future date and time" },
            { "请先连接目标并开启监测", "Connect a target and enable monitoring first" },
            { "请等待当前操作确认后再设置定时", "Wait for the current action to be confirmed before scheduling" },
            { "定时已设置，请开启自动恢复", "Recovery time set; enable Auto-resume" },
            { "定时恢复已取消", "Scheduled recovery canceled" },
            { "到点且额度满足后才允许恢复，不会主动暂停\n仅本次运行有效；停止监测、切换目标或退出即取消", "Resume only after the time and quota conditions are met; no scheduled pause\nThis session only; stopping, changing targets or exiting cancels it" },
            { "启用额度控制（需开启自动恢复）", "Enable quota control (requires Auto-resume)" },
            { "周额度", "Weekly" },
            { "剩余 ≤", "Pause at ≤" },
            { "% 时暂停", "% remaining" },
            { "无暂停三角形时补发的继续指令", "Continuation text when the resume triangle is gone" },
            { "任一所选额度不足即暂停，全部恢复才继续\n等待时画面每分钟采样，额度正常每 10 秒查询\n后台输入不可用时允许切回 Codex 输入；已有草稿时等待", "Pause if any selected quota is low; resume when all recover\nSample the screen once a minute while waiting; query quota every 10s\nInput may activate Codex if background input is unavailable; preserve drafts" },
            { "账号已变化，请手动核对额度暂停", "Account changed; review the quota pause manually" },
            { "检测到手动继续，本次额度暂停已取消", "Manual continuation detected; quota pause canceled" },
            { "额度恢复，Codex 已继续运行", "Quota recovered; Codex is running again" },
            { "额度操作结果不明，请手动核对；不会重复发送", "Quota action uncertain; review manually; no repeated submission" },
            { "正在按额度暂停，等待画面确认", "Pausing for quota; awaiting screen confirmation" },
            { "额度恢复，等待运行确认", "Quota recovered; awaiting running confirmation" },
            { "额度暂停中 · 每分钟检查画面", "Paused for quota · Screen check every minute" },
            { "额度暂停中 · 等待有效额度", "Paused for quota · Waiting for valid usage" },
            { "等待有效额度，暂不自动点击", "Waiting for valid quota; automatic clicks suspended" },
            { "额度不足，等待运行方块后暂停", "Low quota; waiting for the running square to pause" },
            { "额度已恢复，等待可靠的继续入口", "Quota recovered; waiting for a verified continuation control" },
            { "无法核实原对话，等待重试；可手动继续", "Cannot verify the original conversation; retrying; manual continuation is available" },
            { "原对话有草稿或已尝试继续，请手动核对", "Draft present or continuation already attempted; review manually" },
            { "已补发继续指令，等待运行确认", "Continuation submitted; awaiting running confirmation" },
            { "无法安全补发继续指令，请手动继续", "Cannot safely submit continuation; continue manually" },
            { "额度操作未完成，请手动核对；不会重复发送", "Quota action incomplete; review manually; no repeated submission" },
            { "选择窗口", "Select window" },
            { "向右三角形 · 可恢复", "Right triangle · Can resume" },
            { "白色方块 · 运行中", "White square · Working" },
            { "向上箭头 · 发送", "Up arrow · Send" },
            { "声波图标 · 语音", "Waveform · Voice" },
            { "小叉 · 语音模式关闭", "Cross · Close voice mode" },
            { "语音模式 · 等待操作", "Voice mode · Waiting for input" },
            { "未知图形", "Unknown icon" },
            { "编号 / 图标", "Area / icon" },
            { "识别状态", "Recognized state" },
            { "扫描区域中的位置", "Position in captured area" },
            { "本机自检通过", "Self-check passed" },
            { "此检查不会发送鼠标点击；自动恢复请用演示窗口验证", "This check does not click; use the demo to verify automatic recovery" },
            { "三角形", "Triangle" },
            { "方块", "Square" },
            { "向上箭头", "Up arrow" },
            { "语音", "Voice" },
            { "未知符号", "Unknown" },
            { "双区域", "Two areas" },
            { "换位置", "Move" },
            { "演示已自动连接，无需快捷键", "Demo connected automatically; no shortcut needed" },
            { "在主窗口勾选允许自动点击即可开始", "Enable Auto-resume in the main window to start" },
            { "后台模式可切到其他应用，完全盖住此窗口", "Background mode works while this window is covered" },
            { "三角形恢复后变为方块，计数只增加一次", "Resuming changes the triangle to a square and counts one click" },
            { "本机自检通过：系统接口、窗口控件、内嵌图片和数据目录正常", "Self-check passed" },
            { "后台自测通过：完全遮挡下恢复一次，鼠标和前台窗口保持不变", "Background test passed" },
            { "后台自测已取消；监控保持停止", "Background test canceled · Paused" },
            { "正在后台自测；监控已停止，请暂勿移动鼠标或切换窗口", "Background test running · Monitoring paused" },
            { "自动定位已启用，正在寻找按钮；窗口移动或调整大小后会重新定位", "Automatic targeting enabled" },
            { "CodexAutoResume 后台自测", "CodexAutoResume background test" },
            { "CodexAutoResume 后台自测目标", "CodexAutoResume test target" },
            { "正在完全遮挡自测窗口，并验证后台恢复", "Testing recovery while the target is fully covered" },
            { "通常只需几秒；请暂勿操作鼠标或切换窗口", "Please keep the mouse and foreground window unchanged" },
            { "关闭此窗口可取消自测", "Close this window to cancel the test" },
            { "CodexAutoResume 已在运行，请使用现有窗口", "CodexAutoResume is already running; use the existing window" },
            { "等待你完成操作", "Waiting for you to finish" },
            { "导出捕获对照", "Capture comparison" },
            { "请先连接窗口并等待当前检查完成", "Connect a window and wait for the current check" },
            { "正在导出捕获对照", "Exporting capture comparison" },
            { "设置", "Settings" },
            { "暂停", "Pause" },
            { "继续监测", "Resume" },
            { "目标", "Target" },
            { "方式", "Mode" },
            { "更换", "Change" },
            { "自动恢复", "Auto-resume" },
            { "暂停后空闲 1.5 秒自动继续；关闭后仅观察", "Resume after 1.5s idle; otherwise observe" },
            { "最近进度", "Recent progress" },
            { "完整记录", "Full history" },
            { "选择区域", "Select area" },
            { "等待识别", "No sample" },
            { "采样图", "Sample" },
            { "问答区域", "Question area" },
            { "最近识别 · 问答区域", "Latest sample · Question area" },
            { "最近识别 · 采样图", "Latest sample" },
            { "● 自测中", "● Testing" },
            { "● 已暂停", "● Paused" },
            { "● 正在监测", "● Monitoring" },
            { "● 正在监测 · 仅观察", "● Observing" },
            { "后台监测", "Background" },
            { "前台监测", "Foreground" },
            { "已暂停 · 保留上次取样", "Paused · Last sample" },
            { "正在寻找 Codex", "Looking for Codex" },
            { "Codex 正在运行", "Codex is working" },
            { "正在识别 Codex", "Recognizing Codex" },
            { "等待操作", "Waiting for input" },
            { "等待发送", "Ready to send" },
            { "检测到暂停", "Pause detected" },
            { "检测到暂停 · 仅观察", "Pause detected · Observing" },
            { "已恢复", "Resumed" },
            { "已暂停", "Paused" },
            { "仅观察", "Observing" },
            { "正在恢复…", "Resuming…" },
            { "等待你的回答", "Waiting for your answer" },
            { "还原 Codex 后自动继续", "Restore Codex to continue" },
            { "显示 Codex 后自动继续", "Show Codex to continue" },
            { "切回 Codex 后自动继续", "Switch to Codex to continue" },
            { "发现多个按钮，暂不自动恢复", "Select a button to resume" },
            { "请重新选择按钮区域", "Select the button area again" },
            { "请选择要监测的窗口", "Choose a window to monitor" },
            { "暂时无法获取画面，正在重试", "Capture unavailable · Retrying" },
            { "界面正在变化，继续监测", "View changing · Monitoring" },
            { "已连接 Codex", "Connected to Codex" },
            { "后台监测已开启", "Background monitoring enabled" },
            { "前台监测已开启", "Foreground monitoring enabled" },
            { "自动恢复已开启", "Auto-resume enabled" },
            { "请先继续监测，再更换窗口", "Resume monitoring before changing windows" },
            { "请先继续监测，再选择区域", "Resume monitoring before selecting an area" },
            { "请先连接 Codex 窗口", "Connect to a Codex window first" },
            { "请选择要监测的按钮", "Choose a button to monitor" },
            { "活动记录", "Activity history" },
            { "常规", "General" },
            { "诊断工具", "Diagnostics" },
            { "帮助与更新", "Help & updates" },
            { "语言", "Language" },
            { "后台允许窗口被遮挡；Codex 需保持未最小化", "Background: Codex can be covered, but must not be minimized" },
            { "前台模式需要 Codex 位于前台，按钮可见", "Foreground: Codex and its button must be visible" },
            { "自动检查更新", "Check for updates automatically" },
            { "高级定位", "Advanced targeting" },
            { "选择按钮区域", "Select button area" },
            { "手动区域大小（像素）", "Manual area size (px)" },
            { "打开演示", "Open demo" },
            { "本机自检", "Self-check" },
            { "后台自测", "Background test" },
            { "保存按钮图", "Save sample" },
            { "打开日志", "Open logs" },
            { "单文件便携版 · Windows x64 · MIT", "Portable executable · Windows x64 · MIT" },
            { "检查更新", "Check for updates" },
            { "使用说明", "Help" },
            { "许可证", "License" },
            { "启动默认仅观察，开启自动恢复后才会点击", "Starts in observation mode; enable auto-resume to allow clicks" },
            { "自动检查每 6 小时一次，没有更新时保持安静", "Checks every 6 hours; stays quiet when up to date" },
            { "无法打开日志", "Unable to open logs" },
            { "当前已是最新正式版", "You have the latest stable version" },
            { "发现新版本", "Update available" },
            { "立即更新", "Update now" },
            { "稍后", "Later" },
            { "跳过此版本", "Skip this version" },
            { "检查更新超时，请稍后重试", "Update check timed out; try again later" },
            { "暂时无法检查更新，请稍后重试", "Unable to check for updates; try again later" },
            { "正在更新", "Updating" },
            { "正在下载并校验更新…", "Downloading and verifying update…" },
            { "正在准备替换并重启…", "Preparing to install and restart…" },
            { "取消", "Cancel" },
            { "更新失败", "Update failed" },
            { "更新未完成，当前版本保持可用", "Update incomplete; the current version is still available" },
            { "更新结果", "Update result" },
            { "更新未完成，已保留或恢复原版本", "Update incomplete; the previous version was retained or restored" },
            { "请稍后重试", "Please try again later" },
            { "更新完成，已切换为仅观察", "Update complete · Observing" },
            { "更新完成，继续监测", "Update complete · Monitoring" },
            { "更新完成，已暂停", "Update complete · Paused" },
            { "问答区域仅供查看；请在按钮状态下保存按钮图", "Question previews are view-only; save a sample in button mode" },
            { "尚无局部取样图，请先标定", "No sample yet; select a target first" },
            { "请选择要监测的 Codex 窗口", "Choose a Codex window to monitor" },
            { "请选择要监控的 Codex 按钮", "Choose a Codex button to monitor" },
            { "使用所选窗口", "Use selected window" },
            { "使用所选区域", "Use selected area" },
            { "暂不选择", "Not now" },
            { "发现多个候选，或原选区域已改变。请选择并确认；确认前不会自动点击", "Choose and confirm a button area before automatic clicks can resume" },
            { "检测到多个窗口，请选择一个。确认前不会点击；之后可用主窗口的“选择窗口”更换", "Choose a window before clicks can resume; use Change to switch later" },
            { "本机自检失败", "Self-check failed" },
            { "后台自测通过", "Background test passed" },
            { "后台自测未通过", "Background test failed" },
            { "三角形已确认；等待鼠标、键盘空闲 0.7 秒", "Waiting for the mouse and keyboard to become idle" },
            { "点击前状态变化，已取消本次点击并重新观察", "State changed before click · Observing again" },
            { "监控中：允许自动点击", "Auto-resume enabled" },
            { "监控中：仅观察，不会点击", "Observing" }
        };
        internal static string Language = "zh-CN";
        internal static bool English { get { return Language == "en"; } }
        internal static event Action Changed;
        internal static void SetLanguage(string language)
        {
            Language = language == "en" ? "en" : "zh-CN";
            foreach (Form form in Application.OpenForms) Apply(form);
            if (Changed != null) Changed();
        }
        internal static string Get(string source)
        {
            if (String.IsNullOrEmpty(source)) return source;
            // Each short UI line omits its final Chinese full stop. Internal
            // punctuation in longer explanations remains intact.
            string clean = Regex.Replace(source, "。[\\r\\n]*$", "");
            if (source.Contains("\n"))
            {
                string[] lines = source.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = Get(lines[i]);
                return String.Join(Environment.NewLine, lines);
            }
            if (!English) return clean;
            string result;
            if (english.TryGetValue(clean, out result)) return result;
            if (clean.StartsWith("画面 ")) return clean.Replace("画面 ", "Frame ").Replace(" · 检查 ", " · Checked ");
            Match time = Regex.Match(clean, @"^(\d{2}:\d{2}:\d{2}\s+)(.*)$");
            if (time.Success) return time.Groups[1].Value + Get(time.Groups[2].Value);
            if (clean.StartsWith("按钮收到的点击：")) return "Button clicks: " + clean.Substring("按钮收到的点击：".Length);
            Match count = Regex.Match(clean, @"^本轮已恢复 (\d+) 次$");
            if (count.Success) return "Resumed this session: " + count.Groups[1].Value;
            foreach (string prefix in new[] { "已检查 · ", "正在检查 · ", "等待检查 · ", "最近取样 ", "手动标定：将鼠标移到目标按钮中心，按 " })
                if (clean.StartsWith(prefix))
                {
                    string translated = prefix == "已检查 · " ? "Checked · " : prefix == "正在检查 · " ? "Checking · " : prefix == "等待检查 · " ? "Awaiting check · " : prefix == "最近取样 " ? "Sampled " : "Calibration: point at button, then press ";
                    return translated + Get(clean.Substring(prefix.Length));
                }
            foreach (string prefix in new[] { "已保存上一帧局部图：", "保存失败：", "本机自检失败：", "后台自测未通过：", "本次捕获或点击暂不可用，持续监测：" })
                if (clean.StartsWith(prefix)) return (prefix.StartsWith("已保存") ? "Sample saved: " : prefix.StartsWith("保存") ? "Save failed: " : prefix.StartsWith("本机") ? "Self-check failed: " : prefix.StartsWith("后台") ? "Background test failed: " : "Capture unavailable · Monitoring: ") + clean.Substring(prefix.Length);
            Match update = Regex.Match(clean, @"^发现新版本 (.+)（当前 (.+)）$");
            if (update.Success) return "Version " + update.Groups[1].Value + " available (current " + update.Groups[2].Value + ")";
            return clean;
        }
        internal static string Diagnostics(string source)
        {
            if (!English || String.IsNullOrEmpty(source)) return source;
            string result = Get(source);
            string[,] terms = {
                { "问答面板已消失，恢复正常图标检测", "Question panel closed; resuming icon detection" },
                { "问答面板已识别，降低检测频率", "Question panel detected; checking less frequently" },
                { "不会选择、跳过或提交答案", "Answers are never selected, skipped or submitted automatically" },
                { "等待用户回答", "Waiting for your answer" },
                { "已确认恢复：", "Confirmed resumes: " }, { "已点击：", "Clicks: " },
                { "三角形 ", "Triangle " }, { "方块 ", "Square " }, { "发送 ", "Send " }, { "语音 ", "Voice " },
                { "自动定位", "Automatic targeting" }, { "手动标定", "Manual calibration" },
                { "白色方块 · 运行中", "White square · Working" }, { "向右三角形 · 可恢复", "Right triangle · Resume" },
                { "向上箭头 · 发送", "Up arrow · Send" }, { "声波图标 · 语音", "Waveform · Voice" }, { "小叉 · 语音模式关闭", "Cross · Close voice mode" }, { "未知图形", "Unknown icon" },
                { "画面静止，保留最近观察", "Static view; retaining last observation" },
                { "最近取样有效", "Recent sample valid" }, { "尚无画面", "No capture yet" },
                { "等待本次处理返回", "Processing" }, { "等待下一轮", "Waiting" }, { "监测中", "Monitoring" }, { "已停止", "Stopped" },
                { "最近系统帧：", "Last system frame: " }, { "原生捕获忙：", "Capture busy: " }, { "日志暂时无法写入", "Unable to write logs" },
                { "最近图标=", "Last icon=" }, { "新帧取样 ", "New frames " }, { "读取 ", "Reads " }, { "完成 ", "Completed " },
                { "缓存 ", "Cached " }, { "异常 ", "Errors " }, { "上次取样距今 ", "Last sample age " },
                { "上次系统新帧距今 ", "System frame age " }, { "上次屏幕取样距今 ", "Screen capture age " },
                { "候选 ", "Candidates " }, { "尝试 ", "Attempts " }, { "确认 ", "Confirmed " }, { "后台", "Background" }, { "前台", "Foreground" },
                { "尚无", "None" }, { "秒", "s" }, { "；", "; " }
            };
            for (int i = 0; i < terms.GetLength(0); i++) result = result.Replace(terms[i, 0], terms[i, 1]);
            return result;
        }
        internal static void Set(Control control, string source)
        {
            Binding value = bindings.GetOrCreateValue(control);
            value.Source = source; value.Rendered = Get(source); control.Text = value.Rendered;
        }
        internal static void Apply(Control control)
        {
            if (!(control is TextBoxBase) && !(control is ComboBox) && !(control is NumericUpDown) && control.Name != "TargetName")
            {
                Binding value = bindings.GetOrCreateValue(control);
                if (value.Source == null || control.Text != value.Rendered) value.Source = control.Text;
                value.Rendered = Get(value.Source); control.Text = value.Rendered;
            }
            foreach (Control child in control.Controls) Apply(child);
        }
    }
    internal static class UiDialog
    {
        internal static DialogResult Show(IWin32Window owner, string message, string title)
        { return Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.None); }
        internal static DialogResult Show(IWin32Window owner, string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        { return MessageBox.Show(owner, UiText.Get(message), UiText.Get(title), buttons, icon); }
    }
}
