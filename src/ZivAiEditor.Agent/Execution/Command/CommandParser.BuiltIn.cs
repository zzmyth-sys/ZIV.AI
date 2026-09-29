using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Agent.Execution;

public sealed partial class CommandParser
{
    /// <summary>
    /// Fallback command set used when <c>Template/commands.json</c> is missing or malformed
    /// (Z28). Must stay in sync with that data file (kept side by side on purpose; the file is
    /// the product path, this is the no-file safety net).
    /// </summary>
    internal static IReadOnlyList<CommandDefinition> BuiltInCommands() => new[]
    {
        new CommandDefinition
        {
            Name = "/换背景",
            Params = new List<string> { "description" },
            Variadic = true,
            AllowEmptyPrompt = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Replace the background of <image1> with: {description}. Keep the person's facial identity, hair, body shape, pose and clothing exactly unchanged, and keep the original lighting on the subject. Blend the subject's edges naturally into the new background so there is no visible seam.",
                ["multi"] = "Replace the background of <image1> with the scene from <image2>: {description}. Use <image2> only as the new scene; keep <image1>'s person (facial identity, hair, body shape, pose, clothing) exactly unchanged and blend the edges naturally.",
            },
            Description = "替换背景（1 图直接换 / 2 图参考场景）",
        },
        new CommandDefinition
        {
            Name = "/换装",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Change the clothing of the person in <image1> to: {description}. Keep the facial identity, hair, body shape, pose and the original background exactly unchanged; make the garment fit the body naturally with consistent folds, material and lighting.",
                ["multi"] = "Dress the person in <image1> with the garment from <image2>: {description}. Use <image2> only for the garment; keep <image1>'s facial identity, hair, body shape, pose and background exactly unchanged.",
            },
            Description = "更换服装（1 图文字描述 / 2 图参考服装）",
        },
        new CommandDefinition
        {
            Name = "/换发色",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Change the hair color of the person in <image1> to: {description}. Keep the hairstyle, facial identity, pose and background exactly unchanged.",
            },
            Description = "更换发色",
        },
        new CommandDefinition
        {
            Name = "/换表情",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Change the person's facial expression in <image1> to: {description}. Keep the facial identity, hairstyle, pose and background exactly unchanged.",
            },
            Description = "更换表情",
        },
        new CommandDefinition
        {
            Name = "/换光线",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Relight the image to: {description}. Keep the subject, pose, composition and all content exactly unchanged; only change the lighting.",
            },
            Description = "改变打光/光线",
        },
        new CommandDefinition
        {
            Name = "/合照",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            DefaultVariant = "multi",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["multi"] = "Create a new scene with the identities from the references: the person from <image1> and the person from <image2> stand together. {description}. Preserve each person's identity independently; do not merge facial features or clothing between them.",
            },
            Description = "多主体合照（需至少 2 张图）",
        },
        new CommandDefinition
        {
            Name = "/生成",
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            T2i = true,
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "{description}",
            },
            Description = "文生图（大模型扩写提示词）",
        },
        new CommandDefinition
        {
            Name = "/去水印",
            Params = new List<string>(),
            Tool = "QW21edit",
            Quick = true,
            ShortcutLabel = "去水印",
            Template = "Remove all watermarks, logos and subtitles from <image1>. Reconstruct the covered area to match the surrounding content naturally; keep everything else exactly unchanged.",
            Description = "去除水印",
        },
        new CommandDefinition
        {
            Name = "/去背景",
            Handler = CommandHandler.Edit,
            Params = new List<string>(),
            Tool = "QW21edit",
            Quick = true,
            ShortcutLabel = "去背景",
            Template = "This is an RGBA image with transparency. The image has an alpha channel and the background is transparent.",
            Description = "生成透明通道PNG图",
        },
        new CommandDefinition
        {
            Name = "/去物体",
            Params = new List<string> { "object" },
            Tool = "QW21edit",
            Template = "Remove the {object} from <image1>. Fill the removed area naturally to match the surrounding context; keep everything else exactly unchanged.",
            Description = "移除指定物体",
        },
        new CommandDefinition
        {
            Name = "/提取",
            Handler = CommandHandler.Edit,
            Params = new List<string> { "object" },
            Variadic = true,
            Tool = "QW21edit",
            Template = "Extract the {object} from the image and place it on a clean, solid white background. Preserve the original details, textures, colors, and materials. Remove the body, pose, and other unrelated elements.",
            Description = "提取指定主体到纯白底",
        },
        new CommandDefinition
        {
            Name = "/扩图",
            Handler = CommandHandler.Edit,
            Params = new List<string>(),
            Tool = "QW21edit",
            Template = "Outpaint the image to fill the entire canvas. Replace all solid blue padded regions with coherent continuation of the scene. Keep the original subject and content unchanged outside the blue areas.",
            Description = "填充裁切外扩的蓝底区域",
        },
        new CommandDefinition
        {
            Name = "/全景",
            Handler = CommandHandler.Edit,
            Params = new List<string>(),
            Tool = "QW21edit",
            Quick = true,
            ShortcutLabel = "全景",
            FixedResolution = new ResolutionPolicy
            {
                Mode = ResolutionMode.Explicit,
                Width = 2048,
                Height = 1024,
            },
            Template = "Generate a 180-degree equirectangular panorama (front hemisphere only, 2:1 aspect ratio) from the input perspective image. Use a true equirectangular projection covering 180 degrees horizontal and 180 degrees vertical. The left and right edges should be at the 90-degree-left and 90-degree-right extremes of the front hemisphere. Keep the scene, style, lighting and all content continuous and consistent with the original image.",
            Description = "将当前图转为 180° 半全景",
        },
        new CommandDefinition
        {
            Name = "/换脸",
            Handler = CommandHandler.Edit,
            Params = new List<string> { "description" },
            Variadic = true,
            Tool = "QW21edit",
            Template = "head_swap: start with Picture 1 as the base image, keeping its lighting, environment, and background. Remove the head from Picture 1 completely and replace it with the head from Picture 2. Ensure the head and body have correct anatomical proportions and natural blending. {description}",
            Loras = new List<LoraOptions>
            {
                new LoraOptions { Path = "face-swap", StrengthModel = 1.0, StrengthClip = 1.0 },
            },
            Description = "换脸：把参考图（<image2>）的脸换到主图（<image1>）人物上（需自备换脸 LoRA）",
        },
    };
}
