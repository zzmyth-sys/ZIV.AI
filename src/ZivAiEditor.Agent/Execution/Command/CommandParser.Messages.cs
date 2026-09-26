using ZivAiEditor.Contracts.Execution;

namespace ZivAiEditor.Agent.Execution;

public sealed partial class CommandParser
{
    /// <summary>
    /// T5/S4-fix6: plain-language message for "the user forgot the argument". Built-in parameter
    /// commands answer with their own question; anything else falls back to a generic hint plus an
    /// example of the expected shape.
    /// </summary>
    private static string MissingArgsMessage(CommandDefinition command)
    {
        var paramList = string.Join(", ", command.Params);
        var example = command.Name switch
        {
            "/合照" => "/合照 两人在森林握手",
            "/换背景" => "/换背景 森林",
            "/换装" => "/换装 红色连衣裙",
            "/生成" => "/生成 森林里的精灵",
            "/提取" => "/提取 猫",
            "/去物体" => "/去物体 水印",
            _ => paramList.Length > 0 ? $"{command.Name} <{paramList}>" : command.Name,
        };

        var question = command.Name switch
        {
            "/提取" => "你要提取什么？",
            "/去物体" => "你要移除什么物体？",
            "/换背景" => "你要换成什么背景？",
            "/换装" => "你要换成什么服装？",
            "/换发色" => "你要换成什么发色？",
            "/换表情" => "你要换成什么表情？",
            "/换光线" => "你要换成什么光线？",
            "/合照" => "你想要什么样的合照？",
            "/生成" => "你想生成什么？",
            _ => null,
        };

        return question is null
            ? $"「{command.Name}」缺少参数（{paramList}）。例如：{example}"
            : $"「{command.Name}」{question}例如：{example}";
    }
}
