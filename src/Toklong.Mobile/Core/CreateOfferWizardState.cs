namespace Toklong.Mobile.Core;

public enum CreateOfferStep
{
    Deal,
    Fulfillment,
    Review
}

public static class CreateOfferExitPrompt
{
    public const string Title =
        "ยังสร้างข้อเสนอไม่เสร็จ";
    public const string Message =
        "ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย";
    public const string KeepEditing =
        "กลับไปกรอกต่อ";
    public const string Discard =
        "ออกจากหน้านี้";
}

public sealed class CreateOfferWizardState
{
    public CreateOfferStep CurrentStep { get; private set; }
    public bool IsDirty { get; private set; }

    public void MarkDirty() => IsDirty = true;

    public bool MoveNext()
    {
        if (CurrentStep == CreateOfferStep.Review)
            return false;

        CurrentStep++;
        return true;
    }

    public bool MoveBack()
    {
        if (CurrentStep == CreateOfferStep.Deal)
            return false;

        CurrentStep--;
        return true;
    }

    public void Reset()
    {
        CurrentStep = CreateOfferStep.Deal;
        IsDirty = false;
    }
}
