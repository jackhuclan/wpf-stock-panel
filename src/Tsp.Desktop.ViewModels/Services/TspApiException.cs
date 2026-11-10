namespace Tsp.Desktop.ViewModels.Services;

/// <summary>
/// <see cref="ITspApiClient"/> 的失败信号：实现取数不成时一律抛本异常，
/// 由 <see cref="Exception.Message"/> 承载一句可直接展示给用户的中文说明。
/// 界面据此呈现错误，不需要认识背后是夹具还是网络——这正是 P2 换成
/// HTTP 实现时 View 与 ViewModel 不必改动的原因。
/// 刻意只有类型本身：没有错误码、严重级别或子类，因为没有调用方消费它们。
/// </summary>
public sealed class TspApiException : Exception
{
    /// <summary>
    /// 用一条面向用户的说明构造异常。
    /// </summary>
    /// <param name="message">可读的失败原因，例如缺失的目录路径或文件名。</param>
    public TspApiException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 用一条面向用户的说明与保留的原始异常构造。原始异常经 <see cref="Exception.InnerException"/>
    /// 传递，供排障使用——例如 <see cref="System.Text.Json.JsonException"/> 的结构化行列位置，
    /// 它比消息文本更能直接定位到出错的成员。
    /// </summary>
    /// <param name="message">可读的失败原因，例如缺失的目录路径或文件名。</param>
    /// <param name="innerException">触发本次失败的原始异常。</param>
    public TspApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
