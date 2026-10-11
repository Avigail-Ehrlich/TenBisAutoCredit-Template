# TenBis Auto Credit

טעינה אוטומטית של קרדיט תן ביס, כל יום א'–ה' ב-09:00, מהענן, בלי מחשב דולק.

## איך זה עובד

1. **cron-job.org** קורא ל-GitHub API ומפעיל את ה-workflow בשעה קבועה.
   (התזמון המובנה של GitHub Actions לא אמין ונוטה לדלג על הרצות, לכן הוא לא בשימוש.)
2. **GitHub Actions** מריץ את התוכנית (C# + Playwright, Chromium headless) עם הסשן השמור של 10bis.
3. התוכנית נכנסת ל"דו"ח חיובים" ולוחצת "הטענת תן ביס קרדיט" ← "המשך" ← "הטענת קרדיט".
   אם הכפתור לא פעיל, הקרדיט כבר נטען היום ואין מה לעשות.
4. התוצאה נרשמת כתגובה ב-Issue בשם **"יומן טעינות קרדיט"** (נוצר אוטומטית בהרצה הראשונה)
   עם תיוג שלך, ו-GitHub שולח מייל:
   - ✅ הקרדיט נטען.
   - ❌ הטעינה נכשלה, עם הסיבה (פעם אחת ביום לכל היותר).
   - אין מייל אם הקרדיט כבר נטען קודם באותו יום.

## דרישות

- חשבון GitHub
- .NET SDK 10 ו-PowerShell (להתחברות הראשונית, מקומית)
- [GitHub CLI](https://cli.github.com/) (`gh`) — אופציונלי, אפשר גם דרך האתר

## הקמה

### 1. ריפו פרטי משלך

בדף הריפו הזה: **Use this template → Create a new repository** → **Private**.

> ⚠️ הריפו חייב להיות פרטי. היומן וההרצות שלך יהיו גלויים בו.

```powershell
git clone https://github.com/<USER>/<REPO>.git
cd <REPO>
```

### 2. התחברות ל-10bis ושמירת הסשן

```powershell
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
dotnet run -- --login
```

נפתח דפדפן. מתחברים ל-10bis (כולל הקוד החד-פעמי) עד שמופיע מסך הבית עם כפתור התפריט ☰.
הסשן נשמר ל-`playwright-auth/tenbis-state.json` והדפדפן נסגר לבד (עד 5 דקות).

> ⚠️ הקובץ הזה הוא גישה מלאה לחשבון ה-10bis שלך. הוא ב-`.gitignore` — **לעולם לא לעשות לו commit** ולא לשלוח אותו לאף אחד.

אפשר לבדוק מקומית (זה יטען קרדיט אמיתי אם עוד לא נטען היום):

```powershell
dotnet run
```

### 3. שמירת הסשן כ-Secret

```powershell
Get-Content playwright-auth\tenbis-state.json -Raw | gh secret set TENBIS_STATE --repo <USER>/<REPO>
```

או באתר: **Settings → Secrets and variables → Actions → New repository secret**,
שם `TENBIS_STATE`, ובערך — כל התוכן של הקובץ.

### 4. טוקן להפעלת ה-workflow

[github.com/settings/personal-access-tokens/new](https://github.com/settings/personal-access-tokens/new):

| שדה | ערך |
|---|---|
| Token name | `cron-job.org TenBis` |
| Expiration | **Custom** — שנה קדימה (ברירת המחדל היא 30 יום!) |
| Repository access | **Only select repositories** → הריפו שלך |
| Permissions → Actions | **Read and write** (ושום דבר אחר) |

הטוקן מאפשר רק להפעיל workflows בריפו הזה — לא גישה לקוד או ל-Secrets.

### 5. תזמון ב-cron-job.org

נרשמים ל-[cron-job.org](https://cron-job.org) (חינם) → **Create cronjob**:

**Common**

- **URL:**
  ```
  https://api.github.com/repos/<USER>/<REPO>/actions/workflows/load-credit.yml/dispatches
  ```
- **Execution schedule:** Custom → Days of week: Sunday–Thursday, Hours: 9, Minutes: 0.
  ברשימות בוחרים/מבטלים עם **Ctrl+לחיצה**. בשדה Crontab expression צריך להופיע `0 9 * * 0-4`.
- **Time zone:** `Asia/Jerusalem` (אחרת זה ירוץ לפי UTC ויזוז במעבר שעון).
- **Notifications:** לסמן התראה על כישלון הרצה — מכסה גם טוקן שפג.

**Advanced**

- **Request method:** `POST`
- **Headers:**

  | Key | Value |
  |---|---|
  | `Authorization` | `Bearer github_pat_...` (המילה `Bearer`, רווח, הטוקן) |
  | `Accept` | `application/vnd.github+json` |
  | `User-Agent` | `cron-job.org` |

- **Request body:** `{"ref":"main"}`
- "Requires HTTP authentication" — **לא מסומן**.

שומרים ולוחצים **Test run**. צריך לחזור **204**.
ההרצה ב-GitHub לוקחת כדקה וחצי; אחריה אמור להגיע מייל ✅ (או ❌ עם סיבה).

| תשובה | משמעות |
|---|---|
| 204 | עובד |
| 401 | ה-Header לא נכון — בדרך כלל חסר `Bearer ` |
| 403 / 404 | הטוקן בלי הרשאת Actions, או לא לריפו הנכון, או URL שגוי |
| 422 | חסר ה-Request body |

### 6. הגדרות מייל ב-GitHub

[github.com/settings/notifications](https://github.com/settings/notifications):

- **Participating, @mentions and custom** → Email מסומן (מיילי ✅ / ❌).
- **Actions** → Email + **Only notify for failed workflows** (רק תקלות ב-workflow עצמו).

## תחזוקה

| מה קרה | מה עושים |
|---|---|
| מייל ❌ `Login was not detected` | הסשן פג: שלב 2 (`dotnet run -- --login`) ושלב 3 שוב |
| מייל ❌ אחר | כנראה 10bis שינו את האתר — לבדוק את הלוג בקישור שבמייל |
| לא הגיע שום מייל עד 09:15 | לבדוק **History** במשימה ב-cron-job.org ואת לשונית Actions |
| הטוקן עומד לפוג | בדף הטוקנים: **Regenerate token**, ולהחליף ב-Header ב-cron-job.org |

הרצה ידנית בכל זמן: **Test run** ב-cron-job.org, או בלשונית **Actions → Load 10bis credit → Run workflow**.

## עלות

חינם. ריפו פרטי מקבל 2,000 דקות GitHub Actions בחודש; הרצה לוקחת כ-2 דקות.
