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

/*
 * Reads the "יתרה עדכנית" (current balance) from the "תן ביס קרדיט" card
 * in the charges report. The balance is informational only, so any problem
 * here returns null instead of failing the run.
 */
async Task<string?> ReadCreditBalanceAsync(IPage page, bool reload)
{
    try
    {
        if (reload)
        {
            await page.ReloadAsync(
                new PageReloadOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });
        }

        // The nearest ancestor of the label that also contains digits is the card.
        var balanceCard = page.Locator(
            "xpath=//*[normalize-space(text())='יתרה עדכנית']" +
            "/ancestor::*[contains(translate(., '0123456789', '##########'), '#')][1]");

        await balanceCard.First.WaitForAsync(
            new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30000
            });

        string cardText = await balanceCard.First.InnerTextAsync();

        var match = System.Text.RegularExpressions.Regex.Match(
            cardText,
            @"\d[\d,]*(\.\d+)?");

        return match.Success ? match.Value : null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Could not read the credit balance: {ex.Message}");
        return null;
    }
}

string FormatBalance(string? balance) =>
    balance is null ? "" : $" Credit balance: {balance}";

IPage? currentPage = null;

/*
 * On a failure, saves a screenshot and prints the visible buttons and
 * dialog text, so the log shows what 10bis displayed instead of the
 * expected button. Never throws.
 */
async Task SaveDiagnosticsAsync()
{
    if (currentPage is null)
    {
        return;
    }

    try
    {
        Console.WriteLine($"Diagnostics - page URL: {currentPage.Url}");

        await currentPage.ScreenshotAsync(
            new PageScreenshotOptions
            {
                Path = Path.Combine(AppContext.BaseDirectory, "failure.png"),
                FullPage = true
            });

        var buttons = await currentPage
            .Locator("button:visible, [role=button]:visible")
            .AllInnerTextsAsync();

        Console.WriteLine(
            "Diagnostics - visible buttons: " +
            string.Join(" | ", buttons.Select(b => b.Replace("\n", " ").Trim())));

        var dialogs = await currentPage
            .Locator("[role=dialog]:visible, [role=alertdialog]:visible")
            .AllInnerTextsAsync();

        foreach (string dialog in dialogs)
        {
            Console.WriteLine(
                "Diagnostics - dialog text: " +
                dialog.Replace("\n", " ").Trim());
        }
    }
    catch (Exception diagnosticsEx)
    {
        Console.WriteLine($"Could not save diagnostics: {diagnosticsEx.Message}");
    }
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
    currentPage = page;
    try
    {

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

        /*
         * 10bis replaces the session cookies when it refreshes them and
         * invalidates the old ones, so the refreshed session must be saved
         * for the next run. In GitHub Actions it is kept in the Actions cache.
         */
        await context.StorageStateAsync(
            new BrowserContextStorageStateOptions
            {
                Path = statePath
            });

        Console.WriteLine("Refreshed session was saved.");
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

            string? currentBalance = await ReadCreditBalanceAsync(page, reload: false);

            WriteResult(
                "SKIPPED",
                "Credit was already loaded today. No action was required." +
                FormatBalance(currentBalance));

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

        // Reload so the charges report shows the balance after this load.
        string? balance = await ReadCreditBalanceAsync(page, reload: true);

        WriteResult(
            "SUCCESS",
            "Credit was loaded successfully." + FormatBalance(balance));

        Environment.ExitCode = 0;
    }
    catch
    {
        // The browser is still open here, unlike in the outer catch blocks.
        await SaveDiagnosticsAsync();
        throw;
    }
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