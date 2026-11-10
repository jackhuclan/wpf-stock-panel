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

    /// <summary>
    /// 用一条面向用户的说明与保留的原始异常构造。原始异常经 <see cref="Exception.InnerException"/>
    /// 传递，供排障使用——例如 <see cref="System.Text.Json.JsonException"/> 的结构化行列位置，
    /// 它比消息文本更能直接定位到出错的成员。
    /// </summary>
    /// <param name="message">可读的失败原因，含目录路径或文件名。</param>
    /// <param name="innerException">触发本次失败的原始异常。</param>
    public FixtureDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
