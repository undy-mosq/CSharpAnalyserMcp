namespace CSharpAnalyzerMcp.Tools;

/// <summary>
/// 服务级说明：在 initialize 握手时发送给客户端，客户端通常将其作为系统提示注入模型上下文。
/// </summary>
internal static class ServerInstructions
{
    /// <summary>
    /// kind 取值与 kinds 筛选语义说明。使用英文并保持精简，以控制每次会话的固定 token 开销。
    /// </summary>
    public const string Text = """
        This server reports the kind of every C# type in the `kind` field. Values are lowercase and mutually exclusive:
        class, static class, record, struct, record struct, interface, enum, delegate.
        Only classes can be static, so "class" and "static class" are distinct; struct/enum/interface/delegate have no static form.

        The `kinds` filter parameter is case-insensitive and accepts multiple values (omitted = ["class"]):
        ["class"] = every class (including static classes and records); ["static"] or ["static class"] = static classes only;
        ["struct"] = structs (including record structs); ["record"] / ["enum"] / ["interface"] / ["delegate"] = that kind;
        ["all"] = every type.
        """;
}
