namespace Toklong.Mobile.Core.Tests;

public sealed class CompleteRegistrationNameTests
{
    [Fact]
    public void Complete_registration_keeps_first_and_last_name_as_distinct_inputs()
    {
        var xaml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Ui",
                "Pages",
                "CompleteRegistrationPage.xaml"));

        Assert.Contains("Text=\"{Binding FirstName}\"", xaml);
        Assert.Contains("Text=\"{Binding LastName}\"", xaml);
        Assert.DoesNotContain("Placeholder=\"ชื่อ นามสกุล\"", xaml);
    }
}
