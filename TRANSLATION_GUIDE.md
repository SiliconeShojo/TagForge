# 🌍 Translation Guide for TagForge

Thank you for helping make TagForge accessible to users around the world! This guide will walk you through everything you need to know to contribute a translation, even if you've never contributed to a GitHub project before.

---

## 📚 Table of Contents

1. [What is Localization?](#what-is-localization)
2. [Before You Start](#before-you-start)
3. [Step-by-Step Translation Process](#step-by-step-translation-process)
4. [Understanding the Translation Format](#understanding-the-translation-format)
5. [Testing Your Translation](#testing-your-translation)
6. [Submitting Your Translation](#submitting-your-translation)
7. [Tips for Quality Translations](#tips-for-quality-translations)

---

## What is Localization?

**Localization** (often shortened to "L10n") is the process of adapting software to different languages and regions. When you contribute a translation, you're helping users in your country or language community use TagForge in their native language.

**Examples:**
- A French user can see "Enregistrer" instead of "Save"
- A Spanish user can read "Configuración" instead of "Settings"
- A Japanese user can navigate the app entirely in 日本語

---

## Before You Start

### What You'll Need

1. **A text editor** - Any text editor works! Examples:
   - **Windows**: Notepad, Notepad++, VS Code
   - **Mac**: TextEdit, VS Code
   - **Online**: You can even use GitHub's online editor

2. **Basic understanding of JSON** - Don't worry! I'll explain the format below. It's very simple.

3. **Fluency in your target language** - You should be a native speaker or highly proficient.

### Check if Your Language Already Exists

Before starting, check the `Assets/Localization/` folder in the TagForge repository to see if your language is already there:

**Current Translations:**
- 🇺🇸 English (`en-US.json`) - Base language
- 🇫🇷 French (`fr-FR.json`)
- 🇪🇸 Spanish (`es-ES.json`)

If your language is already listed, you can help improve it! Otherwise, you'll create a new file.

---

## Step-by-Step Translation Process

### Step 1: Get the English Template

The English file (`en-US.json`) serves as the master template. You'll copy this and translate it.

**Where to find it:**
`TagForge/Assets/Localization/en-US.json`

**Option A - Download from GitHub:**
1. Go to the [TagForge repository](https://github.com/SiliconeShojo/TagForge)
2. Navigate to `Assets/Localization/`
3. Click on `en-US.json`
4. Click the "Raw" button
5. Right-click → "Save As" and save it to your computer

**Option B - Clone the repository:**
```bash
git clone https://github.com/SiliconeShojo/TagForge.git
cd TagForge/Assets/Localization
```

### Step 2: Create Your Language File

**Naming Convention:**
Use the standard `language-COUNTRY` format. The language code should be lowercase, and the country code uppercase.

**Examples:**
- German (Germany): `de-DE.json`
- Japanese (Japan): `ja-JP.json`
- Portuguese (Brazil): `pt-BR.json`
- Chinese (Simplified): `zh-CN.json`
- Russian (Russia): `ru-RU.json`

**How to create it:**
1. Copy `en-US.json`
2. Rename it to your language code (e.g., `de-DE.json`)
3. Open it in your text editor

### Step 3: Translate the Values

Now comes the fun part! Open your new file and start translating.

**IMPORTANT RULES:**

✅ **DO:**
- Translate the text on the **right side** (after the `:`)
- Keep the **keys on the left side** exactly as they are
- Preserve placeholders like `{0}`, `{1}`, etc.
- Use natural, native-sounding language

❌ **DON'T:**
- Change the keys (left side before `:`)
- Remove special characters like `{0}` or `\n`
- Add or remove commas between lines

---

## Understanding the Translation Format

### Basic Structure

Each translation file is a **JSON object** with key-value pairs:

```json
{
  "KeyName": "Value to translate"
}
```

### Example Entry

**Original (English):**
```json
{
  "Settings.Save": "Save"
}
```

**Translated (French):**
```json
{
  "Settings.Save": "Enregistrer"
}
```

**Breakdown:**
- `"Settings.Save"` = **KEY** (Don't change this!)
- `"Enregistrer"` = **VALUE** (Translate this!)

### Handling Placeholders

Some strings contain **placeholders** like `{0}`, `{1}`, etc. These are replaced with dynamic content at runtime.

**Example:**
```json
"Agent.ModelsLoaded": "Success! Loaded {0} models."
```

In this case:
- `{0}` will be replaced with a number (e.g., "15")
- The full text might appear as: "Success! Loaded 15 models."

**When translating:**
- **Keep the placeholder** `{0}` in your translation
- **Move it** to where it makes sense grammatically in your language

**French example:**
```json
"Agent.ModelsLoaded": "Succès ! {0} modèles chargés."
```

Notice how `{0}` moved to a different position because French grammar requires it.

### Special Characters

Some strings contain:
- **`\n`** = Line break (new line)
- **`\"`** = Quotation mark (escaped)
- **`\\`** = Backslash (escaped)

**Example:**
```json
"Error.Message": "Connection failed.\nPlease check your API key."
```

Keep `\n` in your translation where you want the line break to appear.

### Complete Example

Here's a before/after comparison:

**Before (English):**
```json
{
  "Settings.Title": "Settings",
  "Settings.Language": "Language",
  "Settings.Save": "Save",
  "Agent.ApiKey": "API Key",
  "Agent.ModelsLoaded": "Success! Loaded {0} models.",
  "Error.ConnectionFailed": "Connection failed.\nCheck your settings."
}
```

**After (German):**
```json
{
  "Settings.Title": "Einstellungen",
  "Settings.Language": "Sprache",
  "Settings.Save": "Speichern",
  "Agent.ApiKey": "API-Schlüssel",
  "Agent.ModelsLoaded": "Erfolg! {0} Modelle geladen.",
  "Error.ConnectionFailed": "Verbindung fehlgeschlagen.\nÜberprüfen Sie Ihre Einstellungen."
}
```

---

## Testing Your Translation

### Method 1: For Developers

If you have .NET 9 installed and can build the project:

1. **Add your file** to `Assets/Localization/your-language.json`

2. **Register your language** in `Services/LocalizationService.cs`:
   - Find the `GetAvailableLanguages()` method
   - Add your language to the list:
     ```csharp
     var languages = new List<LanguageInfo>
     {
         new("en-US", "English"),
         new("fr-FR", "Français"),
         new("your-CODE", "YourLanguageNativeName")  // Add this line
     };
     ```

3. **Build and run:**
   ```bash
   dotnet build
   dotnet run
   ```

4. **Test in the app:**
   - Open TagForge
   - Go to Settings → Language
   - Select your language
   - Navigate through all screens to verify translations

### Method 2: For Non-Developers

If you can't build the app, you can:

1. **Submit your translation** (see next section)
2. **Request a test build** - Mention in your Pull Request or Issue that you'd like to test it

---

## Submitting Your Translation

You have two options depending on your comfort level with GitHub.

### Option 1: GitHub Pull Request (Recommended)

**If you're familiar with Git:**

1. **Fork the repository** on GitHub
2. **Clone your fork:**
   ```bash
   git clone https://github.com/YOUR-USERNAME/TagForge.git
   ```
3. **Create a new branch:**
   ```bash
   git checkout -b add-translation-LANGUAGE
   ```
4. **Add your translation file** to `Assets/Localization/`
5. **Update `LocalizationService.cs`** (see "Testing" section above)
6. **Commit your changes:**
   ```bash
   git add .
   git commit -m "Add [Language] translation"
   ```
7. **Push to your fork:**
   ```bash
   git push origin add-translation-LANGUAGE
   ```
8. **Create a Pull Request** on GitHub

### Option 2: GitHub Issue (Easier for Beginners)

**If you're new to GitHub:**

1. Go to [TagForge Issues](https://github.com/SiliconeShojo/TagForge/issues)
2. Click **"New Issue"**
3. Use this title: `Translation: [Your Language]`
4. In the description, write:
   ```
   I've created a translation for [Language].
   Language Code: XX-YY
   Native Name: [Name]
   
   Please find my translation file attached.
   ```
5. **Attach your `.json` file** by dragging it into the comment box
6. Submit the issue

**I'll integrate it for you!**

---

## Tips for Quality Translations

### 1. Be Concise
UI space is limited. Shorter translations are better when they convey the same meaning.

**Example:**
- ❌ "Click this button to save your settings"
- ✅ "Save Settings"

### 2. Match the Tone
TagForge is professional but friendly. Avoid overly formal or overly casual language.

### 3. Context Matters
Some words can mean different things. Here's how keys are organized:

- `Settings.*` - Appears in the Settings screen
- `Agent.*` - Related to AI agent configuration
- `Chat.*` - Chat interface
- `Common.*` - Buttons/actions used everywhere (Save, Cancel, Copy, etc.)

### 4. Test Thoroughly
If possible, check:
- All tabs (Generator, Chat, Agent Config, Settings, Logs)
- All buttons and labels
- Error messages
- Tooltips (hover text)

### 5. Native Names
When registering your language in `LocalizationService.cs`, use the **native name**:
- ✅ "Français" (not "French")
- ✅ "日本語" (not "Japanese")
- ✅ "Español" (not "Spanish")

### 6. Consistency
If you translate "API Key" as "Clé API" once, use the same translation throughout.

### 7. Ask Questions!
Not sure about a translation? Open a [GitHub Discussion](https://github.com/SiliconeShojo/TagForge/discussions) and ask!

---

## Need Help?

- **Questions?** → [GitHub Discussions](https://github.com/SiliconeShojo/TagForge/discussions)
- **Issues?** → [GitHub Issues](https://github.com/SiliconeShojo/TagForge/issues)
- **General chat?** → Check the README for community links

---

## Thank You! 🎉

Your contribution helps make TagForge accessible to millions of users worldwide. Every translation matters, no matter how small your language community is.

**Happy translating!** 🌍✨
