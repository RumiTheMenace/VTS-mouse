namespace RumiVtsController
{
    public enum RumiActionType
    {
        TriggerHotkey,
        Blink,
        WinkLeft,
        WinkRight,
        Smile,
        HalfSmile,
        EnterAfk,
        ExitAfk,
        StartDizzy,
        StopDizzy
    }

    public sealed record RumiAction(RumiActionType Type, string? Name = null);

    public interface IRumiActionSink
    {
        bool TryEnqueue(RumiAction action);
    }
}
