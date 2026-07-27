# TOKLONG Logo and Startup Motion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every current TOKLONG brand mark with the approved Transaction Rail identity and play its accessible 1.2-second logo-build animation once per cold mobile-app launch.

**Architecture:** Keep brand geometry in small SVG assets and animate three rasterized-at-build MAUI image layers, avoiding a new runtime dependency. A testable `StartupCoordinator` runs session lookup concurrently with the optional animation, then `App` swaps the temporary startup page for the existing `AppShell`; the startup page never enters Shell history.

**Tech Stack:** .NET 10, .NET MAUI 10, C# 14, XAML, SVG, xUnit, existing HTML/CSS landing page.

## Global Constraints

- Read and preserve the approved design in `docs/superpowers/specs/2026-07-27-toklong-logo-and-startup-motion-design.md`.
- The completed mark uses Brand Blue `#2B7FFF`, Sky Blue `#73C8FF`, Mint `#65D6BF`, and Ink `#122A47`; the startup background is `#F6FAFF`.
- Do not use a baht sign, coin, banknote, wallet, safe, lock, shield, arrowhead, or copy that implies TOKLONG holds money.
- The in-app motion is exactly 1,200 ms: arrival 250 ms, connection 400 ms, confirmation 200 ms, and wordmark entrance 350 ms.
- Play motion once per cold launch, never on foreground resume, never in a loop, and never with sound, haptics, spin, bounce, or particles.
- Reduced Motion shows the completed static mark immediately and adds no animation delay.
- Preserve existing authentication truth, deep-link authorization, push behavior, and transaction/domain states.
- Use .NET 10 `TranslateToAsync`, `ScaleToAsync`, and `FadeToAsync`; do not use their deprecated non-`Async` names.
- Use `CancelAnimations()` when the startup view is removed because .NET MAUI 10 animation methods do not accept a cancellation token.
- Do not add GIF, video, WebView, Lottie, or another package.
- Existing worktree changes belong to the user. Before any commit, inspect `git diff --cached --name-only`; if a task touches a path that was already modified or untracked before this work, leave the task uncommitted rather than capturing user-owned changes.
- Source references: [MAUI shapes](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/shapes/?view=net-maui-10.0), [MAUI 10 async animation APIs](https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-10?view=net-maui-10.0), and [animation cancellation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.controls.viewextensions.cancelanimations?view=net-maui-10.0).

## File map

### New files

- `src/Toklong.Mobile/Core/StartupCoordinator.cs` — testable one-shot startup/session coordination.
- `src/Toklong.Mobile/Services/StartupMotionPreference.cs` — iOS, Mac Catalyst, and Android reduced-motion adapter.
- `src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml` — layered mark and wordmark.
- `src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml.cs` — exact four-beat animation and cancellation.
- `src/Toklong.Mobile/Pages/StartupLogoPage.xaml` — non-interactive startup surface.
- `src/Toklong.Mobile/Pages/StartupLogoPage.xaml.cs` — exposes play, final-state, and cancel operations.
- `src/Toklong.Mobile/Resources/Images/brand_rail_upper.svg` — full-canvas upper animation rail.
- `src/Toklong.Mobile/Resources/Images/brand_rail_lower.svg` — full-canvas lower animation rail.
- `src/Toklong.Mobile/Resources/Images/brand_confirmation_node.svg` — full-canvas Mint node.
- `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs` — route, reduced-motion, failure, and one-shot tests.
- `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs` — cross-surface mark contract.

### Modified files

- `src/Toklong.Mobile/Resources/Images/brand_mark.svg` — completed reversed Transaction Rail mark.
- `src/Toklong.Mobile/Resources/AppIcon/appicon.svg` — approved blue app-icon background.
- `src/Toklong.Mobile/Resources/AppIcon/appiconfg.svg` — completed app-icon rail foreground.
- `src/Toklong.Mobile/Resources/Splash/splash.svg` — separated initial animation frame.
- `src/Toklong.Mobile/Resources/Images/ui_ai_assist.svg` — compact rail mark inside scan corners.
- `src/Toklong.Mobile/Controls/BrandLockupView.xaml` — updated lockup sizing and semantics.
- `src/Toklong.Mobile/App.xaml.cs` — temporary startup root, Shell swap, and post-startup services.
- `src/Toklong.Mobile/MauiProgram.cs` — startup DI registrations.
- `landing.html` — header/footer Transaction Rail mark.
- `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj` — copies control and brand assets for tests.
- `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs` — startup accessibility and Shell-history assertions.
- `docs/02_UI_UX_AND_CONTENT_SPEC.md` — replaces the old `T` logo rule and records startup motion.
- `docs/05_ACCEPTANCE_TESTS.md` — adds logo/motion acceptance criteria.

---

### Task 1: Lock the Transaction Rail identity across brand surfaces

**Files:**
- Create: `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `src/Toklong.Mobile/Resources/Images/brand_mark.svg`
- Modify: `src/Toklong.Mobile/Resources/AppIcon/appicon.svg`
- Modify: `src/Toklong.Mobile/Resources/AppIcon/appiconfg.svg`
- Modify: `src/Toklong.Mobile/Resources/Splash/splash.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/ui_ai_assist.svg`
- Modify: `src/Toklong.Mobile/Controls/BrandLockupView.xaml`
- Modify: `landing.html`

**Interfaces:**
- Consumes: approved Transaction Rail geometry from the design spec.
- Produces: SVG groups identified by `id="transaction-rail"` and landing marks identified by `data-logo-mark="transaction-rail"`.

- [ ] **Step 1: Copy brand surfaces into the mobile test output**

Add this `ItemGroup` to `Toklong.Mobile.Core.Tests.csproj`:

```xml
<ItemGroup>
  <None Include="../../src/Toklong.Mobile/Resources/Images/brand_mark.svg"
        Link="Brand/brand_mark.svg"
        CopyToOutputDirectory="PreserveNewest" />
  <None Include="../../src/Toklong.Mobile/Resources/AppIcon/appicon.svg"
        Link="Brand/appicon.svg"
        CopyToOutputDirectory="PreserveNewest" />
  <None Include="../../src/Toklong.Mobile/Resources/AppIcon/appiconfg.svg"
        Link="Brand/appiconfg.svg"
        CopyToOutputDirectory="PreserveNewest" />
  <None Include="../../src/Toklong.Mobile/Resources/Splash/splash.svg"
        Link="Brand/splash.svg"
        CopyToOutputDirectory="PreserveNewest" />
  <None Include="../../src/Toklong.Mobile/Resources/Images/ui_ai_assist.svg"
        Link="Brand/ui_ai_assist.svg"
        CopyToOutputDirectory="PreserveNewest" />
  <None Include="../../landing.html"
        Link="Brand/landing.html"
        CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing brand consistency tests**

Create `BrandAssetConsistencyTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Toklong.Mobile.Core.Tests;

public sealed class BrandAssetConsistencyTests
{
    [Theory]
    [InlineData("brand_mark.svg")]
    [InlineData("appiconfg.svg")]
    [InlineData("splash.svg")]
    [InlineData("ui_ai_assist.svg")]
    public void BrandAsset_UsesTransactionRail(string fileName)
    {
        var asset = Read(fileName);

        Assert.Contains("transaction-rail", asset);
        Assert.DoesNotContain("M176 209h160", asset);
        Assert.DoesNotContain("M36 47h56", asset);
    }

    [Fact]
    public void BrandPalette_RemainsApproved()
    {
        Assert.Contains("#2B7FFF", Read("appicon.svg"));
        Assert.Contains("#65D6BF", Read("appiconfg.svg"));
        Assert.Contains("#F6FAFF", Read("splash.svg"));
    }

    [Fact]
    public void LandingHeaderAndFooter_UseTheSameMark()
    {
        var landing = Read("landing.html");

        Assert.Equal(
            2,
            Regex.Matches(
                landing,
                "data-logo-mark=\"transaction-rail\"").Count);
        Assert.DoesNotContain(
            "M6.5 7.5h11v9h-11z",
            landing);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Brand",
                fileName));
}
```

- [ ] **Step 3: Run the tests and confirm the old assets fail**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~BrandAssetConsistencyTests
```

Expected: failures because the current `T`, check-box, and splash marks have no `transaction-rail` identifier.

- [ ] **Step 4: Replace the static SVG geometry**

Replace `brand_mark.svg` with:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 82 82">
  <g id="transaction-rail" fill="none">
    <path d="M13 27c0-7.7 6.3-14 14-14h28c7.7 0 14 6.3 14 14s-6.3 14-14 14H41"
          stroke="#FFFFFF" stroke-width="12" stroke-linecap="round"/>
    <path d="M69 55c0 7.7-6.3 14-14 14H27c-7.7 0-14-6.3-14-14s6.3-14 14-14h14"
          stroke="#BFE2FF" stroke-width="12" stroke-linecap="round"/>
    <circle cx="41" cy="41" r="9" fill="#65D6BF"
            stroke="#FFFFFF" stroke-width="4"/>
  </g>
</svg>
```

Keep `appicon.svg` as the approved background:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <rect width="512" height="512" rx="116" fill="#2B7FFF"/>
</svg>
```

Replace `appiconfg.svg` with the same completed reversed mark scaled to the
512×512 foreground canvas:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <g id="transaction-rail" fill="none"
     transform="translate(51 51) scale(5)">
    <path d="M13 27c0-7.7 6.3-14 14-14h28c7.7 0 14 6.3 14 14s-6.3 14-14 14H41"
          stroke="#FFFFFF" stroke-width="12" stroke-linecap="round"/>
    <path d="M69 55c0 7.7-6.3 14-14 14H27c-7.7 0-14-6.3-14-14s6.3-14 14-14h14"
          stroke="#BFE2FF" stroke-width="12" stroke-linecap="round"/>
    <circle cx="41" cy="41" r="9" fill="#65D6BF"
            stroke="#FFFFFF" stroke-width="4"/>
  </g>
</svg>
```

Replace `splash.svg` with the separated initial frame:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
  <rect width="128" height="128" fill="#F6FAFF"/>
  <g id="transaction-rail" fill="none"
     transform="translate(15 15) scale(1.2)">
    <path transform="translate(-16 0)"
          d="M13 27c0-7.7 6.3-14 14-14h28c7.7 0 14 6.3 14 14s-6.3 14-14 14H41"
          stroke="#2B7FFF" stroke-width="12" stroke-linecap="round"/>
    <path transform="translate(16 0)"
          d="M69 55c0 7.7-6.3 14-14 14H27c-7.7 0-14-6.3-14-14s6.3-14 14-14h14"
          stroke="#73C8FF" stroke-width="12" stroke-linecap="round"/>
  </g>
</svg>
```

Replace `ui_ai_assist.svg` with:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <path d="M10 23V15a5 5 0 0 1 5-5h8M41 10h8a5 5 0 0 1 5 5v8M54 41v8a5 5 0 0 1-5 5h-8M23 54h-8a5 5 0 0 1-5-5v-8"
        fill="none" stroke="#145FC7" stroke-width="4.5"
        stroke-linecap="round"/>
  <g id="transaction-rail" fill="none"
     transform="translate(9 9) scale(.56)">
    <path d="M13 27c0-7.7 6.3-14 14-14h28c7.7 0 14 6.3 14 14s-6.3 14-14 14H41"
          stroke="#145FC7" stroke-width="12" stroke-linecap="round"/>
    <path d="M69 55c0 7.7-6.3 14-14 14H27c-7.7 0-14-6.3-14-14s6.3-14 14-14h14"
          stroke="#73C8FF" stroke-width="12" stroke-linecap="round"/>
    <circle cx="41" cy="41" r="9" fill="#65D6BF"
            stroke="#FFFFFF" stroke-width="4"/>
  </g>
</svg>
```

- [ ] **Step 5: Update the horizontal lockups**

Keep `BrandLockupView.xaml` on its existing gradient tile, continue using
`brand_mark.png`, and retain exactly one
`SemanticProperties.Description="โลโก้ TOKLONG"`.

Replace the landing header and footer inline check-box SVG with this exact
completed mark and keep the existing `TOKLONG` wordmark and 36 px tile:

```html
<svg data-logo-mark="transaction-rail"
     width="25" height="25" viewBox="0 0 82 82"
     fill="none" aria-hidden="true">
  <path d="M13 27c0-7.7 6.3-14 14-14h28c7.7 0 14 6.3 14 14s-6.3 14-14 14H41"
        stroke="#fff" stroke-width="12" stroke-linecap="round"/>
  <path d="M69 55c0 7.7-6.3 14-14 14H27c-7.7 0-14-6.3-14-14s6.3-14 14-14h14"
        stroke="#BFE2FF" stroke-width="12" stroke-linecap="round"/>
  <circle cx="41" cy="41" r="9" fill="#65D6BF"
          stroke="#fff" stroke-width="4"/>
</svg>
```

- [ ] **Step 6: Run the focused asset and UI tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~BrandAssetConsistencyTests|FullyQualifiedName~AuthenticationStartsAtWelcome"
```

Expected: all selected tests pass.

- [ ] **Step 7: Record a safe task checkpoint**

Run:

```bash
git diff --check
git status --short
git diff --cached --name-only
```

Commit only if the staged list contains no pre-existing user-owned file:

```bash
git commit -m "feat: unify Toklong transaction rail branding"
```

Otherwise leave this task uncommitted and list its paths in the completion
report.

---

### Task 2: Build the one-shot startup coordinator with TDD

**Files:**
- Create: `src/Toklong.Mobile/Core/StartupCoordinator.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationService.HasSessionAsync()`.
- Produces:
  - `IStartupMotionPreference.IsReducedMotionEnabled`.
  - `StartupResult(string Route, Exception? SessionError)`.
  - `StartupCoordinator.StartAsync(Func<CancellationToken, Task>, CancellationToken)`.

- [ ] **Step 1: Write the failing coordinator tests**

Create `StartupCoordinatorTests.cs` with these cases:

```csharp
namespace Toklong.Mobile.Core.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WithSession_PlaysMotionAndRoutesToTransactions()
    {
        var authentication = new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new MotionPreferenceStub(false));
        var plays = 0;

        var result = await coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });

        Assert.Equal("//transactions", result.Route);
        Assert.Null(result.SessionError);
        Assert.Equal(1, plays);
    }

    [Fact]
    public async Task StartAsync_WithReducedMotion_SkipsMotion()
    {
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(() => Task.FromResult(false)),
            new MotionPreferenceStub(true));

        var result = await coordinator.StartAsync(
            _ => throw new InvalidOperationException("must not run"));

        Assert.Equal("//welcome", result.Route);
        Assert.Null(result.SessionError);
    }

    [Fact]
    public async Task StartAsync_WhenSessionLookupFails_FallsBackToWelcome()
    {
        var failure = new InvalidOperationException("secure store failed");
        var coordinator = new StartupCoordinator(
            new AuthenticationStub(() => Task.FromException<bool>(failure)),
            new MotionPreferenceStub(false));

        var result = await coordinator.StartAsync(_ => Task.CompletedTask);

        Assert.Equal("//welcome", result.Route);
        Assert.Same(failure, result.SessionError);
    }

    [Fact]
    public async Task StartAsync_ResolvesSessionWhileMotionIsStillPlaying()
    {
        var animationGate =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication =
            new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new MotionPreferenceStub(false));

        var startup = coordinator.StartAsync(
            _ => animationGate.Task);

        Assert.Equal(1, authentication.SessionChecks);
        Assert.False(startup.IsCompleted);
        animationGate.SetResult();
        Assert.Equal(
            "//transactions",
            (await startup).Route);
    }

    [Fact]
    public async Task StartAsync_WhenCalledTwice_ReusesOneStartupTask()
    {
        var authentication = new AuthenticationStub(() => Task.FromResult(true));
        var coordinator = new StartupCoordinator(
            authentication,
            new MotionPreferenceStub(false));
        var plays = 0;

        var first = coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });
        var second = coordinator.StartAsync(_ =>
        {
            plays++;
            return Task.CompletedTask;
        });

        Assert.Same(first, second);
        await Task.WhenAll(first, second);
        Assert.Equal(1, authentication.SessionChecks);
        Assert.Equal(1, plays);
    }

    private sealed class MotionPreferenceStub(bool reduced)
        : IStartupMotionPreference
    {
        public bool IsReducedMotionEnabled { get; } = reduced;
    }

    private sealed class AuthenticationStub(
        Func<Task<bool>> hasSession)
        : IAuthenticationService
    {
        public int SessionChecks { get; private set; }

        public Task<bool> HasSessionAsync()
        {
            SessionChecks++;
            return hasSession();
        }

        public Task<OtpChallengeResult> RequestCodeAsync(
            string phoneNumber,
            AuthenticationMode mode,
            string? fullName,
            string? email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task VerifyCodeAsync(
            string challengeId,
            string code,
            AuthenticationMode mode,
            string? fullName,
            string? email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MobileProfile> GetProfileAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> UpdateEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SignOutAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run the tests and verify missing types fail**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~StartupCoordinatorTests
```

Expected: compile failure for missing `StartupCoordinator`,
`StartupResult`, and `IStartupMotionPreference`.

- [ ] **Step 3: Implement the minimal coordinator**

Create `Core/StartupCoordinator.cs`:

```csharp
namespace Toklong.Mobile.Core;

public interface IStartupMotionPreference
{
    bool IsReducedMotionEnabled { get; }
}

public sealed record StartupResult(
    string Route,
    Exception? SessionError);

public sealed class StartupCoordinator(
    IAuthenticationService authentication,
    IStartupMotionPreference motionPreference)
{
    private readonly object gate = new();
    private Task<StartupResult>? startupTask;

    public Task<StartupResult> StartAsync(
        Func<CancellationToken, Task> playAnimationAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playAnimationAsync);
        lock (gate)
        {
            return startupTask ??= RunAsync(
                playAnimationAsync,
                cancellationToken);
        }
    }

    private async Task<StartupResult> RunAsync(
        Func<CancellationToken, Task> playAnimationAsync,
        CancellationToken cancellationToken)
    {
        var sessionTask = ResolveSessionAsync();
        if (!motionPreference.IsReducedMotionEnabled)
        {
            await Task.WhenAll(
                sessionTask,
                playAnimationAsync(cancellationToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionTask;
        return new StartupResult(
            session.HasSession ? "//transactions" : "//welcome",
            session.Error);
    }

    private async Task<(bool HasSession, Exception? Error)>
        ResolveSessionAsync()
    {
        try
        {
            return (await authentication.HasSessionAsync(), null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }
}
```

- [ ] **Step 4: Run the coordinator tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~StartupCoordinatorTests
```

Expected: five tests pass.

- [ ] **Step 5: Run all mobile core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: zero failures.

- [ ] **Step 6: Record a safe task checkpoint**

Inspect staged paths before any commit. If only the two new task-owned files are
staged, commit:

```bash
git commit -m "feat: coordinate accessible mobile startup"
```

Otherwise leave the task uncommitted and continue with a clean staged index.

---

### Task 3: Render and animate the Transaction Rail startup page

**Files:**
- Create: `src/Toklong.Mobile/Resources/Images/brand_rail_upper.svg`
- Create: `src/Toklong.Mobile/Resources/Images/brand_rail_lower.svg`
- Create: `src/Toklong.Mobile/Resources/Images/brand_confirmation_node.svg`
- Create: `src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml`
- Create: `src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml.cs`
- Create: `src/Toklong.Mobile/Pages/StartupLogoPage.xaml`
- Create: `src/Toklong.Mobile/Pages/StartupLogoPage.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes: `IStartupMotionPreference.IsReducedMotionEnabled`.
- Produces:
  - `TransactionRailMarkView.PlayAsync(CancellationToken)`.
  - `TransactionRailMarkView.ShowInitialState()`.
  - `TransactionRailMarkView.ShowCompletedState()`.
  - `TransactionRailMarkView.CancelMotion()`.
  - matching delegating methods on `StartupLogoPage`.

- [ ] **Step 1: Make the new XAML controls visible to tests**

Add to the test project:

```xml
<None Include="../../src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml"
      Link="Ui/Controls/TransactionRailMarkView.xaml"
      CopyToOutputDirectory="PreserveNewest" />
```

`Pages/*.xaml` already includes `StartupLogoPage.xaml`.

- [ ] **Step 2: Write failing startup presentation tests**

Add to `UiLayoutConsistencyTests.cs`:

```csharp
[Fact]
public void StartupLogo_IsNonInteractiveAndHasOneAccessibleName()
{
    var page = Load("Ui", "Pages", "StartupLogoPage.xaml");
    var mark = Load(
        "Ui",
        "Controls",
        "TransactionRailMarkView.xaml");

    Assert.Empty(page.Descendants(Maui + "Button"));
    Assert.Empty(page.Descendants(Maui + "Entry"));
    Assert.Contains(
        page.Descendants(),
        element =>
            element.Name.LocalName == "TransactionRailMarkView");

    Assert.Equal(
        "True",
        AttributeValue(
            mark.Root!,
            "AutomationProperties.IsInAccessibleTree"));
    Assert.Equal(
        "โลโก้ TOKLONG",
        AttributeValue(
            mark.Root!,
            "SemanticProperties.Description"));
    var decorativeChildren = mark
        .Descendants()
        .Where(element =>
            element.Name.LocalName is "Image" or "Border" or "Label");
    Assert.All(
        decorativeChildren,
        element => Assert.Equal(
            "False",
            AttributeValue(
                element,
                "AutomationProperties.IsInAccessibleTree")));
}

[Fact]
public void StartupLogo_IsNotPartOfShellHistory()
{
    var shell = Load("Ui", "AppShell.xaml");

    Assert.DoesNotContain(
        shell.Descendants(),
        element =>
            AttributeValue(element, "Route") == "startup");
    Assert.Equal(
        "welcome",
        AttributeValue(
            shell.Descendants()
                .First(element =>
                    element.Name.LocalName == "ShellContent"),
            "Route"));
}
```

- [ ] **Step 3: Run the focused UI tests and verify they fail**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~StartupLogo"
```

Expected: failure because the startup page and mark control do not exist.

- [ ] **Step 4: Add the three full-canvas animation SVG layers**

Each file uses `viewBox="0 0 82 82"` so the layers remain aligned.

Create `brand_rail_upper.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 82 82">
  <path id="transaction-rail-upper"
        d="M13 27c0-7.7 6.3-14 14-14h28c7.7 0 14 6.3 14 14s-6.3 14-14 14H41"
        fill="none" stroke="#2B7FFF" stroke-width="12"
        stroke-linecap="round"/>
</svg>
```

Create `brand_rail_lower.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 82 82">
  <path id="transaction-rail-lower"
        d="M69 55c0 7.7-6.3 14-14 14H27c-7.7 0-14-6.3-14-14s6.3-14 14-14h14"
        fill="none" stroke="#73C8FF" stroke-width="12"
        stroke-linecap="round"/>
</svg>
```

Create `brand_confirmation_node.svg`:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 82 82">
  <circle id="transaction-rail-confirmation"
          cx="41" cy="41" r="9"
          fill="#65D6BF" stroke="#FFFFFF" stroke-width="4"/>
</svg>
```

- [ ] **Step 5: Build the accessible layered mark**

Create `TransactionRailMarkView.xaml` with:

```xml
<ContentView
    x:Class="Toklong.Mobile.Controls.TransactionRailMarkView"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    x:Name="Root"
    AutomationProperties.IsInAccessibleTree="True"
    SemanticProperties.Description="โลโก้ TOKLONG">
    <Grid
        ColumnDefinitions="110,Auto"
        ColumnSpacing="16">
        <Grid
            WidthRequest="110"
            HeightRequest="110">
            <Image
                x:Name="UpperRail"
                AutomationProperties.IsInAccessibleTree="False"
                Source="brand_rail_upper.png" />
            <Image
                x:Name="LowerRail"
                AutomationProperties.IsInAccessibleTree="False"
                Source="brand_rail_lower.png" />
            <Border
                x:Name="ConfirmationPulse"
                WidthRequest="26"
                HeightRequest="26"
                HorizontalOptions="Center"
                VerticalOptions="Center"
                AutomationProperties.IsInAccessibleTree="False"
                BackgroundColor="Transparent"
                Stroke="#65D6BF"
                StrokeThickness="2">
                <Border.StrokeShape>
                    <Ellipse />
                </Border.StrokeShape>
            </Border>
            <Image
                x:Name="ConfirmationNode"
                AutomationProperties.IsInAccessibleTree="False"
                Source="brand_confirmation_node.png" />
        </Grid>
        <Label
            x:Name="Wordmark"
            Grid.Column="1"
            VerticalOptions="Center"
            AutomationProperties.IsInAccessibleTree="False"
            FontAttributes="Bold"
            FontFamily="NotoSansThai"
            FontSize="30"
            CharacterSpacing="-0.4"
            Text="TOKLONG"
            TextColor="#122A47" />
    </Grid>
</ContentView>
```

- [ ] **Step 6: Implement the exact 1.2-second motion**

Create `TransactionRailMarkView.xaml.cs`. Use these constants and sequence:

```csharp
namespace Toklong.Mobile.Controls;

public partial class TransactionRailMarkView : ContentView
{
    private const uint ArrivalMilliseconds = 250;
    private const uint ConnectionMilliseconds = 400;
    private const uint ConfirmationMilliseconds = 200;
    private const uint WordmarkMilliseconds = 350;

    public TransactionRailMarkView()
    {
        InitializeComponent();
        ShowInitialState();
    }

    public void ShowInitialState()
    {
        CancelMotion();
        UpperRail.TranslationX = -22;
        LowerRail.TranslationX = 22;
        ConfirmationNode.Opacity = 0;
        ConfirmationNode.Scale = 0.2;
        ConfirmationPulse.Opacity = 0;
        ConfirmationPulse.Scale = 0.65;
        Wordmark.Opacity = 0;
        Wordmark.TranslationX = -7;
    }

    public void ShowCompletedState()
    {
        CancelMotion();
        UpperRail.TranslationX = 0;
        LowerRail.TranslationX = 0;
        ConfirmationNode.Opacity = 1;
        ConfirmationNode.Scale = 1;
        ConfirmationPulse.Opacity = 0;
        ConfirmationPulse.Scale = 1.8;
        Wordmark.Opacity = 1;
        Wordmark.TranslationX = 0;
    }

    public async Task PlayAsync(
        CancellationToken cancellationToken = default)
    {
        ShowInitialState();
        using var registration =
            cancellationToken.Register(CancelMotion);

        await Task.WhenAll(
            UpperRail.TranslateToAsync(
                -10,
                0,
                ArrivalMilliseconds,
                Easing.CubicOut),
            LowerRail.TranslateToAsync(
                10,
                0,
                ArrivalMilliseconds,
                Easing.CubicOut));
        cancellationToken.ThrowIfCancellationRequested();

        await Task.WhenAll(
            UpperRail.TranslateToAsync(
                0,
                0,
                ConnectionMilliseconds,
                Easing.SinInOut),
            LowerRail.TranslateToAsync(
                0,
                0,
                ConnectionMilliseconds,
                Easing.SinInOut));
        cancellationToken.ThrowIfCancellationRequested();

        ConfirmationNode.Opacity = 1;
        ConfirmationPulse.Opacity = 0.38;
        await Task.WhenAll(
            ConfirmationNode.ScaleToAsync(
                1,
                ConfirmationMilliseconds,
                Easing.CubicOut),
            ConfirmationPulse.ScaleToAsync(
                1.8,
                ConfirmationMilliseconds,
                Easing.CubicOut),
            ConfirmationPulse.FadeToAsync(
                0,
                ConfirmationMilliseconds,
                Easing.CubicOut));
        cancellationToken.ThrowIfCancellationRequested();

        await Task.WhenAll(
            Wordmark.TranslateToAsync(
                0,
                0,
                WordmarkMilliseconds,
                Easing.CubicOut),
            Wordmark.FadeToAsync(
                1,
                WordmarkMilliseconds,
                Easing.CubicOut));
    }

    public void CancelMotion()
    {
        UpperRail.CancelAnimations();
        LowerRail.CancelAnimations();
        ConfirmationNode.CancelAnimations();
        ConfirmationPulse.CancelAnimations();
        Wordmark.CancelAnimations();
    }
}
```

- [ ] **Step 7: Add the startup page and reduced-motion initial state**

Create `StartupLogoPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="Toklong.Mobile.Pages.StartupLogoPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:controls="clr-namespace:Toklong.Mobile.Controls"
    Shell.NavBarIsVisible="False"
    Shell.TabBarIsVisible="False">
    <ContentPage.Background>
        <RadialGradientBrush Center="0.5,0.42" Radius="0.88">
            <GradientStop Color="#FFFFFF" Offset="0" />
            <GradientStop Color="#F6FAFF" Offset="1" />
        </RadialGradientBrush>
    </ContentPage.Background>
    <Grid Padding="28">
        <controls:TransactionRailMarkView
            x:Name="Mark"
            HorizontalOptions="Center"
            VerticalOptions="Center" />
    </Grid>
</ContentPage>
```

Create `StartupLogoPage.xaml.cs`:

```csharp
using Toklong.Mobile.Controls;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Pages;

public partial class StartupLogoPage : ContentPage
{
    public StartupLogoPage(
        IStartupMotionPreference motionPreference)
    {
        InitializeComponent();
        if (motionPreference.IsReducedMotionEnabled)
            Mark.ShowCompletedState();
        else
            Mark.ShowInitialState();
    }

    public Task PlayAsync(
        CancellationToken cancellationToken = default) =>
        Mark.PlayAsync(cancellationToken);

    public void ShowCompletedState() =>
        Mark.ShowCompletedState();

    public void CancelMotion() =>
        Mark.CancelMotion();
}
```

- [ ] **Step 8: Run UI tests and compile the mobile project**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~StartupLogo"
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -r iossimulator-arm64
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -c Release -f net10.0-ios -r iossimulator-arm64
```

Expected: startup UI tests pass and both simulator configurations build without
a C# or XAML error. The Release build verifies the native splash resources that
the current project intentionally excludes from Debug iOS simulator builds.

- [ ] **Step 9: Record a safe task checkpoint**

Run `git diff --check`, inspect only the task paths, and commit only if doing so
does not capture pre-existing mobile work:

```bash
git commit -m "feat: animate the Toklong transaction rail"
```

Otherwise leave the task uncommitted.

---

### Task 4: Integrate platform motion preferences and startup routing

**Files:**
- Create: `src/Toklong.Mobile/Services/StartupMotionPreference.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `src/Toklong.Mobile/App.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`

**Interfaces:**
- Consumes:
  - `StartupCoordinator.StartAsync(...)`.
  - `StartupLogoPage.PlayAsync(...)`.
  - existing `AppShell`, `IPushRegistrationService`, and
    `IDeepLinkCoordinator`.
- Produces: one startup Shell installation and an authenticated or welcome
  initial route.

- [ ] **Step 1: Add a failing DI/source contract test**

Copy `MauiProgram.cs` and `App.xaml.cs` into test output:

```xml
<None Include="../../src/Toklong.Mobile/MauiProgram.cs"
      Link="Ui/MauiProgram.cs"
      CopyToOutputDirectory="PreserveNewest" />
<None Include="../../src/Toklong.Mobile/App.xaml.cs"
      Link="Ui/App.xaml.cs"
      CopyToOutputDirectory="PreserveNewest" />
```

Add to `UiLayoutConsistencyTests.cs`:

```csharp
[Fact]
public void StartupServices_AreRegisteredAndShellIsInstalledAfterIntro()
{
    var program = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "MauiProgram.cs"));
    var app = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "Ui",
            "App.xaml.cs"));

    Assert.Matches(
        @"AddSingleton<\s*IStartupMotionPreference,\s*StartupMotionPreference>\(\)",
        program);
    Assert.Contains("AddSingleton<StartupCoordinator>()", program);
    Assert.Contains("AddSingleton<StartupLogoPage>()", program);
    Assert.Contains("new Window(startupPage)", app);
    Assert.Contains("window.Page = shell", app);
}
```

- [ ] **Step 2: Run the test and verify the missing integration fails**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~StartupServices
```

Expected: test fails because the services and temporary root are absent.

- [ ] **Step 3: Implement platform reduced-motion detection**

Create `Services/StartupMotionPreference.cs`:

```csharp
using Microsoft.Maui.Controls;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class StartupMotionPreference
    : IStartupMotionPreference
{
    public bool IsReducedMotionEnabled
    {
        get
        {
            if (!Animation.IsEnabled)
                return true;
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            var resolver =
                Android.App.Application.Context.ContentResolver;
            var scale = Android.Provider.Settings.Global.GetFloat(
                resolver,
                Android.Provider.Settings.Global.AnimatorDurationScale,
                1f);
            return scale == 0f;
#else
            return false;
#endif
        }
    }
}
```

- [ ] **Step 4: Register startup services**

Add these registrations in `MauiProgram.CreateMauiApp()`:

```csharp
builder.Services.AddSingleton<
    IStartupMotionPreference,
    StartupMotionPreference>();
builder.Services.AddSingleton<StartupCoordinator>();
builder.Services.AddSingleton<StartupLogoPage>();
```

Keep the existing `AppShell` singleton and all current service registrations.

- [ ] **Step 5: Replace the immediate Shell root with the startup page**

Add these usings and replace the existing `App` fields/constructor:

```csharp
using Microsoft.Extensions.Logging;
using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile;

public partial class App : Application
{
    private readonly AppShell shell;
    private readonly IDeepLinkCoordinator deepLinks;
    private readonly IPushRegistrationService pushRegistration;
    private readonly StartupLogoPage startupPage;
    private readonly StartupCoordinator startupCoordinator;
    private readonly ILogger<App> logger;
    private readonly CancellationTokenSource startupCancellation = new();
    private int startupStarted;

    public App(
        AppShell shell,
        IDeepLinkCoordinator deepLinks,
        IPushRegistrationService pushRegistration,
        StartupLogoPage startupPage,
        StartupCoordinator startupCoordinator,
        ILogger<App> logger)
    {
        InitializeComponent();
        this.shell = shell;
        this.deepLinks = deepLinks;
        this.pushRegistration = pushRegistration;
        this.startupPage = startupPage;
        this.startupCoordinator = startupCoordinator;
        this.logger = logger;
    }
```

Remove the direct `IAuthenticationService` field from `App`; the coordinator
now owns the single startup session check.

Use this startup shape:

```csharp
protected override Window CreateWindow(
    IActivationState? activationState)
{
    var window = new Window(startupPage);
#if MACCATALYST
    window.Width = 440;
    window.Height = 790;
    window.X = 280;
    window.Y = 35;
#endif
    window.Created += async (_, _) =>
        await OpenInitialRouteAsync(window);
    window.Destroying += (_, _) =>
    {
        startupCancellation.Cancel();
        startupPage.CancelMotion();
    };
    return window;
}

private async Task OpenInitialRouteAsync(Window window)
{
    if (Interlocked.Exchange(ref startupStarted, 1) != 0)
        return;

    try
    {
        var result = await startupCoordinator.StartAsync(
            startupPage.PlayAsync,
            startupCancellation.Token);
        if (result.SessionError is not null)
        {
            logger.LogWarning(
                result.SessionError,
                "Mobile session lookup failed during startup.");
        }

        window.Page = shell;
        await shell.GoToAsync(result.Route, false);
        if (result.Route == "//transactions")
            _ = InitializeAuthenticatedServicesAsync();
    }
    catch (OperationCanceledException)
    {
        // Window destruction cancels startup without installing a second root.
    }
}

private async Task InitializeAuthenticatedServicesAsync()
{
    try
    {
        await pushRegistration.InitializeAsync();
    }
    catch (Exception exception)
    {
        logger.LogWarning(
            exception,
            "Push registration did not complete during startup.");
    }

    try
    {
        await deepLinks.ResumePendingAsync();
    }
    catch (Exception exception)
    {
        logger.LogWarning(
            exception,
            "Pending deep-link navigation did not complete during startup.");
    }
}
}
```

Do not add `StartupLogoPage` to `AppShell.xaml`; the root replacement is what
keeps the intro out of Back navigation.

- [ ] **Step 6: Run coordinator, UI, and startup integration tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter "FullyQualifiedName~Startup"
```

Expected: coordinator, accessibility, Shell-history, and DI/source tests pass.

- [ ] **Step 7: Build iOS simulator and Android targets**

Run:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-ios -r iossimulator-arm64
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -f net10.0-android
```

Expected: both builds complete without compile or XAML errors. Existing pinned
Stripe wrapper warnings may remain, but no new startup-motion warning is
accepted.

- [ ] **Step 8: Record a safe task checkpoint**

Run `git diff --check` and inspect staged paths. Commit only when no user-owned
baseline is captured:

```bash
git commit -m "feat: show logo motion on cold app launch"
```

Otherwise leave changes uncommitted.

---

### Task 5: Document behavior and complete end-to-end verification

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Test: `tests/Toklong.Mobile.Core.Tests`
- Test: all solution test projects

**Interfaces:**
- Consumes: completed brand assets, motion view, coordinator, and startup
  integration from Tasks 1–4.
- Produces: auditable product requirements and fresh verification evidence.

- [ ] **Step 1: Update the UI/content specification**

In `docs/02_UI_UX_AND_CONTENT_SPEC.md`:

- replace “TOKLONG `T` inside scan corners” with “TOKLONG Transaction Rail
  inside scan corners”;
- add a `Mobile startup brand motion` section containing the exact
  250/400/200/350 ms sequence;
- state that the OS launch screen is static and uses the separated first frame;
- state that Reduced Motion immediately shows the completed mark;
- state that cold launch plays once and foreground resume does not replay; and
- state that the Mint node is brand confirmation, not payment or payout
  confirmation.

- [ ] **Step 2: Add acceptance criteria**

Append `H2 — Mobile startup logo respects motion and routing` to
`docs/05_ACCEPTANCE_TESTS.md`:

```markdown
### H2 — Mobile startup logo respects motion and routing

**Given** the native mobile app starts from a cold launch with normal motion
**When** the static launch surface hands off to the app
**Then** the two Transaction Rail layers assemble, the Mint node confirms once,
and the TOKLONG wordmark enters in exactly 1.2 seconds
**And** authentication lookup occurs concurrently
**And** the intro is not placed in Shell history
**And** the animation does not replay on foreground resume.

**Given** the platform requests reduced motion
**When** the app starts
**Then** the completed static mark appears immediately
**And** no animation-duration delay is added
**And** the same authenticated or unauthenticated route is selected.

**Given** startup session lookup fails
**When** the animation or static reduced-motion presentation completes
**Then** the app opens the unauthenticated welcome route
**And** no credential, session content, payment state, or success claim is
displayed.
```

- [ ] **Step 3: Run formatting and focused tests**

Run:

```bash
git diff --check
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: no whitespace errors and zero mobile core test failures.

- [ ] **Step 4: Run the complete solution test suite**

Run:

```bash
dotnet test Toklong.slnx
```

Expected: zero failures. If unrelated pre-existing dirty-worktree failures
occur, capture their exact test names and output without weakening or deleting
the tests.

- [ ] **Step 5: Perform simulator visual verification**

Launch a supported iPhone simulator and verify:

1. cold signed-out launch: separated static rails continue inward without a
   flash, reverse movement, or loop;
2. animation duration: final wordmark settles at 1.2 seconds;
3. signed-in launch: destination is `รายการ` after the same animation;
4. background/foreground: animation does not replay;
5. Reduce Motion: completed mark appears immediately and routing is not delayed;
6. small sizes: app icon, 24 px AI-assist mark, header, footer, and auth lockup
   remain recognizable; and
7. VoiceOver: one `โลโก้ TOKLONG` element is announced and no rail/node child is
   announced.

Capture a screenshot of the final assembled startup frame and the rendered app
icon for the completion report:

```bash
xcrun simctl io booted screenshot /tmp/toklong-startup-final.png
```

Use the Simulator home screen for a separate app-icon screenshot. Do not add
generated screenshots to git unless the user asks.

- [ ] **Step 6: Run final source and secret checks**

Run:

```bash
rg -n 'M176 209h160|M36 47h56|M6\\.5 7\\.5h11v9h-11z' src/Toklong.Mobile landing.html
rg -n -i '(api[_-]?key|secret|password|private[_-]?key)\\s*[:=]\\s*[^< ]+' src/Toklong.Mobile/Resources
git status --short
```

Expected: no legacy-logo geometry in live brand surfaces, no committed secret
in changed assets/docs, and only understood user/task changes in status.

- [ ] **Step 7: Apply the completion gate**

Use `superpowers:requesting-code-review` for the completed implementation, fix
verified findings, then use `superpowers:verification-before-completion` and
rerun the relevant commands fresh before reporting success.

Report:

1. what changed;
2. that no transaction state transition changed;
3. tests added/updated and fresh results;
4. assumptions about cold launch and reduced motion;
5. any platform/provider blocker; and
6. the next smallest vertical slice.
