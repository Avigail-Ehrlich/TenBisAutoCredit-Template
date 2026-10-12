# TenBis Auto Credit

Automatically loads your daily 10bis (Ten Bis) credit every Sunday–Thursday at 09:00, from the cloud. No computer needs to be on.

## How it works

1. **cron-job.org** calls the GitHub API and triggers the workflow at a fixed time.
   (GitHub Actions' built-in `schedule` trigger is unreliable and tends to skip runs, so it is not used.)
2. **GitHub Actions** runs the program (C# + Playwright, headless Chromium) with your saved 10bis session.
3. The program opens "דו"ח חיובים" (charges report) and clicks "הטענת תן ביס קרדיט" → "המשך" → "הטענת קרדיט".
   If the button is disabled, credit was already loaded today and there is nothing to do.
4. The result is posted as a comment on an issue named **"יומן טעינות קרדיט"** (credit log), created automatically
   on the first run. The comment mentions you, so GitHub sends you an email:
   - ✅ Credit was loaded.
   - ❌ Loading failed, with the reason (at most once a day).
   - No email if credit was already loaded earlier that day.

## Requirements

- A GitHub account
- .NET SDK 10 and PowerShell (for the initial login, run locally)
- [GitHub CLI](https://cli.github.com/) (`gh`) — optional, the website works too

## Setup

### 1. Your own private repository

On this repository's page: **Use this template → Create a new repository** → **Private**.

> ⚠️ The repository must be private. Your credit log and workflow runs will be visible in it.

```powershell
git clone https://github.com/<USER>/<REPO>.git
cd <REPO>
```

### 2. Log in to 10bis and save the session

```powershell
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
dotnet run -- --login
```

A browser opens. Log in to 10bis (including the one-time code) until the home screen with the ☰ menu button appears.
The session is saved to `playwright-auth/tenbis-state.json` and the browser closes by itself (5-minute limit).

> ⚠️ This file is full access to your 10bis account. It is in `.gitignore` — **never commit it** and never send it to anyone.

You can test locally (this loads real credit if it has not been loaded today):

```powershell
dotnet run
```

### 3. Store the session as a secret

```powershell
Get-Content playwright-auth\tenbis-state.json -Raw | gh secret set TENBIS_STATE --repo <USER>/<REPO>
```

Or on the website: **Settings → Secrets and variables → Actions → New repository secret**,
name `TENBIS_STATE`, value — the entire contents of the file.

### 4. A token for triggering the workflow

[github.com/settings/personal-access-tokens/new](https://github.com/settings/personal-access-tokens/new):

| Field | Value |
|---|---|
| Token name | `cron-job.org TenBis` |
| Expiration | **Custom** — one year ahead (the default is 30 days!) |
| Repository access | **Only select repositories** → your repository |
| Permissions → Actions | **Read and write** (nothing else) |

The token can only trigger workflows in this repository — no access to the code or to secrets.

### 5. Scheduling on cron-job.org

Sign up at [cron-job.org](https://cron-job.org) (free) → **Create cronjob**:

**Common**

- **URL:**
  ```
  https://api.github.com/repos/<USER>/<REPO>/actions/workflows/load-credit.yml/dispatches
  ```
- **Execution schedule:** Custom → Days of week: Sunday–Thursday, Hours: 9, Minutes: 0.
  The lists are multi-select: use **Ctrl+click** to select/deselect. The Crontab expression field should show `0 9 * * 0-4`.
- **Time zone:** `Asia/Jerusalem` (otherwise it runs on UTC and shifts with daylight saving time).
- **Notifications:** enable notification on execution failure — this also covers an expired token.

**Advanced**

- **Request method:** `POST`
- **Headers:**

  | Key | Value |
  |---|---|
  | `Authorization` | `Bearer github_pat_...` (the word `Bearer`, a space, then the token) |
  | `Accept` | `application/vnd.github+json` |
  | `User-Agent` | `cron-job.org` |

- **Request body:** `{"ref":"main"}`
- "Requires HTTP authentication" — **unchecked**.

Save and click **Test run**. It should return **204**.
The GitHub run takes about a minute and a half; then you should get a ✅ email (or ❌ with a reason).

| Response | Meaning |
|---|---|
| 204 | Working |
| 401 | Wrong header — usually the `Bearer ` prefix is missing |
| 403 / 404 | Token lacks the Actions permission, is for the wrong repository, or the URL is wrong |
| 422 | Request body is missing |

### 6. GitHub email settings

[github.com/settings/notifications](https://github.com/settings/notifications):

- **Participating, @mentions and custom** → Email checked (the ✅ / ❌ emails).
- **Actions** → Email + **Only notify for failed workflows** (only for failures of the workflow itself).

## Maintenance

| What happened | What to do |
|---|---|
| ❌ email `Login was not detected` | The session expired: repeat step 2 (`dotnet run -- --login`) and step 3. This also happens if no run happened for 7 days (see below) |
| Any other ❌ email | 10bis probably changed their site — check the log linked in the email |
| No email at all by 09:15 | Check **History** for the job on cron-job.org and the repository's Actions tab |
| Token about to expire | On the tokens page: **Regenerate token**, then replace it in the cron-job.org header |

Manual run at any time: **Test run** on cron-job.org, or **Actions → Load 10bis credit → Run workflow** in the repository.

### About the session

10bis replaces the session cookies when it refreshes them and invalidates the old ones.
So every run saves the refreshed session to the GitHub Actions cache, and the next run uses it
instead of the original one from the `TENBIS_STATE` secret. After a new login and secret update,
the cached session is ignored automatically. GitHub deletes caches that were not used for 7 days,
so if the job stops running for a week you will need to log in again.

## Cost

Free. Private repositories get 2,000 GitHub Actions minutes per month; a run takes about 2 minutes.
