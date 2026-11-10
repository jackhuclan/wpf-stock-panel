namespace Tsp.Desktop.ViewModels.Services;

/// <summary>
/// 夹具数据不可用（目录缺失、文件缺失、JSON 损坏）。消息是直接给用户看的整句中文，
/// 不含错误码或严重级别——调用方只需把它呈现出来。
/// </summary>
public sealed class FixtureDataException : Exception
{
    /// <summary>
    /// 用一条面向用户的说明构造异常。
    /// </summary>
    /// <param name="message">可读的失败原因，含目录路径或文件名。</param>
    public FixtureDataException(string message)
        : base(message)
    {
    }
}
