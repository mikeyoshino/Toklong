namespace Toklong.Mobile.Core;

public enum CreateOfferStep
{
    Deal,
    Fulfillment,
    Review
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
