using Microsoft.Playwright;

const string TenBisUrl = "https://www.10bis.co.il/next/";

/*
 * The saved 10bis session (cookies) is read from this file.
 * In GitHub Actions it is written from the TENBIS_STATE secret.
 */
string statePath =
    Environment.GetEnvironmentVariable("TENBIS_STATE_PATH")
    ?? Path.Combine("playwright-auth", "tenbis-state.json");

bool loginMode = args.Contains("--login");

string logFilePath = Path.Combine(
    AppContext.BaseDirectory,
    "TenBisAutoCredit.log");

bool resultWasWritten = false;

void WriteResult(string status, string message)
{
    if (resultWasWritten)
    {
        return;
    }

    resultWasWritten = true;

    // Keep every execution result on exactly one line.
    string cleanMessage = message
        .Replace("\r", " ")
        .Replace("\n", " ")
        .Trim();

    string logLine =
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | " +
        $"{Environment.UserName} | " +
        $"{status,-7} | " +
        $"{cleanMessage}";

    File.AppendAllText(
        logFilePath,
        logLine + Environment.NewLine);

    Console.WriteLine();
    Console.WriteLine(logLine);
}

try
{
    if (!loginMode && !File.Exists(statePath))
    {
        WriteResult(
            "ERROR",
            $"Session file was not found: {statePath}. Run with --login first.");

        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine("Opening 10bis...");

    using var playwright = await Playwright.CreateAsync();

    await using var browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions
        {
            // A visible browser is needed only for the manual login.
            Headless = !loginMode
        });

    await using var context = await browser.NewContextAsync(
        new BrowserNewContextOptions
        {
            StorageStatePath = loginMode ? null : statePath,
            Locale = "he-IL",
            TimezoneId = "Asia/Jerusalem"
        });

    var page = await context.NewPageAsync();

    await page.GotoAsync(
        TenBisUrl,
        new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });

    await page.WaitForTimeoutAsync(2000);

    var sideMenuButton = page.Locator(
        "[data-test-id=\"sideMenuBtn\"]");

    if (loginMode)
    {
        Console.WriteLine("Log in to 10bis in the opened browser (up to 5 minutes)...");

        await sideMenuButton.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5 * 60 * 1000
            });

        Directory.CreateDirectory(
            Path.GetDirectoryName(Path.GetFullPath(statePath))!);

        await context.StorageStateAsync(
            new BrowserContextStorageStateOptions
            {
                Path = statePath
            });

        Console.WriteLine($"Session was saved to {Path.GetFullPath(statePath)}");
        return;
    }

    /*
     * Login is detected according to the side-menu button.
     * In an automatic execution, the application must not wait
     * for manual login or Console.ReadLine().
     */
    try
    {
        await sideMenuButton.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10000
            });
    }
    catch (TimeoutException)
    {
        Console.WriteLine("Login was not detected.");

        WriteResult(
            "ERROR",
            "Login was not detected. The saved 10bis session may have expired.");

        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine("Login detected.");
    Console.WriteLine("Opening side menu...");

    await sideMenuButton.ClickAsync();

    var chargesReportLink = page.GetByRole(
        AriaRole.Link,
        new PageGetByRoleOptions
        {
            Name = "דו\"ח חיובים",
            Exact = true
        });

    await chargesReportLink.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    Console.WriteLine("Opening charges report...");

    await chargesReportLink.ClickAsync();

    await page.WaitForTimeoutAsync(2000);

    var openCreditDialogButton = page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions
        {
            Name = "הטענת תן ביס קרדיט",
            Exact = true
        });

    await openCreditDialogButton.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    /*
     * A disabled button means that credit was already loaded today.
     * This is an expected result and not an error.
     */
    if (!await openCreditDialogButton.IsEnabledAsync())
    {
        Console.WriteLine("Credit was already loaded today.");
        Console.WriteLine("No action is required.");

        WriteResult(
            "SKIPPED",
            "Credit was already loaded today. No action was required.");

        Environment.ExitCode = 0;
        return;
    }

    Console.WriteLine("Credit is available for loading.");
    Console.WriteLine("Opening credit-loading dialog...");

    await openCreditDialogButton.ClickAsync();

    var continueButton = page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions
        {
            Name = "המשך",
            Exact = true
        });

    await continueButton.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    if (!await continueButton.IsEnabledAsync())
    {
        throw new InvalidOperationException(
            "The Continue button was found but remained disabled.");
    }

    Console.WriteLine("Clicking Continue...");

    await continueButton.ClickAsync();

    var finalLoadButton = page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions
        {
            Name = "הטענת קרדיט",
            Exact = true
        });

    await finalLoadButton.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    if (!await finalLoadButton.IsEnabledAsync())
    {
        throw new InvalidOperationException(
            "The final Load Credit button was found but remained disabled.");
    }

    Console.WriteLine("Loading credit...");

    await finalLoadButton.ClickAsync();

    var closeButton = page.GetByRole(
        AriaRole.Button,
        new PageGetByRoleOptions
        {
            Name = "סגירה",
            Exact = true
        });

    await closeButton.WaitForAsync(
        new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

    Console.WriteLine("Credit loading completed successfully.");

    WriteResult(
        "SUCCESS",
        "Credit was loaded successfully.");

    Environment.ExitCode = 0;

    await closeButton.ClickAsync();
}
catch (TimeoutException ex)
{
    WriteResult(
        "ERROR",
        $"Timeout while waiting for the 10bis website: {ex.Message}");

    Environment.ExitCode = 1;
}
catch (PlaywrightException ex)
{
    WriteResult(
        "ERROR",
        $"Playwright error: {ex.Message}");

    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    WriteResult(
        "ERROR",
        $"{ex.GetType().Name}: {ex.Message}");

    Environment.ExitCode = 1;
}