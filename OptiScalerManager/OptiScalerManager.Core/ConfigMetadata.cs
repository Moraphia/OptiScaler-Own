namespace OptiScalerManager.Core;

public static class ConfigMetadata
{
    private static readonly IReadOnlyDictionary<string, string> Terms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Enable"]="启用", ["Enabled"]="启用", ["Disable"]="禁用", ["Disabled"]="禁用", ["Use"]="使用", ["Allow"]="允许",
        ["Force"]="强制", ["Override"]="覆盖", ["Skip"]="跳过", ["Ignore"]="忽略", ["Prevent"]="阻止", ["Preserve"]="保留",
        ["Debug"]="调试", ["View"]="视图", ["Input"]="输入", ["Output"]="输出", ["Source"]="来源", ["Target"]="目标",
        ["Frame"]="帧", ["Frames"]="帧", ["Generation"]="生成", ["Gen"]="生成", ["Rate"]="帧率", ["Framerate"]="帧率",
        ["Pacing"]="节奏控制", ["Interpolation"]="插帧", ["Count"]="数量", ["Limit"]="上限", ["Ahead"]="提前",
        ["Quality"]="画质", ["Sharpness"]="锐化", ["Scale"]="缩放", ["Scaling"]="缩放", ["Ratio"]="比例",
        ["Width"]="宽度", ["Height"]="高度", ["Left"]="左边界", ["Top"]="上边界", ["Rect"]="区域",
        ["Camera"]="相机", ["Near"]="近裁剪面", ["Far"]="远裁剪面", ["Vertical"]="垂直", ["Horizontal"]="水平",
        ["Depth"]="深度", ["Velocity"]="运动矢量", ["Motion"]="运动", ["Vector"]="矢量", ["Vectors"]="矢量",
        ["Mask"]="遮罩", ["Reactive"]="反应式", ["Transparency"]="透明度", ["UI"]="界面", ["HUD"]="HUD", ["Hudless"]="无 HUD 画面",
        ["Swapchain"]="交换链", ["SwapChain"]="交换链", ["Buffer"]="缓冲区", ["Buffers"]="缓冲区", ["Resource"]="资源",
        ["Tracking"]="追踪", ["Capture"]="捕获", ["Copy"]="复制", ["State"]="状态", ["Valid"]="有效",
        ["Kernel"]="内核", ["Kernels"]="内核", ["Model"]="模型", ["Preset"]="预设", ["Network"]="网络",
        ["Log"]="日志", ["File"]="文件", ["Path"]="路径", ["Dll"]="DLL", ["Library"]="库", ["Libraries"]="库",
        ["Hook"]="Hook", ["Hooks"]="Hook", ["Pattern"]="特征匹配", ["Init"]="初始化", ["Check"]="检查", ["Checks"]="检查",
        ["Thread"]="线程", ["Async"]="异步", ["Mutex"]="互斥锁", ["Spin"]="自旋等待", ["Wait"]="等待",
        ["Time"]="时间", ["Margin"]="余量", ["Variance"]="波动系数", ["Factor"]="系数", ["Offset"]="偏移",
        ["Color"]="颜色", ["Colour"]="颜色", ["Exposure"]="曝光", ["Tone"]="色调", ["White"]="白色", ["Point"]="点",
        ["Pass"]="处理阶段", ["Passes"]="处理阶段", ["Strength"]="强度", ["Intensity"]="强度", ["Structure"]="结构",
        ["Min"]="最小", ["Max"]="最大", ["Mode"]="模式", ["Index"]="索引", ["Refresh"]="刷新", ["Screen"]="屏幕"
    };

    private static readonly Lazy<IReadOnlyDictionary<string, string>> SourceDocumentation = new(() =>
    {
        using var stream = typeof(ConfigMetadata).Assembly.GetManifestResourceStream("OptiScalerManager.Core.OptiScalerTemplate.ini");
        if (stream is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(stream);
        return ParseDocumentation(reader.ReadToEnd());
    });

    public static IReadOnlyDictionary<string, string> ParseDocumentation(string template)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var comments = new List<string>();
        var section = "";
        foreach (var raw in template.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); comments.Clear(); continue; }
            if (line.StartsWith(';'))
            {
                var comment = line[1..].Trim();
                if (comment.Equals("Selected FG Input/Source", StringComparison.OrdinalIgnoreCase)) comments.Clear();
                if (comment.Length > 0 && !comment.All(c => c == '-')) comments.Add(comment);
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals > 0 && section.Length > 0)
            {
                var key = line[..equals].Trim();
                if (comments.Count > 0) result[$"{section}.{key}"] = string.Join(Environment.NewLine, comments);
                comments.Clear();
            }
            else if (line.Length > 0) comments.Clear();
        }
        return result;
    }

    public static string Documentation(string section, string key) => SourceDocumentation.Value.TryGetValue($"{section}.{key}", out var text)
        ? text : "当前源码附带的 OptiScaler.ini 没有此项的说明；请检查对应版本源码。";

    public static string Category(string section) => section.ToLowerInvariant() switch
    {
        "upscalers" or "dlss" or "fsr" or "xess" => "01 · 超分辨率与画质",
        "framegen" or "dlssg" or "xefg" or "fsrfg" => "02 · 帧生成",
        "dlssnr" => "03 · 神经渲染",
        "menu" or "overlay" or "shortcuts" => "04 · 菜单与快捷键",
        "log" or "logging" or "debug" => "05 · 日志与诊断",
        _ => $"06 · 其他 / [{section}]"
    };

    public static string Description(string section, string key)
    {
        var id = $"{section}.{key}".ToLowerInvariant();
        return id switch
        {
            "upscalers.dx12upscaler" => "DX12 超分算法；auto 由程序决定。",
            "upscalers.dx11upscaler" => "选择 DX11 游戏的超分算法；部分后端通过 DX11-on-12 运行。",
            "upscalers.vulkanupscaler" => "选择 Vulkan 游戏的超分算法；后端可为原生 Vulkan 或转接 DX12。",
            "framegen.enabled" => "启用或关闭帧生成总开关。",
            "framegen.fginput" => "游戏向 OptiScaler 提供的帧生成输入管线。",
            "framegen.fgoutput" => "OptiScaler 使用的帧生成后端。",
            "framegen.fgnvngxreplacement" => "NVNGX 帧生成替代实现。",
            "framegen.ftinput" => "选择帧时间来源，影响帧生成的 pacing（呈现节奏）。",
            "framegen.allowedframeahead" => "允许帧生成比游戏渲染提前的帧数；增大可能减少开关抖动，也可能增加问题。",
            "framegen.drawuioverfg" => "将 UI 绘制在生成帧之上；需要 Hudless 画面和 UI 纹理，主要用于 FSR FG。",
            "framegen.disablehudless" => "即使输入端提供 Hudless，也禁用该资源的使用。",
            "framegen.disableui" => "即使输入端提供 UI 纹理，也禁止使用它。",
            "framegen.preserveswapchain" => "保留 FG 交换链，避免释放时的部分崩溃。",
            "framegen.depthvalidnow" => "始终将 Depth 标为 ValidNow；可能增加 VRAM 占用。",
            "framegen.velocityvalidnow" => "始终将 Velocity 标为 ValidNow；可能增加 VRAM 占用。",
            "framegen.hudlessvalidnow" => "始终将 Hudless 标为 ValidNow；可能增加 VRAM 占用。",
            "fsrfg.allowasync" => "允许 FSR 3.1 FG 异步执行；可能改变帧节奏和稳定性。",
            "fsrfg.framepacingtuning" => "启用 FSR FG 的自定义 frame pacing 参数。",
            "xefg.interpolationcount" => "XeFG 插帧数量；1=2×、2=3×、3=4×，实际以运行时能力为准。",
            "dlssg.interpolationcount" => "生成帧数：1=2×，5=6×；源码接受 6，但会请求 7×，实际可能被运行时限制。",
            "dlssg.overrideinterpolationcount" => "覆盖游戏传给 Streamline 的生成帧数；auto 表示不覆盖。",
            "dlssg.overrideforcedmfg" => "尝试强制游戏原生 DLSSG 使用 Dynamic MFG；需要兼容的 Streamline 与硬件。",
            "dlssg.forcedmfg" => "在 OptiScaler 自有 DLSSG 输出中强制 Dynamic MFG。",
            "dlssg.frameratetargetdmfg" => "Dynamic MFG 的目标帧率；非零时不能手动固定倍率。",
            "dlssg.adamfgunlock" => "RTX 40 多帧生成实验性解锁；不保证游戏或 DLL 支持。",
            "dlssg.adablackwellkernels" => "RTX 40 上尝试 Blackwell 内核路径；实验性。",
            "dlssnr.enabled" => "启用 DLSS 神经渲染；需要运行库、输入资源和硬件支持。",
            "dlssnr.transferstrength" => "模型画面混合强度：0 保留超分输出，1 使用模型结果；大于 1 会进一步强化。",
            "dlssnr.colourstrength" => "控制模型颜色参与程度：0 保留游戏原有色相，1 引入模型颜色。",
            "dlssnr.workingscale" => "模型内部工作分辨率比例；降低可减少计算量，但可能损失细节。",
            "dlssnr.preupscale" => "在超分之前运行模型；成本较低，但镜头移动时可能出现细节闪烁。",
            "dlssnr.dualfeature" => "将超分拆成两段，在中间运行神经模型，再放大到显示分辨率。",
            "dlssnr.dualenlarger" => "DualFeature 第二阶段使用的放大算法；auto 为较柔和的空间放大。",
            "dlssnr.passes" => "神经模型连续运行次数；次数越高，计算与 VRAM 开销越大。",
            "dlssnr.maxratio" => "限制神经渲染让像素变亮的最大倍数，抑制过亮伪影。",
            "menu.overlaymenu" => "启用游戏内 ImGui 菜单；关闭后项目注释称所有 FG 功能也会被禁用。",
            "menu.shortcutkey" => "打开游戏内菜单的虚拟键码；auto 默认 Insert，-1 为无快捷键。",
            "menu.showfps" => "显示游戏内 FPS 叠加层。",
            "menu.fpsoverlaytype" => "选择 FPS 叠加层信息量：基础、详细、图表或 Reflex 时间等。",
            "menu.fpsoverlaypos" => "设置 FPS 叠加层位于屏幕哪个角落。",
            "menu.usehqfont" => "使用高质量菜单字体；可能增加 VRAM 占用。",
            "menu.ttffontpath" => "自定义菜单 TTF 字体路径；需要 UseHQFont=true。",
            "log.logtofile" => "将 OptiScaler 日志写入文件。",
            _ => InferDescription(section, key, SourceDocumentation.Value.ContainsKey(id))
        };
    }

    private static string InferDescription(string section, string key, bool documented)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(key, @"[A-Z]+(?=[A-Z][a-z]|\b)|[A-Z]?[a-z]+|\d+|[A-Z]+")
            .Select(match => Terms.TryGetValue(match.Value, out var translated) ? translated : match.Value).ToArray();
        var label = words.Length > 0 ? string.Join("", words) : key;
        var action = key.StartsWith("Disable", StringComparison.OrdinalIgnoreCase) ? "控制" :
            key.StartsWith("Enable", StringComparison.OrdinalIgnoreCase) ? "控制" :
            key.StartsWith("Force", StringComparison.OrdinalIgnoreCase) ? "设置" :
            key.StartsWith("Skip", StringComparison.OrdinalIgnoreCase) ? "设置" : "调整";
        return $"{(documented ? "" : "推测：")}{action} {section} 的{label}；下方可查看项目原注释与适用条件。";
    }

    public static string DetailedExplanation(string section, string key, string current)
    {
        var id = $"{section}.{key}";
        var documented = SourceDocumentation.Value.ContainsKey(id);
        var scope = Category(section).Split('·').Last().Trim();
        var options = Options(section, key, current);
        var valueHint = options.Contains("true", StringComparer.OrdinalIgnoreCase) && options.Contains("false", StringComparer.OrdinalIgnoreCase)
            ? "true=启用，false=关闭，auto=交给 OptiScaler 默认逻辑。"
            : options.Count > 1 ? $"常用预设：{string.Join("、", options.Take(10))}。也可以手动输入其他原始值。" : "数值或字符串的合法范围以原项目注释和运行时代码为准。";
        var caution = key.Contains("Force", StringComparison.OrdinalIgnoreCase) || key.Contains("Override", StringComparison.OrdinalIgnoreCase) || key.Contains("Debug", StringComparison.OrdinalIgnoreCase)
            ? "强制、覆盖或调试选项可能改变兼容性与性能；建议一次只改一项，异常时恢复 auto。" : "修改后请完全重启游戏再判断效果；INI 已保存不等于运行时成功启用。";
        return $"[{section}] {key}\n\n用途：{Description(section, key)}\n影响范围：{scope}。\n当前值：{current}\n取值：{valueHint}\n注意：{caution}\n说明性质：{(documented ? "依据当前分支 OptiScaler.ini 注释；中文摘要包含归纳。" : "项目未提供注释；以上用途根据键名和所属分组推测，需结合源码验证。")}\n\n项目原注释（英文）：\n{Documentation(section, key)}";
    }

    public static IReadOnlyList<string> Options(string section, string key, string current)
    {
        var id = $"{section}.{key}".ToLowerInvariant();
        string[] values = id switch
        {
            "dlssg.interpolationcount" => ["auto", "1", "2", "3", "4", "5"],
            "dlssg.overrideinterpolationcount" => ["auto", "0", "1", "2", "3", "4", "5"],
            "framegen.fginput" => ["auto", "nofg", "upscaler", "dlssg", "fsrfg", "fsrfg30", "nvngxfg"],
            "framegen.fgoutput" => ["auto", "nofg", "dlssg", "fsrfg", "xefg"],
            "framegen.fgnvngxreplacement" => ["auto", "none", "Nukems", "Arturs", "FFX", "Combo"],
            "upscalers.dx12upscaler" => ["auto", "dlss", "fsr21", "fsr22", "ffx", "xess"],
            "dlssnr.dualenlarger" => ["auto", "dlss", "fsr22", "fsr31", "ffx", "xess"],
            _ when key.Equals("Enabled", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Use", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Force", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Enable", StringComparison.OrdinalIgnoreCase)
                || id is "dlssg.adamfgunlock" or "dlssg.adablackwellkernels" or "dlssnr.dualfeature" or "log.logtofile" => ["auto", "true", "false"],
            _ => ["auto"]
        };
        return values.Append(current).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
