namespace Perigon.MiniDb.Client.Services;

public enum StatusTone
{
    Neutral,
    Success,
    Warning,
    Error
}

public sealed class StatusToneService
{
    public StatusTone Resolve(string message)
    {
        var lower = message.ToLowerInvariant();

        var isError = lower.Contains("失败")
                      || lower.Contains("错误")
                      || lower.Contains("invalid")
                      || lower.Contains("error")
                      || lower.Contains("不存在")
                      || lower.Contains("无效");

        if (isError)
        {
            return StatusTone.Error;
        }

        var isWarning = lower.Contains("警告")
                        || lower.Contains("warning")
                        || lower.Contains("锁定")
                        || lower.Contains("注意");

        if (isWarning)
        {
            return StatusTone.Warning;
        }

        var isSuccess = lower.Contains("已")
                        || lower.Contains("成功")
                        || lower.Contains("完成")
                        || lower.Contains("success")
                        || lower.Contains("connected")
                        || lower.Contains("disconnected")
                        || lower.Contains("loaded")
                        || lower.Contains("opened")
                        || lower.Contains("created")
                        || lower.Contains("updated");

        return isSuccess ? StatusTone.Success : StatusTone.Neutral;
    }
}
