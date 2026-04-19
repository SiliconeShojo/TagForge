# 🌍 Translation Guide for TagForge

Thank you for helping make **TagForge** accessible to creators worldwide! 

This guide outlines how to add a new language or improve an existing translation. Whether you are a developer or a localization enthusiast, your contribution is highly valued.

<br>

---

<br>

## 🚀 Quick Start

1.  **Check Existing Languages:** Look in `Assets/Localization/` to see if your language exists.
2.  **Get the Template:** Use `en-US.json` as your base.
3.  **Create Your File:** Rename it to your locale code (e.g., `de-DE.json`).
4.  **Translate:** Edit the values (right side) while keeping keys (left side) intact.
5.  **Submit:** Open a Pull Request or create an Issue.

<br>

---

<br>

## 📂 Step 1: File Setup

> [!NOTE]
> The project uses standard **JSON** key-value pairs. You only need a basic text editor (Notepad, TextEdit, VS Code) to work on these files.

### 1. Get the English Template
You need the master file: `Assets/Localization/en-US.json`.

* **Option A (Browser):** [Click here](https://github.com/SiliconeShojo/TagForge/blob/main/Assets/Localization/en-US.json), click **Raw**, and save it (CTRL+S).
* **Option B (Git):**
    ```bash
    git clone https://github.com/SiliconeShojo/TagForge.git
    cd TagForge/Assets/Localization
    ```

### 2. Create Your Language File
Copy the template and rename it using the **IETF Language Tag** format (`language-COUNTRY`).

| Language | Filename |
| :--- | :--- |
| **German** (Germany) | `de-DE.json` |
| **Portuguese** (Brazil) | `pt-BR.json` |
| **Chinese** (Simplified) | `zh-CN.json` |
| **Russian** (Russia) | `ru-RU.json` |

<br>

---

<br>

## ✍️ Step 2: Translation Rules

Open your new file in any text editor.

### The Format

> [!WARNING]
> **Do not change the keys on the left side.** The app uses these keys to find the text. If you change them, your translation will not load.

```json
{
  "Settings.Title": "Settings",      // ❌ DON'T CHANGE LEFT
  "Settings.Save": "Enregistrer"     // ✅ TRANSLATE RIGHT
}
```

### ⚠️ Critical Rules

| Feature | Rule | Example |
| :--- | :--- | :--- |
| **Placeholders** | **Keep `{0}`, `{1}` intact.** You may move them to fit your grammar. | `"Loaded {0} models"` → `"{0} modèles chargés"` |
| **Newlines** | **Keep `\n` intact.** This forces a line break in the UI. | `"Error.\nTry again"` → `"Erreur.\nRéessayez"` |
| **Quotes** | **Keep `\"` intact.** This displays a literal quote mark. | `"Say \"Hello\""` → `"Dites \"Bonjour\""` |

> [!IMPORTANT]
> **Syntax Watch:** Do not remove the comma `,` at the end of lines. Doing so will break the JSON structure.

<br>

---

<br>

## 🧪 Step 3: Testing & Integration

### For Non-Developers
If you cannot build the app, don't worry!

> [!TIP]
> **Sanity Check:** Copy and paste your file content into [JSONLint](https://jsonlint.com/) before submitting. It will tell you if you missed a comma or a quote. **I** will handle the final integration!

### For Developers (Build & Test)
If you have the .NET SDK installed, you can see your translation in-app immediately.

<details>
<summary><b>🛠️ Click to expand Developer Integration Steps</b></summary>

1.  **Place your file:** Ensure your new `.json` file is in `Assets/Localization/`.
2.  **Register the Language:**
    Open `Services/LocalizationService.cs` and find `GetAvailableLanguages()`. Add your entry using the **native name**:

    ```csharp
    var languages = new List<LanguageInfo>
    {
        new("en-US", "English"),
        new("fr-FR", "Français"),
        new("de-DE", "Deutsch") // <--- Add this line
    };
    ```
3.  **Build & Run:**
    ```bash
    dotnet run
    ```
4.  **Verify:** Go to **Settings > Language** and select your new language. Check for UI overflow or missing text.

</details>

<br>

---

<br>

## 📮 Step 4: Submission

**I** welcome contributions in whatever way is easiest for you.

### Option A: Pull Request (Recommended)
1.  Fork the repo and create a branch: `git checkout -b lang/add-german`
2.  Commit your JSON file (and the C# change if you did it).
3.  Open a Pull Request.

### Option B: GitHub Issue (Easy Mode)
1.  Go to [Issues](https://github.com/SiliconeShojo/TagForge/issues/new).
2.  Create a new issue titled **"Translation: [Language Name]"**.
3.  **Drag and drop** your `.json` file into the description box.
4.  Submit! **I** will add it for you.

<br>

---

<br>

## 💡 Quality Tips

* **Context:**
    * `Settings.*` → Settings Menu
    * `Agent.*` → AI Configuration
    * `Chat.*` → Chat Interface
* **Conciseness:** UI space is limited. Prefer short, punchy translations over literal ones (e.g., "Save" vs "Click here to save").
* **Tone:** Professional yet helpful. Avoid slang.

<br>

---

> [!NOTE]
> If you are unsure about a specific term or context, feel free to ask in [Discussions](https://github.com/SiliconeShojo/TagForge/discussions) or open a draft PR.

**Happy Translating!** 🌍
