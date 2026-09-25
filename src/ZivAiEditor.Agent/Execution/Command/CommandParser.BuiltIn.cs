using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;

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
            Tool = "QW21edit",
            DefaultVariant = "single",
            Variants = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["single"] = "Keep the character and pose in <image1> unchanged. Replace the background with {description}. Preserve the original facial identity, hair, body shape and pose.",
                ["multi"] = "Keep the character and pose in <image1> unchanged. Use the scene from <image2> as the new background. {description}. Preserve the original facial identity, hair, body shape and pose.",
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
                ["single"] = "Change the clothing of the person in <image1> to: {description}. Keep the facial identity, hair, body shape and pose unchanged, and keep the original background and lighting.",
                ["multi"] = "Use the garment from <image2> to dress the person in <image1>. {description}. Preserve the facial identity, body shape and pose, and keep the original background.",
            },
            Description = "更换服装（1 图文字描述 / 2 图参考服装）",
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
                ["multi"] = "Create a new scene using the reference identities. The person from <image1> and the person from <image2> stand together. {description}. Preserve each person's identity independently; do not merge facial features or clothing between them.",
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
            Template = "Remove all watermarks, logos, and subtitles from the image. Keep all other content unchanged.",
            Description = "去除水印",
        },
        new CommandDefinition
        {
            Name = "/去物体",
            Params = new List<string> { "object" },
            Tool = "QW21edit",
            Template = "Remove the {object} from <image1>. Fill the removed area naturally to match the surrounding context. Keep all other content unchanged.",
            Description = "移除指定物体",
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
            FixedResolution = new ResolutionPolicy
            {
                Mode = ResolutionMode.Explicit,
                Width = 2048,
                Height = 1024,
            },
            Template = "Generate a 180-degree equirectangular panorama (front hemisphere only, 2:1 aspect ratio) from the input perspective image. Use a true equirectangular projection covering 180 degrees horizontal and 180 degrees vertical. The left and right edges should be at the 90-degree-left and 90-degree-right extremes of the front hemisphere. Keep the scene, style, lighting and all content continuous and consistent with the original image.",
            Description = "将当前图转为 180° 半全景",
        },
    };
}
