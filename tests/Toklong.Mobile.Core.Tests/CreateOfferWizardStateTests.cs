using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CreateOfferWizardStateTests
{
    [Fact]
    public void Starts_pristine_on_deal_step()
    {
        var wizard = new CreateOfferWizardState();

        Assert.Equal(CreateOfferStep.Deal, wizard.CurrentStep);
        Assert.False(wizard.IsDirty);
    }

    [Fact]
    public void Moves_forward_and_back_without_losing_dirty_state()
    {
        var wizard = new CreateOfferWizardState();
        wizard.MarkDirty();

        Assert.True(wizard.MoveNext());
        Assert.Equal(
            CreateOfferStep.Fulfillment,
            wizard.CurrentStep);
        Assert.True(wizard.MoveNext());
        Assert.Equal(CreateOfferStep.Review, wizard.CurrentStep);
        Assert.False(wizard.MoveNext());
        Assert.True(wizard.MoveBack());
        Assert.Equal(
            CreateOfferStep.Fulfillment,
            wizard.CurrentStep);
        Assert.True(wizard.IsDirty);
    }

    [Fact]
    public void Cannot_move_before_first_step()
    {
        var wizard = new CreateOfferWizardState();

        Assert.False(wizard.MoveBack());
        Assert.Equal(CreateOfferStep.Deal, wizard.CurrentStep);
    }

    [Fact]
    public void Reset_returns_to_pristine_first_step()
    {
        var wizard = new CreateOfferWizardState();
        wizard.MarkDirty();
        wizard.MoveNext();

        wizard.Reset();

        Assert.Equal(CreateOfferStep.Deal, wizard.CurrentStep);
        Assert.False(wizard.IsDirty);
    }

    [Fact]
    public void Exit_prompt_puts_safe_action_before_destructive_action()
    {
        Assert.Equal(
            "ยังสร้างข้อเสนอไม่เสร็จ",
            CreateOfferExitPrompt.Title);
        Assert.Equal(
            "ถ้าออกตอนนี้ ข้อมูลที่กรอกไว้จะหาย",
            CreateOfferExitPrompt.Message);
        Assert.Equal(
            "กลับไปกรอกต่อ",
            CreateOfferExitPrompt.KeepEditing);
        Assert.Equal(
            "ออกจากหน้านี้",
            CreateOfferExitPrompt.Discard);
    }
}
